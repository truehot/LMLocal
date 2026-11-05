using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common
{
    [TestFixture]
    public class SearchQueryFallbackTests
    {
        [TestCase("ToolCallRecord")]
        [TestCase("finish_reason == \"length\"")]
        [TestCase("error or warning")]
        [TestCase("ORDER BY name")]
        [TestCase("a|b")]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void Create_PlainLiteral_HasNoFallback(string text)
        {
            var f = SearchQueryFallback.Create(text);

            Assert.That(f.Kind, Is.EqualTo(SearchFallbackKind.None));
            Assert.That(f.HasTerms, Is.False);
        }

        [Test]
        public void Create_OrSeparated_SplitsIntoDistinctTermsInOrder()
        {
            var f = SearchQueryFallback.Create("Summarize OR Summary OR summarize OR Summar");

            Assert.That(f.Kind, Is.EqualTo(SearchFallbackKind.OrSplit));
            Assert.That(f.Terms, Is.EqualTo(new[] { "Summarize", "Summary", "Summar" }));
        }

        [TestCase("ChatService | MainChat")]
        [TestCase("ChatService || MainChat")]
        [TestCase("  ChatService   OR   MainChat  ")]
        public void Create_PipeAndWhitespaceVariants_Split(string text)
        {
            var f = SearchQueryFallback.Create(text);

            Assert.That(f.Kind, Is.EqualTo(SearchFallbackKind.OrSplit));
            Assert.That(f.Terms, Is.EqualTo(new[] { "ChatService", "MainChat" }));
        }

        [Test]
        public void Create_OrWithQuotedPhrases_UnquotesEachTerm()
        {
            var f = SearchQueryFallback.Create("context_length_exceeded OR \"max context\" OR token");

            Assert.That(f.Terms, Is.EqualTo(new[] { "context_length_exceeded", "max context", "token" }));
        }

        [Test]
        public void Create_QuotedTextWithOrInside_UnquotesBeforeSplitting()
        {
            var f = SearchQueryFallback.Create("\"foo OR bar\"");

            Assert.That(f.Kind, Is.EqualTo(SearchFallbackKind.OrSplit));
            Assert.That(f.Terms, Is.EqualTo(new[] { "foo", "bar" }));
        }

        [Test]
        public void Create_SeparatelyQuotedTerms_AreNotTreatedAsOneQuotedValue()
        {
            var f = SearchQueryFallback.Create("\"foo\" OR \"bar baz\"");

            Assert.That(f.Kind, Is.EqualTo(SearchFallbackKind.OrSplit));
            Assert.That(f.Terms, Is.EqualTo(new[] { "foo", "bar baz" }));
        }

        [Test]
        public void Create_QuotedValueWithUnescapedInnerQuote_IsKeptAsLiteral()
        {
            Assert.That(SearchQueryFallback.Create("\"a\" == \"b\"").HasTerms, Is.False);
        }

        [Test]
        public void Create_WholeTextQuoted_Unquotes()
        {
            var f = SearchQueryFallback.Create("\"WebView2MessageType.\"");

            Assert.That(f.Kind, Is.EqualTo(SearchFallbackKind.Unquoted));
            Assert.That(f.Terms, Is.EqualTo(new[] { "WebView2MessageType." }));
        }

        [Test]
        public void Create_WholeTextQuoted_UnescapesInnerQuotes()
        {
            var f = SearchQueryFallback.Create("\"Name == \\\"read\"");

            Assert.That(f.Terms, Is.EqualTo(new[] { "Name == \"read" }));
        }

        [TestCase("\"\"")]
        [TestCase("\"")]
        public void Create_EmptyOrLoneQuote_HasNoFallback(string text)
        {
            Assert.That(SearchQueryFallback.Create(text).HasTerms, Is.False);
        }

        [Test]
        public void FindTerm_ReturnsFirstMatchingTermCaseInsensitive()
        {
            var f = SearchQueryFallback.Create("Alpha OR Beta");

            Assert.That(f.FindTerm("has beta and alpha"), Is.EqualTo("Alpha"));
            Assert.That(f.FindTerm("only BETA here"), Is.EqualTo("Beta"));
            Assert.That(f.FindTerm("nothing"), Is.Null);
            Assert.That(f.FindTerm(null), Is.Null);
        }

        [Test]
        public void BuildNote_OrSplit_ListsTermsAndOutcome()
        {
            var f = SearchQueryFallback.Create("Alpha OR Beta");

            var found = f.BuildNote(3);
            var none = f.BuildNote(0);

            Assert.That(found, Does.Contain("'Alpha', 'Beta'").And.Contain("does not support OR").And.Contain("matched_term"));
            Assert.That(found, Does.Contain("not for the original text"));
            Assert.That(none, Does.Contain("found no matches either"));
        }

        [Test]
        public void BuildNote_Unquoted_MentionsRemovedQuotes()
        {
            var note = SearchQueryFallback.Create("\"Interlocked\"").BuildNote(1);

            Assert.That(note, Does.Contain("quotes were removed").And.Contain("'Interlocked'"));
        }

        [Test]
        public void BuildNote_None_IsNull()
        {
            Assert.That(SearchQueryFallback.None.BuildNote(0), Is.Null);
        }
    }
}
