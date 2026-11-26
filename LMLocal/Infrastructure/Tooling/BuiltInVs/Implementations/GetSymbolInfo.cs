using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
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
    /// all declaration sites (definition locations) of every matching symbol and optionally usages (references)
    /// computed for one selected symbol (the first symbol declared in file_path if provided, otherwise an
    /// exact case-sensitive name match, else the first case-insensitive match).
    /// </summary>
    internal interface IGetSymbolInfo : IBuiltInTool
    {
    }
    internal class GetSymbolInfo : IGetSymbolInfo
    {
        private readonly IPathResolver _pathResolver;
        private readonly ISearchResultCache _searchCache;
        private readonly IFileSystem _fileSystem;
        private readonly IMissingFileHint _missingFileHint;
        private const int MaxTotalReferences = 5000;
        private const int DefaultPageSize = 50;
        private const int MaxPageSize = 200;
        private const int MaxDefinitions = 100;
        internal const int MaxSymbolNames = 10;
        internal const string SymbolNameParameter = "symbol_name";
        internal const string SymbolNamesParameter = "symbol_names";

        public string ToolName => "get_symbol_info";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.ReadOnly;

        public GetSymbolInfo(IPathResolver pathResolver, ISearchResultCache searchCache, IFileSystem fileSystem, IMissingFileHint missingFileHint = null)
        {
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _searchCache = searchCache ?? throw new ArgumentNullException(nameof(searchCache));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _missingFileHint = missingFileHint ?? new MissingFileHint(pathResolver, fileSystem);
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Use this tool to find where a C# symbol (class, interface, struct, enum, method, property, field) is declared and every place it is used. Prefer it over search_file_content for a C# identifier: it resolves the symbol through the compiler, so matches in comments, strings and unrelated same-named text are not returned. " +
                    $"Several symbols whose declarations you need: pass them all in 'symbol_names' (up to {MaxSymbolNames}) in ONE call, not one call per symbol; the response has 'symbols', one block per name in request order with its 'symbol_name' and 'definitions' (or its own success=false and error_message). 'symbol_names' returns definitions only: for usages call again with one 'symbol_name'. " +
                    "Definitions: declaration sites of every matching symbol (file, line, text of that line, end_line, column, full signature with parameters, modifiers, namespace), deduplicated, up to 100; not paginated.line..end_line is the whole declaration (attributes through the closing brace, or the ';' line of an expression-bodied member): to read a whole method or property, call read_file_lines with exactly that range. References (default included): usages of ONE symbol (file, line, line text, symbol kind), up to 5000 total, paginated (page size default 50, max 200) via page_token. Reference target: if file_path is set, the first symbol declared in that file, folder or project;otherwise the first exact case-sensitive name match, else the first case-insensitive match. For overloads/partials, definitions include all declarations, but references cover only the selected symbol; when several different symbols share the name and file_path is not set, 'note' says whose references these are.Name matching prefers exact case-sensitive matches and falls back to case-insensitive only if no exact match exists. file_path restricts only the returned definitions to that place; references are still searched across the whole solution. Does NOT return method bodies, fields, properties, base classes, or dependencies. Lines are 1-indexed. Independent: several calls can go in one response.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { SymbolNameParameter, new ToolDetails { Type = "string", Description = "The name of the symbol to look up, like 'ToolCalls'. 'Type.Member' (like 'ChatMessage.ToolCalls') selects the member of that type. Exact case-sensitive matches are preferred; if none exist, case-insensitive matches are used. Required unless 'symbol_names' is passed." } },
                        { "file_path", new ToolDetails { Type = "string", Description = "Restricts returned definitions to this file, folder or project of the solution (relative or absolute path, or a project name): only declaration sites inside it are returned. Also selects the reference target — usages are computed for the first symbol declared there. References themselves are still searched across the entire solution." } },
                        { "include_references", new ToolDetails { Type = "boolean", Description = "Whether to include usages (references). Default true." } },
                        { "max_references", new ToolDetails { Type = "integer", Description = "Number of references per page (page size). Default 50, max 200. Total references never exceed 5000 regardless of page size." } },
                        { "page_token", new ToolDetails { Type = "string", Description = "The value of next_page_token from the previous response, copied exactly; never a value that no response returned. The page is taken from that result, so symbol_name and file_path may be left out. Pass 'last' (together with the same symbol_name and file_path) to get the last page of references, 'first' for the first." } },
                        {
                            SymbolNamesParameter, new ToolDetails
                            {
                                Type = "array",
                                Description = $"Several symbol names to look up in one call (up to {MaxSymbolNames}), each like 'symbol_name'; definitions only (no references). file_path, if set, restricts the definitions of every name.",
                                Items = new ToolDetails { Type = "string" }
                            }
                        }
                    },
                    Required = new List<string>()
                }
            };
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            object namesObj = null;
            parameters?.TryGetValue(SymbolNamesParameter, out namesObj);
            if (!LiteralListParameter.IsMissing(namesObj))
                return await ExecuteManyAsync(parameters, namesObj, cancellationToken);

            try
            {
                var selection = PageSelection.From(parameters);
                if (selection.Error != null)
                    return Error(selection.Error, null);

                if (selection.IsCursor)
                {
                    SymbolInfo entry = null;
                    bool found = _searchCache.TryGetById(selection.Id, out CachedToolResults<SymbolInfo> byId)
                        && (entry = byId.AllResults?.FirstOrDefault()) != null
                        && !string.IsNullOrEmpty(entry.SymbolName);
                    string callName = parameters != null && parameters.TryGetValue(SymbolNameParameter, out object nameObj) && nameObj is string ns && !string.IsNullOrWhiteSpace(ns) ? ns : null;
                    selection.Resolve(found, found && (callName == null || string.Equals(callName, entry.SymbolName, StringComparison.Ordinal)));

                    if (selection.UseEntry)
                    {
                        int entryPageSize = ReadPageSize(parameters);
                        return BuildPage(entry, entry.SymbolName, selection.Id, selection.Offset(entryPageSize, entry.References?.Count ?? 0), entryPageSize, null);
                    }
                }

                var (symbolName, filePathParam, includeReferences, pageSize, _, error) = ExtractAndValidateParameters(parameters);
                if (!string.IsNullOrEmpty(error))
                    return Error(selection.WithPagingHint(error), symbolName);

                string cacheKey = BuildCacheKey(symbolName, filePathParam, includeReferences);
                if (_searchCache.TryGet(cacheKey, "", out CachedToolResults<SymbolInfo> cached))
                {
                    var cachedInfo = cached.AllResults.FirstOrDefault();
                    if (cachedInfo != null)
                        return BuildPage(cachedInfo, symbolName, _searchCache.GetId(cacheKey), selection.Offset(pageSize, cachedInfo.References?.Count ?? 0), pageSize, selection.Warning);
                }

                var fullResult = await ExecuteCoreAsync(symbolName, filePathParam, includeReferences, cancellationToken);
                if (!fullResult.Success)
                    return fullResult;

                var info = new SymbolInfo
                {
                    SymbolName = symbolName,
                    Definitions = fullResult.Definitions ?? new List<DefinitionItem>(),
                    References = fullResult.References ?? new List<ReferenceItem>(),
                    HasMoreResults = fullResult.HasMoreResults,
                    Note = fullResult.Note
                };
                _searchCache.Set(cacheKey, "", new CachedToolResults<SymbolInfo>
                {
                    AllResults = new List<SymbolInfo> { info },
                    ItemsScanned = 1
                });

                return BuildPage(info, symbolName, _searchCache.GetId(cacheKey), selection.Offset(pageSize, info.References?.Count ?? 0), pageSize, selection.Warning);
            }
            catch (OperationCanceledException)
            {
                return Error("Operation was cancelled.", null);
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("GetSymbolInfo: symbol lookup failed", ex);
                return Error(ex.Message, null);
            }
        }

        /// <summary>One page of the references of a cached (or just computed) symbol; the next_page_token is a cursor token of the same entry.</summary>
        private static SymbolInfoResponse BuildPage(SymbolInfo info, string symbolName, string cacheId, int skip, int pageSize, string warning)
        {
            var pagedRefs = info.References?.Skip(skip).Take(pageSize).ToList() ?? new List<ReferenceItem>();
            int total = info.References?.Count ?? 0;
            string nextToken = ToolPagination.NextToken(skip + pagedRefs.Count, total, cacheId);
            return new SymbolInfoResponse
            {
                Success = true,
                SymbolName = symbolName,
                Definitions = info.Definitions ?? new List<DefinitionItem>(),
                References = pagedRefs,
                TotalReferences = total,
                Showing = ToolPagination.Describe(skip, pagedRefs.Count, total, nextToken != null, pageSize),
                NextPageToken = nextToken,
                HasMoreResults = nextToken != null || info.HasMoreResults,
                Note = info.Note,
                Warning = warning
            };
        }

        /// <summary>max_references as the page size (default and bounds as in <see cref="ExtractAndValidateParameters"/>).</summary>
        private static int ReadPageSize(Dictionary<string, object> parameters)
        {
            if (parameters != null && parameters.TryGetValue("max_references", out object maxObj) && maxObj != null && int.TryParse(maxObj.ToString(), out int maxVal))
                return Math.Min(Math.Max(maxVal, 1), MaxPageSize);
            return DefaultPageSize;
        }

        /// <summary>'symbol_names': definitions of each name, one block per name in request order; no references.</summary>
        private async Task<object> ExecuteManyAsync(Dictionary<string, object> parameters, object namesObj, CancellationToken cancellationToken)
        {
            var (names, error) = ExtractSymbolNames(parameters, namesObj);
            if (error != null)
                return new SymbolInfoMultiResponse { Success = false, ErrorMessage = error, Symbols = new List<SymbolDefinitionsBlock>() };

            string filePath = parameters.TryGetValue("file_path", out object fpObj) && fpObj is string fp && !string.IsNullOrEmpty(fp) ? fp : null;

            try
            {
                var blocks = new List<SymbolDefinitionsBlock>();
                foreach (var name in names)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var definitions = await GetDefinitionsAsync(name, filePath, cancellationToken);
                    blocks.Add(new SymbolDefinitionsBlock
                    {
                        SymbolName = name,
                        Success = definitions.Success,
                        ErrorMessage = definitions.Success ? null : definitions.ErrorMessage,
                        Note = definitions.Success ? definitions.Note : null,
                        Definitions = definitions.Definitions ?? new List<DefinitionItem>()
                    });
                }

                bool any = blocks.Any(b => b.Success);
                return new SymbolInfoMultiResponse
                {
                    Success = any,
                    ErrorMessage = any ? null : "No symbol was looked up: see error_message of each block.",
                    Warning = BuildNoReferencesWarning(parameters),
                    Symbols = blocks
                };
            }
            catch (OperationCanceledException)
            {
                return new SymbolInfoMultiResponse { Success = false, ErrorMessage = "Operation was cancelled.", Symbols = new List<SymbolDefinitionsBlock>() };
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("GetSymbolInfo: symbol_names lookup failed", ex);
                return new SymbolInfoMultiResponse { Success = false, ErrorMessage = ex.Message, Symbols = new List<SymbolDefinitionsBlock>() };
            }
        }

        /// <summary>Definitions of one name without references, through the same cache entry as a single call with include_references=false.</summary>
        private async Task<SymbolInfoResponse> GetDefinitionsAsync(string symbolName, string filePath, CancellationToken cancellationToken)
        {
            string cacheKey = BuildCacheKey(symbolName, filePath, false);
            if (_searchCache.TryGet(cacheKey, "", out CachedToolResults<SymbolInfo> cached))
            {
                var cachedInfo = cached.AllResults.FirstOrDefault();
                if (cachedInfo != null)
                    return new SymbolInfoResponse { Success = true, SymbolName = symbolName, Definitions = cachedInfo.Definitions ?? new List<DefinitionItem>(), Note = cachedInfo.Note };
            }

            var result = await ExecuteCoreAsync(symbolName, filePath, false, cancellationToken);
            if (result.Success)
            {
                _searchCache.Set(cacheKey, "", new CachedToolResults<SymbolInfo>
                {
                    AllResults = new List<SymbolInfo> { new SymbolInfo { Definitions = result.Definitions ?? new List<DefinitionItem>(), Note = result.Note } },
                    ItemsScanned = 1
                });
            }
            return result;
        }

        /// <summary>
        /// Names of a 'symbol_names' call: a top-level symbol_name first, then the list; exact duplicates dropped
        /// (names are case-sensitive). page_token pages references, which this mode does not return.
        /// </summary>
        internal static (List<string> names, string error) ExtractSymbolNames(Dictionary<string, object> parameters, object namesObj)
        {
            object nameObj = null;
            parameters?.TryGetValue(SymbolNameParameter, out nameObj);
            if (nameObj != null && !(nameObj is string))
                return (null, $"Parameter '{SymbolNameParameter}' must be a string.");

            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            LiteralListParameter.Add(nameObj as string, names, seen);
            if (!LiteralListParameter.TryAdd(namesObj, names, seen))
                return (null, $"Parameter '{SymbolNamesParameter}' must be an array of strings.");

            if (names.Count == 0)
                return (names, $"Parameter '{SymbolNamesParameter}' has no non-empty name.");
            if (names.Count > MaxSymbolNames)
                return (names, $"At most {MaxSymbolNames} symbol names per call (got {names.Count}). Split them into several calls.");
            if (ToolPagination.ReadToken(parameters) != null)
                return (names, $"'{ToolPagination.TokenParameter}' pages the references of one symbol: call again with '{SymbolNameParameter}' and the next_page_token.");

            return (names, null);
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

                var (memberName, containerName) = SplitQualifiedName(symbolName);

                string targetFilePath = null;
                string scopeFolder = null;
                string scopeProject = null;
                string scopeNote = null;
                if (!string.IsNullOrEmpty(filePathParam))
                {
                    var namedProject = solutionSnapshot.Projects.FirstOrDefault(p => string.Equals(p.Name, filePathParam.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (namedProject != null)
                    {
                        scopeProject = namedProject.Name;
                        scopeNote = BuildScopeNote(filePathParam, true);
                    }
                    else
                    {
                        if (!_pathResolver.TryResolveFilePath(filePathParam, solutionDir, out string absolutePath))
                            return Error($"Cannot resolve file path: {filePathParam}", symbolName);
                        if (_fileSystem.FileExists(absolutePath))
                        {
                            targetFilePath = absolutePath;
                        }
                        else if (_fileSystem.DirectoryExists(absolutePath))
                        {
                            scopeFolder = absolutePath.TrimEnd('\\', '/') + "\\";
                            scopeNote = BuildScopeNote(filePathParam, false);
                        }
                        else
                        {
                            var replacement = await _missingFileHint.TryResolveUniqueAsync(filePathParam, solutionDir, cancellationToken).ConfigureAwait(false);
                            if (replacement == null)
                                return Error(await _missingFileHint.BuildFileNotFoundErrorAsync(filePathParam, absolutePath, solutionDir, cancellationToken).ConfigureAwait(false), symbolName);

                            targetFilePath = replacement.AbsolutePath;
                            scopeNote = replacement.Note;
                        }
                    }
                }

                bool scoped = targetFilePath != null || scopeFolder != null;
                Func<string, bool> inScope = path =>
                    path != null && (targetFilePath != null
                        ? string.Equals(path, targetFilePath, StringComparison.OrdinalIgnoreCase)
                        : scopeFolder == null || path.Replace('/', '\\').StartsWith(scopeFolder, StringComparison.OrdinalIgnoreCase));

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
                            if (scopeProject != null && !string.Equals(project.Name, scopeProject, StringComparison.OrdinalIgnoreCase)) continue;

                            var allSymbols = await SymbolFinder.FindDeclarationsAsync(
                                project,
                                memberName,
                                ignoreCase: true,
                                cancellationToken: cancellationToken
                            );

                            var candidates = containerName == null
                                ? allSymbols.ToList()
                                : allSymbols.Where(s => string.Equals(s.ContainingSymbol?.Name, containerName, StringComparison.OrdinalIgnoreCase)).ToList();
                            var exactMatches = candidates.Where(s => string.Equals(s.Name, memberName, StringComparison.Ordinal)).ToList();
                            var symbolsToProcess = exactMatches.Any() ? exactMatches : candidates;

                            foreach (var symbol in symbolsToProcess)
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                if (scoped && !symbol.DeclaringSyntaxReferences.Any(r => inScope(r.SyntaxTree?.FilePath)))
                                    continue;

                                foundSymbols.Add(symbol);

                                var syntaxRefs = symbol.DeclaringSyntaxReferences;
                                foreach (var syntaxRef in syntaxRefs)
                                {
                                    var syntaxNode = await syntaxRef.GetSyntaxAsync(cancellationToken);
                                    var location = syntaxNode.GetLocation();
                                    var filePath = location.SourceTree?.FilePath;
                                    if (string.IsNullOrEmpty(filePath))
                                        continue;

                                    if (scoped && !inScope(filePath))
                                        continue;

                                    var lineSpan = location.GetLineSpan();
                                    int line = lineSpan.StartLinePosition.Line + 1;
                                    int endLine = lineSpan.EndLinePosition.Line + 1;
                                    int column = lineSpan.StartLinePosition.Character + 1;

                                    var locationKey = $"{filePath}:{line}:{column}";
                                    if (!seenLocations.Add(locationKey))
                                        continue;

                                    var declarationText = await location.SourceTree.GetTextAsync(cancellationToken);
                                    var item = new DefinitionItem
                                    {
                                        FilePath = _pathResolver.TryGetRelativePath(filePath, solutionDir, out var rel) ? rel : filePath,
                                        Line = line,
                                        LineText = line - 1 < declarationText.Lines.Count ? declarationText.Lines[line - 1].ToString().Trim() : null,
                                        EndLine = endLine,
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
                            HasMoreResults = false,
                            Note = JoinNotes(BuildQualifiedNameNote(symbolName, memberName, containerName, foundSymbols.Count > 0), scopeNote)
                        };

                        if (includeReferences && foundSymbols.Any())
                        {
                            ISymbol targetSymbol;
                            if (scoped)
                            {
                                targetSymbol = foundSymbols.FirstOrDefault(s => s.DeclaringSyntaxReferences.Any(r => inScope(r.SyntaxTree?.FilePath)))
                                              ?? foundSymbols.First();
                            }
                            else
                            {
                                targetSymbol = foundSymbols.FirstOrDefault(s => string.Equals(s.Name, memberName, StringComparison.Ordinal))
                                              ?? foundSymbols.First();
                            }

                            if (targetFilePath == null)
                            {
                                // Several different symbols share the name: say whose references these are, or the model takes them for the one it meant.
                                int symbolCount = foundSymbols.Select(DeclarationKey).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                                var targetRef = targetSymbol.DeclaringSyntaxReferences.FirstOrDefault();
                                string targetFile = targetRef?.SyntaxTree?.FilePath ?? "";
                                int targetLine = targetRef?.SyntaxTree != null ? targetRef.SyntaxTree.GetLineSpan(targetRef.Span).StartLinePosition.Line + 1 : 0;
                                if (_pathResolver.TryGetRelativePath(targetFile, solutionDir, out var targetRel))
                                    targetFile = targetRel;
                                response.Note = JoinNotes(response.Note, BuildReferenceTargetNote(memberName, symbolCount,
                                    targetSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), targetFile, targetLine));
                            }

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
                        InternalLogger.Warn($"GetSymbolInfo: symbol lookup of '{symbolName}' failed", ex);
                        return Error(ex.Message, symbolName);
                    }
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                if (!(ex is OperationCanceledException))
                    InternalLogger.Warn($"GetSymbolInfo: symbol lookup of '{symbolName}' failed before the background task started", ex);
                return Error(ex.Message, symbolName);
            }
        }

        /// <summary>
        /// Note for a file_path that names a project or a folder: it was used as the place of the declarations, not as a file.
        /// </summary>
        internal static string BuildScopeNote(string filePath, bool isProjectName)
        {
            string what = isProjectName ? "a project" : "a folder";
            return $"file_path '{filePath}' is {what}: definitions are only the declarations inside it, and the references are those of the first of them; references are still searched across the whole solution.";
        }

        /// <summary>
        /// Splits "Type.Member" into the member name and the name of its containing type or namespace (the segment before the last dot);
        /// "global::", a trailing "()" and surrounding spaces are dropped. A name without a dot gives (name, null).
        /// </summary>
        internal static (string member, string container) SplitQualifiedName(string name)
        {
            string text = (name ?? string.Empty).Trim();
            if (text.StartsWith("global::", StringComparison.Ordinal))
                text = text.Substring("global::".Length);
            if (text.EndsWith("()", StringComparison.Ordinal))
                text = text.Substring(0, text.Length - 2);

            int dot = text.LastIndexOf('.');
            if (dot <= 0 || dot == text.Length - 1)
                return (text.Trim('.'), null);

            string container = text.Substring(0, dot);
            int previous = container.LastIndexOf('.');
            if (previous >= 0)
                container = container.Substring(previous + 1);
            return (text.Substring(dot + 1), container);
        }

        /// <summary>
        /// Warning for a 'symbol_names' call that also asks for references (include_references true or max_references): that mode returns
        /// definitions only, and without the warning the empty references read as "no usages". Null otherwise.
        /// </summary>
        internal static string BuildNoReferencesWarning(Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return null;

            bool askedForReferences = (parameters.TryGetValue("include_references", out object includeObj) && includeObj is bool include && include)
                || (parameters.TryGetValue("max_references", out object maxObj) && maxObj != null);
            if (!askedForReferences)
                return null;

            return $"References were NOT computed: a call with '{SymbolNamesParameter}' returns definitions only, so this is not a reference count. " +
                   $"For the references of one symbol, call again with '{SymbolNameParameter}' alone (no '{SymbolNamesParameter}').";
        }

        /// <summary>Says how a qualified symbol_name was read; null for a plain name.</summary>
        internal static string BuildQualifiedNameNote(string symbolName, string member, string container, bool found)
        {
            if (container == null)
                return null;

            return found
                ? $"symbol_name '{symbolName}' was read as the member '{member}' of '{container}'."
                : $"symbol_name '{symbolName}' was read as the member '{member}' of '{container}', and no such member was found. Check the type name, or call with symbol_name '{member}' alone to see every '{member}' in 'definitions'.";
        }

        private static string JoinNotes(string first, string second)
        {
            if (string.IsNullOrEmpty(first))
                return string.IsNullOrEmpty(second) ? null : second;
            return string.IsNullOrEmpty(second) ? first : first + " " + second;
        }

        /// <summary>
        /// Note for a call without file_path that found several different symbols with the name: the references belong to one of them only.
        /// Null when there is only one symbol.
        /// </summary>
        internal static string BuildReferenceTargetNote(string symbolName, int symbolCount, string targetSignature, string targetFile, int targetLine)
        {
            if (symbolCount <= 1)
                return null;

            return $"{symbolCount} different symbols named '{symbolName}' were found (see definitions). The references are only those of {targetSignature} declared at {targetFile}:{targetLine}. " +
                   "For the references of another one, call again with file_path set to the file of its declaration.";
        }

        /// <summary>Where a symbol is first declared ("path:line"): partial declarations and the same symbol seen from two projects count once.</summary>
        private static string DeclarationKey(ISymbol symbol)
        {
            var reference = symbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (reference?.SyntaxTree == null)
                return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return reference.SyntaxTree.FilePath + ":" + reference.SyntaxTree.GetLineSpan(reference.Span).StartLinePosition.Line;
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
                case Accessibility.ProtectedAndInternal: modifiers.Add("private protected"); break;
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

        internal static string BuildCacheKey(string symbolName, string filePath, bool includeReferences)
        {
            return $"{symbolName}||{filePath ?? ""}||{includeReferences}";
        }

        private (string symbolName, string filePath, bool includeReferences, int pageSize, string pageToken, string error) ExtractAndValidateParameters(
            Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return (null, null, true, DefaultPageSize, null, "Parameters are required.");

            if (!parameters.TryGetValue("symbol_name", out object symbolObj) || !(symbolObj is string symbolName))
                return (null, null, true, DefaultPageSize, null, $"Parameter '{SymbolNameParameter}' (or '{SymbolNamesParameter}') is required and must be a string.");

            if (string.IsNullOrWhiteSpace(symbolName))
                return (null, null, true, DefaultPageSize, null, "Parameter 'symbol_name' cannot be empty.");

            string filePath = null;
            if (parameters.TryGetValue("file_path", out object fpObj) && fpObj is string fp && !string.IsNullOrEmpty(fp))
                filePath = fp;

            bool includeReferences = true;
            if (parameters.TryGetValue("include_references", out object includeObj) && includeObj is bool includeBool)
                includeReferences = includeBool;

            int pageSize = DefaultPageSize;
            if (parameters.TryGetValue("max_references", out object maxObj) && maxObj != null && int.TryParse(maxObj.ToString(), out int maxVal))
                pageSize = Math.Min(Math.Max(maxVal, 1), MaxPageSize);

            string pageToken = ToolPagination.ReadToken(parameters);

            return (symbolName, filePath, includeReferences, pageSize, pageToken, null);
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            object namesObj = null;
            parameters?.TryGetValue(SymbolNamesParameter, out namesObj);
            if (!LiteralListParameter.IsMissing(namesObj))
            {
                var (names, _) = ExtractSymbolNames(parameters, namesObj);
                if (names != null && names.Count > 0)
                    return $"Getting symbol info for {string.Join(", ", names.Select(n => $"'{n}'"))}... ";
            }

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

                string msg = $"{resp.Definitions?.Count ?? 0} {Pluralizer.Pluralize(resp.Definitions?.Count ?? 0, "definition", "definitions")} found";
                int pageRefs = resp.References?.Count ?? 0;
                if (pageRefs > 0 || resp.TotalReferences > 0)
                    msg += $", {pageRefs} {Pluralizer.Pluralize(pageRefs, "reference", "references")}";
                if (resp.TotalReferences > 0 && pageRefs < resp.TotalReferences)
                    msg += $" (total: {resp.TotalReferences} {Pluralizer.Pluralize(resp.TotalReferences, "reference", "references")})";
                return msg + ".";
            }

            if (result is SymbolInfoMultiResponse multi)
            {
                var blocks = multi.Symbols ?? new List<SymbolDefinitionsBlock>();
                if (blocks.Count == 0)
                    return $"Failed: {multi.ErrorMessage}";

                int total = blocks.Where(b => b.Success).Sum(b => b.Definitions?.Count ?? 0);
                var counts = string.Join(", ", blocks.Select(b => $"'{b.SymbolName}': {(b.Success ? (b.Definitions?.Count ?? 0).ToString() : "failed")}"));
                return $"Found {total} {Pluralizer.Pluralize(total, "definition", "definitions")} for {blocks.Count} {Pluralizer.Pluralize(blocks.Count, "symbol", "symbols")} ({counts}).";
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
            /// <summary>Text of <see cref="Line"/>, trimmed: the first line of the declaration (an attribute line when the declaration has attributes).</summary>
            [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
            public string LineText { get; set; }
            /// <summary>Last line of the declaration syntax: the closing brace of a body, or the ';' line of an expression-bodied or body-less member.</summary>
            [JsonProperty("end_line")]
            public int EndLine { get; set; }
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
            /// <summary>The symbol this entry belongs to: a cursor page_token finds the entry by id and needs no symbol_name.</summary>
            public string SymbolName { get; set; }
            /// <summary>Whose references these are when several symbols share the name; returned with every page.</summary>
            public string Note { get; set; }
        }

        public class SymbolInfoResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }
            [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
            public string ErrorMessage { get; set; }
            [JsonProperty("symbol_name")]
            public string SymbolName { get; set; }
            [JsonProperty("definitions")]
            public List<DefinitionItem> Definitions { get; set; }
            [JsonProperty("definition", NullValueHandling = NullValueHandling.Ignore)]
            public DefinitionItem Definition => Definitions?.FirstOrDefault();
            /// <summary>Set when several different symbols share the name and no file_path was given: whose references these are.</summary>
            [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
            public string Note { get; set; }
            [JsonProperty("references")]
            public List<ReferenceItem> References { get; set; }
            [JsonProperty("total_references")]
            public int TotalReferences { get; set; }
            /// <summary>Which page this is: "page 2 of 3". Only data.</summary>
            [JsonProperty("showing", NullValueHandling = NullValueHandling.Ignore)]
            public string Showing { get; set; }
            [JsonProperty("next_page_token", NullValueHandling = NullValueHandling.Ignore)]
            public string NextPageToken { get; set; }
            [JsonProperty("has_more_results")]
            public bool HasMoreResults { get; set; }
            [JsonProperty("warning", NullValueHandling = NullValueHandling.Ignore)]
            public string Warning { get; set; }
        }

        /// <summary>One name of a 'symbol_names' call: its definitions only.</summary>
        public class SymbolDefinitionsBlock
        {
            [JsonProperty("symbol_name")]
            public string SymbolName { get; set; }
            [JsonProperty("success")]
            public bool Success { get; set; }
            [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
            public string ErrorMessage { get; set; }
            /// <summary>How a qualified name was read, or the place file_path limited the definitions to.</summary>
            [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
            public string Note { get; set; }
            [JsonProperty("definitions")]
            public List<DefinitionItem> Definitions { get; set; }
        }

        /// <summary>Response of a call with 'symbol_names': one block per name in request order.</summary>
        public class SymbolInfoMultiResponse
        {
            [JsonProperty("symbols")]
            public List<SymbolDefinitionsBlock> Symbols { get; set; }
            [JsonProperty("success")]
            public bool Success { get; set; }
            [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
            public string ErrorMessage { get; set; }
            [JsonProperty("warning", NullValueHandling = NullValueHandling.Ignore)]
            public string Warning { get; set; }
        }

        #endregion
    }
}
