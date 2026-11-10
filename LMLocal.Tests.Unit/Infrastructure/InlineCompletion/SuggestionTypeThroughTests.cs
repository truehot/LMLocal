using LMLocal.Infrastructure.Autocompletions.InlineCompletion;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.InlineCompletion
{
    [TestFixture]
    public class SuggestionTypeThroughTests
    {
        [TestCase("r", "return x;", "eturn x;")]
        [TestCase("R", "return x;", "eturn x;")]
        [TestCase("\n", "\n    return;", "return;")]
        [TestCase("\n        ", "\n    return;", "return;")]
        [TestCase("    ", "    return;", "return;")]
        [TestCase("    ", "return;", null)]
        [TestCase("\n", "return;", null)]
        [TestCase("\nx", "\n    return;", null)]
        [TestCase("x", "return;", null)]
        public void TryConsume_MatchesPlanTable(string inserted, string suggestion, string expected)
        {
            bool result = SuggestionTagger.TryConsume(suggestion, inserted, out string remainder);

            if (expected == null)
            {
                Assert.That(result, Is.False, "expected no match");
            }
            else
            {
                Assert.That(result, Is.True, "expected a match");
                Assert.That(remainder, Is.EqualTo(expected));
            }
        }

        [Test]
        public void TryConsume_EmptyInserted_NoMatch()
        {
            Assert.That(SuggestionTagger.TryConsume("return;", "", out _), Is.False);
        }

        [Test]
        public void TryConsume_FullSuggestionConsumed_EmptyRemainder()
        {
            bool result = SuggestionTagger.TryConsume("ret", "ret", out string remainder);
            Assert.That(result, Is.True);
            Assert.That(remainder, Is.Empty);
        }
    }
}
