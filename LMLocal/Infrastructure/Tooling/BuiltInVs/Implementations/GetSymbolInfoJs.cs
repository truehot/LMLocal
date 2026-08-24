using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Js;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Newtonsoft.Json;
using static LMLocal.Infrastructure.Tooling.BuiltInVs.Common.VsSolutionFilesScanner;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Tool for retrieving navigation information about a JavaScript symbol: declarations, calls/references, imports and the import chain.
    /// </summary>
    internal interface IGetSymbolInfoJs : IBuiltInTool
    {
    }

    internal class GetSymbolInfoJs : IGetSymbolInfoJs
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly IVsSolutionFilesScanner _solutionFilesScanner;
        private readonly ISearchResultCache _searchCache;
        private readonly IFileSystem _fileSystem;
        private readonly IJsConfigResolver _configResolver;

        private const int MaxTotalReferences = 5000;
        private const int DefaultPageSize = 50;
        private const int MaxPageSize = 200;
        private const int MaxDefinitions = 100;
        private const int MaxFilesToScan = 1500;
        private const long MaxFileSizeBytes = 1024 * 1024; // 1 MB
        private const int DefaultMaxDepth = 2;
        private const int MinSymbolNameLength = 3;
        private const int MaxParseCandidates = 200;
        private const int MaxChainParses = 50;
        private const int MaxConfigRoots = 100;

        private static readonly string[] DefaultExtensions = { ".js", ".mjs", ".cjs" };

        public string ToolName => "get_symbol_info_js";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.ReadOnly;

        public GetSymbolInfoJs(
            IVsDependencies vsDependencies,
            IPathResolver pathResolver,
            IVsSolutionFilesScanner solutionFilesScanner,
            ISearchResultCache searchCache,
            IFileSystem fileSystem,
            IJsConfigResolver configResolver)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _solutionFilesScanner = solutionFilesScanner ?? throw new ArgumentNullException(nameof(solutionFilesScanner));
            _searchCache = searchCache ?? throw new ArgumentNullException(nameof(searchCache));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _configResolver = configResolver ?? throw new ArgumentNullException(nameof(configResolver));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Retrieves navigation information for a JavaScript symbol: declarations (file, line, column, declaration type), calls/references (file, line, line text, context), the definition chain (forward through imports) and importers (backward references). Results are grouped by file. Supports only .js, .mjs, .cjs. Symbol_name must be at least 3 characters. References limited to 5000, paginated by page size (default 50).",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "symbol_name", new ToolDetails { Type = "string", Description = "The name of the symbol to search for (case-insensitive). Minimum 3 characters." } },
                        { "file_path", new ToolDetails { Type = "string", Description = "Limit search to a specific file (relative or absolute path). When set, only that file is scanned (real scope), no full-solution scan." } },
                        { "include_references", new ToolDetails { Type = "boolean", Description = "Whether to include calls/references. Default true." } },
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
                var (symbolName, filePathParam, includeReferences, pageSize, pageToken, error) =
                                    ExtractAndValidateParameters(parameters);
                if (!string.IsNullOrEmpty(error))
                    return Error(error, symbolName);

                if (!_vsDependencies.IsSolutionOpen)
                    return Error("No solution is currently open.", symbolName);

                string solutionDir = _vsDependencies.GetSolutionDirectory();

                string targetFilePath = null;
                if (!string.IsNullOrEmpty(filePathParam))
                {
                    if (!_pathResolver.TryResolveFilePath(filePathParam, solutionDir, out string absolutePath))
                        return Error($"Cannot resolve file path: {filePathParam}", symbolName);
                    if (!_fileSystem.FileExists(absolutePath))
                        return Error($"File not found: {absolutePath}", symbolName);
                    targetFilePath = absolutePath;
                }

                var filter = new EnumerateSolutionFilesFilter
                {
                    ExtensionFilter = string.Join(",", DefaultExtensions),
                    ReturnRelative = false,
                    Limit = MaxFilesToScan,
                    IncludeProjects = true
                };

                List<string> allFiles = null;
                if (targetFilePath == null)
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                    allFiles = (await _solutionFilesScanner.EnumerateSolutionFilesAsync(filter, cancellationToken)).ToList();
                    await TaskScheduler.Default;
                }

                var roots = new List<string>();
                if (targetFilePath != null)
                {
                    string dir = Path.GetDirectoryName(targetFilePath);
                    if (!string.IsNullOrEmpty(dir))
                        roots.Add(dir);
                }
                else if (allFiles != null)
                {
                    var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var f in allFiles)
                    {
                        string dir = Path.GetDirectoryName(f);
                        if (string.IsNullOrEmpty(dir) || !seenDirs.Add(dir))
                            continue;
                        roots.Add(dir);
                        if (roots.Count >= MaxConfigRoots)
                            break;
                    }
                }

                var config = _configResolver.Load(solutionDir, roots);
                string configHash = config?.ConfigHash ?? "";
                string configFilePath = config?.ConfigFilePath ?? "";

                int pageNumber = string.IsNullOrEmpty(pageToken) || !int.TryParse(pageToken, out var pn) ? 0 : Math.Max(0, pn);

                int maxSafePage = (int.MaxValue - pageSize) / pageSize;
                if (pageNumber > maxSafePage)
                    pageNumber = maxSafePage;
                int skip = pageNumber * pageSize;

                string cacheKey = BuildCacheKey(symbolName, filePathParam, includeReferences, pageSize, configHash, configFilePath);
                if (_searchCache.TryGet(cacheKey, solutionDir, out CachedToolResults<JsSymbolInfoResponse> cached))
                {
                    var cachedInfo = cached.AllResults.FirstOrDefault();
                    if (cachedInfo != null)
                    {
                        return BuildResponse(cachedInfo, skip, pageSize, pageNumber);
                    }
                }

                var fullResult = await ExecuteCoreAsync(symbolName, targetFilePath, includeReferences, solutionDir, config, allFiles, cancellationToken);
                if (!fullResult.Success)
                    return fullResult;

                var cacheEntry = new CachedToolResults<JsSymbolInfoResponse>
                {
                    AllResults = new List<JsSymbolInfoResponse> { fullResult },
                    ItemsScanned = 1
                };
                _searchCache.Set(cacheKey, solutionDir, cacheEntry);

                return BuildResponse(fullResult, skip, pageSize, pageNumber);
            }
            catch (OperationCanceledException)
            {
                return Error("Operation was cancelled.", null);
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"GetSymbolInfoJs: unexpected error: {ex}");
                return Error(ex.Message, null);
            }
        }

        private JsSymbolInfoResponse BuildResponse(JsSymbolInfoResponse fullResult, int skip, int pageSize, int pageNumber)
        {
            var pagedRefs = fullResult.References?.Skip(skip).Take(pageSize).ToList() ?? new List<JsReferenceItem>();
            string nextToken = (skip + pageSize) < (fullResult.References?.Count ?? 0) ? (pageNumber + 1).ToString() : null;

            return new JsSymbolInfoResponse
            {
                Success = true,
                SymbolName = fullResult.SymbolName,
                Definitions = fullResult.Definitions ?? new List<JsDefinitionItem>(),
                References = pagedRefs,
                Files = BuildFileGroups(fullResult.Definitions ?? new List<JsDefinitionItem>(), pagedRefs, fullResult.Importers ?? new List<JsImporterLink>()),
                TotalReferences = fullResult.References?.Count ?? 0,
                NextPageToken = nextToken,
                HasMoreResults = fullResult.HasMoreResults,
                DefinitionChain = fullResult.DefinitionChain ?? new List<JsImportChainLink>(),
                Importers = fullResult.Importers ?? new List<JsImporterLink>()
            };
        }

        internal static List<JsFileGroup> BuildFileGroups(
                    List<JsDefinitionItem> definitions,
                    List<JsReferenceItem> references,
                    List<JsImporterLink> importers)
        {
            var groups = new Dictionary<string, JsFileGroup>(StringComparer.OrdinalIgnoreCase);

            foreach (var def in definitions)
            {
                if (string.IsNullOrEmpty(def.FilePath))
                    continue;

                if (!groups.TryGetValue(def.FilePath, out var group))
                {
                    group = new JsFileGroup
                    {
                        FilePath = def.FilePath,
                        Definitions = new List<JsDefinitionItem>(),
                        References = new List<JsReferenceItem>()
                    };
                    groups[def.FilePath] = group;
                }

                group.Definitions.Add(def);
            }

            foreach (var reference in references)
            {
                if (string.IsNullOrEmpty(reference.FilePath))
                    continue;

                if (!groups.TryGetValue(reference.FilePath, out var group))
                {
                    group = new JsFileGroup
                    {
                        FilePath = reference.FilePath,
                        Definitions = new List<JsDefinitionItem>(),
                        References = new List<JsReferenceItem>()
                    };
                    groups[reference.FilePath] = group;
                }

                group.References.Add(reference);
            }

            foreach (var importer in importers)
            {
                if (string.IsNullOrEmpty(importer.FilePath))
                    continue;

                if (!groups.TryGetValue(importer.FilePath, out var group))
                {
                    group = new JsFileGroup
                    {
                        FilePath = importer.FilePath,
                        Definitions = new List<JsDefinitionItem>(),
                        References = new List<JsReferenceItem>()
                    };
                    groups[importer.FilePath] = group;
                }

                if (string.IsNullOrEmpty(group.ImportSource))
                    group.ImportSource = importer.ImportSource;
            }

            return groups.Values.ToList();
        }

        private async Task<JsSymbolInfoResponse> ExecuteCoreAsync(
            string symbolName,
            string targetFilePath,
            bool includeReferences,
            string solutionDir,
            JsConfig config,
            List<string> allFiles,
            CancellationToken cancellationToken)
        {
            try
            {
                var parseResults = new ConcurrentDictionary<string, JsFileParseResult>(StringComparer.OrdinalIgnoreCase);

                var candidates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (targetFilePath != null)
                {
                    string content = await ReadFileContentSafeAsync(targetFilePath, cancellationToken);
                    if (content != null)
                        candidates[targetFilePath] = content;
                }
                else if (allFiles != null)
                {
                    foreach (var filePath in allFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (SourceFileFilter.ShouldExclude(filePath))
                            continue;

                        string content = await ReadFileContentSafeAsync(filePath, cancellationToken);
                        if (content == null)
                            continue;

                        if (content.IndexOf(symbolName, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            candidates[filePath] = content;
                            if (candidates.Count >= MaxParseCandidates)
                            {
                                InternalLogger.Warn($"GetSymbolInfoJs: candidate limit {MaxParseCandidates} reached for '{symbolName}', stopping scan.");
                                break;
                            }
                        }
                    }
                }

                int maxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount);
                using (var semaphore = new SemaphoreSlim(maxDegreeOfParallelism))
                {
                    var parseTasks = candidates.Select(async kvp =>
                    {
                        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var parser = new JsFileParser();
                            var result = parser.Parse(kvp.Key, kvp.Value);
                            if (result != null)
                                parseResults.TryAdd(kvp.Key, result);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });

                    await Task.WhenAll(parseTasks).ConfigureAwait(false);
                }

                var allDefinitions = new List<JsDefinitionItem>();
                var seenLocations = new HashSet<string>(StringComparer.Ordinal);

                foreach (var kvp in parseResults)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    foreach (var decl in kvp.Value.Declarations)
                    {
                        if (!string.Equals(decl.Name, symbolName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        string locationKey = $"{kvp.Key}:{decl.Line}:{decl.Column}";
                        if (!seenLocations.Add(locationKey))
                            continue;

                        allDefinitions.Add(new JsDefinitionItem
                        {
                            FilePath = ToRelativePath(kvp.Key, solutionDir),
                            Line = decl.Line,
                            Column = decl.Column,
                            DeclarationType = decl.DeclarationType,
                            SymbolKind = decl.SymbolKind
                        });

                        if (allDefinitions.Count >= MaxDefinitions)
                            break;
                    }
                    if (allDefinitions.Count >= MaxDefinitions)
                        break;
                }

                var response = new JsSymbolInfoResponse
                {
                    Success = true,
                    SymbolName = symbolName,
                    Definitions = allDefinitions,
                    References = new List<JsReferenceItem>(),
                    Files = new List<JsFileGroup>(),
                    TotalReferences = 0,
                    NextPageToken = null,
                    HasMoreResults = false,
                    DefinitionChain = new List<JsImportChainLink>(),
                    Importers = new List<JsImporterLink>()
                };

                response.DefinitionChain = BuildDefinitionChain(symbolName, parseResults, config, solutionDir, DefaultMaxDepth, cancellationToken);

                if (includeReferences)
                {
                    var allRefItems = new List<JsReferenceItem>();
                    var seenRefs = new HashSet<string>(StringComparer.Ordinal);
                    bool hasMoreResults = false;
                    int totalRefs = 0;

                    foreach (var kvp in parseResults)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        foreach (var call in kvp.Value.Calls)
                        {
                            if (!string.Equals(call.Name, symbolName, StringComparison.OrdinalIgnoreCase))
                                continue;

                            string refKey = $"{kvp.Key}:{call.LineNumber}:{call.Column}:{call.Context}";
                            if (!seenRefs.Add(refKey))
                                continue;

                            string importSource = null;
                            if (kvp.Value.Imports.TryGetValue(symbolName, out string src))
                                importSource = src;

                            allRefItems.Add(new JsReferenceItem
                            {
                                FilePath = ToRelativePath(kvp.Key, solutionDir),
                                LineNumber = call.LineNumber,
                                LineText = call.LineText,
                                Context = call.Context,
                                ImportSource = importSource,
                                ObjectName = call.ObjectName,
                                IsComputed = call.IsComputed
                            });

                            totalRefs++;
                            if (totalRefs >= MaxTotalReferences)
                            {
                                hasMoreResults = true;
                                break;
                            }
                        }
                        if (hasMoreResults) break;
                    }

                    foreach (var kvp in parseResults)
                    {
                        var match = kvp.Value.ImportRecords.FirstOrDefault(rec =>
                            string.Equals(rec.ImportedName, symbolName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(rec.LocalName, symbolName, StringComparison.OrdinalIgnoreCase));

                        if (match == null)
                            continue;

                        response.Importers.Add(new JsImporterLink
                        {
                            FilePath = ToRelativePath(kvp.Key, solutionDir),
                            ImportSource = match.Source
                        });
                    }

                    response.References = allRefItems;
                    response.TotalReferences = totalRefs;
                    response.HasMoreResults = hasMoreResults;
                }

                response.Files = BuildFileGroups(allDefinitions, response.References, response.Importers);

                return response;
            }
            catch (OperationCanceledException)
            {
                return Error("Operation was cancelled.", symbolName);
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"GetSymbolInfoJs: ExecuteCoreAsync error: {ex}");
                return Error(ex.Message, symbolName);
            }
        }

        private async Task<string> ReadFileContentSafeAsync(string filePath, CancellationToken cancellationToken)
        {
            if (!_fileSystem.FileExists(filePath))
            {
                InternalLogger.Warn($"GetSymbolInfoJs: file not found: '{filePath}'");
                return null;
            }

            try
            {
                var (length, _) = _fileSystem.GetFileInfo(filePath);
                if (length > MaxFileSizeBytes)
                {
                    InternalLogger.Warn($"GetSymbolInfoJs: skipping large file '{filePath}' ({length} bytes)");
                    return null;
                }

                return await _fileSystem.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"GetSymbolInfoJs: error reading '{filePath}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Builds the definition chain (forward). Starts from every parsed file that uses or imports the symbol and follows the module imports (via ResolveModule) to the defining module, up to max_depth.
        /// </summary>
        private List<JsImportChainLink> BuildDefinitionChain(
            string symbolName,
            ConcurrentDictionary<string, JsFileParseResult> parseResults,
            JsConfig config,
            string solutionDir,
            int maxDepth,
            CancellationToken cancellationToken)
        {
            var chain = new List<JsImportChainLink>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<(string FilePath, int Depth, string ImportSource)>();
            int onDemandParses = 0;

            foreach (var kvp in parseResults)
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool uses = kvp.Value.Calls.Any(call => string.Equals(call.Name, symbolName, StringComparison.OrdinalIgnoreCase));
                if (!uses)
                {
                    uses = kvp.Value.ImportRecords.Any(rec =>
                        string.Equals(rec.ImportedName, symbolName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(rec.LocalName, symbolName, StringComparison.OrdinalIgnoreCase));
                }

                if (uses && visited.Add(kvp.Key))
                    queue.Enqueue((kvp.Key, 0, null));
            }

            while (queue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (filePath, depth, importSource) = queue.Dequeue();
                if (depth > maxDepth)
                    continue;

                chain.Add(new JsImportChainLink
                {
                    FilePath = ToRelativePath(filePath, solutionDir),
                    Depth = depth,
                    ImportSource = importSource
                });

                if (depth >= maxDepth)
                    continue;

                if (!parseResults.TryGetValue(filePath, out var parse))
                {
                    if (onDemandParses >= MaxChainParses)
                        continue;
                    onDemandParses++;

                    try
                    {
                        if (!_fileSystem.FileExists(filePath))
                            continue;

                        var (length, _) = _fileSystem.GetFileInfo(filePath);
                        if (length > MaxFileSizeBytes)
                            continue;

                        string content = _fileSystem.ReadAllText(filePath);
                        parse = new JsFileParser().Parse(filePath, content);
                        if (parse == null)
                            continue;
                        parseResults.TryAdd(filePath, parse);
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Warn($"GetSymbolInfoJs: error parsing '{filePath}' during chain build: {ex.Message}");
                        continue;
                    }
                }

                foreach (var rec in parse.ImportRecords)
                {
                    bool matches = string.Equals(rec.ImportedName, symbolName, StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(rec.LocalName, symbolName, StringComparison.OrdinalIgnoreCase);
                    if (!matches)
                        continue;

                    string resolved = _configResolver.ResolveModule(rec.Source, filePath, config);
                    if (resolved != null && visited.Add(resolved))
                        queue.Enqueue((resolved, depth + 1, rec.Source));
                }
            }

            return chain;
        }

        private string ToRelativePath(string absolutePath, string solutionDir)
        {
            if (_pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string rel))
                return rel;
            return absolutePath;
        }

        private string BuildCacheKey(string symbolName, string filePath, bool includeReferences, int pageSize, string configHash, string configFilePath)
        {
            return $"get_symbol_info_js:{symbolName}:{filePath ?? ""}:{includeReferences}:{pageSize}:{configHash}:{configFilePath ?? ""}";
        }

        internal static (string symbolName, string filePath, bool includeReferences, int pageSize, string pageToken, string error) ExtractAndValidateParameters(Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return (null, null, true, DefaultPageSize, null, "Parameters are required.");

            if (!parameters.TryGetValue("symbol_name", out object symbolObj) || !(symbolObj is string symbolName))
                return (null, null, true, DefaultPageSize, null, "Parameter 'symbol_name' is required and must be a string.");

            if (string.IsNullOrWhiteSpace(symbolName))
                return (null, null, true, DefaultPageSize, null, "Parameter 'symbol_name' cannot be empty.");

            if (symbolName.Trim().Length < MinSymbolNameLength)
                return (null, null, true, DefaultPageSize, null, $"Parameter 'symbol_name' must be at least {MinSymbolNameLength} characters.");

            symbolName = symbolName.Trim();

            string filePath = null;
            if (parameters.TryGetValue("file_path", out object fpObj) && fpObj is string fp && !string.IsNullOrEmpty(fp))
                filePath = fp;

            bool includeReferences = true;
            if (parameters.TryGetValue("include_references", out object includeObj) && includeObj is bool includeBool)
                includeReferences = includeBool;

            int pageSize = DefaultPageSize;
            if (parameters.TryGetValue("max_references", out object maxObj) && maxObj != null && int.TryParse(maxObj.ToString(), out int maxVal))
                pageSize = Math.Min(Math.Max(maxVal, 1), MaxPageSize);

            string pageToken = parameters.TryGetValue("page_token", out object tokenObj) ? tokenObj as string : null;

            return (symbolName, filePath, includeReferences, pageSize, pageToken, null);
        }

        private static JsSymbolInfoResponse Error(string message, string symbolName)
        {
            return new JsSymbolInfoResponse
            {
                Success = false,
                ErrorMessage = message,
                SymbolName = symbolName ?? "",
                Definitions = new List<JsDefinitionItem>(),
                References = new List<JsReferenceItem>(),
                Files = new List<JsFileGroup>(),
                TotalReferences = 0,
                NextPageToken = null,
                HasMoreResults = false,
                DefinitionChain = new List<JsImportChainLink>(),
                Importers = new List<JsImporterLink>()
            };
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            var symbolName = parameters?.TryGetValue("symbol_name", out var s) == true ? s?.ToString() : "";
            var filePath = parameters?.TryGetValue("file_path", out var f) == true ? f?.ToString() : null;
            if (!string.IsNullOrEmpty(filePath))
                return $"Getting JS symbol info for '{symbolName}' in '{filePath}'... ";
            return $"Getting JS symbol info for '{symbolName}'... ";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is JsSymbolInfoResponse resp)
            {
                if (!resp.Success)
                    return $"Failed: {resp.ErrorMessage}";

                int definitions = resp.Definitions?.Count ?? 0;
                string msg = definitions > 0
                    ? $"{definitions} {Pluralizer.Pluralize(definitions, "definition", "definitions")} found"
                    : string.Empty;

                if (resp.TotalReferences > 0)
                    msg += (msg.Length > 0 ? ", " : "") + $"{resp.TotalReferences} {Pluralizer.Pluralize(resp.TotalReferences, "reference", "references")}";

                if (resp.HasMoreResults)
                    msg += " (more results exist, limit 5000 reached)";

                return msg.Length > 0 ? msg : "No results found.";
            }
            return "JS symbol info retrieval completed.";
        }

        #region DTOs

        public class JsDefinitionItem
        {
            [JsonIgnore]
            public string FilePath { get; set; }
            [JsonProperty("line")]
            public int Line { get; set; }
            [JsonProperty("column")]
            public int Column { get; set; }
            [JsonProperty("declaration_type")]
            public string DeclarationType { get; set; }
            [JsonProperty("symbol_kind")]
            public string SymbolKind { get; set; }
        }

        public class JsReferenceItem
        {
            [JsonIgnore]
            public string FilePath { get; set; }
            [JsonProperty("line")]
            public int LineNumber { get; set; }
            [JsonProperty("text")]
            public string LineText { get; set; }
            [JsonProperty("context")]
            public string Context { get; set; }
            [JsonProperty("import_source", NullValueHandling = NullValueHandling.Ignore)]
            public string ImportSource { get; set; }
            [JsonProperty("object_name", NullValueHandling = NullValueHandling.Ignore)]
            public string ObjectName { get; set; }
            [JsonProperty("is_computed")]
            public bool IsComputed { get; set; }
        }

        public class JsFileGroup
        {
            [JsonProperty("file_path")]
            public string FilePath { get; set; }
            [JsonProperty("definitions")]
            public List<JsDefinitionItem> Definitions { get; set; }
            [JsonProperty("references")]
            public List<JsReferenceItem> References { get; set; }
            [JsonProperty("import_source", NullValueHandling = NullValueHandling.Ignore)]
            public string ImportSource { get; set; }
        }

        public class JsImportChainLink
        {
            [JsonProperty("file_path")]
            public string FilePath { get; set; }
            [JsonProperty("depth")]
            public int Depth { get; set; }
            [JsonProperty("import_source", NullValueHandling = NullValueHandling.Ignore)]
            public string ImportSource { get; set; }
        }

        public class JsImporterLink
        {
            [JsonProperty("file_path")]
            public string FilePath { get; set; }
            [JsonProperty("import_source", NullValueHandling = NullValueHandling.Ignore)]
            public string ImportSource { get; set; }
        }

        public class JsSymbolInfoResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }
            [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
            public string ErrorMessage { get; set; }
            [JsonProperty("symbol_name")]
            public string SymbolName { get; set; }
            [JsonIgnore]
            public List<JsDefinitionItem> Definitions { get; set; }
            [JsonIgnore]
            public List<JsReferenceItem> References { get; set; }
            [JsonProperty("files")]
            public List<JsFileGroup> Files { get; set; }
            [JsonProperty("total_references")]
            public int TotalReferences { get; set; }
            [JsonProperty("next_page_token", NullValueHandling = NullValueHandling.Ignore)]
            public string NextPageToken { get; set; }
            [JsonProperty("has_more_results")]
            public bool HasMoreResults { get; set; }
            [JsonProperty("definition_chain")]
            public List<JsImportChainLink> DefinitionChain { get; set; }
            [JsonProperty("importers")]
            public List<JsImporterLink> Importers { get; set; }
        }

        #endregion
    }
}
