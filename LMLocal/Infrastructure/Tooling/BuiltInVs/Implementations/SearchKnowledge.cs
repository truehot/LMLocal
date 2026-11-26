using LMLocal.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using static LMLocal.Core.Common.Pluralizer;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Performs a deterministic search over .md knowledge-base files. Returns short snippets.
    /// </summary>
    internal interface ISearchKnowledge : IBuiltInTool
    {
    }

    internal class SearchKnowledge : ISearchKnowledge
    {
        private const int SnippetRadius = 150;

        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly IFileSystem _fileSystem;
        private readonly ISettingsManager _settingsManager;
        private readonly ISearchResultCache _searchCache;

        private const string KnowledgeExtension = "*.md";
        private const int MaxFilesToScan = 1000;
        private const int DefaultMaxResults = 5;
        private const int MaxMaxResults = 20;

        public string ToolName => "search_solution_knowledge";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.ReadOnly;

        public SearchKnowledge(
            IVsDependencies vsDependencies,
            IPathResolver pathResolver,
            IFileSystem fileSystem,
            ISettingsManager settingsManager,
            ISearchResultCache searchCache)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _searchCache = searchCache ?? throw new ArgumentNullException(nameof(searchCache));
        }

        /// <summary>
        /// Gets the knowledge base paths from settings, falling back to defaults when empty.
        /// </summary>
        private string[] GetKnowledgePaths()
        {
            var paths = _settingsManager.Current?.KnowledgeBasePaths;
            if (string.IsNullOrWhiteSpace(paths))
                return Array.Empty<string>();

            return paths
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToArray();
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Search the solution knowledge base stored in Markdown files for project conventions, architecture decisions, plans, and other documented knowledge. Use when you need information from project documentation or plans stored in the solution knowledge base. Do not use this tool for source code search or file content outside the knowledge base. Prefer short keyword phrases, technical terms, identifiers, feature names, or concise topics over full sentences. Exact-phrase matches score highest. Returns the most relevant matching files with their file path, last-modified date, closest Markdown heading, and a short relevant snippet. If the needed information is not found, do not guess. Lines are 1-indexed. When deciding which document to update or whether to create a new one, use this tool first to find relevant existing documentation. Independent: several calls can go in one response.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "query", new ToolDetails { Type = "string", Description = "A short keyword phrase, technical term, identifier, feature name, or topic to search for. Prefer specific terms over full sentences." } },
                        { "max_results", new ToolDetails { Type = "integer", Description = "Maximum number of top matching files to return. Default 5, maximum 20." } }
                    },
                    Required = new List<string> { "query" }
                }
            };
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            var (query, maxResults, error) = ExtractAndValidateParameters(parameters);
            if (error != null)
                return Failure(error);

            if (!_vsDependencies.IsSolutionOpen)
                return Failure("No solution is currently open.");

            string solutionDir = _vsDependencies.GetSolutionDirectory();

            await TaskScheduler.Default;

            try
            {
                var files = EnumerateKnowledgeFiles(solutionDir, cancellationToken);
                if (files.Count == 0)
                    return Success(new List<KnowledgeMatchResult>(), 0, KnowledgeSearchOutcome.NoFiles);

                string[] partialTerms = KnowledgeScorer.TokenizeQuery(query).Length > 1
                    ? KnowledgePartialMatcher.GetSignificantTerms(query)
                    : Array.Empty<string>();
                if (partialTerms.Length < 2)
                    partialTerms = Array.Empty<string>();
                var foundTerms = new bool[partialTerms.Length];

                var results = new List<ScoredFile>();

                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var match = await ScoreFileAsync(file, query, partialTerms, foundTerms, solutionDir, cancellationToken).ConfigureAwait(false);
                        if (match != null)
                            results.Add(match);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Warn($"search_solution_knowledge: error processing '{file.Path}'", ex);
                    }
                }

                if (results.Count == 0)
                    return Success(new List<KnowledgeMatchResult>(), 0, KnowledgeSearchOutcome.NoMatches);

                results.Sort((x, y) =>
                {
                    var a = x.Result;
                    var b = y.Result;

                    int c = b.Kind.CompareTo(a.Kind);
                    if (c != 0) return c;

                    c = b.SubScore.CompareTo(a.SubScore);
                    if (c != 0) return c;

                    c = b.TieBreakScore.CompareTo(a.TieBreakScore);
                    if (c != 0) return c;

                    c = b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc);
                    if (c != 0) return c;

                    return string.CompareOrdinal(a.FilePath, b.FilePath);
                });

                results = KeepBestPartialResults(results);
                var top = results.Take(maxResults).ToList();

                bool hasPartial = false;
                foreach (var scored in top)
                {
                    if (scored.Partial == null)
                        continue;
                    FillPartialDetails(scored, partialTerms);
                    hasPartial = true;
                }

                int totalMatches = partialTerms.Length > 0
                    ? results.Count(r => r.Result.Kind != KnowledgeMatchKind.FilenameSubstring)
                        + top.Count(r => r.Result.Kind == KnowledgeMatchKind.FilenameSubstring)
                    : results.Count;

                var response = Success(top.Select(r => r.Result).ToList(), totalMatches);
                if (hasPartial)
                    response.Note = KnowledgePartialMatcher.BuildNote(partialTerms, foundTerms);
                return response;
            }
            catch (OperationCanceledException)
            {
                return Failure("Operation was cancelled.");
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("search_solution_knowledge: unexpected error", ex);
                return Failure(ex.Message);
            }
        }

        private List<KnowledgeFileEntry> EnumerateKnowledgeFiles(string solutionDir, CancellationToken cancellationToken)
        {
            var paths = GetKnowledgePaths();
            if (paths.Length == 0)
                return new List<KnowledgeFileEntry>();

            var cacheKey = BuildCacheKey(paths);
            if (_searchCache.TryGet(cacheKey, solutionDir, out CachedToolResults<KnowledgeFileEntry> cached)
                && cached != null
                && cached.AllResults != null)
            {
                return cached.AllResults;
            }

            var entries = ScanKnowledgeFiles(solutionDir, paths, cancellationToken);

            _searchCache.Set(cacheKey, solutionDir, new CachedToolResults<KnowledgeFileEntry>
            {
                AllResults = entries
            });

            return entries;
        }

        private static string BuildCacheKey(IEnumerable<string> paths)
        {
            return "search_kb|" + string.Join(";", paths);
        }

        private List<KnowledgeFileEntry> ScanKnowledgeFiles(string solutionDir, string[] paths, CancellationToken cancellationToken)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<KnowledgeFileEntry>();

            foreach (var configuredPath in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!_pathResolver.TryResolveFilePath(configuredPath, solutionDir, out string absoluteDir))
                    continue;

                if (!_fileSystem.DirectoryExists(absoluteDir))
                    continue;

                string[] filePaths;
                try
                {
                    filePaths = _fileSystem.GetFiles(absoluteDir, KnowledgeExtension);
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"search_solution_knowledge: cannot enumerate '{absoluteDir}'", ex);
                    continue;
                }

                foreach (var filePath in filePaths)
                {
                    if (!seen.Add(filePath))
                        continue;

                    entries.Add(new KnowledgeFileEntry { Path = filePath });
                }
            }

            foreach (var entry in entries)
            {
                try
                {
                    var (_, lastWrite) = _fileSystem.GetFileInfo(entry.Path);
                    entry.LastWriteTimeUtc = lastWrite;
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"search_solution_knowledge: cannot stat '{entry.Path}'", ex);
                    entry.LastWriteTimeUtc = DateTime.MinValue;
                }
            }

            return entries
                .OrderByDescending(e => e.LastWriteTimeUtc)
                .Take(MaxFilesToScan)
                .ToList();
        }

        /// <summary>
        /// Scores one file: all-words tiers first, then the partial-words fallback, then a file-name substring match.
        /// </summary>
        private async Task<ScoredFile> ScoreFileAsync(
            KnowledgeFileEntry file,
            string query,
            string[] partialTerms,
            bool[] foundTerms,
            string solutionDir,
            CancellationToken cancellationToken)
        {
            string fileName = Path.GetFileName(file.Path);
            string content = await _fileSystem.ReadAllTextAsync(file.Path, cancellationToken).ConfigureAwait(false);

            string normalized = KnowledgeScorer.NormalizeLineEndings(content);
            var score = KnowledgeScorer.Score(fileName, normalized, query);

            KnowledgePartialMatch partial = null;
            if (partialTerms.Length > 0)
            {
                partial = KnowledgePartialMatcher.Analyze(fileName, normalized, partialTerms);
                for (int i = 0; i < foundTerms.Length; i++)
                {
                    if (partial.PresentTerms[i])
                        foundTerms[i] = true;
                }
            }

            if (score.Kind < KnowledgeMatchKind.FilenameExact && partial != null && partial.IsMatch)
            {
                var partialResult = BuildResult(file, solutionDir, KnowledgeMatchKind.PartialWords, partial.WordCount, null, 0, string.Empty);
                partialResult.TieBreakScore = partial.SectionCount;
                return new ScoredFile(partialResult, partial, normalized);
            }

            if (!score.IsMatch)
                return null;

            string snippet = KnowledgeScorer.BuildSnippet(normalized, score.BestMatchIndex, SnippetRadius);
            return new ScoredFile(BuildResult(file, solutionDir, score.Kind, score.SubScore, score.Heading, score.BestMatchLine, snippet), null, null);
        }

        /// <summary>
        /// Keeps the first <see cref="KnowledgePartialMatcher.MaxResults"/> fallback results of the sorted list and drops the rest.
        /// </summary>
        private static List<ScoredFile> KeepBestPartialResults(List<ScoredFile> sorted)
        {
            var kept = new List<ScoredFile>(sorted.Count);
            int partialCount = 0;
            foreach (var scored in sorted)
            {
                if (scored.Partial != null && ++partialCount > KnowledgePartialMatcher.MaxResults)
                    continue;
                kept.Add(scored);
            }
            return kept;
        }

        /// <summary>
        /// Builds the snippet, line, heading and matched_terms of a fallback result that made it into the response.
        /// </summary>
        private static void FillPartialDetails(ScoredFile scored, string[] partialTerms)
        {
            var details = KnowledgePartialMatcher.BuildDetails(scored.Content, scored.Partial, partialTerms, SnippetRadius);
            scored.Result.Snippet = KnowledgeScorer.BuildSnippet(scored.Content, details.CenterIndex, SnippetRadius);
            scored.Result.LineNumber = details.Line;
            scored.Result.Heading = details.Heading;
            scored.Result.MatchedTerms = details.MatchedTerms;
        }

        private KnowledgeMatchResult BuildResult(
            KnowledgeFileEntry file,
            string solutionDir,
            KnowledgeMatchKind kind,
            int subScore,
            string heading,
            int lineNumber,
            string snippet)
        {
            string relativePath = file.Path;
            if (!string.IsNullOrEmpty(solutionDir)
                && _pathResolver.TryGetRelativePath(file.Path, solutionDir, out string rel))
            {
                relativePath = rel;
            }

            return new KnowledgeMatchResult
            {
                Kind = kind,
                SubScore = subScore,
                LastWriteTimeUtc = file.LastWriteTimeUtc,
                FilePath = relativePath,
                Updated = FormatDate(file.LastWriteTimeUtc),
                Heading = heading,
                Snippet = snippet,
                LineNumber = lineNumber
            };
        }

        private static string FormatDate(DateTime dateTimeUtc)
        {
            if (dateTimeUtc == DateTime.MinValue)
                return string.Empty;
            return dateTimeUtc.ToString("yyyy-MM-dd");
        }

        private static KnowledgeSearchResponse Failure(string message)
        {
            return new KnowledgeSearchResponse
            {
                Success = false,
                ErrorMessage = message,
                Results = new List<KnowledgeMatchResult>(),
                TotalMatches = 0,
                Outcome = KnowledgeSearchOutcome.None
            };
        }

        private static KnowledgeSearchResponse Success(
            List<KnowledgeMatchResult> results,
            int totalMatches,
            KnowledgeSearchOutcome outcome = KnowledgeSearchOutcome.Success)
        {
            return new KnowledgeSearchResponse
            {
                Success = true,
                Results = results,
                TotalMatches = totalMatches,
                Outcome = outcome
            };
        }

        private (string query, int maxResults, string error) ExtractAndValidateParameters(Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return (null, DefaultMaxResults, "Parameters cannot be null.");

            if (!parameters.TryGetValue("query", out object queryObj) || !(queryObj is string queryStr))
                return (null, DefaultMaxResults, "Parameter 'query' is required and must be a string.");

            queryStr = queryStr.Trim();
            if (string.IsNullOrEmpty(queryStr))
                return (null, DefaultMaxResults, "Parameter 'query' must be a non-empty string.");

            int maxResults = DefaultMaxResults;
            if (parameters.TryGetValue("max_results", out object maxObj) && maxObj != null && int.TryParse(maxObj.ToString(), out int max))
                maxResults = Math.Min(Math.Max(max, 1), MaxMaxResults);

            return (queryStr, maxResults, null);
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            var query = parameters != null && parameters.TryGetValue("query", out var q) ? q?.ToString() : "";
            return $"Searching knowledge base for '{query}'... ";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is KnowledgeSearchResponse resp)
            {
                if (!resp.Success)
                    return $"Searching knowledge base failed: {resp.ErrorMessage}";

                switch (resp.Outcome)
                {
                    case KnowledgeSearchOutcome.NoFiles:
                        return "No matching knowledge files found in configured paths.";
                    case KnowledgeSearchOutcome.NoMatches:
                        return "No occurrences of the query were found in the scanned knowledge files.";
                    case KnowledgeSearchOutcome.Success:
                        return $"Found {resp.TotalMatches} {Pluralize(resp.TotalMatches, "relevant knowledge match", "relevant knowledge matches")}.";
                    default:
                        return "Knowledge search finished.";
                }
            }

            return "Knowledge search finished.";
        }

        private class KnowledgeFileEntry
        {
            public string Path { get; set; }
            public DateTime LastWriteTimeUtc { get; set; }
        }

        /// <summary>
        /// A scored file; a fallback result keeps its match and text until its snippet is built.
        /// </summary>
        private sealed class ScoredFile
        {
            public KnowledgeMatchResult Result { get; }
            public KnowledgePartialMatch Partial { get; }
            public string Content { get; }

            public ScoredFile(KnowledgeMatchResult result, KnowledgePartialMatch partial, string content)
            {
                Result = result;
                Partial = partial;
                Content = content;
            }
        }
    }
}
