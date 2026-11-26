using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    [TestFixture]
    public class KnowledgePartialMatcherTests
    {
        [Test]
        public void GetSignificantTerms_DropsStopWordsLettersNumbersAndExtensions()
        {
            var terms = KnowledgePartialMatcher.GetSignificantTerms("error-list-context-menu-plan.md in b 2 JS v4");

            Assert.That(terms, Is.EqualTo(new[] { "error", "list", "context", "menu", "plan", "JS", "v4" }));
        }

        [Test]
        public void GetSignificantTerms_KeepsWordThatIsNotAnExtensionAfterDot()
        {
            var terms = KnowledgePartialMatcher.GetSignificantTerms("app.css styles window.lmApi");

            Assert.That(terms, Is.EqualTo(new[] { "app", "styles", "window", "lmApi" }));
        }

        [TestCase(2, 2)]
        [TestCase(3, 2)]
        [TestCase(4, 2)]
        [TestCase(5, 3)]
        [TestCase(7, 4)]
        [TestCase(10, 5)]
        public void GetRequiredCount_IsHalfButAtLeastTwo(int termCount, int expected)
        {
            Assert.That(KnowledgePartialMatcher.GetRequiredCount(termCount), Is.EqualTo(expected));
        }

        [Test]
        public void Stem_DropsTrailingSOfLongWordsOnly()
        {
            Assert.That(KnowledgePartialMatcher.Stem("guards"), Is.EqualTo("guard"));
            Assert.That(KnowledgePartialMatcher.Stem("bus"), Is.EqualTo("bus"));
            Assert.That(KnowledgePartialMatcher.Stem("policy"), Is.EqualTo("policy"));
        }

        [Test]
        public void Analyze_CountsFileNameAndSectionWordsTogether()
        {
            var terms = new[] { "subagent", "guards", "policy" };

            var match = KnowledgePartialMatcher.Analyze("subagent-notes.md", "# Rules\n\nThe guard blocks repeats.\n", terms);

            Assert.That(match.IsMatch, Is.True);
            Assert.That(match.WordCount, Is.EqualTo(2));
            Assert.That(match.SectionCount, Is.EqualTo(1));
            Assert.That(match.PresentTerms, Is.EqualTo(new[] { true, true, false }));
        }

        [Test]
        public void Analyze_NameWordPlusTwoSectionWords_BeatsSectionWithTwoWords()
        {
            var terms = new[] { "alpha", "beta", "gamma" };

            var withName = KnowledgePartialMatcher.Analyze("alpha-notes.md", "# A\n\nbeta gamma\n", terms);
            var withoutName = KnowledgePartialMatcher.Analyze("other.md", "# A\n\nbeta gamma\n", terms);

            Assert.That(withName.WordCount, Is.EqualTo(3));
            Assert.That(withoutName.WordCount, Is.EqualTo(2));
        }

        [Test]
        public void Analyze_BelowRequiredCount_IsNoMatch()
        {
            var terms = new[] { "alpha", "beta", "gamma", "delta", "epsilon" };

            var match = KnowledgePartialMatcher.Analyze("other.md", "# A\n\nalpha beta\n", terms);

            Assert.That(match.IsMatch, Is.False);
            Assert.That(match.PresentTerms, Is.EqualTo(new[] { true, true, false, false, false }));
        }

        [Test]
        public void Analyze_MatchesWordStartIncludingCamelCaseTail()
        {
            var match = KnowledgePartialMatcher.Analyze("other.md", "# A\n\nSubAgentsService uses the policy.\n", new[] { "subagent", "policy" });

            Assert.That(match.WordCount, Is.EqualTo(2));
        }

        [Test]
        public void Analyze_DoesNotMatchInsideWord()
        {
            var match = KnowledgePartialMatcher.Analyze("other.md", "# A\n\nbarfoo baz\n", new[] { "foo", "baz" });

            Assert.That(match.IsMatch, Is.False);
            Assert.That(match.PresentTerms, Is.EqualTo(new[] { false, true }));
        }

        [Test]
        public void Analyze_SectionCount_CountsOnlySectionsWithBestWordCount()
        {
            const string content = "# A\n\nalpha beta\n\n# B\n\nalpha beta\n\n# C\n\nalpha\n";

            var match = KnowledgePartialMatcher.Analyze("other.md", content, new[] { "alpha", "beta" });

            Assert.That(match.WordCount, Is.EqualTo(2));
            Assert.That(match.SectionCount, Is.EqualTo(2));
        }

        [Test]
        public void BuildDetails_CentersOnDensestWindow()
        {
            string content = "# A\n\nalpha " + new string('x', 400) + " beta gamma alpha\n";
            var terms = new[] { "alpha", "beta", "gamma" };
            var match = KnowledgePartialMatcher.Analyze("other.md", content, terms);

            var details = KnowledgePartialMatcher.BuildDetails(content, match, terms, 150);

            Assert.That(details.CenterIndex, Is.EqualTo(content.IndexOf("beta", System.StringComparison.Ordinal)));
            Assert.That(details.Line, Is.EqualTo(3));
            Assert.That(details.Heading, Is.EqualTo("A"));
            Assert.That(details.MatchedTerms, Is.EqualTo(new[] { "alpha", "beta", "gamma" }));
        }

        [Test]
        public void BuildDetails_UsesSectionOfBestRecord()
        {
            const string content = "# Name only\n\nnothing here\n\n# Text\n\nbeta notes\n";
            var terms = new[] { "alpha", "beta", "gamma" };
            var match = KnowledgePartialMatcher.Analyze("alpha-gamma.md", content, terms);

            var details = KnowledgePartialMatcher.BuildDetails(content, match, terms, 150);

            Assert.That(match.WordCount, Is.EqualTo(3));
            Assert.That(details.Heading, Is.EqualTo("Text"));
            Assert.That(details.CenterIndex, Is.EqualTo(content.IndexOf("beta", System.StringComparison.Ordinal)));
        }

        [Test]
        public void BuildDetails_WordsOnlyInFileName_HasNoSnippetCenter()
        {
            var terms = new[] { "alpha", "beta" };
            var match = KnowledgePartialMatcher.Analyze("alpha-beta.md", "# A\n\nnothing\n", terms);

            var details = KnowledgePartialMatcher.BuildDetails("# A\n\nnothing\n", match, terms, 150);

            Assert.That(details.CenterIndex, Is.EqualTo(-1));
            Assert.That(details.Line, Is.EqualTo(0));
            Assert.That(details.Heading, Is.Null);
            Assert.That(details.MatchedTerms, Is.EqualTo(new[] { "alpha", "beta" }));
        }

        [Test]
        public void BuildNote_ListsWordsFoundNowhere()
        {
            var note = KnowledgePartialMatcher.BuildNote(new[] { "subagent", "guardrails" }, new[] { true, false });

            Assert.That(note, Does.Contain("matched_terms"));
            Assert.That(note, Does.Contain("'guardrails'"));
            Assert.That(note, Does.Not.Contain("'subagent'"));
        }

        [Test]
        public void BuildNote_AllWordsFound_HasNoMissingList()
        {
            var note = KnowledgePartialMatcher.BuildNote(new[] { "alpha", "beta" }, new[] { true, true });

            Assert.That(note, Does.Not.Contain("Not found"));
        }
    }
}
