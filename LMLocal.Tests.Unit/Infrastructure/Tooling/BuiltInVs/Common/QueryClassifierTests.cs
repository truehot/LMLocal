using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common
{
    [TestFixture]
    public class QueryClassifierTests
    {
        [TestCase("PaymentService")]
        [TestCase("Foo.Bar")]
        [TestCase("my-service")]
        [TestCase("_temp")]
        [TestCase("Foo2")]
        [TestCase("Payment_Service")]
        [TestCase("IService1")]
        [TestCase("$foo")]
        public void IsIdentifierQuery_ReturnsTrue_ForIdentifierLikeQueries(string query)
        {
            Assert.That(QueryClassifier.IsIdentifierQuery(query), Is.True);
        }

        [TestCase("error connecting to database")]
        [TestCase("hello world")]
        [TestCase("foo bar!")]
        [TestCase("")]
        [TestCase("  ")]
        [TestCase("PaymentService ")]
        [TestCase("two words")]
        public void IsIdentifierQuery_ReturnsFalse_ForPhrasesAndEmpty(string query)
        {
            Assert.That(QueryClassifier.IsIdentifierQuery(query), Is.False);
        }

        [Test]
        public void IsIdentifierQuery_ReturnsFalse_ForNull()
        {
            Assert.That(QueryClassifier.IsIdentifierQuery(null), Is.False);
        }
    }
}
