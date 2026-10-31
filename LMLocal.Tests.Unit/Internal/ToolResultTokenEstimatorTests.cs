using System.Collections.Generic;
using LMLocal.Application.Tool;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Internal
{
    [TestFixture]
    public class ToolResultTokenEstimatorTests
    {
        [Test]
        public void Estimate_WhenNull_IsZero()
        {
            Assert.That(ToolResultTokenEstimator.Estimate(null), Is.EqualTo(0));
        }

        [Test]
        public void Estimate_WhenEmpty_IsZero()
        {
            Assert.That(ToolResultTokenEstimator.Estimate(new List<ToolResultMessage>()), Is.EqualTo(0));
        }

        [Test]
        public void Estimate_ForStringResult_UsesDenseWeight()
        {
            var results = new List<ToolResultMessage>
            {
                new ToolResultMessage { Result = new string('a', 300) }
            };

            // Dense weight: 300 / 3 = 100 tokens. The prose weight would give 75.
            Assert.That(ToolResultTokenEstimator.Estimate(results), Is.EqualTo(100));
        }

        [Test]
        public void Estimate_ForNullResult_ContributesNothing()
        {
            var results = new List<ToolResultMessage>
            {
                new ToolResultMessage { Result = null }
            };

            Assert.That(ToolResultTokenEstimator.Estimate(results), Is.EqualTo(0));
        }

        [Test]
        public void Estimate_ForNonStringResult_CountsSerializedJson()
        {
            var results = new List<ToolResultMessage>
            {
                new ToolResultMessage { Result = new { Success = true } }
            };

            // Measures exactly the text ToolMessageFactory hands to the model, i.e. the JSON.
            var expected = new StreamedTokenEstimator(3, StreamedTokenEstimator.DefaultNonAsciiCharsPerToken);
            expected.Add(ToolMessageFactory.ToContent(new { Success = true }));

            Assert.That(ToolResultTokenEstimator.Estimate(results), Is.EqualTo(expected.EstimatedTokens));
        }

        [Test]
        public void Estimate_AccumulatesAcrossResults()
        {
            var results = new List<ToolResultMessage>
            {
                new ToolResultMessage { Result = new string('a', 300) },
                new ToolResultMessage { Result = new string('a', 300) }
            };

            Assert.That(ToolResultTokenEstimator.Estimate(results), Is.EqualTo(200));
        }

        [Test]
        public void Estimate_ForNonAsciiResult_UsesNonAsciiWeight()
        {
            var results = new List<ToolResultMessage>
            {
                new ToolResultMessage { Result = new string('\u0436', 400) }
            };

            // Non-ASCII keeps the 2 chars/token weight: 400 / 2 = 200.
            Assert.That(ToolResultTokenEstimator.Estimate(results), Is.EqualTo(200));
        }

        [Test]
        public void ToContent_ForNull_IsEmptyString()
        {
            Assert.That(ToolMessageFactory.ToContent(null), Is.Empty);
        }

        [Test]
        public void ToContent_ForString_IsUnchanged()
        {
            Assert.That(ToolMessageFactory.ToContent("plain text"), Is.EqualTo("plain text"));
        }
    }
}
