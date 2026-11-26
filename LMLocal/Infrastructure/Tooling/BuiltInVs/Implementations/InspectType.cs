using LMLocal.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.InspectTypes;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Inspects a fully qualified type (from source or any referenced assembly) and returns its
    /// full metadata: constructors, properties, methods, events, fields, attributes, base types, interfaces
    /// and generic parameters.
    /// Supports exact lookup via metadata name, and partial case-insensitive search (including NuGet packages).
    /// </summary>
    internal interface IInspectType : IBuiltInTool
    {
    }

    internal class InspectType : IInspectType
    {
        private readonly ISearchResultCache _searchCache;
        private readonly IPathResolver _pathResolver;
        private const int MaxTypeSearchMatches = 50;
        internal const int MaxCandidates = 5;
        internal const int MaxOtherMatches = 5;
        internal const int MaxTypeNames = 10;
        internal const string TypeNameParameter = "type_name";
        internal const string TypeNamesParameter = "type_names";

        public string ToolName => "inspect_type";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.ReadOnly;

        public InspectType(ISearchResultCache searchCache, IPathResolver pathResolver)
        {
            _searchCache = searchCache ?? throw new ArgumentNullException(nameof(searchCache));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Performs deep structural inspection of a type. " +
                               "If exact metadata name is given (e.g., 'System.String', 'System.Collections.Generic.List`1'), returns full details. " +
                               "If not found, or if 'search_mode' is true, performs a partial case-insensitive search over all types (including NuGet packages). " +
                               $"When that search finds exactly one type whose name equals 'type_name' (the simple name, or the full name for a namespace-qualified query), its full details are returned and 'other_matches' lists up to {MaxOtherMatches} other matching types by full name ('total_matches' counts all matches when there are more); call again with one of them for its details. " +
                               $"Several types: pass them all in 'type_names' (up to {MaxTypeNames}) in ONE call, not one call per type; project_name, namespace, assembly_name and search_mode apply to every name; the response has 'types', one block per name in request order, each shaped like a single-name response. " +
                               "A short name (e.g. 'Proposal') is matched as a substring of the type's simple name; " +
                               "a namespace-qualified name (e.g. 'Microsoft.VisualStudio.Language.Proposals' or 'System.Collections.Generic.List') is matched against the fully qualified name by dot-separated segments. " +
                               "Optionally filter by 'project_name', 'namespace' and/or 'assembly_name' (NuGet package id usually matches the assembly name). " +
                               "Generic types use arity for exact lookup (e.g., 'System.Collections.Generic.List`1'). " +
                               "For a type declared in the solution, 'file_path' is its file and every member has 'line', the 1-based line with its name " +
                               "(a member declared in another file of a partial type also has its own 'file_path'). " +
                               "Member lists are capped at 100 per category; totals and a 'has_more_results' flag are included. " +
                               "Partial search results are capped at 50 matches. " +
                               "If nothing passes the 'namespace'/'assembly_name' filters, success=false and 'candidates' lists up to 5 types that match by name only, with their actual namespace and assembly. " +
                               "Independent: several calls can go in one response.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { TypeNameParameter, new ToolDetails { Type = "string", Description = $"Type name: full metadata name for exact match (e.g. 'System.Collections.Generic.List`1'), or partial name for search. In search mode a short name is matched as a substring of the simple name, and a namespace-qualified name (e.g. 'Microsoft.VisualStudio.Language.Proposals') is matched against the fully qualified name by dot-separated segments. Required unless '{TypeNamesParameter}' is passed." } },
                        { "project_name", new ToolDetails { Type = "string", Description = "Name of the project to search in. If omitted, all projects." } },
                        { "namespace", new ToolDetails { Type = "string", Description = "Filter results by containing namespace (case-insensitive partial match)." } },
                        { "assembly_name", new ToolDetails { Type = "string", Description = "Filter results by containing assembly name (case-insensitive partial match). Useful for listing types from a NuGet package; package id usually equals the assembly name." } },
                        { "search_mode", new ToolDetails { Type = "boolean", Description = "If true, skip exact metadata name lookup and perform a partial case-insensitive search directly (capped at 50 matches)." } },
                        {
                            TypeNamesParameter, new ToolDetails
                            {
                                Type = "array",
                                Description = $"Several type names to inspect in one call (up to {MaxTypeNames}), each like 'type_name'; the other parameters apply to every name.",
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
            parameters?.TryGetValue(TypeNamesParameter, out namesObj);
            if (!LiteralListParameter.IsMissing(namesObj))
                return await ExecuteManyAsync(parameters, namesObj, cancellationToken);

            try
            {
                var (typeName, projectName, nsFilter, assemblyFilter, searchMode, error) = ExtractAndValidateParameters(parameters);
                if (!string.IsNullOrEmpty(error))
                    return ErrorResponse(null, error);

                return await InspectOneAsync(typeName, projectName, nsFilter, assemblyFilter, searchMode, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return ErrorResponse(null, "Operation was cancelled.");
            }
            catch (Exception ex)
            {
                LMLocal.Core.Common.InternalLogger.Warn("InspectType: type inspection failed", ex);
                return ErrorResponse(null, $"Internal error: {ex.Message}");
            }
        }

        /// <summary>'type_names': one single-name response per name, in request order; the filters apply to every name.</summary>
        private async Task<object> ExecuteManyAsync(Dictionary<string, object> parameters, object namesObj, CancellationToken cancellationToken)
        {
            var (names, error) = ExtractTypeNames(parameters, namesObj);
            if (error != null)
                return new TypeInspectionMultiResponse { Success = false, ErrorMessage = error, Types = new List<TypeInspectionResponse>() };

            var (projectName, nsFilter, assemblyFilter, searchMode) = ExtractFilters(parameters);
            var blocks = new List<TypeInspectionResponse>();
            try
            {
                foreach (var raw in names)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var (typeName, nameError) = NormalizeTypeName(raw);
                    if (nameError != null)
                    {
                        blocks.Add(ErrorResponse(raw, nameError));
                        continue;
                    }

                    try
                    {
                        blocks.Add(await InspectOneAsync(typeName, projectName, nsFilter, assemblyFilter, searchMode, cancellationToken));
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        LMLocal.Core.Common.InternalLogger.Warn($"InspectType: inspection of '{typeName}' failed", ex);
                        blocks.Add(ErrorResponse(typeName, $"Internal error: {ex.Message}"));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return new TypeInspectionMultiResponse { Success = false, ErrorMessage = "Operation was cancelled.", Types = blocks };
            }

            bool any = blocks.Any(b => b.Success);
            return new TypeInspectionMultiResponse
            {
                Success = any,
                ErrorMessage = any ? null : "No type was inspected: see error_message of each block.",
                Types = blocks
            };
        }

        private async Task<TypeInspectionResponse> InspectOneAsync(
            string typeName, string projectName, string nsFilter, string assemblyFilter, bool searchMode, CancellationToken cancellationToken)
        {
            var (result, matches, candidates, matchTotal, otherMatches, coreError) = await ExecuteCoreAsync(typeName, projectName, nsFilter, assemblyFilter, searchMode, cancellationToken);
            if (!string.IsNullOrEmpty(coreError))
                return ErrorResponse(typeName, coreError, candidates);

            if (result == null)
                return MatchResponse(typeName, matches, matchTotal);

            var response = SuccessResponse(typeName, result);
            if (otherMatches != null && otherMatches.Count > 0)
            {
                response.OtherMatches = otherMatches;
                if (matchTotal - 1 > otherMatches.Count)
                    response.TotalMatches = matchTotal;
            }
            return response;
        }

        private async Task<(TypeInspectionResult result, List<TypeSearchMatch> matches, List<TypeSearchMatch> candidates, int matchTotal, List<string> otherMatches, string error)> ExecuteCoreAsync(
            string typeName, string projectName, string nsFilter, string assemblyFilter, bool searchMode, CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var componentModel = (IComponentModel)ServiceProvider.GlobalProvider.GetService(typeof(SComponentModel)) ?? throw new InvalidOperationException("Component model is not available.");
            var workspace = componentModel.GetService<VisualStudioWorkspace>() ?? throw new InvalidOperationException("Visual Studio workspace is not available.");
            var solution = workspace.CurrentSolution ?? throw new InvalidOperationException("No solution is currently open.");
            var projects = solution.Projects.ToList();
            if (!string.IsNullOrEmpty(projectName))
                projects = projects.Where(p => string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase)).ToList();

            if (projects.Count == 0)
                return (null, null, null, 0, null, $"Project '{projectName}' was not found in the current solution.");

            string solutionDir = string.IsNullOrEmpty(solution.FilePath) ? null : Path.GetDirectoryName(solution.FilePath);
            string toRelativePath(string path) => _pathResolver.TryGetRelativePath(path, solutionDir, out var rel) ? rel : path;

            return await Task.Run(async () =>
            {
                bool exactFilteredOut = false;

                // 1. Exact lookup (unless search_mode).
                if (!searchMode)
                {
                    foreach (var project in projects)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!project.SupportsCompilation) continue;

                        var compilation = await project.GetCompilationAsync(cancellationToken);
                        if (compilation == null) continue;

                        var typeSymbol = compilation.GetTypeByMetadataName(typeName);
                        if (typeSymbol == null) continue;

                        if (!MatchesNamespace(TypeInspector.GetNamespaceName(typeSymbol.ContainingNamespace), nsFilter)
                            || !MatchesAssembly(typeSymbol.ContainingAssembly?.Name, assemblyFilter))
                        {
                            exactFilteredOut = true;
                            continue;
                        }

                        return (TypeInspector.Inspect(typeSymbol, project.Name, toRelativePath), (List<TypeSearchMatch>)null, (List<TypeSearchMatch>)null, 0, (List<string>)null, (string)null);
                    }
                }

                // 2. Partial search over an indexed snapshot of source + referenced assemblies.
                var search = await FindTypeMatchesAsync(projects, typeName, nsFilter, assemblyFilter, cancellationToken);

                if (search.Matches.Count == 0)
                {
                    if (search.Candidates.Count > 0)
                    {
                        return ((TypeInspectionResult)null, (List<TypeSearchMatch>)null,
                            search.Candidates.Select(ToTypeSearchMatch).ToList(), 0, (List<string>)null,
                            BuildCandidatesMessage(typeName, search.CandidateTotal, search.Candidates.Count));
                    }

                    string notFound = !searchMode && exactFilteredOut
                        ? BuildFilteredOutMessage(typeName, nsFilter, assemblyFilter)
                        : BuildNotFoundMessage(typeName, searchMode, nsFilter, assemblyFilter);
                    return ((TypeInspectionResult)null, (List<TypeSearchMatch>)null, (List<TypeSearchMatch>)null, 0, (List<string>)null, notFound);
                }

                if (search.Matches.Count == 1)
                    return (TypeInspector.Inspect(search.Matches[0].Symbol, search.Matches[0].ProjectName, toRelativePath), (List<TypeSearchMatch>)null, (List<TypeSearchMatch>)null, 0, (List<string>)null, (string)null);

                int exact = PickSingleExactMatch(search.Matches, TypeSearchQuery.Create(typeName));
                if (exact >= 0)
                {
                    var picked = search.Matches[exact];
                    var others = search.Matches
                        .Where((m, i) => i != exact)
                        .Take(MaxOtherMatches)
                        .Select(m => m.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                        .ToList();
                    return (TypeInspector.Inspect(picked.Symbol, picked.ProjectName, toRelativePath), (List<TypeSearchMatch>)null, (List<TypeSearchMatch>)null, search.MatchTotal, others, (string)null);
                }

                return ((TypeInspectionResult)null, search.Matches.Select(ToTypeSearchMatch).ToList(), (List<TypeSearchMatch>)null, search.MatchTotal, (List<string>)null, (string)null);
            }, cancellationToken);
        }

        private async Task<SearchOutcome> FindTypeMatchesAsync(
            List<Project> projects, string typeName, string nsFilter, string assemblyFilter, CancellationToken cancellationToken)
        {
            var query = TypeSearchQuery.Create(typeName);
            int comparison(MatchedType a, MatchedType b) => CompareMatches(a, b, query);
            var outcome = new SearchOutcome
            {
                Matches = new List<MatchedType>(),
                Candidates = new List<MatchedType>()
            };

            var seenMatches = new HashSet<string>(StringComparer.Ordinal);
            var seenCandidates = new HashSet<string>(StringComparer.Ordinal);

            foreach (var project in projects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!project.SupportsCompilation) continue;

                var compilation = await project.GetCompilationAsync(cancellationToken);
                if (compilation == null) continue;

                var index = GetOrBuildTypeIndex(project, compilation, cancellationToken);
                if (index == null || index.Types == null) continue;

                foreach (var indexed in index.Types)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (indexed.Symbol.DeclaredAccessibility == Accessibility.Private)
                        continue;

                    if (!MatchesQuery(indexed, query))
                        continue;

                    if (!MatchesNamespace(TypeInspector.GetNamespaceName(indexed.Symbol.ContainingNamespace), nsFilter)
                        || !MatchesAssembly(indexed.Symbol.ContainingAssembly?.Name, assemblyFilter))
                    {
                        if (seenCandidates.Add(indexed.MatchKey))
                        {
                            outcome.CandidateTotal++;
                            AddTopMatch(outcome.Candidates, ToMatchedType(indexed, project.Name), MaxCandidates, comparison);
                        }

                        continue;
                    }

                    if (seenMatches.Add(indexed.MatchKey))
                        AddTopMatch(outcome.Matches, ToMatchedType(indexed, project.Name), MaxTypeSearchMatches, comparison);
                }

                InternalLogger.Info($"[InspectType] project '{project.Name}': search '{typeName}' over {index.Types.Count} type(s) from cached index; keeping {outcome.Matches.Count} best match(es) and {outcome.CandidateTotal} candidate(s).");
            }

            outcome.MatchTotal = seenMatches.Count;
            return outcome;
        }

        /// <summary>
        /// Inserts <paramref name="item"/> into the ascending <paramref name="top"/> list and trims it to
        /// <paramref name="limit"/>, so a full scan never has to materialize all matches.
        /// </summary>
        internal static void AddTopMatch<T>(List<T> top, T item, int limit, Comparison<T> comparison)
        {
            if (top == null || comparison == null || limit <= 0)
                return;

            if (top.Count >= limit && comparison(top[top.Count - 1], item) <= 0)
                return;

            int position = 0;
            while (position < top.Count && comparison(top[position], item) <= 0)
                position++;

            if (position >= limit)
                return;

            top.Insert(position, item);
            if (top.Count > limit)
                top.RemoveAt(top.Count - 1);
        }

        /// <summary>Orders by rank, then by normalized full name so the output is stable.</summary>
        internal static int CompareMatches(MatchedType a, MatchedType b, TypeSearchQuery query)
        {
            int byRank = ComputeMatchRank(a, query).CompareTo(ComputeMatchRank(b, query));
            if (byRank != 0)
                return byRank;

            return string.Compare(a?.FullName ?? "", b?.FullName ?? "", StringComparison.Ordinal);
        }

        /// <summary>
        /// Lower is better: exact name (4) before declaration in source (2) before a name that starts with the query (1).
        /// </summary>
        internal static int ComputeMatchRank(MatchedType match, TypeSearchQuery query)
        {
            if (match == null || query == null)
                return int.MaxValue;

            bool exact = IsExactName(match, query);
            bool startsWith = query.HasNamespace
                ? (match.FullName ?? "").StartsWith(query.FullNameQuery ?? "", StringComparison.OrdinalIgnoreCase)
                : (match.ShortName ?? "").StartsWith(query.ShortName ?? "", StringComparison.OrdinalIgnoreCase);

            int rank = 0;
            if (!exact) rank += 4;
            if (!match.IsFromSource) rank += 2;
            if (!startsWith) rank += 1;
            return rank;
        }

        /// <summary>
        /// The type's name equals the query: the simple name for a short query, the full name for a namespace-qualified one
        /// (case-insensitive, generic arity ignored).
        /// </summary>
        internal static bool IsExactName(MatchedType match, TypeSearchQuery query)
        {
            if (match == null || query == null)
                return false;

            return query.HasNamespace
                ? string.Equals(match.FullName, query.FullNameQuery, StringComparison.OrdinalIgnoreCase)
                : string.Equals(match.ShortName, query.ShortName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Index of the only match whose name equals the query, or -1 when there is none or more than one.</summary>
        internal static int PickSingleExactMatch(IReadOnlyList<MatchedType> matches, TypeSearchQuery query)
        {
            if (matches == null)
                return -1;

            int found = -1;
            for (int i = 0; i < matches.Count; i++)
            {
                if (!IsExactName(matches[i], query))
                    continue;
                if (found >= 0)
                    return -1;
                found = i;
            }
            return found;
        }

        private static MatchedType ToMatchedType(IndexedType indexed, string projectName)
        {
            return new MatchedType
            {
                Symbol = indexed.Symbol,
                ProjectName = projectName,
                ShortName = indexed.ShortName,
                FullName = indexed.FullName,
                IsFromSource = !indexed.Symbol.DeclaringSyntaxReferences.IsEmpty
            };
        }

        private ProjectTypeIndex GetOrBuildTypeIndex(Project project, Compilation compilation, CancellationToken cancellationToken)
        {
            string cacheKey = "inspect_type:index:" + project.Id.Id.ToString();
            if (_searchCache.TryGet<ProjectTypeIndex>(cacheKey, "", out var cached)
                && cached.AllResults != null
                && cached.AllResults.Count > 0
                && ReferenceEquals(cached.AllResults[0].Compilation, compilation))
            {
                return cached.AllResults[0];
            }

            var index = BuildTypeIndex(compilation, cancellationToken);
            _searchCache.Set(cacheKey, "", new CachedToolResults<ProjectTypeIndex>
            {
                AllResults = new List<ProjectTypeIndex> { index },
                ItemsScanned = index.Types.Count
            });
            return index;
        }

        private static ProjectTypeIndex BuildTypeIndex(Compilation compilation, CancellationToken cancellationToken)
        {
            var types = new List<IndexedType>();
            foreach (var root in GetSearchRoots(compilation))
                WalkNamespaceForIndex(root, types, cancellationToken, 0);

            InternalLogger.Info($"[InspectType] built type index: {types.Count} type(s) from source + referenced assemblies.");
            return new ProjectTypeIndex { Compilation = compilation, Types = types };
        }

        private static List<INamespaceSymbol> GetSearchRoots(Compilation compilation)
        {
            var roots = new List<INamespaceSymbol>();
            if (compilation.GlobalNamespace != null)
                roots.Add(compilation.GlobalNamespace);

            foreach (var reference in compilation.References)
            {
                if (reference.Properties.Aliases.IsEmpty)
                    continue;

                bool hasGlobalAlias = false;
                foreach (var alias in reference.Properties.Aliases)
                {
                    if (string.Equals(alias, "global", StringComparison.Ordinal))
                    {
                        hasGlobalAlias = true;
                        break;
                    }
                }

                if (hasGlobalAlias)
                    continue;

                var symbol = compilation.GetAssemblyOrModuleSymbol(reference);
                var global = (symbol as IAssemblySymbol)?.GlobalNamespace
                             ?? (symbol as IModuleSymbol)?.GlobalNamespace;
                if (global != null)
                    roots.Add(global);
            }

            return roots;
        }

        private static void WalkNamespaceForIndex(INamespaceSymbol ns, List<IndexedType> types, CancellationToken cancellationToken, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ns == null || depth > 32)
                return;

            foreach (var type in ns.GetTypeMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (type.DeclaredAccessibility == Accessibility.Private)
                    continue;

                types.Add(ToIndexedType(type));
                WalkNestedTypesForIndex(type, types, cancellationToken, 0);
            }

            foreach (var child in ns.GetNamespaceMembers())
                WalkNamespaceForIndex(child, types, cancellationToken, depth + 1);
        }

        private static void WalkNestedTypesForIndex(INamedTypeSymbol type, List<IndexedType> types, CancellationToken cancellationToken, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (depth > 3)
                return;

            foreach (var nested in type.GetTypeMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (nested.DeclaredAccessibility == Accessibility.Private)
                    continue;

                types.Add(ToIndexedType(nested));
                WalkNestedTypesForIndex(nested, types, cancellationToken, depth + 1);
            }
        }

        private static IndexedType ToIndexedType(INamedTypeSymbol symbol)
        {
            return new IndexedType
            {
                Symbol = symbol,
                ShortName = symbol.Name,
                FullName = GetNormalizedFullName(symbol),
                MatchKey = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            };
        }

        private static string GetNormalizedFullName(INamedTypeSymbol symbol)
        {
            var parts = new List<string>();
            for (var type = symbol; type != null; type = type.ContainingType)
                parts.Insert(0, type.Name);

            string body = string.Join(".", parts);
            string ns = TypeInspector.GetNamespaceName(symbol.ContainingNamespace);
            return string.IsNullOrEmpty(ns) ? body : ns + "." + body;
        }

        internal static bool MatchesQuery(IndexedType indexed, TypeSearchQuery query)
        {
            if (!query.HasNamespace)
                return indexed.ShortName.IndexOf(query.ShortName, StringComparison.OrdinalIgnoreCase) >= 0;

            return IsSegmentMatch(indexed.FullName, query.FullNameQuery);
        }

        internal static bool IsSegmentMatch(string fullName, string query)
        {
            if (string.IsNullOrEmpty(fullName) || string.IsNullOrEmpty(query))
                return false;

            if (fullName.Equals(query, StringComparison.OrdinalIgnoreCase))
                return true;

            if (fullName.StartsWith(query + ".", StringComparison.OrdinalIgnoreCase))
                return true;

            if (fullName.IndexOf("." + query + ".", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return fullName.EndsWith("." + query, StringComparison.OrdinalIgnoreCase);
        }

        internal class TypeSearchQuery
        {
            public string ShortName;
            public string FullNameQuery;
            public bool HasNamespace;

            public static TypeSearchQuery Create(string typeName)
            {
                if (string.IsNullOrEmpty(typeName))
                    return new TypeSearchQuery { ShortName = typeName };

                int lastDot = typeName.LastIndexOf('.');
                string shortName = lastDot >= 0 ? typeName.Substring(lastDot + 1) : typeName;
                shortName = StripArity(shortName);

                bool hasNamespace = lastDot > 0;
                string fullNameQuery = null;
                if (hasNamespace)
                    fullNameQuery = StripArity(typeName);

                return new TypeSearchQuery
                {
                    ShortName = shortName,
                    FullNameQuery = fullNameQuery,
                    HasNamespace = hasNamespace
                };
            }

            private static string StripArity(string name)
            {
                if (string.IsNullOrEmpty(name))
                    return name;

                int tick = name.IndexOf('`');
                return tick > 0 ? name.Substring(0, tick) : name;
            }
        }

        private class ProjectTypeIndex
        {
            public Compilation Compilation;
            public List<IndexedType> Types;
        }

        private sealed class SearchOutcome
        {
            /// <summary>Best matches after ranking, capped at <see cref="MaxTypeSearchMatches"/>.</summary>
            public List<MatchedType> Matches;
            /// <summary>Best candidates rejected by the filters, capped at <see cref="MaxCandidates"/>.</summary>
            public List<MatchedType> Candidates;
            /// <summary>Distinct candidates seen by name, including the ones not shown.</summary>
            public int CandidateTotal;
            /// <summary>Distinct matches seen, including the ones cut by the cap.</summary>
            public int MatchTotal;
        }

        internal class IndexedType
        {
            public INamedTypeSymbol Symbol;
            public string ShortName;
            public string FullName;
            public string MatchKey;
        }

        /// <summary>One type found by name, with the project it was found in and the inputs of the ranking.</summary>
        internal sealed class MatchedType
        {
            public INamedTypeSymbol Symbol;
            public string ProjectName;
            public string ShortName;
            public string FullName;
            public bool IsFromSource;
        }

        internal static bool MatchesNamespace(string ns, string nsFilter)
        {
            if (string.IsNullOrEmpty(nsFilter))
                return true;

            return !string.IsNullOrEmpty(ns) && ns.IndexOf(nsFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool MatchesAssembly(string assemblyName, string assemblyFilter)
        {
            if (string.IsNullOrEmpty(assemblyFilter))
                return true;

            return !string.IsNullOrEmpty(assemblyName) && assemblyName.IndexOf(assemblyFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static string BuildNotFoundMessage(string typeName, bool searchMode, string nsFilter, string assemblyFilter)
        {
            if (!string.IsNullOrEmpty(nsFilter) || !string.IsNullOrEmpty(assemblyFilter))
                return $"Type '{typeName}' not found with the given parameters.";

            return searchMode
                ? $"No types matching '{typeName}' were found in the current solution."
                : $"Type '{typeName}' not found in any project or referenced assembly.";
        }

        internal static string BuildCandidatesMessage(string typeName, int totalCount, int shownCount)
        {
            string countText = totalCount == 1
                ? "1 candidate matches"
                : $"{totalCount} candidates match";

            string message = $"Type '{typeName}' not found with the given parameters. {countText} by name but not by namespace/assembly_name";

            if (totalCount > shownCount)
                message += $"; showing the first {shownCount}";

            return message + ".";
        }

        private static string BuildFilteredOutMessage(string typeName, string nsFilter, string assemblyFilter)
        {
            var filters = new List<string>();
            if (!string.IsNullOrEmpty(nsFilter))
                filters.Add($"namespace '{nsFilter}'");
            if (!string.IsNullOrEmpty(assemblyFilter))
                filters.Add($"assembly '{assemblyFilter}'");

            return $"Type '{typeName}' was found but did not match {string.Join(" and ", filters)}.";
        }

        private static TypeSearchMatch ToTypeSearchMatch(MatchedType match)
        {
            var symbol = match.Symbol;
            return new TypeSearchMatch
            {
                FullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                MetadataName = symbol.MetadataName,
                Kind = symbol.TypeKind.ToString(),
                Accessibility = symbol.DeclaredAccessibility.ToString(),
                ContainingNamespace = TypeInspector.GetNamespaceName(symbol.ContainingNamespace),
                AssemblyName = symbol.ContainingAssembly?.Name ?? "Unknown",
                ProjectName = match.ProjectName,
                IsGeneric = symbol.IsGenericType,
                Arity = symbol.Arity
            };
        }

        internal static (string typeName, string projectName, string nsFilter, string assemblyFilter, bool searchMode, string error) ExtractAndValidateParameters(
            Dictionary<string, object> parameters)
        {
            if (parameters == null || !parameters.TryGetValue(TypeNameParameter, out object typeObj) || !(typeObj is string rawTypeName))
                return (null, null, null, null, false, $"Parameter '{TypeNameParameter}' (or '{TypeNamesParameter}') is required and must be a string.");

            var (typeName, nameError) = NormalizeTypeName(rawTypeName);
            if (nameError != null)
                return (null, null, null, null, false, nameError);

            var (projectName, nsFilter, assemblyFilter, searchMode) = ExtractFilters(parameters);
            return (typeName, projectName, nsFilter, assemblyFilter, searchMode, null);
        }

        /// <summary>Trims the name and drops a leading 'global::'; an empty name or '.' is an error.</summary>
        internal static (string typeName, string error) NormalizeTypeName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return (null, $"Parameter '{TypeNameParameter}' cannot be empty.");

            typeName = typeName.Trim();
            const string GlobalAlias = "global::";
            if (typeName.StartsWith(GlobalAlias, StringComparison.Ordinal))
                typeName = typeName.Substring(GlobalAlias.Length);

            if (typeName.Length == 0)
                return (null, $"Parameter '{TypeNameParameter}' cannot be empty.");

            if (typeName == ".")
                return (null, $"Parameter '{TypeNameParameter}' cannot be '.'.");

            return (typeName, null);
        }

        /// <summary>
        /// Names of a 'type_names' call: a top-level type_name first, then the list; exact duplicates (after trimming) dropped.
        /// </summary>
        internal static (List<string> names, string error) ExtractTypeNames(Dictionary<string, object> parameters, object namesObj)
        {
            object nameObj = null;
            parameters?.TryGetValue(TypeNameParameter, out nameObj);
            if (nameObj != null && !(nameObj is string))
                return (null, $"Parameter '{TypeNameParameter}' must be a string.");

            var raw = new List<string>();
            var seenRaw = new HashSet<string>(StringComparer.Ordinal);
            LiteralListParameter.Add(nameObj as string, raw, seenRaw);
            if (!LiteralListParameter.TryAdd(namesObj, raw, seenRaw))
                return (null, $"Parameter '{TypeNamesParameter}' must be an array of strings.");

            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in raw)
                LiteralListParameter.Add(name.Trim(), names, seen);

            if (names.Count == 0)
                return (names, $"Parameter '{TypeNamesParameter}' has no non-empty name.");
            if (names.Count > MaxTypeNames)
                return (names, $"At most {MaxTypeNames} type names per call (got {names.Count}). Split them into several calls.");

            return (names, null);
        }

        internal static (string projectName, string nsFilter, string assemblyFilter, bool searchMode) ExtractFilters(Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return (null, null, null, false);

            string projectName = null;
            if (parameters.TryGetValue("project_name", out object projObj) && projObj is string projStr && !string.IsNullOrWhiteSpace(projStr))
                projectName = projStr.Trim();

            string nsFilter = null;
            if (parameters.TryGetValue("namespace", out object nsObj) && nsObj is string nsStr && !string.IsNullOrWhiteSpace(nsStr))
                nsFilter = nsStr.Trim();

            string assemblyFilter = null;
            if (parameters.TryGetValue("assembly_name", out object asmObj) && asmObj is string asmStr && !string.IsNullOrWhiteSpace(asmStr))
                assemblyFilter = asmStr.Trim();

            bool searchMode = false;
            if (parameters.TryGetValue("search_mode", out object searchObj) && searchObj is bool searchBool)
                searchMode = searchBool;

            return (projectName, nsFilter, assemblyFilter, searchMode);
        }

        private static TypeInspectionResponse ErrorResponse(string typeName, string message, List<TypeSearchMatch> candidates = null)
        {
            return new TypeInspectionResponse
            {
                Success = false,
                ErrorMessage = message,
                TypeName = typeName,
                Candidates = candidates
            };
        }

        private static TypeInspectionResponse SuccessResponse(string typeName, TypeInspectionResult result)
        {
            return new TypeInspectionResponse
            {
                Success = true,
                TypeName = typeName,
                Data = result
            };
        }

        private static TypeInspectionResponse MatchResponse(string typeName, List<TypeSearchMatch> matches, int matchTotal)
        {
            return new TypeInspectionResponse
            {
                Success = true,
                TypeName = typeName,
                Matches = matches,
                TotalMatches = matchTotal > (matches?.Count ?? 0) ? (int?)matchTotal : null
            };
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            var typeName = parameters?.TryGetValue(TypeNameParameter, out var s) == true ? s?.ToString() : "";
            var searchMode = parameters?.TryGetValue("search_mode", out var m) == true && m is bool searchBool && searchBool;
            var nsFilter = parameters?.TryGetValue("namespace", out var n) == true ? n?.ToString() : null;
            var assemblyFilter = parameters?.TryGetValue("assembly_name", out var a) == true ? a?.ToString() : null;

            List<string> names = null;
            object namesObj = null;
            parameters?.TryGetValue(TypeNamesParameter, out namesObj);
            if (!LiteralListParameter.IsMissing(namesObj))
                names = ExtractTypeNames(parameters, namesObj).names;

            string message;
            if (names != null && names.Count > 0)
            {
                string list = string.Join(", ", names.Select(t => $"'{t}'"));
                message = searchMode ? $"Searching for types matching {list}" : $"Inspecting types {list}";
            }
            else
            {
                message = searchMode
                    ? $"Searching for types matching '{typeName}'"
                    : $"Inspecting type '{typeName}'";
            }

            if (!string.IsNullOrEmpty(nsFilter))
                message += $" in namespace '{nsFilter}'";

            if (!string.IsNullOrEmpty(assemblyFilter))
                message += $" in assembly '{assemblyFilter}'";

            return message + "...";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is TypeInspectionResponse resp)
            {
                if (!resp.Success)
                    return $"Inspection failed: {resp.ErrorMessage}";

                var data = resp.Data;
                if (data == null)
                {
                    if (resp.Matches != null && resp.Matches.Count > 0)
                    {
                        int foundCount = resp.TotalMatches ?? resp.Matches.Count;
                        string matchMessage = $"Found {foundCount} {Pluralizer.Pluralize(foundCount, "type", "types")} matching '{resp.TypeName}'";
                        if (resp.TotalMatches.HasValue)
                            matchMessage += $" (capped at {MaxTypeSearchMatches})";
                        return matchMessage + ".";
                    }

                    return "Type inspection completed.";
                }

                var memberCounts = new List<string>();
                if (data.TotalConstructors > 0)
                    memberCounts.Add($"{data.TotalConstructors} {Pluralizer.Pluralize(data.TotalConstructors, "constructor", "constructors")}");
                if (data.TotalProperties > 0)
                    memberCounts.Add($"{data.TotalProperties} {Pluralizer.Pluralize(data.TotalProperties, "property", "properties")}");
                if (data.TotalMethods > 0)
                    memberCounts.Add($"{data.TotalMethods} {Pluralizer.Pluralize(data.TotalMethods, "method", "methods")}");
                if (data.TotalEvents > 0)
                    memberCounts.Add($"{data.TotalEvents} {Pluralizer.Pluralize(data.TotalEvents, "event", "events")}");
                if (data.TotalFields > 0)
                    memberCounts.Add($"{data.TotalFields} {Pluralizer.Pluralize(data.TotalFields, "field", "fields")}");

                string message = $"Inspected {data.FullName}: {(memberCounts.Count > 0 ? string.Join(", ", memberCounts) : "no members")}";
                if (data.HasMoreResults)
                    message += " (truncated)";
                if (resp.OtherMatches != null && resp.OtherMatches.Count > 0)
                {
                    int others = resp.TotalMatches.HasValue ? resp.TotalMatches.Value - 1 : resp.OtherMatches.Count;
                    message += $"; {others} other {Pluralizer.Pluralize(others, "match", "matches")}";
                }
                return message;
            }

            if (result is TypeInspectionMultiResponse multi)
            {
                var blocks = multi.Types ?? new List<TypeInspectionResponse>();
                if (blocks.Count == 0)
                    return $"Inspection failed: {multi.ErrorMessage}";

                int inspected = blocks.Count(b => b.Success && b.Data != null);
                var states = string.Join(", ", blocks.Select(b => $"'{b.TypeName}': {DescribeBlock(b)}"));
                return $"Inspected {inspected} of {blocks.Count} {Pluralizer.Pluralize(blocks.Count, "type", "types")} ({states}).";
            }

            return "Type inspection completed.";
        }

        private static string DescribeBlock(TypeInspectionResponse block)
        {
            if (!block.Success)
                return "failed";
            if (block.Data != null)
                return "inspected";

            int count = block.TotalMatches ?? block.Matches?.Count ?? 0;
            return $"{count} {Pluralizer.Pluralize(count, "match", "matches")}";
        }

        #region DTOs

        public class TypeSearchMatch
        {
            [JsonProperty("full_name")]
            public string FullName { get; set; }
            [JsonProperty("metadata_name")]
            public string MetadataName { get; set; }
            [JsonProperty("kind")]
            public string Kind { get; set; }
            [JsonProperty("accessibility")]
            public string Accessibility { get; set; }
            [JsonProperty("containing_namespace")]
            public string ContainingNamespace { get; set; }
            [JsonProperty("assembly_name")]
            public string AssemblyName { get; set; }
            [JsonProperty("project_name")]
            public string ProjectName { get; set; }
            [JsonProperty("is_generic", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public bool IsGeneric { get; set; }
            [JsonProperty("arity")]
            public int Arity { get; set; }
        }

        public class TypeInspectionResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }
            [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
            public string ErrorMessage { get; set; }
            [JsonProperty("type_name", NullValueHandling = NullValueHandling.Ignore)]
            public string TypeName { get; set; }
            [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
            public TypeInspectionResult Data { get; set; }
            [JsonProperty("matches", NullValueHandling = NullValueHandling.Ignore)]
            public List<TypeSearchMatch> Matches { get; set; }
            [JsonProperty("total_matches", NullValueHandling = NullValueHandling.Ignore)]
            public int? TotalMatches { get; set; }
            [JsonProperty("candidates", NullValueHandling = NullValueHandling.Ignore)]
            public List<TypeSearchMatch> Candidates { get; set; }
            /// <summary>Full names of the other search matches when 'data' is the only exact-name match.</summary>
            [JsonProperty("other_matches", NullValueHandling = NullValueHandling.Ignore)]
            public List<string> OtherMatches { get; set; }
        }

        /// <summary>Response of a 'type_names' call: one single-name response per name, in request order.</summary>
        public class TypeInspectionMultiResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }
            [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
            public string ErrorMessage { get; set; }
            [JsonProperty("types")]
            public List<TypeInspectionResponse> Types { get; set; }
        }

        #endregion
    }
}
