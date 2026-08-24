using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common
{
    [TestFixture]
    public class ContentSearchMatcherTests
    {
        [Test]
        public void Match_ReturnsNoMatch_WhenQueryAbsent()
        {
            var m = ContentSearchMatcher.Match("public class Foo", "Bar", ".cs", true);
            Assert.That(m.IsMatch, Is.False);
        }

        [Test]
        public void Match_SubstringOnly_DoesNotRequireWholeWord()
        {
            var m = ContentSearchMatcher.Match("public class FooService", "Foo", ".cs", true);
            Assert.That(m.IsMatch, Is.True);
            Assert.That(m.IsExactWord, Is.False);
        }

        [Test]
        public void Match_ExactWord_AtWordBoundaries()
        {
            var m = ContentSearchMatcher.Match("public class Foo", "Foo", ".cs", true);
            Assert.That(m.IsMatch, Is.True);
            Assert.That(m.IsExactWord, Is.True);
        }

        [Test]
        public void Match_ExactWord_False_WhenInsideIdentifier()
        {
            var m = ContentSearchMatcher.Match("public class FooBar", "Foo", ".cs", true);
            Assert.That(m.IsMatch, Is.True);
            Assert.That(m.IsExactWord, Is.False);
        }

        [Test]
        public void Match_ExactWord_True_WhenSurroundedByDot()
        {
            // Dot is not an identifier char, so Foo in "ns.Foo" is a whole word.
            var m = ContentSearchMatcher.Match("Foo.Bar()", "Bar", ".cs", true);
            Assert.That(m.IsMatch, Is.True);
            Assert.That(m.IsExactWord, Is.True);
        }

        [Test]
        public void Match_ExactWord_NotComputed_WhenComputeExactWordFalse()
        {
            var m = ContentSearchMatcher.Match("public class Foo", "Foo", ".cs", false);
            Assert.That(m.IsMatch, Is.True);
            Assert.That(m.IsExactWord, Is.False);
        }

        [Test]
        public void Match_ClassifiesDeclarationLine()
        {
            var m = ContentSearchMatcher.Match("public class Foo", "Foo", ".cs", true);
            Assert.That(m.IsMatch, Is.True);
            Assert.That(m.Kind, Is.EqualTo(SearchMatchKind.Type));
        }

        [Test]
        public void IsExactWord_HandlesStartAndEndOfString()
        {
            Assert.That(ContentSearchMatcher.IsExactWord("Foo", "Foo", 0), Is.True);
            Assert.That(ContentSearchMatcher.IsExactWord("xFoo", "Foo", 1), Is.False);
        }
    }
}
