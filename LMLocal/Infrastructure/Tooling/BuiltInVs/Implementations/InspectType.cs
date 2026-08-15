using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Inspects a fully qualified type (from source or any referenced assembly) and returns its
    /// full metadata: constructors, properties, methods, fields, attributes, base types, interfaces
    /// and generic parameters.
    /// Supports exact lookup via metadata name, and partial case-insensitive search (including NuGet packages).
    /// </summary>
    internal interface IInspectType : IBuiltInTool
    {
    }

    internal class InspectType : IInspectType
    {
        private readonly ISearchResultCache _searchCache;
        private const int MaxMembersPerCategory = 100;
        private const int MaxTypeSearchMatches = 50;

        public string ToolName => "inspect_type";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.ReadOnly;

        public InspectType(ISearchResultCache searchCache)
        {
            _searchCache = searchCache ?? throw new ArgumentNullException(nameof(searchCache));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Performs deep structural inspection of a type. " +
                               "If exact metadata name is given (e.g., 'System.String', 'System.Collections.Generic.List`1'), returns full details. " +
                               "If not found, or if 'search_mode' is true, performs a partial case-insensitive search over all types (including NuGet packages). " +
                               "A short name (e.g. 'Proposal') is matched as a substring of the type's simple name; " +
                               "a namespace-qualified name (e.g. 'Microsoft.VisualStudio.Language.Proposals' or 'System.Collections.Generic.List') is matched against the fully qualified name by dot-separated segments. " +
                               "Optionally filter by 'project_name', 'namespace' and/or 'assembly_name' (NuGet package id usually matches the assembly name). " +
                               "Generic types use arity for exact lookup (e.g., 'System.Collections.Generic.List`1'). " +
                               "Member lists are capped at 100 per category; totals and a 'truncated' flag are included. " +
                               "Partial search results are capped at 50 matches.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "type_name", new ToolDetails { Type = "string", Description = "Type name: full metadata name for exact match (e.g. 'System.Collections.Generic.List`1'), or partial name for search. In search mode a short name is matched as a substring of the simple name, and a namespace-qualified name (e.g. 'Microsoft.VisualStudio.Language.Proposals') is matched against the fully qualified name by dot-separated segments." } },
                        { "project_name", new ToolDetails { Type = "string", Description = "Name of the project to search in. If omitted, all projects." } },
                        { "namespace", new ToolDetails { Type = "string", Description = "Filter results by containing namespace (case-insensitive partial match)." } },
                        { "assembly_name", new ToolDetails { Type = "string", Description = "Filter results by containing assembly name (case-insensitive partial match). Useful for listing types from a NuGet package; package id usually equals the assembly name." } },
                        { "search_mode", new ToolDetails { Type = "boolean", Description = "If true, skip exact metadata name lookup and perform a partial case-insensitive search directly (capped at 50 matches)." } }
                    },
                    Required = new List<string> { "type_name" }
                }
            };
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            try
            {
                var (typeName, projectName, nsFilter, assemblyFilter, searchMode, error) = ExtractAndValidateParameters(parameters);
                if (!string.IsNullOrEmpty(error))
                    return ErrorResponse(null, error);

                string cacheKey = $"inspect_type||{typeName}||{projectName ?? ""}||{nsFilter ?? ""}||{assemblyFilter ?? ""}||{searchMode}";
                if (_searchCache.TryGet(cacheKey, "", out CachedToolResults<TypeInspectionResult> cached))
                {
                    var cachedResult = cached.AllResults?.FirstOrDefault();
                    if (cachedResult != null)
                        return SuccessResponse(typeName, cachedResult);
                }

                var (result, matches, coreError) = await ExecuteCoreAsync(typeName, projectName, nsFilter, assemblyFilter, searchMode, cancellationToken);
                if (!string.IsNullOrEmpty(coreError))
                    return ErrorResponse(typeName, coreError);

                if (result == null)
                    return MatchResponse(typeName, matches);

                _searchCache.Set(cacheKey, "", new CachedToolResults<TypeInspectionResult>
                {
                    AllResults = new List<TypeInspectionResult> { result },
                    ItemsScanned = 1
                });

                return SuccessResponse(typeName, result);
            }
            catch (OperationCanceledException)
            {
                return ErrorResponse(null, "Operation was cancelled.");
            }
            catch (Exception ex)
            {
                return ErrorResponse(null, $"Internal error: {ex.Message}");
            }
        }

        private async Task<(TypeInspectionResult result, List<TypeSearchMatch> matches, string error)> ExecuteCoreAsync(
            string typeName, string projectName, string nsFilter, string assemblyFilter, bool searchMode, CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var componentModel = (IComponentModel)ServiceProvider.GlobalProvider.GetService(typeof(SComponentModel));
            if (componentModel == null)
                throw new InvalidOperationException("Component model is not available.");

            var workspace = componentModel.GetService<VisualStudioWorkspace>();
            if (workspace == null)
                throw new InvalidOperationException("Visual Studio workspace is not available.");

            var solution = workspace.CurrentSolution;
            if (solution == null)
                throw new InvalidOperationException("No solution is currently open.");

            var projects = solution.Projects.ToList();
            if (!string.IsNullOrEmpty(projectName))
                projects = projects.Where(p => string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase)).ToList();

            if (projects.Count == 0)
                return (null, null, $"Project '{projectName}' was not found in the current solution.");

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

                        if (!MatchesNamespace(typeSymbol.ContainingNamespace?.ToString(), nsFilter)
                            || !MatchesAssembly(typeSymbol.ContainingAssembly?.Name, assemblyFilter))
                        {
                            exactFilteredOut = true;
                            continue;
                        }

                        return (InspectSymbol(typeSymbol, project.Name), (List<TypeSearchMatch>)null, (string)null);
                    }
                }

                // 2. Partial search over an indexed snapshot of source + referenced assemblies.
                var matches = await FindTypeMatchesAsync(projects, typeName, nsFilter, assemblyFilter, cancellationToken);

                if (matches.Count == 0)
                {
                    if (!searchMode && exactFilteredOut)
                    {
                        return ((TypeInspectionResult)null, (List<TypeSearchMatch>)null,
                            BuildFilteredOutMessage(typeName, nsFilter, assemblyFilter));
                    }

                    string notFound = searchMode
                        ? BuildSearchNotFound(typeName, nsFilter, assemblyFilter)
                        : $"Type '{typeName}' not found in any project or referenced assembly.";
                    return ((TypeInspectionResult)null, (List<TypeSearchMatch>)null, notFound);
                }

                if (matches.Count == 1)
                    return (InspectSymbol(matches[0].Symbol, matches[0].ProjectName), (List<TypeSearchMatch>)null, (string)null);

                return ((TypeInspectionResult)null, matches.Select(ToTypeSearchMatch).ToList(), (string)null);
            }, cancellationToken);
        }

        private async Task<List<(INamedTypeSymbol Symbol, string ProjectName)>> FindTypeMatchesAsync(
            List<Project> projects, string typeName, string nsFilter, string assemblyFilter, CancellationToken cancellationToken)
        {
            var matches = new List<(INamedTypeSymbol Symbol, string ProjectName)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var query = TypeSearchQuery.Create(typeName);

            foreach (var project in projects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!project.SupportsCompilation) continue;

                var compilation = await project.GetCompilationAsync(cancellationToken);
                if (compilation == null) continue;

                var index = GetOrBuildTypeIndex(project, compilation, cancellationToken);
                if (index == null || index.Types == null) continue;

                int before = matches.Count;
                foreach (var indexed in index.Types)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (indexed.Symbol.DeclaredAccessibility == Accessibility.Private)
                        continue;

                    if (!MatchesQuery(indexed, query))
                        continue;

                    if (!MatchesNamespace(indexed.Symbol.ContainingNamespace?.ToString(), nsFilter))
                        continue;

                    if (!MatchesAssembly(indexed.Symbol.ContainingAssembly?.Name, assemblyFilter))
                        continue;

                    if (!seen.Add(indexed.MatchKey))
                        continue;

                    matches.Add((indexed.Symbol, project.Name));
                    if (matches.Count >= MaxTypeSearchMatches)
                        return OrderMatches(matches);
                }

                InternalLogger.Info($"[InspectType] project '{project.Name}': search '{typeName}' added {matches.Count - before} match(es) from cached index ({index.Types.Count} type(s)).");
            }

            return OrderMatches(matches);
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
            string ns = symbol.ContainingNamespace?.ToString();
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

        internal class IndexedType
        {
            public INamedTypeSymbol Symbol;
            public string ShortName;
            public string FullName;
            public string MatchKey;
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

        private static string BuildSearchNotFound(string typeName, string nsFilter, string assemblyFilter)
        {
            var filters = new List<string>();
            if (!string.IsNullOrEmpty(nsFilter))
                filters.Add($"namespace '{nsFilter}'");
            if (!string.IsNullOrEmpty(assemblyFilter))
                filters.Add($"assembly '{assemblyFilter}'");

            if (filters.Count == 0)
                return $"No types matching '{typeName}' were found in the current solution.";

            return $"No types matching '{typeName}' were found in {string.Join(" and ", filters)}.";
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

        private static List<(INamedTypeSymbol Symbol, string ProjectName)> OrderMatches(
            List<(INamedTypeSymbol Symbol, string ProjectName)> matches)
        {
            return matches
                .OrderBy(m => m.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
                .ToList();
        }

        private static TypeSearchMatch ToTypeSearchMatch((INamedTypeSymbol Symbol, string ProjectName) match)
        {
            var symbol = match.Symbol;
            return new TypeSearchMatch
            {
                FullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                MetadataName = symbol.MetadataName,
                Kind = symbol.TypeKind.ToString(),
                Accessibility = symbol.DeclaredAccessibility.ToString(),
                ContainingNamespace = symbol.ContainingNamespace?.ToString() ?? "",
                AssemblyName = symbol.ContainingAssembly?.Name ?? "Unknown",
                ProjectName = match.ProjectName,
                IsGeneric = symbol.IsGenericType,
                Arity = symbol.Arity
            };
        }

        private static TypeInspectionResult InspectSymbol(INamedTypeSymbol symbol, string projectName)
        {
            var result = new TypeInspectionResult
            {
                FullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                MetadataName = symbol.MetadataName,
                Kind = symbol.TypeKind.ToString(),
                Accessibility = symbol.DeclaredAccessibility.ToString(),
                ContainingNamespace = symbol.ContainingNamespace?.ToString() ?? "",
                AssemblyName = symbol.ContainingAssembly?.Name ?? "Unknown",
                ProjectName = projectName,
                IsAbstract = symbol.IsAbstract,
                IsSealed = symbol.IsSealed,
                IsStatic = symbol.IsStatic,
                IsGenericType = symbol.IsGenericType,
                Arity = symbol.Arity,
                TypeParameters = symbol.TypeParameters.Select(tp => tp.Name).ToList(),
                BaseType = symbol.BaseType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Interfaces = symbol.AllInterfaces
                    .Select(i => i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .Distinct()
                    .ToList(),
                Attributes = symbol.GetAttributes()
                    .Select(a => new AttributeInfo
                    {
                        Name = a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "Unknown",
                        ConstructorArguments = a.ConstructorArguments.Select(c => c.Value?.ToString()).ToList()
                    })
                    .ToList()
            };

            var members = symbol.GetMembers();

            // Constructors (public + internal).
            var constructors = symbol.InstanceConstructors
                .Where(c => c.DeclaredAccessibility == Accessibility.Public || c.DeclaredAccessibility == Accessibility.Internal)
                .ToList();
            result.TotalConstructors = constructors.Count;
            result.Constructors = constructors
                .Take(MaxMembersPerCategory)
                .Select(c => new MethodInfo
                {
                    Name = c.Name,
                    Accessibility = c.DeclaredAccessibility.ToString(),
                    IsStatic = c.IsStatic,
                    Parameters = c.Parameters.Select(p => new ParamInfo
                    {
                        Name = p.Name,
                        Type = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    }).ToList()
                })
                .ToList();

            // Properties (public).
            var properties = members.OfType<IPropertySymbol>()
                .Where(p => p.DeclaredAccessibility == Accessibility.Public)
                .ToList();
            result.TotalProperties = properties.Count;
            result.Properties = properties
                .Take(MaxMembersPerCategory)
                .Select(p => new PropertyInfo
                {
                    Name = p.Name,
                    Type = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    IsStatic = p.IsStatic,
                    IsReadOnly = p.IsReadOnly,
                    IsWriteOnly = p.IsWriteOnly,
                    HasGetter = p.GetMethod != null,
                    HasSetter = p.SetMethod != null,
                    Accessibility = p.DeclaredAccessibility.ToString()
                })
                .ToList();

            // Methods (public, excluding constructors and property accessors).
            var methods = members.OfType<IMethodSymbol>()
                .Where(m => m.DeclaredAccessibility == Accessibility.Public
                            && m.MethodKind != MethodKind.Constructor
                            && m.MethodKind != MethodKind.PropertyGet
                            && m.MethodKind != MethodKind.PropertySet)
                .ToList();
            result.TotalMethods = methods.Count;
            result.Methods = methods
                .Take(MaxMembersPerCategory)
                .Select(m => new MethodInfo
                {
                    Name = m.Name,
                    Accessibility = m.DeclaredAccessibility.ToString(),
                    IsStatic = m.IsStatic,
                    IsExtension = m.IsExtensionMethod,
                    IsGeneric = m.IsGenericMethod,
                    ReturnType = m.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    TypeParameters = m.TypeParameters.Select(tp => tp.Name).ToList(),
                    Parameters = m.Parameters.Select(p => new ParamInfo
                    {
                        Name = p.Name,
                        Type = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    }).ToList()
                })
                .ToList();

            // Fields (public).
            var fields = members.OfType<IFieldSymbol>()
                .Where(f => f.DeclaredAccessibility == Accessibility.Public)
                .ToList();
            result.TotalFields = fields.Count;
            result.Fields = fields
                .Take(MaxMembersPerCategory)
                .Select(f => new FieldInfo
                {
                    Name = f.Name,
                    Type = f.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    IsStatic = f.IsStatic,
                    IsReadOnly = f.IsReadOnly,
                    IsConst = f.IsConst,
                    Accessibility = f.DeclaredAccessibility.ToString()
                })
                .ToList();

            result.Truncated =
                result.TotalConstructors > MaxMembersPerCategory ||
                result.TotalProperties > MaxMembersPerCategory ||
                result.TotalMethods > MaxMembersPerCategory ||
                result.TotalFields > MaxMembersPerCategory;

            return result;
        }

        internal static (string typeName, string projectName, string nsFilter, string assemblyFilter, bool searchMode, string error) ExtractAndValidateParameters(
            Dictionary<string, object> parameters)
        {
            if (parameters == null || !parameters.TryGetValue("type_name", out object typeObj) || !(typeObj is string typeName))
                return (null, null, null, null, false, "Parameter 'type_name' is required and must be a string.");

            if (string.IsNullOrWhiteSpace(typeName))
                return (null, null, null, null, false, "Parameter 'type_name' cannot be empty.");

            string projectName = null;
            if (parameters.TryGetValue("project_name", out object projObj) && projObj is string projStr && !string.IsNullOrWhiteSpace(projStr))
                projectName = projStr;

            string nsFilter = null;
            if (parameters.TryGetValue("namespace", out object nsObj) && nsObj is string nsStr && !string.IsNullOrWhiteSpace(nsStr))
                nsFilter = nsStr;

            string assemblyFilter = null;
            if (parameters.TryGetValue("assembly_name", out object asmObj) && asmObj is string asmStr && !string.IsNullOrWhiteSpace(asmStr))
                assemblyFilter = asmStr;

            bool searchMode = false;
            if (parameters.TryGetValue("search_mode", out object searchObj) && searchObj is bool searchBool)
                searchMode = searchBool;

            return (typeName, projectName, nsFilter, assemblyFilter, searchMode, null);
        }

        private static TypeInspectionResponse ErrorResponse(string typeName, string message)
        {
            return new TypeInspectionResponse
            {
                Success = false,
                ErrorMessage = message,
                TypeName = typeName
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

        private static TypeInspectionResponse MatchResponse(string typeName, List<TypeSearchMatch> matches)
        {
            return new TypeInspectionResponse
            {
                Success = true,
                TypeName = typeName,
                Matches = matches
            };
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            var typeName = parameters?.TryGetValue("type_name", out var s) == true ? s?.ToString() : "";
            var searchMode = parameters?.TryGetValue("search_mode", out var m) == true && m is bool searchBool && searchBool;
            var nsFilter = parameters?.TryGetValue("namespace", out var n) == true ? n?.ToString() : null;
            var assemblyFilter = parameters?.TryGetValue("assembly_name", out var a) == true ? a?.ToString() : null;

            string message = searchMode
                ? $"Searching for types matching '{typeName}'"
                : $"Inspecting type '{typeName}'";

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
                        string matchMessage = $"Found {resp.Matches.Count} {Pluralizer.Pluralize(resp.Matches.Count, "type", "types")} matching '{resp.TypeName}'";
                        if (resp.Matches.Count >= MaxTypeSearchMatches)
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
                if (data.TotalFields > 0)
                    memberCounts.Add($"{data.TotalFields} {Pluralizer.Pluralize(data.TotalFields, "field", "fields")}");

                string message = $"Inspected {data.FullName}: {(memberCounts.Count > 0 ? string.Join(", ", memberCounts) : "no members")}";
                if (data.Truncated)
                    message += " (truncated)";
                return message;
            }
            return "Type inspection completed.";
        }

        #region DTOs

        public class ParamInfo
        {
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("type")]
            public string Type { get; set; }
        }

        public class MethodInfo
        {
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("accessibility")]
            public string Accessibility { get; set; }
            [JsonProperty("is_static")]
            public bool IsStatic { get; set; }
            [JsonProperty("is_extension", NullValueHandling = NullValueHandling.Ignore)]
            public bool IsExtension { get; set; }
            [JsonProperty("is_generic", NullValueHandling = NullValueHandling.Ignore)]
            public bool IsGeneric { get; set; }
            [JsonProperty("return_type", NullValueHandling = NullValueHandling.Ignore)]
            public string ReturnType { get; set; }
            [JsonProperty("type_parameters", NullValueHandling = NullValueHandling.Ignore)]
            public List<string> TypeParameters { get; set; }
            [JsonProperty("parameters")]
            public List<ParamInfo> Parameters { get; set; }
        }

        public class PropertyInfo
        {
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("type")]
            public string Type { get; set; }
            [JsonProperty("accessibility")]
            public string Accessibility { get; set; }
            [JsonProperty("is_static")]
            public bool IsStatic { get; set; }
            [JsonProperty("is_read_only")]
            public bool IsReadOnly { get; set; }
            [JsonProperty("is_write_only")]
            public bool IsWriteOnly { get; set; }
            [JsonProperty("has_getter")]
            public bool HasGetter { get; set; }
            [JsonProperty("has_setter")]
            public bool HasSetter { get; set; }
        }

        public class FieldInfo
        {
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("type")]
            public string Type { get; set; }
            [JsonProperty("accessibility")]
            public string Accessibility { get; set; }
            [JsonProperty("is_static")]
            public bool IsStatic { get; set; }
            [JsonProperty("is_read_only")]
            public bool IsReadOnly { get; set; }
            [JsonProperty("is_const")]
            public bool IsConst { get; set; }
        }

        public class AttributeInfo
        {
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("constructor_arguments")]
            public List<string> ConstructorArguments { get; set; }
        }

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
            [JsonProperty("is_generic")]
            public bool IsGeneric { get; set; }
            [JsonProperty("arity")]
            public int Arity { get; set; }
        }

        public class TypeInspectionResult
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
            [JsonProperty("is_abstract")]
            public bool IsAbstract { get; set; }
            [JsonProperty("is_sealed")]
            public bool IsSealed { get; set; }
            [JsonProperty("is_static")]
            public bool IsStatic { get; set; }
            [JsonProperty("is_generic")]
            public bool IsGenericType { get; set; }
            [JsonProperty("arity")]
            public int Arity { get; set; }
            [JsonProperty("type_parameters")]
            public List<string> TypeParameters { get; set; }
            [JsonProperty("base_type")]
            public string BaseType { get; set; }
            [JsonProperty("interfaces")]
            public List<string> Interfaces { get; set; }
            [JsonProperty("attributes")]
            public List<AttributeInfo> Attributes { get; set; }
            [JsonProperty("constructors")]
            public List<MethodInfo> Constructors { get; set; }
            [JsonProperty("total_constructors")]
            public int TotalConstructors { get; set; }
            [JsonProperty("properties")]
            public List<PropertyInfo> Properties { get; set; }
            [JsonProperty("total_properties")]
            public int TotalProperties { get; set; }
            [JsonProperty("methods")]
            public List<MethodInfo> Methods { get; set; }
            [JsonProperty("total_methods")]
            public int TotalMethods { get; set; }
            [JsonProperty("fields")]
            public List<FieldInfo> Fields { get; set; }
            [JsonProperty("total_fields")]
            public int TotalFields { get; set; }
            [JsonProperty("truncated")]
            public bool Truncated { get; set; }
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
        }

        #endregion
    }
}
