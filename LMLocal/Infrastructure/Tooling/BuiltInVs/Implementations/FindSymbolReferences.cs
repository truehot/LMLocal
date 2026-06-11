using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations.FindSymbolReferences;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Tool interface for finding references to a code symbol across the current Visual Studio solution.
    /// </summary>
    internal interface IFindSymbolReferences : IBuiltInTool
    {
        Task<SymbolReferencesResponse> ExecuteAsync(
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken = default);
    }

    internal class FindSymbolReferences : IFindSymbolReferences
    {
        private readonly IPathResolver _pathResolver;
        private readonly ISearchResultCache _searchCache;
        private const int MaxSymbolsToProcess = 5;
        private const int MaxTotalReferences = 200;
        private const int DefaultTake = 25;

        public string ToolName => "Find_Symbol_References";

        public FindSymbolReferences(IPathResolver pathResolver, ISearchResultCache searchCache)
        {
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _searchCache = searchCache ?? throw new ArgumentNullException(nameof(searchCache));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = $"Finds all references of a code symbol across the current Visual Studio solution. Response fields: success (bool), error_message (string), symbol_name (string), total_references (int), results (array of {{file (string), matches (array of {{line (int), text (string)}})}}}}, next_page_token (string or null). If 'next_page_token' is not null, more results exist; pass it as 'page_token' to get next page. Returns up to {MaxTotalReferences} references, paginated by file ({DefaultTake} files per page). Limited to {MaxSymbolsToProcess} symbol candidates.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "symbol_name", new ToolDetails { Type = "string", Description = "The exact name of the code symbol (e.g., 'PaymentService', 'ProcessOrder', or '_logger') to find references for." } },
                        { "page_token", new ToolDetails { Type = "string", Description = "Page token for fetching a specific page of results. Leave empty or null for the first page. Use the next_page_token value from the previous response to get the next page." } }
                    },
                    Required = new List<string> { "symbol_name" }
                }
            };
        }

        private async Task<SymbolReferencesResponse> ExecuteCoreAsync(
            string symbolName,
            CancellationToken cancellationToken = default)
        {
            try
            {
                symbolName = ExtractAndValidateParameters(symbolName);

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                var componentModel = (IComponentModel)ServiceProvider.GlobalProvider.GetService(typeof(SComponentModel));
                if (componentModel == null)
                    return new SymbolReferencesResponse
                    {
                        Success = false,
                        ErrorMessage = "Component model is not available",
                        SymbolName = symbolName,
                        Results = new List<FileReferencesGroup>(),
                        TotalReferences = 0,
                        NextPageToken = null
                    };

                var workspace = componentModel.GetService<VisualStudioWorkspace>();
                if (workspace == null)
                    return new SymbolReferencesResponse
                    {
                        Success = false,
                        ErrorMessage = "Visual Studio workspace is not available",
                        SymbolName = symbolName,
                        Results = new List<FileReferencesGroup>(),
                        TotalReferences = 0,
                        NextPageToken = null
                    };

                var solutionSnapshot = workspace.CurrentSolution;
                if (solutionSnapshot == null)
                    return new SymbolReferencesResponse
                    {
                        Success = false,
                        ErrorMessage = "No solution is currently open",
                        SymbolName = symbolName,
                        Results = new List<FileReferencesGroup>(),
                        TotalReferences = 0,
                        NextPageToken = null
                    };

                var projects = solutionSnapshot.Projects.ToList();
                var solutionDir = System.IO.Path.GetDirectoryName(solutionSnapshot.FilePath);

                var result = await Task.Run(async () =>
                {
                    var fileGroupsDict = new Dictionary<string, FileReferencesGroup>(StringComparer.OrdinalIgnoreCase);
                    int totalReferenceCount = 0;
                    int symbolsProcessed = 0;
                    bool hasMoreResults = false;
                    var seenLocations = new HashSet<string>(StringComparer.Ordinal);

                    foreach (var project in projects)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!project.SupportsCompilation) continue;

                        var symbols = await SymbolFinder.FindDeclarationsAsync(
                            project,
                            symbolName,
                            ignoreCase: false,
                            cancellationToken: cancellationToken
                        ).ConfigureAwait(false);

                        foreach (var symbol in symbols)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (symbolsProcessed >= MaxSymbolsToProcess)
                            {
                                hasMoreResults = true;
                                break;
                            }

                            symbolsProcessed++;

                            var references = await SymbolFinder.FindReferencesAsync(
                                symbol,
                                solutionSnapshot,
                                cancellationToken: cancellationToken
                            ).ConfigureAwait(false);

                            foreach (var reference in references)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                foreach (var location in reference.Locations)
                                {
                                    var document = location.Document;
                                    var filePath = document.FilePath;
                                    if (string.IsNullOrEmpty(filePath))
                                        continue;

                                    var textSpan = location.Location.SourceSpan;
                                    var locationKey = $"{document.Id}:{textSpan.Start}:{textSpan.Length}";

                                    if (!seenLocations.Add(locationKey))
                                        continue;

                                    string relativePath = filePath;
                                    if (!string.IsNullOrEmpty(solutionDir))
                                    {
                                        if (_pathResolver.TryGetRelativePath(filePath, solutionDir, out var relPath))
                                        {
                                            relativePath = relPath;
                                        }
                                    }

                                    var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
                                    var lineSpan = sourceText.Lines.GetLinePositionSpan(textSpan);

                                    int lineNumber = lineSpan.Start.Line;
                                    string lineText = sourceText.Lines[lineNumber].ToString().Trim();

                                    if (!fileGroupsDict.TryGetValue(relativePath, out var fileGroup))
                                    {
                                        fileGroup = new FileReferencesGroup
                                        {
                                            FilePath = relativePath,
                                            Matches = new List<ReferenceItem>()
                                        };
                                        fileGroupsDict[relativePath] = fileGroup;
                                    }

                                    fileGroup.Matches.Add(new ReferenceItem
                                    {
                                        LineNumber = lineNumber,
                                        LineText = lineText
                                    });

                                    totalReferenceCount++;
                                    if (totalReferenceCount >= MaxTotalReferences)
                                    {
                                        hasMoreResults = true;
                                        break;
                                    }
                                }

                                if (hasMoreResults)
                                    break;
                            }
                        }

                        if (hasMoreResults)
                            break;
                    }

                    var sortedResults = fileGroupsDict.Values
                        .OrderBy(r => r.FilePath)
                        .ToList();

                    return new SymbolReferencesResponse
                    {
                        Results = sortedResults,
                        SymbolName = symbolName,
                        TotalReferences = totalReferenceCount,
                        NextPageToken = null,
                        Success = true,
                        ErrorMessage = null
                    };
                }, cancellationToken).ConfigureAwait(false);

                return result;
            }
            catch (Exception ex)
            {
                return new SymbolReferencesResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    SymbolName = symbolName,
                    Results = new List<FileReferencesGroup>(),
                    TotalReferences = 0,
                    NextPageToken = null
                };
            }
        }

        public async Task<SymbolReferencesResponse> ExecuteAsync(
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (symbolName, pageToken) = ExtractAndValidateParametersFromDict(parameters);
                int pageNumber = string.IsNullOrEmpty(pageToken) || !int.TryParse(pageToken, out var pn) ? 0 : pn;
                int skip = pageNumber * DefaultTake;

                string cacheKey = BuildCacheKey(symbolName);
                if (_searchCache.TryGet(cacheKey, "", out CachedToolResults<FileReferencesGroup> cached))
                {
                    var page = cached.AllResults.Skip(skip).Take(DefaultTake).ToList();
                    string nextToken = (skip + DefaultTake) < cached.AllResults.Count ? (pageNumber + 1).ToString() : null;
                    return new SymbolReferencesResponse
                    {
                        Results = page,
                        SymbolName = symbolName,
                        TotalReferences = cached.AllResults.Sum(r => r.Matches.Count),
                        NextPageToken = nextToken,
                        Success = true,
                        ErrorMessage = null
                    };
                }

                var fullResponse = await ExecuteCoreAsync(symbolName, cancellationToken);

                if (fullResponse.Success && fullResponse.Results.Count > 0)
                {
                    var cacheEntry = new CachedToolResults<FileReferencesGroup>
                    {
                        AllResults = fullResponse.Results,
                        ItemsScanned = fullResponse.Results.Count
                    };
                    _searchCache.Set(cacheKey, "", cacheEntry);
                    var page = cacheEntry.AllResults.Skip(skip).Take(DefaultTake).ToList();
                    string nextToken = (skip + DefaultTake) < cacheEntry.AllResults.Count ? (pageNumber + 1).ToString() : null;
                    return new SymbolReferencesResponse
                    {
                        Results = page,
                        SymbolName = symbolName,
                        TotalReferences = cacheEntry.AllResults.Sum(r => r.Matches.Count),
                        NextPageToken = nextToken,
                        Success = true,
                        ErrorMessage = null
                    };
                }

                return fullResponse;
            }
            catch (Exception ex)
            {
                return new SymbolReferencesResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    SymbolName = parameters?.TryGetValue("symbol_name", out var sn) == true ? sn?.ToString() : "",
                    Results = new List<FileReferencesGroup>(),
                    TotalReferences = 0,
                    NextPageToken = null
                };
            }
        }

        private string BuildCacheKey(string symbolName)
        {
            return symbolName ?? string.Empty;
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            if (parameters == null) return "Finding references... ";

            var symbolName = parameters.TryGetValue("symbol_name", out var s) ? s?.ToString() : "";
            var pageToken = parameters.TryGetValue("page_token", out var pt) ? pt?.ToString() : null;

            var message = $"Finding references to '{symbolName}'";
            if (!string.IsNullOrEmpty(pageToken))
                message += $" (page {pageToken})";

            message += "... ";
            return message;
        }

        public string GetCompletionMessage(object result)
        {
            var symbolResult = (SymbolReferencesResponse)result;
            if (!symbolResult.Success)
            {
                return $"Error: {symbolResult.ErrorMessage}";
            }
            int pageReferences = symbolResult.Results.Sum(r => r.Matches.Count);
            return $"Found {pageReferences} references on this page (total: {symbolResult.TotalReferences} references).";
        }

        private string ExtractAndValidateParameters(string symbolName)
        {
            if (string.IsNullOrEmpty(symbolName))
                throw new ArgumentException("Parameter 'symbol_name' cannot be empty.", nameof(symbolName));

            return symbolName;
        }

        private (string symbolName, string pageToken) ExtractAndValidateParametersFromDict(Dictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("symbol_name", out object symbolNameObj) || !(symbolNameObj is string))
                throw new ArgumentException("Parameter 'symbol_name' is required and must be a string.", nameof(parameters));

            var symbolName = (string)symbolNameObj;
            var pageToken = parameters.TryGetValue("page_token", out object tokenObj) ? tokenObj as string : null;

            return (symbolName, pageToken);
        }

        public class ReferenceItem
        {
            [JsonProperty("line")]
            public int LineNumber { get; set; }

            [JsonProperty("text")]
            public string LineText { get; set; }
        }

        public class FileReferencesGroup
        {
            [JsonProperty("file")]
            public string FilePath { get; set; }

            [JsonProperty("matches")]
            public List<ReferenceItem> Matches { get; set; }
        }

        public class SymbolReferencesResponse
        {
            [JsonProperty("symbol_name")]
            public string SymbolName { get; set; }

            [JsonProperty("total_references")]
            public int TotalReferences { get; set; }

            [JsonProperty("results")]
            public List<FileReferencesGroup> Results { get; set; }

            [JsonProperty("next_page_token")]
            public string NextPageToken { get; set; }

            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("error_message")]
            public string ErrorMessage { get; set; }
        }
    }
}
