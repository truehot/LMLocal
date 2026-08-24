using System;
using System.Collections.Generic;
using Acornima;
using Acornima.Ast;
using LMLocal.Core.Common;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Js
{
    /// <summary>
    /// A single declaration found in a JS file.
    /// </summary>
    internal sealed class JsDeclaration
    {
        public string Name { get; set; }
        public int Line { get; set; }      // 1-based
        public int Column { get; set; }    // 1-based
        public string DeclarationType { get; set; } // "function" | "const" | "let" | "var" | "class | "export_default" | "assignment | "object_property" | "object_method | "class_property" | "class_method"
        public string SymbolKind { get; set; }      // Acornima NodeType
    }

    /// <summary>
    /// A single call/reference found in a JS file.
    /// </summary>
    internal sealed class JsCall
    {
        public string Name { get; set; }
        public int LineNumber { get; set; } // 1-based
        public int Column { get; set; }     // 1-based
        public string LineText { get; set; }
        public string Context { get; set; } // "call" | "new" | "member_call" | "member_new | "property_access" | "computed_key" | "identifier" | "dynamic_import"
        public string ImportSource { get; set; } // resolved module source if applicable
        public string ObjectName { get; set; }   // owner path for member accesses, e.g. "Strings" or "a.b.c"
        public bool IsComputed { get; set; }     // true for obj["x"] / obj[key]
    }

    /// <summary>
    /// A single import binding: local name, original (imported) name and module source.
    /// </summary>
    internal sealed class JsImport
    {
        public string LocalName { get; set; }    // how the symbol is named in the current file
        public string ImportedName { get; set; } // original exported name (null for default/namespace)
        public string Source { get; set; }       // module specifier
    }

    /// <summary>
    /// Result of parsing a single JS file.
    /// </summary>
    internal sealed class JsFileParseResult
    {
        public string FilePath { get; set; }
        public List<JsDeclaration> Declarations { get; set; } = new List<JsDeclaration>();
        public List<JsCall> Calls { get; set; } = new List<JsCall>();
        public Dictionary<string, string> Imports { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal); // localName -> source
        public List<JsImport> ImportRecords { get; set; } = new List<JsImport>();
        public HashSet<string> Exports { get; set; } = new HashSet<string>(StringComparer.Ordinal); // exported names
        public List<string> Notes { get; set; } = new List<string>(); // non-fatal warnings
        public string SourceText { get; set; }

        /// <summary>
        /// Dedup keys for declarations ("name:line:column").
        /// </summary>
        internal HashSet<string> SeenDeclarationLocations { get; } = new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Parses a single JavaScript file using Acornima and extracts declarations, calls, imports and exports.
    /// </summary>
    internal sealed class JsFileParser
    {
        private static readonly ParserOptions ParserOptions = ParserOptions.Default;

        /// <summary>
        /// Parses the given JS source and returns the extracted symbols.
        /// </summary>
        public JsFileParseResult Parse(string filePath, string content)
        {
            if (string.IsNullOrEmpty(content))
                return new JsFileParseResult { FilePath = filePath, SourceText = string.Empty };

            Node program;
            try
            {
                program = new Parser(ParserOptions).ParseModule(content);
            }
            catch (ParseErrorException)
            {
                try
                {
                    program = new Parser(ParserOptions).ParseScript(content, strict: false);
                }
                catch (ParseErrorException ex)
                {
                    InternalLogger.Warn($"JsFileParser: cannot parse '{filePath}': {ex.Message}");
                    return null;
                }
            }

            var result = new JsFileParseResult
            {
                FilePath = filePath,
                SourceText = content
            };

            Traverse(program, result);

            return result;
        }

        private void Traverse(Node node, JsFileParseResult result, Node parent = null)
        {
            if (node == null)
                return;

            switch (node.Type)
            {
                case NodeType.ImportDeclaration:
                    HandleImport((ImportDeclaration)node, result);
                    break;
                case NodeType.ExportNamedDeclaration:
                    HandleExportNamed((ExportNamedDeclaration)node, result);
                    break;
                case NodeType.ExportDefaultDeclaration:
                    HandleExportDefault((ExportDefaultDeclaration)node, result);
                    break;
                case NodeType.ExportAllDeclaration:
                    HandleExportAll((ExportAllDeclaration)node, result);
                    break;
                case NodeType.FunctionDeclaration:
                    HandleFunctionDecl((FunctionDeclaration)node, result);
                    break;
                case NodeType.VariableDeclaration:
                    HandleVariableDecl((VariableDeclaration)node, result);
                    break;
                case NodeType.ClassDeclaration:
                    HandleClassDecl((ClassDeclaration)node, result);
                    break;
                case NodeType.CallExpression:
                    HandleCall((CallExpression)node, result);
                    break;
                case NodeType.NewExpression:
                    HandleNew((NewExpression)node, result);
                    break;
                case NodeType.AssignmentExpression:
                    HandleAssignment((AssignmentExpression)node, result);
                    break;
                case NodeType.ImportExpression:
                    HandleDynamicImport((ImportExpression)node, result);
                    break;
                case NodeType.MemberExpression:
                    HandleMemberExpression((MemberExpression)node, result, parent);
                    break;
                case NodeType.ObjectExpression:
                    HandleObjectExpression((ObjectExpression)node, result);
                    break;
                case NodeType.MethodDefinition:
                    HandleClassMethod((MethodDefinition)node, result);
                    break;
                case NodeType.PropertyDefinition:
                    HandleClassProperty((PropertyDefinition)node, result);
                    break;
                case NodeType.Identifier:
                    HandleIdentifier((Identifier)node, result, parent);
                    break;
            }

            foreach (var child in node.ChildNodes)
            {
                if (child != null)
                    Traverse(child, result, node);
            }
        }

        private static void HandleImport(ImportDeclaration node, JsFileParseResult result)
        {
            string source = node.Source?.Value;
            if (string.IsNullOrEmpty(source))
                return;

            foreach (var specifier in node.Specifiers)
            {
                string localName = specifier.Local?.Name;
                if (string.IsNullOrEmpty(localName))
                    continue;

                if (!result.Imports.ContainsKey(localName))
                    result.Imports[localName] = source;

                string importedName = null;
                if (specifier is ImportSpecifier importSpec)
                    importedName = (importSpec.Imported as Identifier)?.Name ?? localName;
                else if (specifier is ImportDefaultSpecifier)
                    importedName = "default";
                else if (specifier is ImportNamespaceSpecifier)
                    importedName = "*";

                result.ImportRecords.Add(new JsImport
                {
                    LocalName = localName,
                    ImportedName = importedName,
                    Source = source
                });
            }
        }

        private static void HandleExportNamed(ExportNamedDeclaration node, JsFileParseResult result)
        {
            // export function foo() {} / export const foo = ...
            if (node.Declaration != null)
            {
                CollectDeclarationNames(node.Declaration, result, "export");
                CollectExportNames(node.Declaration, result);
            }

            // export { foo, bar as baz }  —  optionally from './y' (re-export)
            string source = node.Source?.Value;
            foreach (var specifier in node.Specifiers)
            {
                string localName = (specifier.Local as Identifier)?.Name;
                if (string.IsNullOrEmpty(localName))
                    continue;

                result.Exports.Add(localName);

                if (!string.IsNullOrEmpty(source))
                {
                    if (!result.Imports.ContainsKey(localName))
                        result.Imports[localName] = source;

                    result.ImportRecords.Add(new JsImport
                    {
                        LocalName = localName,
                        ImportedName = localName,
                        Source = source
                    });
                }
            }
        }

        private static void CollectExportNames(Node declaration, JsFileParseResult result)
        {
            switch (declaration)
            {
                case FunctionDeclaration fn when fn.Id != null:
                    result.Exports.Add(fn.Id.Name);
                    break;
                case ClassDeclaration cls when cls.Id != null:
                    result.Exports.Add(cls.Id.Name);
                    break;
                case VariableDeclaration varDecl:
                    foreach (var declarator in varDecl.Declarations)
                    {
                        if (declarator.Id is Identifier id)
                            result.Exports.Add(id.Name);
                    }
                    break;
            }
        }

        private static void HandleExportDefault(ExportDefaultDeclaration node, JsFileParseResult result)
        {
            result.Exports.Add("default");

            if (node.Declaration is FunctionDeclaration fn && fn.Id != null)
            {
                AddDeclaration(result, fn.Id.Name, GetLocation(fn), "export_default", fn.Type.ToString());
            }
            else if (node.Declaration is ClassDeclaration cls && cls.Id != null)
            {
                AddDeclaration(result, cls.Id.Name, GetLocation(cls), "export_default", cls.Type.ToString());
            }
            else if (node.Declaration is FunctionExpression fnExpr && fnExpr.Id != null)
            {
                AddDeclaration(result, fnExpr.Id.Name, GetLocation(fnExpr), "export_default", fnExpr.Type.ToString());
            }
        }

        private static void HandleExportAll(ExportAllDeclaration node, JsFileParseResult result)
        {
            string source = node.Source?.Value;
            if (!string.IsNullOrEmpty(source))
                result.Exports.Add("*:" + source);
        }

        private static void HandleFunctionDecl(FunctionDeclaration node, JsFileParseResult result)
        {
            if (node.Id == null)
                return;
            AddDeclaration(result, node.Id.Name, GetLocation(node), "function", node.Type.ToString());
        }

        private static void HandleVariableDecl(VariableDeclaration node, JsFileParseResult result)
        {
            string kind = node.Kind.ToString().ToLowerInvariant(); // "const" | "let" | "var"

            foreach (var declarator in node.Declarations)
            {
                // const UIText = "..." / const foo = () => {} — every named variable is a definition.
                if (declarator.Id is Identifier id)
                {
                    AddDeclaration(result, id.Name, GetLocation(declarator), kind, node.Type.ToString());
                }
            }
        }

        private static void HandleClassDecl(ClassDeclaration node, JsFileParseResult result)
        {
            if (node.Id == null)
                return;
            AddDeclaration(result, node.Id.Name, GetLocation(node), "class", node.Type.ToString());
        }

        /// <summary>
        /// const o = { UIText: "..." } → declaration of property UIText ("object_property").
        /// </summary>
        private static void HandleObjectExpression(ObjectExpression node, JsFileParseResult result)
        {
            foreach (var prop in node.Properties)
            {
                if (!(prop is Property property))
                    continue; // SpreadElement etc.

                // { [UIText]: 1 } — computed key references a variable, not a static property name.
                if (property.Computed)
                    continue;

                string name = GetStaticKeyName(property.Key);
                if (string.IsNullOrEmpty(name))
                    continue;

                string declType = property.Method ? "object_method" : "object_property";
                AddDeclaration(result, name, GetLocation(property), declType, property.Type.ToString());
            }
        }

        /// <summary>
        /// class Foo { bar() {} } → declaration of method bar ("class_method").
        /// </summary>
        private static void HandleClassMethod(MethodDefinition node, JsFileParseResult result)
        {
            if (node.Computed)
                return;

            string name = GetStaticKeyName(node.Key);
            if (string.IsNullOrEmpty(name))
                return;

            AddDeclaration(result, name, GetLocation(node), "class_method", node.Type.ToString());
        }

        /// <summary>
        /// class Foo { UIText = 1 } → declaration of field UIText ("class_property").
        /// </summary>
        private static void HandleClassProperty(PropertyDefinition node, JsFileParseResult result)
        {
            if (node.Computed)
                return;

            string name = GetStaticKeyName(node.Key);
            if (string.IsNullOrEmpty(name))
                return;

            AddDeclaration(result, name, GetLocation(node), "class_property", node.Type.ToString());
        }

        private static void HandleCall(CallExpression node, JsFileParseResult result)
        {
            string name = null;
            string context = null;
            string objectName = null;

            if (node.Callee is Identifier id)
            {
                name = id.Name;
                context = "call";
            }
            else if (node.Callee is MemberExpression member)
            {
                name = GetMemberName(member.Property);
                context = "member_call";
                objectName = GetObjectPath(member.Object);
            }

            if (string.IsNullOrEmpty(name))
                return;

            AddCall(result, name, GetLocation(node), context, objectName);
        }

        private static void HandleNew(NewExpression node, JsFileParseResult result)
        {
            string name = null;
            string context = null;
            string objectName = null;

            if (node.Callee is Identifier id)
            {
                name = id.Name;
                context = "new";
            }
            else if (node.Callee is MemberExpression member)
            {
                name = GetMemberName(member.Property);
                context = "member_new";
                objectName = GetObjectPath(member.Object);
            }

            if (string.IsNullOrEmpty(name))
                return;

            AddCall(result, name, GetLocation(node), context, objectName);
        }

        private static void HandleAssignment(AssignmentExpression node, JsFileParseResult result)
        {
            if (node.Operator != Operator.Assignment)
                return;

            if (node.Left is Identifier leftId &&
                (node.Right is FunctionExpression || node.Right is ArrowFunctionExpression))
            {
                AddDeclaration(result, leftId.Name, GetLocation(node), "assignment", node.Type.ToString());
            }
        }

        private static void HandleDynamicImport(ImportExpression node, JsFileParseResult result)
        {
            if (node.Source is StringLiteral literal)
            {
                AddCall(result, literal.Value, GetLocation(node), "dynamic_import");
            }
            else
            {
                result.Notes.Add($"Dynamic import with non-literal source at line {GetLocation(node).Line}");
            }
        }

        /// <summary>
        /// Member access classification:
        ///   obj.foo        → "property_access"   (reading a property)
        ///   obj[UIText]    → "computed_key"      (reading variable UIText as a key)
        ///   obj["UIText"]  → "property_access" + IsComputed (static name via brackets)
        ///   obj.foo() / new obj.foo() — skipped: already recorded as member_call/member_new.
        /// </summary>
        private static void HandleMemberExpression(MemberExpression node, JsFileParseResult result, Node parent)
        {
            if (parent is CallExpression callExpr && ReferenceEquals(callExpr.Callee, node))
                return;
            if (parent is NewExpression newExpr && ReferenceEquals(newExpr.Callee, node))
                return;

            string objectPath = GetObjectPath(node.Object);

            if (node.Computed)
            {
                if (node.Property is Identifier propId)
                {
                    // obj[UIText] — reading the symbol UIText (a variable), not a static property name.
                    AddCall(result, propId.Name, GetLocation(node), "computed_key", objectPath, computed: true);
                }
                else if (node.Property is StringLiteral str)
                {
                    // obj["UIText"] — static property name via brackets.
                    AddCall(result, str.Value, GetLocation(node), "property_access", objectPath, computed: true);
                }
                // Any other dynamic key (a[i + 1] etc.) — cannot be resolved statically.
                return;
            }

            // Plain access obj.UIText.
            string propertyName = GetMemberName(node.Property);
            if (string.IsNullOrEmpty(propertyName))
                return;

            AddCall(result, propertyName, GetLocation(node), "property_access", objectPath, computed: false);
        }

        /// <summary>
        /// Bare Identifier usage — a read/write of a variable (e.g. argument foo(UIText)).
        /// </summary>
        private static void HandleIdentifier(Identifier node, JsFileParseResult result, Node parent)
        {
            if (string.IsNullOrEmpty(node.Name))
                return;

            if (parent is VariableDeclarator decl && ReferenceEquals(decl.Id, node))
                return;
            if (parent is FunctionDeclaration fnDecl && ReferenceEquals(fnDecl.Id, node))
                return;
            if (parent is FunctionExpression fnExpr && ReferenceEquals(fnExpr.Id, node))
                return;
            if (parent is ClassDeclaration clsDecl && ReferenceEquals(clsDecl.Id, node))
                return;
            if (parent is ClassExpression clsExpr && ReferenceEquals(clsExpr.Id, node))
                return;

            if (parent is Property property && ReferenceEquals(property.Key, node) && !property.Computed)
                return;
            if (parent is MethodDefinition methodDef && ReferenceEquals(methodDef.Key, node) && !methodDef.Computed)
                return;
            if (parent is PropertyDefinition propDef && ReferenceEquals(propDef.Key, node) && !propDef.Computed)
                return;

            if (parent is CallExpression callExpr && ReferenceEquals(callExpr.Callee, node))
                return;
            if (parent is NewExpression newExpr && ReferenceEquals(newExpr.Callee, node))
                return;

            if (parent is MemberExpression memberExpr && ReferenceEquals(memberExpr.Property, node))
                return;

            if (parent is ImportSpecifier || parent is ImportDefaultSpecifier || parent is ImportNamespaceSpecifier)
                return;
            if (parent is ExportSpecifier)
                return;

            AddCall(result, node.Name, GetLocation(node), "identifier");
        }

        private static void CollectDeclarationNames(Node declaration, JsFileParseResult result, string declarationType)
        {
            switch (declaration)
            {
                case FunctionDeclaration fn when fn.Id != null:
                    AddDeclaration(result, fn.Id.Name, GetLocation(fn), declarationType, fn.Type.ToString());
                    break;
                case ClassDeclaration cls when cls.Id != null:
                    AddDeclaration(result, cls.Id.Name, GetLocation(cls), declarationType, cls.Type.ToString());
                    break;
                case VariableDeclaration varDecl:
                    foreach (var declarator in varDecl.Declarations)
                    {
                        if (declarator.Id is Identifier id)
                            AddDeclaration(result, id.Name, GetLocation(declarator), declarationType, varDecl.Type.ToString());
                    }
                    break;
            }
        }

        /// <summary>
        /// Full owner path for a member access chain: a.b.c.UIText → "a.b.c".
        /// </summary>
        private static string GetObjectPath(Expression obj)
        {
            if (obj is Identifier id)
                return id.Name;
            if (obj is ThisExpression)
                return "this";
            if (obj is Super)
                return "super";
            if (obj is MemberExpression member)
            {
                string parentPath = GetObjectPath(member.Object);
                string propName = member.Computed
                    ? (member.Property as StringLiteral)?.Value ?? (member.Property as Identifier)?.Name
                    : GetMemberName(member.Property);

                if (string.IsNullOrEmpty(parentPath))
                    return propName;
                if (string.IsNullOrEmpty(propName))
                    return parentPath;
                return parentPath + "." + propName;
            }
            return null;
        }

        /// <summary>
        /// Property name in a member access: Identifier (incl. #private via PrivateIdentifier).
        /// </summary>
        private static string GetMemberName(Expression property)
        {
            return (property as Identifier)?.Name ?? (property as PrivateIdentifier)?.Name;
        }

        /// <summary>
        /// Static key name for object/class members: UIText | "UIText".
        /// </summary>
        private static string GetStaticKeyName(Expression key)
        {
            switch (key)
            {
                case Identifier id:
                    return id.Name;
                case PrivateIdentifier priv:
                    return priv.Name;
                case StringLiteral str:
                    return str.Value;
                case Literal lit when lit.Value is string s:
                    return s;
                default:
                    return null;
            }
        }

        private static void AddDeclaration(JsFileParseResult result, string name, (int Line, int Column) location, string declarationType, string symbolKind)
        {
            if (string.IsNullOrEmpty(name))
                return;

            string key = name + ":" + location.Line + ":" + location.Column;
            if (!result.SeenDeclarationLocations.Add(key))
                return;

            result.Declarations.Add(new JsDeclaration
            {
                Name = name,
                Line = location.Line,
                Column = location.Column,
                DeclarationType = declarationType,
                SymbolKind = symbolKind
            });
        }

        private static void AddCall(JsFileParseResult result, string name, (int Line, int Column) location, string context, string objectName = null, bool computed = false)
        {
            if (string.IsNullOrEmpty(name))
                return;

            string lineText = string.Empty;
            if (!string.IsNullOrEmpty(result.SourceText))
            {
                var lines = result.SourceText.Split('\n');
                if (location.Line - 1 >= 0 && location.Line - 1 < lines.Length)
                    lineText = lines[location.Line - 1].Trim();
            }

            result.Calls.Add(new JsCall
            {
                Name = name,
                LineNumber = location.Line,
                Column = location.Column,
                LineText = lineText,
                Context = context,
                ObjectName = objectName,
                IsComputed = computed
            });
        }

        private static (int Line, int Column) GetLocation(Node node)
        {
            if (node?.Location != null)
            {
                // Line is 1-based, Position.Column is 0-based
                return (node.Location.Start.Line, node.Location.Start.Column + 1);
            }

            return (0, 0);
        }
    }
}
