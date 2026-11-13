using LMLocal.Core.Common;
using LMLocal.Infrastructure.Streaming;

using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class TokenSpeedCalculatorTests
    {
        [Test]
        public void NoDeltas_ReportZero()
        {
            var calculator = new TokenSpeedCalculator(windowSeconds: 5);

            Assert.That(calculator.GetTokensPerSecond(), Is.EqualTo(0.0));
            Assert.That(calculator.GetAverageTokensPerSecond(), Is.EqualTo(0.0));
        }

        [Test]
        public void EstimatorAdd_ReturnsRawEstimateOfTheChunk()
        {
            // One ASCII character is a quarter of a token; rounding/clamping to 1 would inflate per-delta speeds 4x.
            Assert.That(new StreamedTokenEstimator().Add("a"), Is.EqualTo(0.25).Within(0.0001));
            Assert.That(new StreamedTokenEstimator().Add("abcd"), Is.EqualTo(1.0).Within(0.0001));
            Assert.That(new StreamedTokenEstimator().Add("\u0436"), Is.EqualTo(0.5).Within(0.0001));
            Assert.That(new StreamedTokenEstimator().Add(null), Is.EqualTo(0.0));
            Assert.That(new StreamedTokenEstimator().Add(string.Empty), Is.EqualTo(0.0));
        }
    }
}
