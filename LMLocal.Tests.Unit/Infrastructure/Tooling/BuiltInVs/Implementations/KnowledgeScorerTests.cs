using System;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    [TestFixture]
    public class KnowledgeScorerTests
    {
        // ---------------------------------------------------------------
        // Strict tier ordering: lower tiers must never outrank higher tiers.
        // ---------------------------------------------------------------

        [Test]
        public void Score_BodyExact_RanksAboveFilenameExact()
        {
            var body = KnowledgeScorer.Score(
                "unrelated.md",
                "The ChatHistory component is discussed.",
                "ChatHistory");

            var filename = KnowledgeScorer.Score(
                "ChatHistory.md",
                "# Other\n\nNothing relevant here.",
                "ChatHistory");

            Assert.That(body.Kind, Is.EqualTo(KnowledgeMatchKind.BodyExact));
            Assert.That(filename.Kind, Is.EqualTo(KnowledgeMatchKind.FilenameExact));
            Assert.That(body.Kind > filename.Kind, Is.True);
        }

        [Test]
        public void Score_AllTokensSection_RanksAboveFilenameExact()
        {
            var section = KnowledgeScorer.Score(
                "unrelated.md",
                "# Design\n\nThe system uses chat and history together.",
                "chat history");

            var filename = KnowledgeScorer.Score(
                "Chat History.md",
                "# Other\n\nNo tokens here.",
                "chat history");

            Assert.That(section.Kind, Is.EqualTo(KnowledgeMatchKind.SectionAllTokens));
            Assert.That(filename.Kind, Is.EqualTo(KnowledgeMatchKind.FilenameExact));
            Assert.That(section.Kind > filename.Kind, Is.True);
        }

        [Test]
        public void Score_ExactPhrase_RanksAboveAllTokensSection()
        {
            var exact = KnowledgeScorer.Score(
                "unrelated.md",
                "chat history is the topic",
                "chat history");

            var allTokens = KnowledgeScorer.Score(
                "unrelated2.md",
                "# S\n\nchat and history",
                "chat history");

            Assert.That(exact.Kind, Is.EqualTo(KnowledgeMatchKind.BodyExact));
            Assert.That(allTokens.Kind, Is.EqualTo(KnowledgeMatchKind.SectionAllTokens));
            Assert.That(exact.Kind > allTokens.Kind, Is.True);
        }

        [Test]
        public void Score_HeadingExact_RanksAboveBodyExact()
        {
            var heading = KnowledgeScorer.Score(
                "unrelated.md",
                "# ChatHistory Overview",
                "ChatHistory");

            var body = KnowledgeScorer.Score(
                "unrelated2.md",
                "The ChatHistory component.",
                "ChatHistory");

            Assert.That(heading.Kind, Is.EqualTo(KnowledgeMatchKind.HeadingExact));
            Assert.That(body.Kind, Is.EqualTo(KnowledgeMatchKind.BodyExact));
            Assert.That(heading.Kind > body.Kind, Is.True);
        }

        [Test]
        public void Score_ManyBodyOccurrences_DoNotOutrankSingleHeading()
        {
            var body = KnowledgeScorer.Score(
                "unrelated.md",
                "ChatHistory ChatHistory ChatHistory ChatHistory ChatHistory ChatHistory",
                "ChatHistory");

            var heading = KnowledgeScorer.Score(
                "unrelated2.md",
                "# ChatHistory\n\nnothing",
                "ChatHistory");

            Assert.That(body.Kind, Is.EqualTo(KnowledgeMatchKind.BodyExact));
            Assert.That(body.SubScore, Is.EqualTo(6));
            Assert.That(heading.Kind, Is.EqualTo(KnowledgeMatchKind.HeadingExact));
            Assert.That(heading.SubScore, Is.EqualTo(1));
            Assert.That(heading.Kind > body.Kind, Is.True);
        }

        [Test]
        public void Score_BodyExactPlusFilename_DoesNotOutrankHeading()
        {
            var bodyPlusFilename = KnowledgeScorer.Score(
                "ChatHistory.md",
                "The ChatHistory component is discussed.",
                "ChatHistory");

            var heading = KnowledgeScorer.Score(
                "unrelated.md",
                "# ChatHistory Overview",
                "ChatHistory");

            Assert.That(bodyPlusFilename.Kind, Is.EqualTo(KnowledgeMatchKind.BodyExact));
            Assert.That(heading.Kind, Is.EqualTo(KnowledgeMatchKind.HeadingExact));
            Assert.That(heading.Kind > bodyPlusFilename.Kind, Is.True);
        }

        [Test]
        public void Score_ExactPhraseWithAllTokenSections_KeepsBodyExactTier()
        {
            // Sections A and B each cover all tokens; section C has the exact phrase.
            // The all-token signal must not be lost or downgrade the exact-phrase tier.
            var result = KnowledgeScorer.Score(
                "unrelated.md",
                "# A\n\nchat and history\n\n# B\n\nchat and history\n\n# C\n\nchat history",
                "chat history");

            Assert.That(result.Kind, Is.EqualTo(KnowledgeMatchKind.BodyExact));
            Assert.That(result.Heading, Is.EqualTo("C"));
            Assert.That(result.SubScore, Is.EqualTo(1));
        }

        // ---------------------------------------------------------------
        // Filename scoring
        // ---------------------------------------------------------------

        [Test]
        public void ScoreFilename_SingleToken_PrefersBoundaryOverSubstring()
        {
            Assert.That(
                KnowledgeScorer.ScoreFilename("ChatHistory.md", new[] { "ChatHistory" }).Kind,
                Is.EqualTo(KnowledgeMatchKind.FilenameExact));

            Assert.That(
                KnowledgeScorer.ScoreFilename("ChatHistoryMigration.md", new[] { "ChatHistory" }).Kind,
                Is.EqualTo(KnowledgeMatchKind.FilenameSubstring));
        }

        [Test]
        public void ScoreFilename_MultiWord_UsesIndividualTokens()
        {
            Assert.That(
                KnowledgeScorer.ScoreFilename("Chat History.md", new[] { "chat", "history" }).Kind,
                Is.EqualTo(KnowledgeMatchKind.FilenameExact));

            Assert.That(
                KnowledgeScorer.ScoreFilename("ChatHistory.md", new[] { "chat", "history" }).Kind,
                Is.EqualTo(KnowledgeMatchKind.FilenameSubstring));
        }

        [Test]
        public void ScoreFilename_PartialMatch_ScoresSubstring()
        {
            var (kind, subScore) = KnowledgeScorer.ScoreFilename("Chat.md", new[] { "chat", "history" });

            Assert.That(kind, Is.EqualTo(KnowledgeMatchKind.FilenameSubstring));
            Assert.That(subScore, Is.EqualTo(1));

            Assert.That(
                KnowledgeScorer.ScoreFilename("Other.md", new[] { "chat", "history" }).Kind,
                Is.EqualTo(KnowledgeMatchKind.None));
        }

        [Test]
        public void ScoreFilename_SnakeCase_UsesUnderscoreAsBoundary()
        {
            Assert.That(
                KnowledgeScorer.ScoreFilename("chat_history.md", new[] { "chat", "history" }).Kind,
                Is.EqualTo(KnowledgeMatchKind.FilenameExact));
        }

        [Test]
        public void ScoreFilename_CamelCase_IsSubstringMatch()
        {
            // CamelCase humps are not split for exact matching: "chat" in "chatHistory"
            // is a substring (partial) match, not a boundary match.
            Assert.That(
                KnowledgeScorer.ScoreFilename("chatHistory.md", new[] { "chat" }).Kind,
                Is.EqualTo(KnowledgeMatchKind.FilenameSubstring));
        }

        // ---------------------------------------------------------------
        // Tokenization and word boundaries
        // ---------------------------------------------------------------

        [Test]
        public void TokenizeQuery_RemovesDuplicatesCaseInsensitively()
        {
            Assert.That(KnowledgeScorer.TokenizeQuery("foo Foo fOO"), Is.EqualTo(new[] { "foo" }));
        }

        [Test]
        public void TokenizeQuery_SplitsOnPunctuation()
        {
            Assert.That(
                KnowledgeScorer.TokenizeQuery("chat,history;architecture"),
                Is.EqualTo(new[] { "chat", "history", "architecture" }));
        }

        [Test]
        public void TokenizeQuery_KeepsApostrophesAndSplitsOnEmDash()
        {
            Assert.That(KnowledgeScorer.TokenizeQuery("what's up"), Is.EqualTo(new[] { "what's", "up" }));
            Assert.That(KnowledgeScorer.TokenizeQuery("foo—bar"), Is.EqualTo(new[] { "foo", "bar" }));
            Assert.That(KnowledgeScorer.TokenizeQuery("l'hopital"), Is.EqualTo(new[] { "l'hopital" }));
        }

        [Test]
        public void ContainsTokenWithWordBoundaries_RequiresBothBoundaries()
        {
            Assert.That(KnowledgeScorer.ContainsTokenWithWordBoundaries("foo bar", "foo"), Is.True);
            Assert.That(KnowledgeScorer.ContainsTokenWithWordBoundaries("a foo!", "foo"), Is.True);
            Assert.That(KnowledgeScorer.ContainsTokenWithWordBoundaries("foobar", "foo"), Is.False);
            Assert.That(KnowledgeScorer.ContainsTokenWithWordBoundaries("foo_bar", "foo"), Is.True);
            Assert.That(KnowledgeScorer.ContainsTokenWithWordBoundaries("foo-bar", "foo"), Is.True);
            Assert.That(KnowledgeScorer.ContainsTokenWithWordBoundaries("", "foo"), Is.False);
        }

        [Test]
        public void TryIndexOfTokenWithWordBoundaries_ReturnsTokenIndex()
        {
            Assert.That(KnowledgeScorer.TryIndexOfTokenWithWordBoundaries("xx foo yy", "foo", out int index), Is.True);
            Assert.That(index, Is.EqualTo(3));
        }

        [Test]
        public void Score_AllTokens_DoesNotMatchSubstringFalsePositive()
        {
            var result = KnowledgeScorer.Score(
                "unrelated.md",
                "# S\n\nThis is foobar and nothing else.",
                "foo bar");

            Assert.That(result.IsMatch, Is.False);
        }

        // ---------------------------------------------------------------
        // Section semantics
        // ---------------------------------------------------------------

        [Test]
        public void Score_AllTokensAcrossLinesInSameSection_Matches()
        {
            var result = KnowledgeScorer.Score(
                "unrelated.md",
                "# Design\n\nchat topic\n\nhistory details",
                "chat history");

            Assert.That(result.Kind, Is.EqualTo(KnowledgeMatchKind.SectionAllTokens));
            Assert.That(result.SubScore, Is.EqualTo(1));
        }

        [Test]
        public void Score_AllTokens_RequiresEveryToken()
        {
            var result = KnowledgeScorer.Score(
                "unrelated.md",
                "# Design\n\nchat topic\n\nnothing else",
                "chat history");

            Assert.That(result.IsMatch, Is.False);
        }

        [Test]
        public void Score_HeadingContributesToSectionTokens()
        {
            // A logical section includes its heading, so "Chat" heading + "history" body
            // together satisfy the all-token section tier.
            var result = KnowledgeScorer.Score(
                "unrelated.md",
                "# Chat\n\nhistory here",
                "chat history");

            Assert.That(result.Kind, Is.EqualTo(KnowledgeMatchKind.SectionAllTokens));
        }

        [Test]
        public void Score_AllTokensSnippet_ComesFromMatchingSection()
        {
            string content = "# Unrelated\n\nnothing to see\n\n# Target\n\nchat and history here";
            var result = KnowledgeScorer.Score("unrelated.md", content, "chat history");

            Assert.That(result.Kind, Is.EqualTo(KnowledgeMatchKind.SectionAllTokens));

            string snippet = KnowledgeScorer.BuildSnippet(content, result.BestMatchIndex, 150);
            Assert.That(snippet, Does.Contain("chat and history"));
        }

        // ---------------------------------------------------------------
        // Heading context
        // ---------------------------------------------------------------

        [Test]
        public void Score_ReturnsClosestHeading_NotLastHeading()
        {
            var result = KnowledgeScorer.Score(
                "unrelated.md",
                "# First Section\n\nChatHistory is discussed here.\n\n# Last Section\n\nNo match.",
                "ChatHistory");

            Assert.That(result.Kind, Is.EqualTo(KnowledgeMatchKind.BodyExact));
            Assert.That(result.Heading, Is.EqualTo("First Section"));
        }

        [Test]
        public void Score_HeadingMatch_ReturnsThatHeading()
        {
            var result = KnowledgeScorer.Score(
                "unrelated.md",
                "# ChatHistory Overview\n\nbody",
                "ChatHistory");

            Assert.That(result.Kind, Is.EqualTo(KnowledgeMatchKind.HeadingExact));
            Assert.That(result.Heading, Is.EqualTo("ChatHistory Overview"));
        }

        [Test]
        public void Score_BodyMatchWithoutHeading_HasNullHeading()
        {
            var result = KnowledgeScorer.Score(
                "unrelated.md",
                "Leading ChatHistory text.",
                "ChatHistory");

            Assert.That(result.Kind, Is.EqualTo(KnowledgeMatchKind.BodyExact));
            Assert.That(result.Heading, Is.Null);
        }

        // ---------------------------------------------------------------
        // Best-match index
        // ---------------------------------------------------------------

        [Test]
        public void Score_BestMatchIndex_IsRelativeToNormalizedContent()
        {
            string content = "The ChatHistory component is discussed.";
            var result = KnowledgeScorer.Score("unrelated.md", content, "ChatHistory");

            Assert.That(result.BestMatchIndex, Is.EqualTo(content.IndexOf("ChatHistory", System.StringComparison.Ordinal)));
            Assert.That(result.BestMatchLine, Is.EqualTo(1));
        }

        [Test]
        public void Score_BestMatchIndex_AccountsForCrlfNormalization()
        {
            string content = "intro\r\nChatHistory here";
            string normalized = KnowledgeScorer.NormalizeLineEndings(content);
            var result = KnowledgeScorer.Score("unrelated.md", normalized, "ChatHistory");

            Assert.That(result.BestMatchIndex, Is.EqualTo(normalized.IndexOf("ChatHistory", System.StringComparison.Ordinal)));
            Assert.That(result.BestMatchLine, Is.EqualTo(2));
        }

        [Test]
        public void Score_EmptyContent_StillScoresFilename()
        {
            var result = KnowledgeScorer.Score("ChatHistory.md", "", "ChatHistory");

            Assert.That(result.IsMatch, Is.True);
            Assert.That(result.Kind, Is.EqualTo(KnowledgeMatchKind.FilenameExact));
            Assert.That(result.BestMatchIndex, Is.EqualTo(-1));
            Assert.That(result.BestMatchLine, Is.EqualTo(0));
        }

        [Test]
        public void Score_NoMatch_ReturnsNoMatch()
        {
            var result = KnowledgeScorer.Score("unrelated.md", "nothing here", "zzz");

            Assert.That(result.IsMatch, Is.False);
            Assert.That(result.Kind, Is.EqualTo(KnowledgeMatchKind.None));
        }

        // ---------------------------------------------------------------
        // Snippet
        // ---------------------------------------------------------------

        [Test]
        public void BuildSnippet_EmptyOrNoCenter_ReturnsEmpty()
        {
            Assert.That(KnowledgeScorer.BuildSnippet("", 0, 10), Is.Empty);
            Assert.That(KnowledgeScorer.BuildSnippet("abc", -1, 10), Is.Empty);
        }

        [Test]
        public void BuildSnippet_ShortContent_HasNoEllipses()
        {
            Assert.That(KnowledgeScorer.BuildSnippet("hello", 2, 10), Is.EqualTo("hello"));
        }

        [Test]
        public void BuildSnippet_TruncatesBothSides()
        {
            string content = "012345678901234567890123456789";
            string snippet = KnowledgeScorer.BuildSnippet(content, 15, 5);

            Assert.That(snippet.StartsWith("..."), Is.True);
            Assert.That(snippet.EndsWith("..."), Is.True);
            Assert.That(snippet.Length, Is.LessThanOrEqualTo(2 * 5 + 6));
            Assert.That(snippet, Does.Contain("56789"));
        }

        [Test]
        public void BuildSnippet_LeadingOnly()
        {
            string content = "01234567890123456789";
            Assert.That(KnowledgeScorer.BuildSnippet(content, 0, 5), Is.EqualTo("01234..."));
        }

        [Test]
        public void BuildSnippet_TrailingOnly()
        {
            string content = "01234567890123456789";
            Assert.That(KnowledgeScorer.BuildSnippet(content, 19, 5), Is.EqualTo("...456789"));
        }

        [Test]
        public void BuildSnippet_SmallRadius_RespectsBound()
        {
            string content = "012345678901234567890123456789";
            string snippet = KnowledgeScorer.BuildSnippet(content, 15, 1);

            Assert.That(snippet.StartsWith("..."), Is.True);
            Assert.That(snippet.EndsWith("..."), Is.True);
            Assert.That(snippet.Length, Is.LessThanOrEqualTo(2 * 1 + 6));
        }

        [Test]
        public void NormalizeLineEndings_HandlesCrlfAndCr()
        {
            Assert.That(KnowledgeScorer.NormalizeLineEndings("a\r\nb\rc"), Is.EqualTo("a\nb\nc"));
        }

        // ---------------------------------------------------------------
        // Signal symmetry (exact phrase vs section all-tokens)
        // ---------------------------------------------------------------

        [Test]
        public void Analyze_ExactPhraseLineFeedsSectionTokens()
        {
            // The exact-phrase line is the only source of "foo" and "bar" in this section.
            // Before the fix it did not contribute tokens, so SectionsWithAllTokens was 0.
            var analysis = KnowledgeScorer.Analyze(
                "unrelated.md",
                "# S\n\nfoo bar only",
                "foo bar");

            Assert.That(analysis.BodyExactOccurrences, Is.EqualTo(1));
            Assert.That(analysis.SectionsWithAllTokens, Is.EqualTo(1));
        }

        [Test]
        public void Analyze_ExactPhraseAndAllTokens_AreIndependentSignals()
        {
            var analysis = KnowledgeScorer.Analyze(
                "unrelated.md",
                "# S\n\nfoo\n\nbar\n\nfoo bar",
                "foo bar");

            Assert.That(analysis.BodyExactOccurrences, Is.EqualTo(1));
            Assert.That(analysis.SectionsWithAllTokens, Is.EqualTo(1));
        }

        // ---------------------------------------------------------------
        // IsHeadingLine: CommonMark compliance — space required after #
        // ---------------------------------------------------------------

        [Test]
        public void IsHeadingLine_ValidMarkdownHeadings_AreDetected()
        {
            // Standard ATX headings
            Assert.That(KnowledgeScorer.Score("test.md", "# Title\nbody", "Title").Kind, Is.GreaterThan(KnowledgeMatchKind.None), "# Title");
            Assert.That(KnowledgeScorer.Score("test.md", "## Subtitle\nbody", "Subtitle").Kind, Is.GreaterThan(KnowledgeMatchKind.None), "## Subtitle");
            Assert.That(KnowledgeScorer.Score("test.md", "###### Deep\nbody", "Deep").Kind, Is.GreaterThan(KnowledgeMatchKind.None), "###### Deep");
            // Tab after # is valid
            Assert.That(KnowledgeScorer.Score("test.md", "#\tTabHeading\nbody", "TabHeading").Kind, Is.GreaterThan(KnowledgeMatchKind.None), "#\tTabHeading");
        }

        [Test]
        public void IsHeadingLine_CodeDirectives_AreNotHeadings()
        {
            // C++ / C# preprocessor / region directives must not be parsed as headings
            var resultInclude = KnowledgeScorer.Score("test.md", "#include <iostream>\nbody", "include");
            // "include" should still be findable as BodyExact with word boundaries
            Assert.That(resultInclude.Kind, Is.EqualTo(KnowledgeMatchKind.BodyExact));

            var resultDefine = KnowledgeScorer.Score("test.md", "#define DEBUG 1\nbody", "DEBUG");
            Assert.That(resultDefine.Kind, Is.EqualTo(KnowledgeMatchKind.BodyExact));

            var resultRegion = KnowledgeScorer.Score("test.md", "#region MyRegion\nbody", "MyRegion");
            Assert.That(resultRegion.Kind, Is.EqualTo(KnowledgeMatchKind.BodyExact));

            var resultPragma = KnowledgeScorer.Score("test.md", "#pragma warning disable\nbody", "warning");
            Assert.That(resultPragma.Kind, Is.EqualTo(KnowledgeMatchKind.BodyExact));
        }

        [Test]
        public void IsHeadingLine_NoSpaceAfterHash_IsNotHeading()
        {
            // CommonMark: #foo is not a heading — no space after #
            var result = KnowledgeScorer.Score("test.md", "#notheading\n\nSome body text.", "notheading");
            // "notheading" should NOT be found as a heading — it's plain body text
            Assert.That(result.Kind, Is.EqualTo(KnowledgeMatchKind.BodyExact));
            Assert.That(result.Heading, Is.Null, "No heading should be recorded");
        }

        [Test]
        public void IsHeadingLine_SectionBoundaries_NotBrokenByCodeDirectives()
        {
            // Code-like lines with # must not start new sections, so all-token matching
            // should work across them within the same logical section.
            var result = KnowledgeScorer.Score(
                "test.md",
                "# Config Section\n\n#define FOO 1\n\nHere is the foo config for bar.",
                "foo bar");

            Assert.That(result.Kind, Is.EqualTo(KnowledgeMatchKind.SectionAllTokens),
                "Tokens 'foo' and 'bar' should be found in the same section despite #define");
        }

        // ---------------------------------------------------------------
        // BestSectionLine: section matches report correct line numbers
        // ---------------------------------------------------------------

        [Test]
        public void Score_SectionAllTokens_LineReportsFirstTokenLine()
        {
            // "chat" first appears on line 3, "history" on line 4
            // The section line should be line 3 (first token's line)
            var result = KnowledgeScorer.Score(
                "test.md",
                "# Section\n\nchat topic\n\nhistory details",
                "chat history");

            Assert.That(result.Kind, Is.EqualTo(KnowledgeMatchKind.SectionAllTokens));
            Assert.That(result.BestMatchLine, Is.EqualTo(3),
                "Line should point to line 3 where 'chat' (first token) appears");
        }

        [Test]
        public void Score_SectionAllTokens_LineIsNotNextHeading()
        {
            // Regression: section line must not point at the following heading.
            // "chat" first appears on line 3, next heading is on line 6.
            var result = KnowledgeScorer.Score(
                "test.md",
                "# Section\n\nchat\n\n# Next Section\n\nhistory irrelevant",
                "chat history");

            Assert.That(result.Kind, Is.EqualTo(KnowledgeMatchKind.None),
                "Should be None: 'history' not in the first section");
        }

        [Test]
        public void Score_SectionAllTokens_LineInFinalSection()
        {
            // Final section: "chat" first on line 7, end of file.
            // BestMatchLine must be 7, not 10 (past EOF) which the old code gave.
            var result = KnowledgeScorer.Score(
                "test.md",
                "# Intro\n\nnothing\n\n# Main\n\nchat stuff\n\nhistory stuff",
                "chat history");

            Assert.That(result.Kind, Is.EqualTo(KnowledgeMatchKind.SectionAllTokens));
            Assert.That(result.BestMatchLine, Is.EqualTo(7),
                "Line should point to line 7 where 'chat' first appears in the final section");
        }

        [Test]
        public void Score_SectionAllTokens_MultipleSections_LinePointsToFirstMatch()
        {
            // Two sections both match. BestMatchLine should point to the first matching section.
            var result = KnowledgeScorer.Score(
                "test.md",
                "# First\n\nchat A\n\nhistory A\n\n# Second\n\nchat B\n\nhistory B",
                "chat history");

            Assert.That(result.Kind, Is.EqualTo(KnowledgeMatchKind.SectionAllTokens));
            // First section: "chat" on line 3, "history" on line 5 — first token is line 3
            Assert.That(result.BestMatchLine, Is.EqualTo(3),
                "Line should point to the first matching section's first token line");
        }

        [Test]
        public void Score_HeadingExact_LineIsCorrect()
        {
            var result = KnowledgeScorer.Score(
                "test.md",
                "\n\n# MyHeading\n\nbody",
                "MyHeading");

            Assert.That(result.Kind, Is.EqualTo(KnowledgeMatchKind.HeadingExact));
            Assert.That(result.BestMatchLine, Is.EqualTo(3));
        }

        [Test]
        public void Score_BodyExact_LineIsCorrect()
        {
            var result = KnowledgeScorer.Score(
                "test.md",
                "\n\nfound it here\n",
                "found");

            Assert.That(result.Kind, Is.EqualTo(KnowledgeMatchKind.BodyExact));
            Assert.That(result.BestMatchLine, Is.EqualTo(3));
        }

        // ---------------------------------------------------------------
        // Single-word BodyExact: word boundaries required to avoid false positives
        // ---------------------------------------------------------------

        [Test]
        public void Score_SingleWordBodyExact_RequiresWordBoundaries()
        {
            // "api" should NOT match inside "rapid" or "skapice"
            var resultNotEmbedded = KnowledgeScorer.Score(
                "test.md",
                "rapid development of the skapice",
                "api");
            Assert.That(resultNotEmbedded.IsMatch, Is.False,
                "'api' must not match inside 'rapid' or 'skapice'");

            // "api" SHOULD match as a standalone word
            var resultStandalone = KnowledgeScorer.Score(
                "test.md",
                "The API is documented here.",
                "api");
            Assert.That(resultStandalone.Kind, Is.EqualTo(KnowledgeMatchKind.BodyExact));

            // "api" SHOULD match at the start of a line
            var resultLineStart = KnowledgeScorer.Score(
                "test.md",
                "API reference:",
                "api");
            Assert.That(resultLineStart.Kind, Is.EqualTo(KnowledgeMatchKind.BodyExact));
        }

        [Test]
        public void Score_SingleWordBodyExact_FindsMultipleMatches()
        {
            var result = KnowledgeScorer.Score(
                "test.md",
                "The API and the api both refer to the same API interface.",
                "api");
            Assert.That(result.Kind, Is.EqualTo(KnowledgeMatchKind.BodyExact));
            Assert.That(result.SubScore, Is.EqualTo(3),
                "Should find 3 word-bounded occurrences of 'api'");
        }

        [Test]
        public void Score_MultiWordBodyExact_PhraseStillMatchesBySubstring()
        {
            // Multi-word queries keep the substring (non-word-bounded) phrase search for BodyExact.
            var result = KnowledgeScorer.Score(
                "test.md",
                "See the chat history map for details.",
                "chat history");

            Assert.That(result.Kind, Is.EqualTo(KnowledgeMatchKind.BodyExact),
                "Multi-word phrase 'chat history' should match as substring in body");
        }

        [Test]
        public void Score_MultiWordSectionTokens_RespectWordBoundariesPerToken()
        {
            // Even for multi-word queries, section token accumulation uses word boundaries per token.
            // "history" must not match inside "ChatHistoryMigration".
            var result = KnowledgeScorer.Score(
                "test.md",
                "# Config\n\nChatHistoryMigration handles chat for the app.",
                "chat history");

            Assert.That(result.Kind, Is.EqualTo(KnowledgeMatchKind.None),
                "'chat' matches standalone but 'history' must not match inside 'ChatHistoryMigration'");
        }

        [Test]
        public void Score_SingleTokenMultiWord_StillNeedsWordBoundaries()
        {
            // A single token like "ChatHistory" (despite being multi-word in meaning) is one token
            // and must have word boundaries to prevent false positives.
            var result = KnowledgeScorer.Score(
                "test.md",
                "The ChatHistoryMigration is different from ChatHistoryBase.",
                "ChatHistory");
            Assert.That(result.Kind, Is.EqualTo(KnowledgeMatchKind.None),
                "Single token 'ChatHistory' should not match inside 'ChatHistoryMigration' or 'ChatHistoryBase'");

            // Standalone "ChatHistory" should match
            var resultStandalone = KnowledgeScorer.Score(
                "test.md",
                "The ChatHistory component is used.",
                "ChatHistory");
            Assert.That(resultStandalone.Kind, Is.EqualTo(KnowledgeMatchKind.BodyExact));
        }
    }
}