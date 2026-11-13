using BuildFixture.Lib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildFixture.Tests
{
    // A second test class, so that a filter by CalculatorTests runs 2 of the 3 tests.
    [TestClass]
    public class FormatTests
    {
        [TestMethod]
        public void Describe_PrefixesValue()
        {
            Assert.AreEqual("value=7", Calculator.Describe(7));
        }
    }
}
