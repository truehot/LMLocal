using System.Collections.Generic;
using LMLocal.Infrastructure.Syntax;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Syntax
{
    [TestFixture]
    public class SyntaxCheckerFactoryTests
    {
        [Test]
        public void GetChecker_ReturnsJsChecker_ForJsExtensions()
        {
            var factory = CreateFactory();
            ISyntaxChecker jsChecker = factory.GetChecker("a.js");
            ISyntaxChecker mjsChecker = factory.GetChecker("b.mjs");
            ISyntaxChecker cjsChecker = factory.GetChecker("c.cjs");

            Assert.That(jsChecker, Is.Not.Null);
            Assert.That(jsChecker, Is.InstanceOf<JsSyntaxChecker>());
            Assert.That(mjsChecker, Is.InstanceOf<JsSyntaxChecker>());
            Assert.That(cjsChecker, Is.InstanceOf<JsSyntaxChecker>());
            Assert.That(mjsChecker, Is.Not.SameAs(jsChecker));
            Assert.That(cjsChecker, Is.Not.SameAs(jsChecker));
        }

        [Test]
        public void GetChecker_ReturnsCsChecker_ForCsFiles()
        {
            var factory = CreateFactory();

            Assert.That(factory.GetChecker("a.cs"), Is.InstanceOf<CSharpSyntaxChecker>());
        }

        [Test]
        public void GetChecker_ReturnsVbChecker_ForVbFiles()
        {
            var factory = CreateFactory();

            Assert.That(factory.GetChecker("a.vb"), Is.InstanceOf<VisualBasicSyntaxChecker>());
        }

        [Test]
        public void GetChecker_ReturnsNull_ForUnsupportedInputs()
        {
            var factory = CreateFactory();

            Assert.That(factory.GetChecker("a.py"), Is.Null);
            Assert.That(factory.GetChecker("a.jsx"), Is.Null);
            Assert.That(factory.GetChecker(null), Is.Null);
            Assert.That(factory.GetChecker(""), Is.Null);
            Assert.That(factory.GetChecker("noextension"), Is.Null);
        }

        [Test]
        public void GetChecker_IsCaseInsensitive()
        {
            var factory = CreateFactory();

            Assert.That(factory.GetChecker("A.JS"), Is.InstanceOf<JsSyntaxChecker>());
            Assert.That(factory.GetChecker("A.CS"), Is.InstanceOf<CSharpSyntaxChecker>());
            Assert.That(factory.GetChecker("A.VB"), Is.InstanceOf<VisualBasicSyntaxChecker>());
        }

        [Test]
        public void MjsChecker_IsModuleOnly()
        {
            ISyntaxChecker mjsChecker = CreateFactory().GetChecker("a.mjs");

            Assert.That(
                mjsChecker.IsSyntaxValid("import { x } from 'y';\nexport default x;", out List<SyntaxError> esErrors),
                Is.True);
            Assert.That(esErrors, Is.Empty);

            Assert.That(mjsChecker.IsSyntaxValid("var n = 0123;", out List<SyntaxError> sloppyErrors), Is.False);
            Assert.That(sloppyErrors.Count, Is.EqualTo(1));
        }

        [Test]
        public void CjsChecker_IsScriptOnly()
        {
            ISyntaxChecker cjsChecker = CreateFactory().GetChecker("a.cjs");

            Assert.That(cjsChecker.IsSyntaxValid("var n = 0123;", out List<SyntaxError> sloppyErrors), Is.True);
            Assert.That(sloppyErrors, Is.Empty);

            Assert.That(cjsChecker.IsSyntaxValid("import { x } from 'y';", out List<SyntaxError> importErrors), Is.False);
            Assert.That(importErrors.Count, Is.EqualTo(1));
        }

        [Test]
        public void JsChecker_FallsBackToScript()
        {
            ISyntaxChecker jsChecker = CreateFactory().GetChecker("a.js");

            Assert.That(jsChecker.IsSyntaxValid("var n = 0123;", out List<SyntaxError> sloppyErrors), Is.True);
            Assert.That(sloppyErrors, Is.Empty);

            Assert.That(jsChecker.IsSyntaxValid("import { x } from 'y';", out List<SyntaxError> importErrors), Is.True);
            Assert.That(importErrors, Is.Empty);
        }

        private static SyntaxCheckerFactory CreateFactory()
        {
            return new SyntaxCheckerFactory(new CSharpSyntaxChecker(), new VisualBasicSyntaxChecker());
        }
    }
}
