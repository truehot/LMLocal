using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Tool interface for finding references to a code symbol across the current Visual Studio solution.
    /// </summary>
    internal interface IFindSymbolReferences : IBuiltInTool
    {
        Task<object> ExecuteAsync(
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken = default);
    }

    internal class FindSymbolReferences : IFindSymbolReferences
    {
        private readonly IPathResolver _pathResolver;
        private const int MaxSymbolsToProcess = 5;
        private const int MaxTotalReferences = 50;

        public string ToolName => "Find_Symbol_References";

        public FindSymbolReferences(IPathResolver pathResolver)
        {
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        }

        public async Task<object> ExecuteAsync(
            string symbolName,
            CancellationToken cancellationToken = default)
        {
            symbolName = ExtractAndValidateParameters(symbolName);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var componentModel = (IComponentModel)ServiceProvider.GlobalProvider.GetService(typeof(SComponentModel));
            if (componentModel == null)
                throw new InvalidOperationException("Component model is not available");

            var workspace = componentModel.GetService<VisualStudioWorkspace>();
            if (workspace == null)
                throw new InvalidOperationException("Visual Studio workspace is not available");

            var solutionSnapshot = workspace.CurrentSolution;
            if (solutionSnapshot == null)
                throw new InvalidOperationException("No solution is currently open");

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
                    HasMoreResults = hasMoreResults
                };
            }, cancellationToken).ConfigureAwait(false);

            return result;
        }

        public async Task<object> ExecuteAsync(
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken = default)
        {
            var symbolName = ExtractAndValidateParametersFromDict(parameters);
            return await ExecuteAsync(symbolName, cancellationToken);
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = $"Finds all references of a specific code symbol (class, method, variable, or property) across the entire Visual Studio solution. Returns a list of file groups with matched references, including line numbers and the exact text of each line where the symbol is used. Search is limited to the first {MaxSymbolsToProcess} matching symbols and {MaxTotalReferences} total references. Use this to trace where a specific function is invoked or where a class is instantiated.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "symbol_name", new ToolDetails { Type = "string", Description = "The exact name of the code symbol (e.g., 'PaymentService', 'ProcessOrder', or '_logger') to find references for." } }
                    },
                    Required = new List<string> { "symbol_name" }
                }
            };
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            if (parameters == null) return "Finding references... ";

            var symbolName = parameters.TryGetValue("symbol_name", out var s) ? s?.ToString() : "";
            return $"Finding references to '{symbolName}'... ";
        }

        public string GetCompletionMessage(object result)
        {
            var symbolResult = (SymbolReferencesResponse)result;
            return $"Found {symbolResult.TotalReferences} references.";
        }

        private string ExtractAndValidateParameters(string symbolName)
        {
            if (string.IsNullOrEmpty(symbolName))
                throw new ArgumentException("Parameter 'symbol_name' cannot be empty.", nameof(symbolName));

            return symbolName;
        }

        private string ExtractAndValidateParametersFromDict(Dictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("symbol_name", out object symbolNameObj) || !(symbolNameObj is string))
                throw new ArgumentException("Parameter 'symbol_name' is required and must be a string.", nameof(parameters));

            var symbolName = (string)symbolNameObj;

            if (string.IsNullOrEmpty(symbolName))
                throw new ArgumentException("Parameter 'symbol_name' cannot be empty.", nameof(symbolName));

            return symbolName;
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

            [JsonProperty("has_more_results")]
            public bool HasMoreResults { get; set; }
        }
    }
}
