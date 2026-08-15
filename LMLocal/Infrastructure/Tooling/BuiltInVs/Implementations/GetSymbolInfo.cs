using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Tool for retrieving navigation information about a C# symbol:
    /// all declaration sites (definition locations) and optionally usages (references) for the first matching symbol.
    /// This is a navigation tool — use inspect_type to see members, fields, properties, and dependencies.
    /// </summary>
    internal interface IGetSymbolInfo : IBuiltInTool
    {
    }
    internal class GetSymbolInfo : IGetSymbolInfo
    {
        private readonly IPathResolver _pathResolver;
        private readonly ISearchResultCache _searchCache;
        private readonly IFileSystem _fileSystem;
        private const int MaxTotalReferences = 5000;
        private const int DefaultPageSize = 50;
        private const int MaxPageSize = 200;
        private const int MaxDefinitions = 100;

        public string ToolName => "get_symbol_info";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.ReadOnly;

        public GetSymbolInfo(IPathResolver pathResolver, ISearchResultCache searchCache, IFileSystem fileSystem)
        {
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _searchCache = searchCache ?? throw new ArgumentNullException(nameof(searchCache));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Retrieves navigation information for a C# symbol: all declaration sites (file path, line, column, full signature including parameters, modifiers, namespace) and optionally usages/references (file path, line, line text, symbol kind). Does NOT return method bodies, fields, properties, base classes, or dependencies — use inspect_type for structural analysis. Case‑insensitive, supports overloads/partials. Use optional file_path to limit search to a specific file. References limited to 5000, paginated by page size (default 50).",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "symbol_name", new ToolDetails { Type = "string", Description = "The name of the symbol (case‑insensitive)." } },
                        { "file_path", new ToolDetails { Type = "string", Description = "Limit search to a specific file (relative or absolute path). If provided, only declarations in this file are returned." } },
                        { "include_references", new ToolDetails { Type = "boolean", Description = "Whether to include usages (references). Default true." } },
                        { "max_references", new ToolDetails { Type = "integer", Description = "Number of references per page (page size). Default 50, max 200." } },
                        { "page_token", new ToolDetails { Type = "string", Description = "Pagination token for the next page of references." } }
                    },
                    Required = new List<string> { "symbol_name" }
                }
            };
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            try
            {
                var (symbolName, filePathParam, includeReferences, pageSize, pageToken, error) = ExtractAndValidateParameters(parameters);
                if (!string.IsNullOrEmpty(error))
                    return Error(error, symbolName);

                int pageNumber = string.IsNullOrEmpty(pageToken) || !int.TryParse(pageToken, out var pn) ? 0 : pn;
                int skip = pageNumber * pageSize;

                string cacheKey = BuildCacheKey(symbolName, filePathParam, includeReferences, pageSize);
                if (_searchCache.TryGet(cacheKey, "", out CachedToolResults<SymbolInfo> cached))
                {
                    var cachedInfo = cached.AllResults.FirstOrDefault();
                    if (cachedInfo != null)
                    {
                        var pagedRefs = cachedInfo.References?.Skip(skip).Take(pageSize).ToList() ?? new List<ReferenceItem>();
                        string nextToken = (skip + pageSize) < (cachedInfo.References?.Count ?? 0) ? (pageNumber + 1).ToString() : null;
                        return new SymbolInfoResponse
                        {
                            Success = true,
                            SymbolName = symbolName,
                            Definitions = cachedInfo.Definitions ?? new List<DefinitionItem>(),
                            References = pagedRefs,
                            TotalReferences = cachedInfo.References?.Count ?? 0,
                            NextPageToken = nextToken,
                            HasMoreResults = cachedInfo.HasMoreResults
                        };
                    }
                }

                var fullResult = await ExecuteCoreAsync(symbolName, filePathParam, includeReferences, cancellationToken);
                if (!fullResult.Success)
                    return fullResult;

                if (fullResult.Success)
                {
                    var cacheEntry = new CachedToolResults<SymbolInfo>
                    {
                        AllResults = new List<SymbolInfo> {
                            new SymbolInfo {
                                Definitions = fullResult.Definitions ?? new List<DefinitionItem>(),
                                References = fullResult.References ?? new List<ReferenceItem>(),
                                HasMoreResults = fullResult.HasMoreResults
                            }
                        },
                        ItemsScanned = 1
                    };
                    _searchCache.Set(cacheKey, "", cacheEntry);
                }

                var pagedRefsResult = fullResult.References?.Skip(skip).Take(pageSize).ToList() ?? new List<ReferenceItem>();
                string nextTokenResult = (skip + pageSize) < (fullResult.References?.Count ?? 0) ? (pageNumber + 1).ToString() : null;

                return new SymbolInfoResponse
                {
                    Success = true,
                    SymbolName = symbolName,
                    Definitions = fullResult.Definitions ?? new List<DefinitionItem>(),
                    References = pagedRefsResult,
                    TotalReferences = fullResult.References?.Count ?? 0,
                    NextPageToken = nextTokenResult,
                    HasMoreResults = fullResult.HasMoreResults
                };
            }
            catch (OperationCanceledException)
            {
                return Error("Operation was cancelled.", null);
            }
            catch (Exception ex)
            {
                return Error(ex.Message, null);
            }
        }

        private async Task<SymbolInfoResponse> ExecuteCoreAsync(string symbolName, string filePathParam, bool includeReferences, CancellationToken cancellationToken)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                var componentModel = (IComponentModel)ServiceProvider.GlobalProvider.GetService(typeof(SComponentModel));
                if (componentModel == null)
                    return Error("Component model is not available", symbolName);

                var workspace = componentModel.GetService<VisualStudioWorkspace>();
                if (workspace == null)
                    return Error("Visual Studio workspace is not available", symbolName);

                var solutionSnapshot = workspace.CurrentSolution;
                if (solutionSnapshot == null)
                    return Error("No solution is currently open", symbolName);

                var solutionDir = Path.GetDirectoryName(solutionSnapshot.FilePath) ?? string.Empty;

                string targetFilePath = null;
                if (!string.IsNullOrEmpty(filePathParam))
                {
                    if (!_pathResolver.TryResolveFilePath(filePathParam, solutionDir, out string absolutePath))
                        return Error($"Cannot resolve file path: {filePathParam}", symbolName);
                    if (!_fileSystem.FileExists(absolutePath))
                        return Error($"File not found: {absolutePath}", symbolName);
                    targetFilePath = absolutePath;
                }

                return await Task.Run(async () =>
                {
                    try
                    {
                        var projects = solutionSnapshot.Projects.ToList();
                        var allDefinitions = new List<DefinitionItem>();
                        var seenLocations = new HashSet<string>(StringComparer.Ordinal);
                        var foundSymbols = new List<ISymbol>();

                        foreach (var project in projects)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!project.SupportsCompilation) continue;

                            var allSymbols = await SymbolFinder.FindDeclarationsAsync(
                                project,
                                symbolName,
                                ignoreCase: true,
                                cancellationToken: cancellationToken
                            );

                            var exactMatches = allSymbols.Where(s => string.Equals(s.Name, symbolName, StringComparison.Ordinal)).ToList();
                            var symbolsToProcess = exactMatches.Any() ? exactMatches : allSymbols;

                            foreach (var symbol in symbolsToProcess)
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                if (!string.IsNullOrEmpty(targetFilePath))
                                {
                                    bool hasDeclarationInFile = symbol.DeclaringSyntaxReferences
                                        .Any(r => r.SyntaxTree?.FilePath == targetFilePath);
                                    if (!hasDeclarationInFile)
                                        continue;
                                }

                                foundSymbols.Add(symbol);

                                var syntaxRefs = symbol.DeclaringSyntaxReferences;
                                foreach (var syntaxRef in syntaxRefs)
                                {
                                    var syntaxNode = await syntaxRef.GetSyntaxAsync(cancellationToken);
                                    var location = syntaxNode.GetLocation();
                                    var filePath = location.SourceTree?.FilePath;
                                    if (string.IsNullOrEmpty(filePath))
                                        continue;

                                    if (!string.IsNullOrEmpty(targetFilePath) && filePath != targetFilePath)
                                        continue;

                                    var lineSpan = location.GetLineSpan();
                                    int line = lineSpan.StartLinePosition.Line + 1;
                                    int column = lineSpan.StartLinePosition.Character + 1;

                                    var locationKey = $"{filePath}:{line}:{column}";
                                    if (!seenLocations.Add(locationKey))
                                        continue;

                                    var item = new DefinitionItem
                                    {
                                        FilePath = _pathResolver.TryGetRelativePath(filePath, solutionDir, out var rel) ? rel : filePath,
                                        Line = line,
                                        Column = column,
                                        SymbolKind = symbol.Kind.ToString(),
                                        FullSignature = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                        ContainingNamespace = symbol.ContainingNamespace?.ToString() ?? "",
                                        Modifiers = GetModifiers(symbol)
                                    };

                                    if (symbol is IMethodSymbol method)
                                    {
                                        item.Parameters = method.Parameters.Select(p => new ParameterInfo
                                        {
                                            Name = p.Name,
                                            Type = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                                        }).ToList();
                                    }

                                    allDefinitions.Add(item);
                                    if (allDefinitions.Count >= MaxDefinitions)
                                        break;
                                }
                                if (allDefinitions.Count >= MaxDefinitions)
                                    break;
                            }
                            if (allDefinitions.Count >= MaxDefinitions)
                                break;
                        }

                        var response = new SymbolInfoResponse
                        {
                            Success = true,
                            SymbolName = symbolName,
                            Definitions = allDefinitions,
                            References = new List<ReferenceItem>(),
                            TotalReferences = 0,
                            NextPageToken = null,
                            HasMoreResults = false
                        };

                        if (includeReferences && foundSymbols.Any())
                        {
                            ISymbol targetSymbol = foundSymbols.FirstOrDefault(s => string.Equals(s.Name, symbolName, StringComparison.Ordinal))
                                                  ?? foundSymbols.First();

                            var references = await SymbolFinder.FindReferencesAsync(
                                targetSymbol,
                                solutionSnapshot,
                                cancellationToken: cancellationToken
                            );

                            var allRefItems = new List<ReferenceItem>();
                            var seenRefs = new HashSet<string>(StringComparer.Ordinal);
                            bool hasMoreResults = false;
                            int totalRefs = 0;

                            var refsByDocument = new Dictionary<DocumentId, List<(Location location, ISymbol symbol)>>();

                            foreach (var reference in references)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                var symbolFullName = reference.Definition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                                var symbolKind = reference.Definition.Kind.ToString();

                                foreach (var location in reference.Locations)
                                {
                                    var document = location.Document;
                                    if (document == null || string.IsNullOrEmpty(document.FilePath))
                                        continue;

                                    var docId = document.Id;
                                    var loc = location.Location;
                                    var textSpan = loc.SourceSpan;

                                    var key = $"{docId}:{textSpan.Start}:{textSpan.Length}";
                                    if (!seenRefs.Add(key))
                                        continue;

                                    if (!refsByDocument.ContainsKey(docId))
                                        refsByDocument[docId] = new List<(Location, ISymbol)>();
                                    refsByDocument[docId].Add((loc, reference.Definition));

                                    totalRefs++;
                                    if (totalRefs >= MaxTotalReferences)
                                    {
                                        hasMoreResults = true;
                                        break;
                                    }
                                }
                                if (hasMoreResults) break;
                            }

                            foreach (var group in refsByDocument)
                            {
                                var document = solutionSnapshot.GetDocument(group.Key);
                                if (document == null) continue;

                                var sourceText = await document.GetTextAsync(cancellationToken);
                                var filePath = document.FilePath;
                                string relativePath = filePath;
                                if (!string.IsNullOrEmpty(solutionDir) && _pathResolver.TryGetRelativePath(filePath, solutionDir, out var relPath))
                                    relativePath = relPath;

                                foreach (var (location, symbol) in group.Value)
                                {
                                    var textSpan = location.SourceSpan;
                                    var lineSpan = sourceText.Lines.GetLinePositionSpan(textSpan);
                                    int lineNumber = lineSpan.Start.Line + 1;
                                    string lineText = sourceText.Lines[lineSpan.Start.Line].ToString().Trim();

                                    allRefItems.Add(new ReferenceItem
                                    {
                                        FilePath = relativePath,
                                        LineNumber = lineNumber,
                                        LineText = lineText,
                                        SymbolFullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                        SymbolKind = symbol.Kind.ToString()
                                    });
                                }
                            }

                            response.References = allRefItems;
                            response.TotalReferences = totalRefs;
                            response.HasMoreResults = hasMoreResults;
                        }

                        return response;
                    }
                    catch (OperationCanceledException)
                    {
                        return Error("Operation was cancelled.", symbolName);
                    }
                    catch (Exception ex)
                    {
                        return Error(ex.Message, symbolName);
                    }
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                return Error(ex.Message, symbolName);
            }
        }

        private static string GetModifiers(ISymbol symbol)
        {
            var modifiers = new List<string>();
            if (symbol.IsStatic) modifiers.Add("static");
            if (symbol.IsAbstract) modifiers.Add("abstract");
            if (symbol.IsVirtual) modifiers.Add("virtual");
            if (symbol.IsOverride) modifiers.Add("override");
            if (symbol.IsSealed) modifiers.Add("sealed");
            switch (symbol.DeclaredAccessibility)
            {
                case Accessibility.Public: modifiers.Add("public"); break;
                case Accessibility.Private: modifiers.Add("private"); break;
                case Accessibility.Protected: modifiers.Add("protected"); break;
                case Accessibility.Internal: modifiers.Add("internal"); break;
                case Accessibility.ProtectedAndInternal: modifiers.Add("protected internal"); break;
                case Accessibility.ProtectedOrInternal: modifiers.Add("protected internal"); break;
            }
            return string.Join(" ", modifiers);
        }

        private static SymbolInfoResponse Error(string message, string symbolName)
        {
            return new SymbolInfoResponse
            {
                Success = false,
                ErrorMessage = message,
                SymbolName = symbolName ?? "",
                Definitions = new List<DefinitionItem>(),
                References = new List<ReferenceItem>(),
                TotalReferences = 0,
                NextPageToken = null,
                HasMoreResults = false
            };
        }

        private string BuildCacheKey(string symbolName, string filePath, bool includeReferences, int pageSize)
        {
            return $"{symbolName}||{filePath ?? ""}||{includeReferences}||{pageSize}";
        }

        private (string symbolName, string filePath, bool includeReferences, int pageSize, string pageToken, string error) ExtractAndValidateParameters(
            Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return (null, null, true, DefaultPageSize, null, "Parameters are required.");

            if (!parameters.TryGetValue("symbol_name", out object symbolObj) || !(symbolObj is string symbolName))
                return (null, null, true, DefaultPageSize, null, "Parameter 'symbol_name' is required and must be a string.");

            if (string.IsNullOrWhiteSpace(symbolName))
                return (null, null, true, DefaultPageSize, null, "Parameter 'symbol_name' cannot be empty.");

            string filePath = null;
            if (parameters.TryGetValue("file_path", out object fpObj) && fpObj is string fp && !string.IsNullOrEmpty(fp))
                filePath = fp;

            bool includeReferences = true;
            if (parameters.TryGetValue("include_references", out object includeObj) && includeObj is bool includeBool)
                includeReferences = includeBool;

            int pageSize = DefaultPageSize;
            if (parameters.TryGetValue("max_references", out object maxObj) && int.TryParse(maxObj.ToString(), out int maxVal))
                pageSize = Math.Min(Math.Max(maxVal, 1), MaxPageSize);

            string pageToken = parameters.TryGetValue("page_token", out object tokenObj) ? tokenObj as string : null;

            return (symbolName, filePath, includeReferences, pageSize, pageToken, null);
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            var symbolName = parameters?.TryGetValue("symbol_name", out var s) == true ? s?.ToString() : "";
            var filePath = parameters?.TryGetValue("file_path", out var f) == true ? f?.ToString() : null;
            if (!string.IsNullOrEmpty(filePath))
                return $"Getting symbol info for '{symbolName}' in '{filePath}'... ";
            return $"Getting symbol info for '{symbolName}'... ";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is SymbolInfoResponse resp)
            {
                if (!resp.Success)
                    return $"Failed: {resp.ErrorMessage}";

                string msg = $"{resp.Definitions?.Count ?? 0} definition(s) found";
                if (resp.References?.Count > 0)
                    msg += $", {resp.TotalReferences} references";
                if (resp.HasMoreResults)
                    msg += " (more results exist, limit 5000 reached)";
                return msg;
            }
            return "Symbol info retrieval completed.";
        }

        #region DTOs

        public class ParameterInfo
        {
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("type")]
            public string Type { get; set; }
        }

        public class DefinitionItem
        {
            [JsonProperty("file_path")]
            public string FilePath { get; set; }
            [JsonProperty("line")]
            public int Line { get; set; }
            [JsonProperty("column")]
            public int Column { get; set; }
            [JsonProperty("symbol_kind")]
            public string SymbolKind { get; set; }
            [JsonProperty("full_signature")]
            public string FullSignature { get; set; }
            [JsonProperty("containing_namespace")]
            public string ContainingNamespace { get; set; }
            [JsonProperty("modifiers")]
            public string Modifiers { get; set; }
            [JsonProperty("parameters", NullValueHandling = NullValueHandling.Ignore)]
            public List<ParameterInfo> Parameters { get; set; }
        }

        public class ReferenceItem
        {
            [JsonProperty("file_path")]
            public string FilePath { get; set; }
            [JsonProperty("line")]
            public int LineNumber { get; set; }
            [JsonProperty("text")]
            public string LineText { get; set; }
            [JsonProperty("symbol_full_name")]
            public string SymbolFullName { get; set; }
            [JsonProperty("symbol_kind")]
            public string SymbolKind { get; set; }
        }

        public class SymbolInfo
        {
            public List<DefinitionItem> Definitions { get; set; } = new List<DefinitionItem>();
            public List<ReferenceItem> References { get; set; } = new List<ReferenceItem>();
            public bool HasMoreResults { get; set; }
        }

        public class SymbolInfoResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }
            [JsonProperty("error_message")]
            public string ErrorMessage { get; set; }
            [JsonProperty("symbol_name")]
            public string SymbolName { get; set; }
            [JsonProperty("definitions")]
            public List<DefinitionItem> Definitions { get; set; }
            [JsonProperty("definition")]
            public DefinitionItem Definition => Definitions?.FirstOrDefault();
            [JsonProperty("references")]
            public List<ReferenceItem> References { get; set; }
            [JsonProperty("total_references")]
            public int TotalReferences { get; set; }
            [JsonProperty("next_page_token")]
            public string NextPageToken { get; set; }
            [JsonProperty("has_more_results")]
            public bool HasMoreResults { get; set; }
        }

        #endregion
    }
}
