using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSharpSyntax = Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSolution = Microsoft.CodeAnalysis.Solution;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Tool for inspecting a type (class, struct, interface, record, enum) inside a file:
    /// returns its members, base class, interfaces, and all referenced types (dependencies).
    /// </summary>
    internal interface IInspectType : IBuiltInTool
    {
    }

    /// <summary>
    /// Uses Roslyn to parse the source file, get semantic model, and collect all referenced types.
    /// </summary>
    internal class InspectType : IInspectType
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly ISearchResultCache _searchCache;

        public string ToolName => "inspect_type";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.ReadOnly;

        public InspectType(
            IVsDependencies vsDependencies,
            IPathResolver pathResolver,
            ISearchResultCache searchCache)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _searchCache = searchCache ?? throw new ArgumentNullException(nameof(searchCache));
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            try
            {
                if (parameters == null || !parameters.TryGetValue("file_path", out var fpObj) || !(fpObj is string filePathParam) || string.IsNullOrEmpty(filePathParam))
                    return ErrorResponse("Parameter 'file_path' is required.");

                string typeName = null;
                if (parameters.TryGetValue("type_name", out var tnObj) && tnObj is string tn && !string.IsNullOrEmpty(tn))
                    typeName = tn;


                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                string solutionDir = _vsDependencies.GetSolutionDirectory();
                if (string.IsNullOrEmpty(solutionDir))
                    return ErrorResponse("Solution directory not available.");

                if (!_pathResolver.TryResolveFilePath(filePathParam, solutionDir, out string absolutePath))
                    return ErrorResponse($"Cannot resolve file path: {filePathParam}");

                if (!File.Exists(absolutePath))
                    return ErrorResponse($"File not found: {absolutePath}");

                string cacheKey = $"{ToolName}_{absolutePath}_{typeName ?? "default"}";
                if (_searchCache.TryGet(cacheKey, "", out CachedToolResults<InspectTypeResponse> cached) && cached.AllResults.Any())
                {
                    return cached.AllResults.First();
                }

                var componentModel = (IComponentModel)ServiceProvider.GlobalProvider.GetService(typeof(SComponentModel));
                if (componentModel == null)
                    return ErrorResponse("Component model is not available.");

                var workspace = componentModel.GetService<VisualStudioWorkspace>();
                if (workspace == null)
                    return ErrorResponse("Workspace not available.");

                var solution = workspace.CurrentSolution;
                if (solution == null)
                    return ErrorResponse("No solution is open.");

                var document = solution.Projects
                    .SelectMany(p => p.Documents)
                    .FirstOrDefault(d => string.Equals(d.FilePath, absolutePath, StringComparison.OrdinalIgnoreCase));

                if (document == null)
                    return ErrorResponse($"Document '{absolutePath}' not found in the workspace.");

                var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
                var root = await syntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(false);
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);


                var typeDeclarations = root.DescendantNodes()
                    .OfType<BaseTypeDeclarationSyntax>()
                    .ToList();

                BaseTypeDeclarationSyntax targetType = null;
                if (!string.IsNullOrEmpty(typeName))
                {
                    targetType = typeDeclarations.FirstOrDefault(t => t.Identifier.Text == typeName);
                    if (targetType == null)
                    {
                        targetType = typeDeclarations.FirstOrDefault(t => string.Equals(t.Identifier.Text, typeName, StringComparison.OrdinalIgnoreCase));
                    }
                }
                else
                {
                    targetType = typeDeclarations.FirstOrDefault(t => t.Modifiers.Any(m => m.Text == "public")) ?? typeDeclarations.FirstOrDefault();
                }

                if (targetType == null)
                    return ErrorResponse($"No type declaration found in the file.{(string.IsNullOrEmpty(typeName) ? "" : $" Looking for '{typeName}'.")}");

                var result = await InspectTypeAsync(
                    targetType,
                    semanticModel,
                    solution,
                    cancellationToken).ConfigureAwait(false);

                if (result.Success)
                {
                    var cacheEntry = new CachedToolResults<InspectTypeResponse>
                    {
                        AllResults = new List<InspectTypeResponse> { result },
                        ItemsScanned = 1
                    };
                    _searchCache.Set(cacheKey, "", cacheEntry);
                }

                return result;
            }
            catch (Exception ex)
            {
                return ErrorResponse($"Inspection failed: {ex.Message}");
            }
        }

        private async Task<InspectTypeResponse> InspectTypeAsync(
            BaseTypeDeclarationSyntax typeDecl,
            SemanticModel semanticModel,
            RoslynSolution solution,
            CancellationToken cancellationToken)
        {
            var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken) as INamedTypeSymbol;
            if (typeSymbol == null)
                return ErrorResponse("Failed to get type symbol.");

            var dependencies = new HashSet<string>(StringComparer.Ordinal);

            var nodesToCheck = typeDecl.DescendantNodes().Where(n =>
                n is CSharpSyntax.TypeSyntax ||
                n is CSharpSyntax.IdentifierNameSyntax ||
                n is CSharpSyntax.QualifiedNameSyntax ||
                n is CSharpSyntax.AliasQualifiedNameSyntax ||
                n is CSharpSyntax.MemberAccessExpressionSyntax ||
                n is CSharpSyntax.ObjectCreationExpressionSyntax ||
                n is CSharpSyntax.TypeOfExpressionSyntax ||
                n is CSharpSyntax.CatchDeclarationSyntax ||
                n is CSharpSyntax.UsingDirectiveSyntax ||
                n is CSharpSyntax.VariableDeclarationSyntax ||
                n is CSharpSyntax.ParameterSyntax ||
                n is CSharpSyntax.ReturnStatementSyntax ||
                n is CSharpSyntax.ThrowStatementSyntax ||
                n is CSharpSyntax.BaseExpressionSyntax ||
                n is CSharpSyntax.ThisExpressionSyntax ||
                n is CSharpSyntax.InvocationExpressionSyntax ||
                n is CSharpSyntax.ElementAccessExpressionSyntax ||
                n is CSharpSyntax.DefaultExpressionSyntax ||
                n is CSharpSyntax.SizeOfExpressionSyntax ||
                n is CSharpSyntax.StackAllocArrayCreationExpressionSyntax ||
                n is CSharpSyntax.ImplicitArrayCreationExpressionSyntax ||
                n is CSharpSyntax.ArrayCreationExpressionSyntax ||
                n is CSharpSyntax.ConditionalAccessExpressionSyntax ||
                n is CSharpSyntax.CastExpressionSyntax ||
                n is CSharpSyntax.BinaryExpressionSyntax
            );

            foreach (var node in nodesToCheck)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var typeInfo = semanticModel.GetTypeInfo(node, cancellationToken);
                if (typeInfo.Type != null)
                {
                    AddTypeDependency(typeInfo.Type, dependencies);
                }

                if (node is CSharpSyntax.InvocationExpressionSyntax invocation)
                {
                    var methodSymbol = semanticModel.GetSymbolInfo(invocation.Expression, cancellationToken).Symbol as IMethodSymbol;
                    if (methodSymbol?.ContainingType != null)
                    {
                        AddTypeDependency(methodSymbol.ContainingType, dependencies);
                    }
                }

                if (node is CSharpSyntax.ObjectCreationExpressionSyntax creation)
                {
                    var createdSymbol = semanticModel.GetSymbolInfo(creation.Type, cancellationToken).Symbol as INamedTypeSymbol;
                    if (createdSymbol != null)
                    {
                        AddTypeDependency(createdSymbol, dependencies);
                    }
                }

                if (node is CSharpSyntax.MemberAccessExpressionSyntax memberAccess)
                {
                    var memberSymbol = semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol;
                    if (memberSymbol?.ContainingType != null)
                    {
                        AddTypeDependency(memberSymbol.ContainingType, dependencies);
                    }
                }

                if (node is CSharpSyntax.TypeOfExpressionSyntax typeOf)
                {
                    var typeOfType = semanticModel.GetTypeInfo(typeOf.Type, cancellationToken).Type;
                    if (typeOfType != null)
                    {
                        AddTypeDependency(typeOfType, dependencies);
                    }
                }

                if (node is CSharpSyntax.VariableDeclarationSyntax varDecl)
                {
                    var varType = semanticModel.GetTypeInfo(varDecl.Type, cancellationToken).Type;
                    if (varType != null)
                    {
                        AddTypeDependency(varType, dependencies);
                    }
                }

                if (node is CSharpSyntax.ParameterSyntax param)
                {
                    var paramType = semanticModel.GetTypeInfo(param.Type, cancellationToken).Type;
                    if (paramType != null)
                    {
                        AddTypeDependency(paramType, dependencies);
                    }
                }
            }

            if (typeSymbol.BaseType != null)
                AddTypeDependency(typeSymbol.BaseType, dependencies);

            foreach (var iface in typeSymbol.Interfaces)
            {
                AddTypeDependency(iface, dependencies);
            }

            var solutionAssemblyNames = new HashSet<string>(solution.Projects.Select(p => p.AssemblyName), StringComparer.Ordinal);
            var internalDeps = new List<string>();
            var externalDeps = new List<string>();

            foreach (var dep in dependencies)
            {
                bool isInternal = false;
                foreach (var proj in solution.Projects)
                {
                    var compilation = await proj.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
                    if (compilation != null)
                    {
                        var typeInCompilation = compilation.GetTypeByMetadataName(dep);
                        if (typeInCompilation != null && typeInCompilation.ContainingAssembly != null &&
                            solutionAssemblyNames.Contains(typeInCompilation.ContainingAssembly.Name))
                        {
                            isInternal = true;
                            break;
                        }
                    }
                }
                if (isInternal)
                {
                    internalDeps.Add(dep);
                }
                else
                {
                    externalDeps.Add(dep);
                }

            }

            var members = ExtractMembers(typeDecl, semanticModel, cancellationToken);

            return new InspectTypeResponse
            {
                Success = true,
                TypeName = typeSymbol.Name,
                Namespace = typeSymbol.ContainingNamespace?.ToString() ?? "",
                Modifiers = string.Join(" ", typeDecl.Modifiers.Select(m => m.Text)),
                BaseClass = typeSymbol.BaseType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Interfaces = typeSymbol.Interfaces.Select(i => i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).ToList(),
                Dependencies = dependencies.ToList(),
                InternalDependencies = internalDeps,
                ExternalDependencies = externalDeps,
                Members = members,
                ErrorMessage = null
            };
        }

        private void AddTypeDependency(ITypeSymbol typeSymbol, HashSet<string> dependencies)
        {
            if (typeSymbol == null)
                return;

            var display = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            dependencies.Add(display);

            if (typeSymbol is INamedTypeSymbol named && named.IsGenericType)
            {
                if (named.IsUnboundGenericType)
                {
                    dependencies.Add(named.ConstructUnboundGenericType().ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                }
                else
                {
                    foreach (var arg in named.TypeArguments)
                    {
                        AddTypeDependency(arg, dependencies);
                    }
                }
            }
            if (typeSymbol is IArrayTypeSymbol array)
            {
                AddTypeDependency(array.ElementType, dependencies);
            }
        }

        private ClassMembers ExtractMembers(BaseTypeDeclarationSyntax typeDecl, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            var members = new ClassMembers();

            MemberDeclarationSyntax[] memberDecls = null;
            if (typeDecl is ClassDeclarationSyntax classDecl)
            {
                memberDecls = classDecl.Members.ToArray();
            }
            else if (typeDecl is StructDeclarationSyntax structDecl)
            {
                memberDecls = structDecl.Members.ToArray();
            }
            else if (typeDecl is InterfaceDeclarationSyntax interfaceDecl)
            {
                memberDecls = interfaceDecl.Members.ToArray();
            }
            else if (typeDecl is RecordDeclarationSyntax recordDecl)
            {
                memberDecls = recordDecl.Members.ToArray();
            }
            else
            {
                return members;
            }

            ITypeSymbol GetTypeSymbol(TypeSyntax typeSyntax)
            {
                if (typeSyntax == null)
                    return null;
                var typeInfo = semanticModel.GetTypeInfo(typeSyntax, cancellationToken);
                return typeInfo.Type;
            }

            foreach (var member in memberDecls)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (member is FieldDeclarationSyntax fieldDecl)
                {
                    var fieldType = GetTypeSymbol(fieldDecl.Declaration.Type);
                    if (fieldType != null)
                    {
                        foreach (var varDecl in fieldDecl.Declaration.Variables)
                        {
                            members.Fields.Add(new MemberInfo
                            {
                                Name = varDecl.Identifier.Text,
                                Type = fieldType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                            });
                        }
                    }
                }
                else if (member is PropertyDeclarationSyntax propDecl)
                {
                    var propType = GetTypeSymbol(propDecl.Type);
                    if (propType != null)
                    {
                        var accessors = new List<string>();
                        if (propDecl.AccessorList != null)
                        {
                            foreach (var accessor in propDecl.AccessorList.Accessors)
                            {
                                if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
                                {
                                    accessors.Add("get");
                                }
                                else if (accessor.IsKind(SyntaxKind.SetAccessorDeclaration))
                                {
                                    accessors.Add("set");
                                }
                                else if (accessor.IsKind(SyntaxKind.InitAccessorDeclaration))
                                {
                                    accessors.Add("init");
                                }
                            }
                        }
                        members.Properties.Add(new PropertyInfo
                        {
                            Name = propDecl.Identifier.Text,
                            Type = propType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            Accessors = accessors
                        });
                    }
                }
                else if (member is MethodDeclarationSyntax methodDecl)
                {
                    var returnType = GetTypeSymbol(methodDecl.ReturnType);
                    var methodInfo = new MethodInfo
                    {
                        Name = methodDecl.Identifier.Text,
                        ReturnType = returnType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "void",
                        Parameters = new List<ParameterInfo>()
                    };
                    foreach (var param in methodDecl.ParameterList.Parameters)
                    {
                        var paramType = GetTypeSymbol(param.Type);
                        methodInfo.Parameters.Add(new ParameterInfo
                        {
                            Name = param.Identifier.Text,
                            Type = paramType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "unknown"
                        });
                    }
                    members.Methods.Add(methodInfo);
                }
            }
            return members;
        }

        private InspectTypeResponse ErrorResponse(string message)
        {
            return new InspectTypeResponse
            {
                Success = false,
                ErrorMessage = message,
                TypeName = "",
                Namespace = "",
                Modifiers = "",
                BaseClass = null,
                Interfaces = new List<string>(),
                Dependencies = new List<string>(),
                InternalDependencies = new List<string>(),
                ExternalDependencies = new List<string>(),
                Members = new ClassMembers()
            };
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            var file = parameters?.TryGetValue("file_path", out var p) == true ? p?.ToString() : "file";
            var typeName = parameters?.TryGetValue("type_name", out var tn) == true ? tn?.ToString() : null;
            if (!string.IsNullOrEmpty(typeName))
            {
                return $"Inspecting type '{typeName}' in file '{file}'... ";
            }
            else
            {
                return $"Inspecting type in file '{file}'... ";
            }
        }

        public string GetCompletionMessage(object result)
        {
            if (result is InspectTypeResponse resp)
            {
                if (!resp.Success)
                    return $"Inspection failed: {resp.ErrorMessage}";

                int depCount = resp.Dependencies?.Count ?? 0;
                if (depCount == 0)
                    return "Inspection completed. No dependencies found.";

                var parts = new List<string>
                {
                    $"Total: {depCount}"
                };

                int internalCount = resp.InternalDependencies?.Count ?? 0;
                if (internalCount > 0)
                    parts.Add($"Internal: {internalCount}");

                int externalCount = resp.ExternalDependencies?.Count ?? 0;
                if (externalCount > 0)
                    parts.Add($"External: {externalCount}");

                return $"Inspection completed. Found {string.Join(", ", parts)}.";
            }
            return "Inspection completed.";
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Inspects a type (class, struct, interface, record, enum) inside a source file using Roslyn semantic analysis. Returns its members (fields, properties, methods with signatures), base class, implemented interfaces, and all referenced types split into internal (same solution) and external (NuGet/framework) dependencies. If type_name is omitted, the first public type (or the first declaration) in the file is inspected. Use to understand a type's API surface before editing or referencing it. Example: {\"file_path\":\"src/Services/PaymentService.cs\",\"type_name\":\"PaymentService\"} → {\"success\":true,\"type_name\":\"PaymentService\",\"namespace\":\"App.Services\",\"modifiers\":\"public\",\"base_class\":\"global::App.Services.BaseService\",\"interfaces\":[\"global::App.Services.IPaymentService\"],\"members\":{\"fields\":[],\"properties\":[{\"accessors\":[\"get\",\"set\"],\"name\":\"Gateway\",\"type\":\"string\"}],\"methods\":[{\"parameters\":[{\"name\":\"amount\",\"type\":\"decimal\"}],\"return_type\":\"global::System.Threading.Tasks.Task<bool>\",\"name\":\"ProcessAsync\"}]},\"internal_dependencies\":[\"global::App.Services.BaseService\"],\"external_dependencies\":[\"global::System.Threading.Tasks.Task\"]}.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_path", new ToolDetails { Type = "string", Description = "Path to the source file containing the type." } },
                        { "type_name", new ToolDetails { Type = "string", Description = "Name of the specific type to inspect (optional)." } }
                    },
                    Required = new List<string> { "file_path" }
                }
            };
        }
    }

    public class InspectTypeResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("error_message")]
        public string ErrorMessage { get; set; }

        [JsonProperty("type_name")]
        public string TypeName { get; set; }

        [JsonProperty("namespace")]
        public string Namespace { get; set; }

        [JsonProperty("modifiers")]
        public string Modifiers { get; set; }

        [JsonProperty("base_class")]
        public string BaseClass { get; set; }

        [JsonProperty("interfaces")]
        public List<string> Interfaces { get; set; }

        [JsonProperty("dependencies")]
        public List<string> Dependencies { get; set; }

        [JsonProperty("internal_dependencies")]
        public List<string> InternalDependencies { get; set; }

        [JsonProperty("external_dependencies")]
        public List<string> ExternalDependencies { get; set; }

        [JsonProperty("members")]
        public ClassMembers Members { get; set; }
    }

    public class ClassMembers
    {
        [JsonProperty("fields")]
        public List<MemberInfo> Fields { get; set; } = new List<MemberInfo>();

        [JsonProperty("properties")]
        public List<PropertyInfo> Properties { get; set; } = new List<PropertyInfo>();

        [JsonProperty("methods")]
        public List<MethodInfo> Methods { get; set; } = new List<MethodInfo>();
    }

    public class MemberInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }
    }

    public class PropertyInfo : MemberInfo
    {
        [JsonProperty("accessors")]
        public List<string> Accessors { get; set; }
    }

    public class MethodInfo : MemberInfo
    {
        [JsonProperty("parameters")]
        public List<ParameterInfo> Parameters { get; set; }

        [JsonProperty("return_type")]
        public string ReturnType { get; set; }
    }

    public class ParameterInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }
    }
}