using System.Linq;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Js;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common.Js
{
    [TestFixture]
    public class JsFileParserTests
    {
        private JsFileParser _parser;

        [SetUp]
        public void SetUp()
        {
            _parser = new JsFileParser();
        }

        private JsFileParseResult Parse(string code)
        {
            return _parser.Parse("test.js", code);
        }

        [Test]
        public void Parse_DetectsFunctionDeclaration()
        {
            var result = Parse("function foo() { return 1; }");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Declarations.Any(d => d.Name == "foo" && d.DeclarationType == "function"), Is.True);
        }

        [Test]
        public void Parse_DetectsConstFunction()
        {
            var result = Parse("const foo = () => { return 1; };");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Declarations.Any(d => d.Name == "foo" && d.DeclarationType == "const"), Is.True);
        }

        [Test]
        public void Parse_DetectsVarFunction()
        {
            var result = Parse("var foo = function() { return 1; };");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Declarations.Any(d => d.Name == "foo" && d.DeclarationType == "var"), Is.True);
        }

        [Test]
        public void Parse_DetectsClassDeclaration()
        {
            var result = Parse("class Foo { constructor() {} }");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Declarations.Any(d => d.Name == "Foo" && d.DeclarationType == "class"), Is.True);
        }

        [Test]
        public void Parse_DetectsCallExpression()
        {
            var result = Parse("foo();");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Calls.Any(c => c.Name == "foo" && c.Context == "call"), Is.True);
        }

        [Test]
        public void Parse_DetectsNewExpression()
        {
            var result = Parse("new Foo();");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Calls.Any(c => c.Name == "Foo" && c.Context == "new"), Is.True);
        }

        [Test]
        public void Parse_DetectsMemberCall()
        {
            var result = Parse("obj.foo();");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Calls.Any(c => c.Name == "foo" && c.Context == "member_call"), Is.True);
        }

        [Test]
        public void Parse_DetectsImports()
        {
            var result = Parse("import { foo } from './bar';");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Imports.ContainsKey("foo"), Is.True);
            Assert.That(result.Imports["foo"], Is.EqualTo("./bar"));
        }

        [Test]
        public void Parse_DetectsDefaultImport()
        {
            var result = Parse("import foo from './bar';");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Imports.ContainsKey("foo"), Is.True);
            Assert.That(result.Imports["foo"], Is.EqualTo("./bar"));
        }

        [Test]
        public void Parse_DetectsNamespaceImport()
        {
            var result = Parse("import * as foo from './bar';");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Imports.ContainsKey("foo"), Is.True);
            Assert.That(result.Imports["foo"], Is.EqualTo("./bar"));
        }

        [Test]
        public void Parse_DetectsNamedExport()
        {
            var result = Parse("export function foo() {}");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Exports.Contains("foo"), Is.True);
        }

        [Test]
        public void Parse_DetectsExportDefault()
        {
            var result = Parse("export default function() {}");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Exports.Contains("default"), Is.True);
        }

        [Test]
        public void Parse_ExportedFunction_SingleDeclarationWithExportType()
        {
            var result = Parse("export function foo() {}");

            Assert.That(result, Is.Not.Null);
            var decls = result.Declarations.Where(d => d.Name == "foo").ToList();
            Assert.That(decls.Count, Is.EqualTo(1));
            Assert.That(decls[0].DeclarationType, Is.EqualTo("export"));
        }

        [Test]
        public void Parse_ExportedConstFunction_SingleDeclarationWithExportType()
        {
            var result = Parse("export const foo = () => {};");

            Assert.That(result, Is.Not.Null);
            var decls = result.Declarations.Where(d => d.Name == "foo").ToList();
            Assert.That(decls.Count, Is.EqualTo(1));
            Assert.That(decls[0].DeclarationType, Is.EqualTo("export"));
        }

        [Test]
        public void Parse_ExportDefaultNamedFunction_SingleDeclarationWithExportDefaultType()
        {
            var result = Parse("export default function foo() {}");

            Assert.That(result, Is.Not.Null);
            var decls = result.Declarations.Where(d => d.Name == "foo").ToList();
            Assert.That(decls.Count, Is.EqualTo(1));
            Assert.That(decls[0].DeclarationType, Is.EqualTo("export_default"));
        }

        [Test]
        public void Parse_DetectsDynamicImportLiteral()
        {
            var result = Parse("import('./foo');");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Calls.Any(c => c.Context == "dynamic_import" && c.Name == "./foo"), Is.True);
        }

        [Test]
        public void Parse_SkipsDynamicImportVariable()
        {
            var result = Parse("const x = './foo'; import(x);");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Calls.Any(c => c.Context == "dynamic_import"), Is.False);
            Assert.That(result.Notes.Count, Is.GreaterThan(0));
        }

        [Test]
        public void Parse_DetectsPlainVar()
        {
            var result = Parse("var foo = 42;");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Declarations.Any(d => d.Name == "foo" && d.DeclarationType == "var"), Is.True);
        }

        [Test]
        public void Parse_FallsBackToScript()
        {
            // No import/export — should parse as script
            var result = Parse("const a = 1; function f() { return a; }");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Declarations.Any(d => d.Name == "f"), Is.True);
        }

        [Test]
        public void Parse_InvalidSyntax_ReturnsNull()
        {
            var result = Parse("const x = (;");

            Assert.That(result, Is.Null);
        }

        [Test]
        public void Parse_ReportsOneBasedLineAndColumn()
        {
            var result = Parse("const a = 1;\nfunction foo() {}\n");

            var decl = result.Declarations.First(d => d.Name == "foo");

            Assert.That(decl.Line, Is.EqualTo(2));
            Assert.That(decl.Column, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void Parse_DetectsConstValue()
        {
            var result = Parse("const UIText = 'Hello';");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Declarations.Any(d => d.Name == "UIText" && d.DeclarationType == "const"), Is.True);
        }

        [Test]
        public void Parse_DetectsObjectProperty()
        {
            var result = Parse("const o = { UIText: 'Hello' };");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Declarations.Any(d => d.Name == "UIText" && d.DeclarationType == "object_property"), Is.True);
        }

        [Test]
        public void Parse_DetectsObjectMethod()
        {
            var result = Parse("const o = { render() {} };");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Declarations.Any(d => d.Name == "render" && d.DeclarationType == "object_method"), Is.True);
        }

        [Test]
        public void Parse_DetectsClassPropertyAndMethod()
        {
            var result = Parse("class Foo { UIText = 1; render() {} }");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Declarations.Any(d => d.Name == "UIText" && d.DeclarationType == "class_property"), Is.True);
            Assert.That(result.Declarations.Any(d => d.Name == "render" && d.DeclarationType == "class_method"), Is.True);
        }

        [Test]
        public void Parse_DetectsPropertyAccess()
        {
            var result = Parse("Strings.UIText;");

            Assert.That(result, Is.Not.Null);
            var call = result.Calls.First(c => c.Context == "property_access");
            Assert.That(call.Name, Is.EqualTo("UIText"));
            Assert.That(call.ObjectName, Is.EqualTo("Strings"));
            Assert.That(call.IsComputed, Is.False);
        }

        [Test]
        public void Parse_DetectsComputedAccessWithIdentifier()
        {
            var result = Parse("obj[UIText];");

            Assert.That(result, Is.Not.Null);
            var call = result.Calls.First(c => c.Context == "computed_key");
            Assert.That(call.Name, Is.EqualTo("UIText"));
            Assert.That(call.ObjectName, Is.EqualTo("obj"));
            Assert.That(call.IsComputed, Is.True);
        }

        [Test]
        public void Parse_DetectsNestedObjectPath()
        {
            var result = Parse("a.b.c.value;");

            Assert.That(result, Is.Not.Null);
            var call = result.Calls.First(c => c.Name == "value");
            Assert.That(call.Context, Is.EqualTo("property_access"));
            Assert.That(call.ObjectName, Is.EqualTo("a.b.c"));
        }

        [Test]
        public void Parse_MemberCall_DoesNotDuplicatePropertyAccess()
        {
            var result = Parse("Strings.UIText();");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Calls.Count(c => c.Name == "UIText"), Is.EqualTo(1));
            Assert.That(result.Calls.Any(c => c.Context == "member_call"), Is.True);
            Assert.That(result.Calls.Any(c => c.Context == "property_access"), Is.False);
        }

        [Test]
        public void Parse_DetectsIdentifierRead()
        {
            var result = Parse("foo(UIText);");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Calls.Any(c => c.Name == "UIText" && c.Context == "identifier"), Is.True);
        }
    }
}
