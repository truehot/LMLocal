using LMLocal.Core.Common;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class DebugStopTextTests
    {
        // ────────────────────────────── IsSimpleType ──────────────────────────────

        [TestCase("int")]
        [TestCase("uint")]
        [TestCase("long")]
        [TestCase("double")]
        [TestCase("decimal")]
        [TestCase("bool")]
        [TestCase("char")]
        [TestCase("string")]
        [TestCase("int?")]
        public void IsSimpleType_IsTrueForLanguageKeywords(string type)
        {
            Assert.That(DebugStopText.IsSimpleType(type), Is.True);
        }

        [TestCase("System.Int32")]
        [TestCase("System.String")]
        [TestCase("System.Boolean")]
        [TestCase("System.Decimal")]
        [TestCase("System.Guid")]
        [TestCase("System.DateTime")]
        [TestCase("System.DateTimeOffset")]
        [TestCase("System.TimeSpan")]
        [TestCase("System.Nullable<System.Int32>")]
        public void IsSimpleType_IsTrueForFullNames(string type)
        {
            Assert.That(DebugStopText.IsSimpleType(type), Is.True);
        }

        [TestCase("  int  ")]
        public void IsSimpleType_IgnoresSurroundingWhitespace(string type)
        {
            Assert.That(DebugStopText.IsSimpleType(type), Is.True);
        }

        [TestCase("Order")]
        [TestCase("System.Collections.Generic.List<int>")]
        [TestCase("MyNamespace.Color")]
        [TestCase("byte[]")]
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void IsSimpleType_IsFalseForEverythingElse(string type)
        {
            Assert.That(DebugStopText.IsSimpleType(type), Is.False,
                "an unrecognized type must not be evaluated: reading its value can run code in the debugged process");
        }

        // ────────────────────────────── Unquote ──────────────────────────────

        [Test]
        public void Unquote_StripsTheQuotes()
        {
            Assert.That(DebugStopText.Unquote("\"text\""), Is.EqualTo("text"));
        }

        [Test]
        public void Unquote_KeepsAnUnquotedValue()
        {
            Assert.That(DebugStopText.Unquote("42"), Is.EqualTo("42"));
        }

        [Test]
        public void Unquote_HandlesEmptyStringLiteral()
        {
            Assert.That(DebugStopText.Unquote("\"\""), Is.EqualTo(string.Empty));
        }

        [TestCase("\"")]
        [TestCase(null)]
        public void Unquote_ReturnsShortOrNullValuesUnchanged(string value)
        {
            Assert.That(DebugStopText.Unquote(value), Is.EqualTo(value));
        }

        // ────────────────────────────── HasValue ──────────────────────────────

        [TestCase("null")]
        [TestCase("NULL")]
        [TestCase("Nothing")]
        [TestCase("")]
        [TestCase(null)]
        public void HasValue_IsFalseForTheNothingValues(string value)
        {
            Assert.That(DebugStopText.HasValue(value), Is.False);
        }

        [TestCase("0")]
        [TestCase("\"\"")]
        [TestCase("false")]
        [TestCase("{}")]
        public void HasValue_IsTrueForRealValues(string value)
        {
            Assert.That(DebugStopText.HasValue(value), Is.True);
        }

        // ────────────────────────────── Flatten ──────────────────────────────

        [Test]
        public void Flatten_ReplacesRealLineBreaksWithSpaces()
        {
            Assert.That(DebugStopText.Flatten("first\r\nsecond"), Is.EqualTo("first second"));
            Assert.That(DebugStopText.Flatten("first\nsecond"), Is.EqualTo("first second"));
        }

        [Test]
        public void Flatten_ReplacesEscapedLineBreaksWithSpaces()
        {
            Assert.That(DebugStopText.Flatten(@"first\r\nsecond"), Is.EqualTo("first second"));
        }

        [Test]
        public void Flatten_CollapsesRunsOfWhitespace()
        {
            Assert.That(DebugStopText.Flatten("  first \t  second  "), Is.EqualTo("first second"));
        }

        [Test]
        public void Flatten_KeepsABackslashThatIsNotAnEscape()
        {
            Assert.That(DebugStopText.Flatten(@"C:\temp\file.cs"), Is.EqualTo(@"C:\temp\file.cs"));
        }

        [Test]
        public void Flatten_KeepsABareEscapedLetterInsideAPath()
        {
            // "C:\new\table" contains \n and \t, which are not line breaks here.
            Assert.That(DebugStopText.Flatten(@"C:\new\table"), Is.EqualTo(@"C:\new\table"));
        }

        [Test]
        public void Flatten_ReplacesSeveralEscapedLineBreaks()
        {
            Assert.That(DebugStopText.Flatten(@"a\r\nb\r\nc"), Is.EqualTo("a b c"));
        }

        [TestCase("")]
        [TestCase(null)]
        public void Flatten_ReturnsEmptyValuesUnchanged(string value)
        {
            Assert.That(DebugStopText.Flatten(value), Is.EqualTo(value));
        }
    }
}
