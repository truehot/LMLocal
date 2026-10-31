using LMLocal.Core.Common;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Core.Common
{
    [TestFixture]
    public class StreamedTokenEstimatorTests
    {
        [Test]
        public void EstimatedTokens_WhenNothingAdded_IsZero()
        {
            var estimator = new StreamedTokenEstimator();

            Assert.That(estimator.EstimatedTokens, Is.EqualTo(0));
        }

        [Test]
        public void Add_WhenNullOrEmpty_DoesNotChangeEstimate()
        {
            var estimator = new StreamedTokenEstimator();

            estimator.Add(null);
            estimator.Add(string.Empty);

            Assert.That(estimator.EstimatedTokens, Is.EqualTo(0));
        }

        [Test]
        public void EstimatedTokens_ForAsciiText_CountsFourCharsPerToken()
        {
            var estimator = new StreamedTokenEstimator();

            estimator.Add(new string('a', 400));

            Assert.That(estimator.EstimatedTokens, Is.EqualTo(100));
        }

        [Test]
        public void EstimatedTokens_ForNonAsciiText_CountsTwoCharsPerToken()
        {
            var estimator = new StreamedTokenEstimator();

            estimator.Add(new string('\u0436', 400));

            Assert.That(estimator.EstimatedTokens, Is.EqualTo(200));
        }

        [Test]
        public void EstimatedTokens_ForMixedText_SumsBothWeights()
        {
            var estimator = new StreamedTokenEstimator();

            estimator.Add(new string('a', 8));
            estimator.Add(new string('\u0436', 4));

            // 8 / 4 + 4 / 2 = 4
            Assert.That(estimator.EstimatedTokens, Is.EqualTo(4));
        }

        [Test]
        public void EstimatedTokens_ForSingleShortChunk_IsAtLeastOne()
        {
            var estimator = new StreamedTokenEstimator();

            estimator.Add("hi");

            Assert.That(estimator.EstimatedTokens, Is.EqualTo(1));
        }

        [Test]
        public void EstimatedTokens_IsMonotonicAcrossAdds()
        {
            var estimator = new StreamedTokenEstimator();
            var previous = 0;

            for (var i = 0; i < 50; i++)
            {
                estimator.Add("token ");
                var current = estimator.EstimatedTokens;

                Assert.That(current, Is.GreaterThanOrEqualTo(previous));
                previous = current;
            }

            Assert.That(previous, Is.GreaterThan(0));
        }

        [Test]
        public void EstimatedTokens_DoesNotRoundPerChunk()
        {
            // 4 separate chunks of 1 character each would each floor to a minimum of 1 token,
            // producing 4. The estimate is computed over accumulated counters instead: 4 / 4 = 1.
            var estimator = new StreamedTokenEstimator();

            for (var i = 0; i < 4; i++)
                estimator.Add("a");

            Assert.That(estimator.EstimatedTokens, Is.EqualTo(1));
        }

        [Test]
        public void EstimatedTokens_WithDenseWeights_CountsThreeCharsPerToken()
        {
            // Tool results are file contents and JSON; they are calibrated with a denser divisor.
            var estimator = new StreamedTokenEstimator(3, StreamedTokenEstimator.DefaultNonAsciiCharsPerToken);

            estimator.Add(new string('a', 300));

            Assert.That(estimator.EstimatedTokens, Is.EqualTo(100));
        }

        [Test]
        public void EstimatedTokens_WithDenseWeights_IsHigherThanDefaultForSameText()
        {
            var dense = new StreamedTokenEstimator(3, StreamedTokenEstimator.DefaultNonAsciiCharsPerToken);
            var prose = new StreamedTokenEstimator();

            var text = new string('a', 1200);
            dense.Add(text);
            prose.Add(text);

            Assert.That(dense.EstimatedTokens, Is.EqualTo(400));
            Assert.That(prose.EstimatedTokens, Is.EqualTo(300));
            Assert.That(dense.EstimatedTokens, Is.GreaterThan(prose.EstimatedTokens));
        }

        [Test]
        public void EstimatedTokens_WithNonPositiveWeights_FallsBackToDefaults()
        {
            var estimator = new StreamedTokenEstimator(0, -1);

            estimator.Add(new string('a', 400));
            estimator.Add(new string('\u0436', 400));

            // Defaults: 400 / 4 + 400 / 2 = 300
            Assert.That(estimator.EstimatedTokens, Is.EqualTo(300));
        }
    }
}
