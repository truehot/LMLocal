using System.Collections.Generic;
using LMLocal.Infrastructure.Syntax;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Syntax
{
    [TestFixture]
    public class VisualBasicSyntaxCheckerTests
    {
        private VisualBasicSyntaxChecker _checker;

        [SetUp]
        public void SetUp()
        {
            _checker = new VisualBasicSyntaxChecker();
        }

        [Test]
        public void ValidModule_ReturnsTrue()
        {
            string source = "Module M\r\nSub Main()\r\nConsole.WriteLine(\"hi\")\r\nEnd Sub\r\nEnd Module";

            bool valid = _checker.IsSyntaxValid(source, out List<SyntaxError> errors);

            Assert.That(valid, Is.True);
            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void EmptyOrNullSource_ReturnsFalse()
        {
            Assert.That(_checker.IsSyntaxValid("", out List<SyntaxError> emptyErrors), Is.False);
            Assert.That(emptyErrors, Is.Empty);

            Assert.That(_checker.IsSyntaxValid(null, out List<SyntaxError> nullErrors), Is.False);
            Assert.That(nullErrors, Is.Empty);
        }

        [Test]
        public void BrokenCode_ReturnsFalse_WithErrors()
        {
            string source = "Module M\r\nSub Main()\r\n    Dim x As = 1\r\nEnd Sub\r\nEnd Module";

            bool valid = _checker.IsSyntaxValid(source, out List<SyntaxError> errors);

            Assert.That(valid, Is.False);
            Assert.That(errors, Is.Not.Empty);
            Assert.That(errors[0].Id, Is.Not.Empty);
            Assert.That(errors[0].Message, Is.Not.Empty);
        }

        [Test]
        public void BrokenCode_ReportsOneBasedPositions()
        {
            string source = "Module M\r\nSub Main()\r\n    Dim x As = 1\r\nEnd Sub\r\nEnd Module";

            bool valid = _checker.IsSyntaxValid(source, out List<SyntaxError> errors);

            Assert.That(valid, Is.False);
            Assert.That(errors[0].StartLine, Is.GreaterThanOrEqualTo(1));
            Assert.That(errors[0].StartColumn, Is.GreaterThanOrEqualTo(1));
        }
    }
}