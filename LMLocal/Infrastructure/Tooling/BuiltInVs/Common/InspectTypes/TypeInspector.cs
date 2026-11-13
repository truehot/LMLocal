using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.InspectTypes
{
    /// <summary>
    /// Builds the inspect_type result for a resolved type symbol: type metadata and its visible members,
    /// with the declaration line of every member declared in the solution.
    /// </summary>
    internal static class TypeInspector
    {
        internal const int MaxMembersPerCategory = 100;

        /// <param name="toRelativePath">Maps an absolute source path to the solution-relative form.</param>
        internal static TypeInspectionResult Inspect(INamedTypeSymbol symbol, string projectName, Func<string, string> toRelativePath)
        {
            if (toRelativePath == null) throw new ArgumentNullException(nameof(toRelativePath));

            string typeFile = SymbolSourceLocation.TryGet(symbol, out var typeLocation) ? typeLocation.FilePath : null;
            var locator = new MemberLocator(typeFile, toRelativePath);

            var result = new TypeInspectionResult
            {
                FullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                MetadataName = symbol.MetadataName,
                Kind = symbol.TypeKind.ToString(),
                Accessibility = symbol.DeclaredAccessibility.ToString(),
                ContainingNamespace = GetNamespaceName(symbol.ContainingNamespace),
                AssemblyName = symbol.ContainingAssembly?.Name ?? "Unknown",
                ProjectName = projectName,
                FilePath = typeFile != null ? toRelativePath(typeFile) : null,
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
                        ConstructorArguments = a.ConstructorArguments.Select(c => c.ToCSharpString()).ToList()
                    })
                    .ToList()
            };

            var members = symbol.GetMembers();

            var constructors = symbol.InstanceConstructors.Where(c => IsVisibleMember(c)).ToList();
            result.TotalConstructors = constructors.Count;
            result.Constructors = Cap(constructors).Select(c => locator.Locate(ToConstructorInfo(c), c)).ToList();

            var properties = members.OfType<IPropertySymbol>().Where(p => IsVisibleMember(p)).ToList();
            result.TotalProperties = properties.Count;
            result.Properties = Cap(properties).Select(p => locator.Locate(ToPropertyInfo(p), p)).ToList();

            var methods = members.OfType<IMethodSymbol>().Where(m => IsVisibleMember(m) && IsReportableMethod(m)).ToList();
            result.TotalMethods = methods.Count;
            result.Methods = Cap(methods).Select(m => locator.Locate(ToMethodInfo(m), m)).ToList();

            var events = members.OfType<IEventSymbol>().Where(e => IsVisibleMember(e)).ToList();
            result.TotalEvents = events.Count;
            result.Events = Cap(events).Select(e => locator.Locate(ToEventInfo(e), e)).ToList();

            var fields = members.OfType<IFieldSymbol>().Where(f => IsVisibleMember(f)).ToList();
            result.TotalFields = fields.Count;
            result.Fields = Cap(fields).Select(f => locator.Locate(ToFieldInfo(f), f)).ToList();

            result.HasMoreResults =
                result.TotalConstructors > MaxMembersPerCategory ||
                result.TotalProperties > MaxMembersPerCategory ||
                result.TotalMethods > MaxMembersPerCategory ||
                result.TotalEvents > MaxMembersPerCategory ||
                result.TotalFields > MaxMembersPerCategory;

            return result;
        }

        /// <summary>Accessibility order (stable, so declaration order within a group), then the per-category cap.</summary>
        private static IEnumerable<T> Cap<T>(IEnumerable<T> members) where T : ISymbol
        {
            return members
                .OrderBy(m => ComputeAccessibilityRank(m.DeclaredAccessibility))
                .Take(MaxMembersPerCategory);
        }

        private static MethodInfo ToConstructorInfo(IMethodSymbol c)
        {
            return new MethodInfo
            {
                Name = c.Name,
                Accessibility = c.DeclaredAccessibility.ToString(),
                IsStatic = c.IsStatic,
                Parameters = ToParams(c)
            };
        }

        private static PropertyInfo ToPropertyInfo(IPropertySymbol p)
        {
            bool hasGetter = IsVisibleMember(p.GetMethod);
            bool hasSetter = IsVisibleMember(p.SetMethod);
            return new PropertyInfo
            {
                Name = p.Name,
                Type = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                IsStatic = p.IsStatic,
                IsReadOnly = hasGetter && !hasSetter,
                IsWriteOnly = hasSetter && !hasGetter,
                HasGetter = hasGetter,
                HasSetter = hasSetter,
                Accessibility = p.DeclaredAccessibility.ToString()
            };
        }

        private static MethodInfo ToMethodInfo(IMethodSymbol m)
        {
            return new MethodInfo
            {
                Name = m.Name,
                Accessibility = m.DeclaredAccessibility.ToString(),
                IsStatic = m.IsStatic,
                IsExtension = m.IsExtensionMethod,
                IsGeneric = m.IsGenericMethod,
                ReturnType = m.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                TypeParameters = m.TypeParameters.Length > 0
                    ? m.TypeParameters.Select(tp => tp.Name).ToList()
                    : null,
                Parameters = ToParams(m)
            };
        }

        private static EventInfo ToEventInfo(IEventSymbol e)
        {
            return new EventInfo
            {
                Name = e.Name,
                Type = e.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                IsStatic = e.IsStatic,
                Accessibility = e.DeclaredAccessibility.ToString()
            };
        }

        private static FieldInfo ToFieldInfo(IFieldSymbol f)
        {
            return new FieldInfo
            {
                Name = f.Name,
                Type = f.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                IsStatic = f.IsStatic,
                IsReadOnly = f.IsReadOnly,
                IsConst = f.IsConst,
                ConstantValue = f.HasConstantValue ? FormatConstantValue(f.ConstantValue) : null,
                Accessibility = f.DeclaredAccessibility.ToString()
            };
        }

        private static List<ParamInfo> ToParams(IMethodSymbol method)
        {
            return method.Parameters.Select(p => new ParamInfo
            {
                Name = p.Name,
                Type = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            }).ToList();
        }

        /// <summary>
        /// Sets the member declaration site relative to the inspected type's file.
        /// </summary>
        internal sealed class MemberLocator
        {
            private readonly string _typeFile;
            private readonly Func<string, string> _toRelativePath;

            /// <param name="typeFile">Absolute file of the type; null for a type from a referenced assembly.</param>
            public MemberLocator(string typeFile, Func<string, string> toRelativePath)
            {
                _typeFile = typeFile;
                _toRelativePath = toRelativePath;
            }

            public T Locate<T>(T member, ISymbol symbol) where T : MemberLocationInfo
            {
                if (symbol.IsImplicitlyDeclared || !SymbolSourceLocation.TryGet(symbol, out var location))
                    return member;

                member.Line = location.Line;
                if (!string.Equals(location.FilePath, _typeFile, StringComparison.OrdinalIgnoreCase))
                    member.FilePath = _toRelativePath(location.FilePath);
                return member;
            }
        }

        /// <summary>
        /// Namespace name for the response; the global namespace renders as an empty string instead of
        /// '&lt;global namespace&gt;', which would also break exact matching of 'Outer.Inner'.
        /// </summary>
        internal static string GetNamespaceName(INamespaceSymbol ns)
        {
            if (ns == null || ns.IsGlobalNamespace)
                return "";

            return ns.ToDisplayString();
        }

        /// <summary>
        /// Members worth reporting: public, protected and internal.
        /// </summary>
        internal static bool IsVisibleMember(ISymbol member)
        {
            if (member == null)
                return false;

            switch (member.DeclaredAccessibility)
            {
                case Accessibility.Public:
                case Accessibility.Internal:
                case Accessibility.Protected:
                case Accessibility.ProtectedOrInternal:
                case Accessibility.ProtectedAndInternal:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Order of member lists before the per-category cap: public, internal, protected internal,
        /// protected, private protected. <see cref="Enumerable.OrderBy{T}"/> is stable, so members of one
        /// group keep their declaration order.
        /// </summary>
        internal static int ComputeAccessibilityRank(Accessibility accessibility)
        {
            switch (accessibility)
            {
                case Accessibility.Public: return 0;
                case Accessibility.Internal: return 1;
                case Accessibility.ProtectedOrInternal: return 2;
                case Accessibility.Protected: return 3;
                case Accessibility.ProtectedAndInternal: return 4;
                default: return 5;
            }
        }

        /// <summary>
        /// Ordinary methods, operators and conversions.
        /// </summary>
        internal static bool IsReportableMethod(IMethodSymbol method)
        {
            switch (method.MethodKind)
            {
                case MethodKind.Constructor:
                case MethodKind.StaticConstructor:
                case MethodKind.Destructor:
                case MethodKind.PropertyGet:
                case MethodKind.PropertySet:
                case MethodKind.EventAdd:
                case MethodKind.EventRemove:
                case MethodKind.EventRaise:
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>Formats a compile-time constant culture-invariantly and quotes strings; null for an unrepresentable value.</summary>
        internal static string FormatConstantValue(object value)
        {
            return SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false);
        }
    }
}
