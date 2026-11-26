using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Persistence;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search
{
    /// <summary>Matches of one search text in the scanned files.</summary>
    internal sealed class TermScanResult
    {
        public string Term { get; set; }

        /// <summary>Files with matches; ranked (score, match count, path) when ranking was requested.</summary>
        public List<SearchResult> Results { get; set; }

        /// <summary>Set when the literal text had no matches and the OR-split / unquoted fallback was searched.</summary>
        public string FallbackNote { get; set; }
    }

    /// <summary>
    /// Literal content search of several texts in one pass over the files. Each text is matched, ranked and
    /// falls back (<see cref="SearchQueryFallback"/>) on its own, exactly as a search for that text alone.
    /// </summary>
    internal static class ContentTermScanner
    {
        public static async Task<List<TermScanResult>> ScanAsync(
            IFileSystem fileSystem,
            IReadOnlyList<string> absolutePaths,
            IReadOnlyList<string> terms,
            bool needRanking,
            Func<string, string> toRelativePath,
            CancellationToken cancellationToken)
        {
            var states = terms.Select(t => new TermState(t, needRanking)).ToList();

            foreach (var absolutePath in absolutePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!fileSystem.FileExists(absolutePath))
                {
                    InternalLogger.Warn($"SearchInSolution: file not found: '{absolutePath}'");
                    continue;
                }

                try
                {
                    string extension = Path.GetExtension(absolutePath);
                    foreach (var state in states)
                        state.BeginFile();

                    int totalLines = 0;
                    await fileSystem.ReadLinesAsync(absolutePath, (lineNumber, line) =>
                    {
                        totalLines = lineNumber;
                        foreach (var state in states)
                            state.MatchLine(lineNumber, line, extension);
                    }, cancellationToken).ConfigureAwait(false);

                    string relativePath = null;
                    foreach (var state in states)
                    {
                        if (state.FileHasMatches)
                            state.EndFile(relativePath ?? (relativePath = toRelativePath(absolutePath)), totalLines);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"SearchInSolution: error reading '{absolutePath}'", ex);
                }
            }

            return states.Select(s => s.ToResult()).ToList();
        }

        private static int ComputeScore(int matchCount, int exactWordCount, int declarationWeightSum)
        {
            return matchCount + exactWordCount * DeclarationWeights.ExactWordBonus + declarationWeightSum;
        }

        /// <summary>Per-text search state across files: literal matches, and fallback matches while no literal match exists.</summary>
        private sealed class TermState
        {
            private readonly string _term;
            private readonly bool _needRanking;
            private readonly bool _isIdentifier;
            private readonly SearchQueryFallback _fallback;
            private readonly Dictionary<string, bool> _fallbackIsIdentifier;
            private readonly List<SearchResult> _literalResults = new List<SearchResult>();
            private readonly List<SearchResult> _fallbackResults = new List<SearchResult>();
            private FileMatchAccumulator _literal;
            private FileMatchAccumulator _fallbackAcc;

            public TermState(string term, bool needRanking)
            {
                _term = term;
                _needRanking = needRanking;
                _isIdentifier = QueryClassifier.IsIdentifierQuery(term);
                _fallback = SearchQueryFallback.Create(term);
                _fallbackIsIdentifier = _fallback.Terms.ToDictionary(t => t, QueryClassifier.IsIdentifierQuery, StringComparer.OrdinalIgnoreCase);
            }

            public bool FileHasMatches => _literal.Count > 0 || (_fallbackAcc != null && _fallbackAcc.Count > 0);

            public void BeginFile()
            {
                _literal = new FileMatchAccumulator(_needRanking);
                _fallbackAcc = _fallback.HasTerms ? new FileMatchAccumulator(_needRanking) : null;
            }

            public void MatchLine(int lineNumber, string line, string extension)
            {
                var m = ContentSearchMatcher.Match(line, _term, extension, _needRanking && _isIdentifier, _needRanking);
                if (m.IsMatch)
                    _literal.Add(lineNumber, line, m, null);

                if (_fallbackAcc == null || _literalResults.Count > 0 || _literal.Count > 0)
                    return;

                string term = _fallback.FindTerm(line);
                if (term == null)
                    return;

                var fm = ContentSearchMatcher.Match(line, term, extension, _needRanking && _fallbackIsIdentifier[term], _needRanking);
                if (fm.IsMatch)
                    _fallbackAcc.Add(lineNumber, line, fm, term);
            }

            public void EndFile(string relativePath, int totalLines)
            {
                if (_literal.Count > 0)
                    _literalResults.Add(_literal.ToResult(relativePath, totalLines));
                else if (_literalResults.Count == 0)
                    _fallbackResults.Add(_fallbackAcc.ToResult(relativePath, totalLines));
            }

            public TermScanResult ToResult()
            {
                var results = _literalResults;
                string note = null;
                if (_literalResults.Count == 0 && _fallback.HasTerms)
                {
                    results = _fallbackResults;
                    note = _fallback.BuildNote(_fallbackResults.Sum(r => r.MatchCount));
                }

                if (_needRanking)
                {
                    results.Sort((a, b) =>
                    {
                        int c = b.Score.CompareTo(a.Score);
                        if (c != 0) return c;
                        c = b.MatchCount.CompareTo(a.MatchCount);
                        if (c != 0) return c;
                        return string.CompareOrdinal(a.FilePath, b.FilePath);
                    });
                }

                return new TermScanResult { Term = _term, Results = results, FallbackNote = note };
            }
        }

        /// <summary>Collects the matches of one file and the ranking inputs for its score.</summary>
        private sealed class FileMatchAccumulator
        {
            private readonly bool _needRanking;
            private readonly List<SearchMatch> _matches = new List<SearchMatch>();
            private int _exactWordCount;
            private int _declarationWeightSum;

            public FileMatchAccumulator(bool needRanking)
            {
                _needRanking = needRanking;
            }

            public int Count => _matches.Count;

            public void Add(int lineNumber, string line, LineMatch m, string matchedTerm)
            {
                var match = new SearchMatch
                {
                    LineNumber = lineNumber,
                    LineText = line.Trim(),
                    MatchedTerm = matchedTerm
                };

                if (_needRanking)
                {
                    match.IsExactWord = m.IsExactWord;
                    match.DeclarationKind = m.Kind == SearchMatchKind.Other ? null : m.Kind.ToString();

                    if (m.IsExactWord)
                        _exactWordCount++;
                    if (m.Kind != SearchMatchKind.Other)
                        _declarationWeightSum += DeclarationWeights.WeightOf(m.Kind);
                }

                _matches.Add(match);
            }

            public SearchResult ToResult(string relativePath, int totalLines)
            {
                return new SearchResult
                {
                    FilePath = relativePath,
                    TotalLines = totalLines,
                    Matches = _matches,
                    MatchCount = _matches.Count,
                    Score = _needRanking ? ComputeScore(_matches.Count, _exactWordCount, _declarationWeightSum) : 0
                };
            }
        }
    }
}
