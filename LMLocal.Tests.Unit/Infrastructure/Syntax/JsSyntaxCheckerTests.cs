using System.Collections.Generic;
using LMLocal.Infrastructure.Syntax;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Syntax
{
    [TestFixture]
    public class JsSyntaxCheckerTests
    {
        private JsSyntaxChecker _checker;

        [SetUp]
        public void SetUp()
        {
            _checker = CreateChecker(JsParseMode.Auto);
        }

        private static JsSyntaxChecker CreateChecker(JsParseMode mode)
        {
            return new JsSyntaxChecker(mode);
        }

        [Test]
        public void ValidPlainScript_ReturnsTrue()
        {
            bool valid = _checker.IsSyntaxValid("const x = 1; function f() { return x; }", out List<SyntaxError> errors);

            Assert.That(valid, Is.True);
            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void EmptyOrNullSource_ReturnsTrue()
        {
            Assert.That(_checker.IsSyntaxValid("", out List<SyntaxError> emptyErrors), Is.True);
            Assert.That(emptyErrors, Is.Empty);

            Assert.That(_checker.IsSyntaxValid(null, out List<SyntaxError> nullErrors), Is.True);
            Assert.That(nullErrors, Is.Empty);
        }

        [Test]
        public void BrokenScript_ReturnsFalse_WithSingleError()
        {
            bool valid = _checker.IsSyntaxValid("const x = (;", out List<SyntaxError> errors);

            Assert.That(valid, Is.False);
            Assert.That(errors.Count, Is.EqualTo(1));
            Assert.That(errors[0].Id, Is.Not.Empty);
            Assert.That(errors[0].Message, Is.Not.Empty);
        }

        [Test]
        public void BrokenScript_ReportsOneBasedLineAndColumn()
        {
            string source = "const a = 1;\nconst b = (;\n";

            bool valid = _checker.IsSyntaxValid(source, out List<SyntaxError> errors);

            Assert.That(valid, Is.False);
            Assert.That(errors.Count, Is.EqualTo(1));
            Assert.That(errors[0].StartLine, Is.EqualTo(2));
            Assert.That(errors[0].StartColumn, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void ModuleWithImportExport_ReturnsTrue()
        {
            string source = "import { x } from 'y';\nexport default x;";

            bool valid = _checker.IsSyntaxValid(source, out List<SyntaxError> errors);

            Assert.That(valid, Is.True);
            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void ImportWithoutSpaces_ReturnsTrue()
        {
            bool valid = _checker.IsSyntaxValid("import{x}from'y';", out List<SyntaxError> errors);

            Assert.That(valid, Is.True);
            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void SloppyOctalLiteral_ReturnsTrue()
        {
            bool valid = _checker.IsSyntaxValid("var n = 0123;", out List<SyntaxError> errors);

            Assert.That(valid, Is.True);
            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void WithStatement_ReturnsTrue()
        {
            bool valid = _checker.IsSyntaxValid("with (obj) { x = 1; }", out List<SyntaxError> errors);

            Assert.That(valid, Is.True);
            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void BrokenModuleCode_ReturnsFalse()
        {
            string source = "import { x } from 'y';\nconst = ;";

            bool valid = _checker.IsSyntaxValid(source, out List<SyntaxError> errors);

            Assert.That(valid, Is.False);
            Assert.That(errors.Count, Is.EqualTo(1));
        }

        [Test]
        public void ModuleOnly_AcceptsEsModule_RejectsSloppyScript()
        {
            var moduleChecker = CreateChecker(JsParseMode.ModuleOnly);

            Assert.That(
                moduleChecker.IsSyntaxValid("import { x } from 'y';\nexport default x;", out List<SyntaxError> esErrors),
                Is.True);
            Assert.That(esErrors, Is.Empty);

            Assert.That(moduleChecker.IsSyntaxValid("var n = 0123;", out List<SyntaxError> sloppyErrors), Is.False);
            Assert.That(sloppyErrors.Count, Is.EqualTo(1));
        }

        [Test]
        public void ScriptOnly_AcceptsSloppyScript_RejectsImportExport()
        {
            var scriptChecker = CreateChecker(JsParseMode.ScriptOnly);

            Assert.That(scriptChecker.IsSyntaxValid("var n = 0123;", out List<SyntaxError> sloppyErrors), Is.True);
            Assert.That(sloppyErrors, Is.Empty);

            Assert.That(scriptChecker.IsSyntaxValid("import { x } from 'y';", out List<SyntaxError> importErrors), Is.False);
            Assert.That(importErrors.Count, Is.EqualTo(1));
        }

    }
}
