using BuildFixture.Lib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildFixture.Tests
{
    [TestClass]
    public class CalculatorTests
    {
        [TestMethod]
        public void Add_ReturnsSum()
        {
            Assert.AreEqual(5, Calculator.Add(2, 3));
        }

        [TestMethod]
        public void Add_WithNegative_ReturnsSum()
        {
            Assert.AreEqual(-1, Calculator.Add(2, -3));
        }
    }
}
