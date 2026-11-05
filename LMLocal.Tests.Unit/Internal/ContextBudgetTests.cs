using System.Collections.Generic;
using LMLocal.Application.Chat;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    /// <summary>
    /// Covers: where the window comes from, how the estimator is calibrated, and that the fixed overhead is not subtracted twice.
    /// </summary>
    [TestFixture]
    public class ContextBudgetTests
    {
        private static ContextBudget Create(
            ContextCalibration calibration = null,
            int? errorContext = null,
            int? profileContext = null,
            int fallbackContext = 16384,
            int? maxOutputTokens = null,
            int fixedOverhead = 0)
        {
            return ContextBudget.Create(calibration, errorContext, profileContext, fallbackContext, maxOutputTokens, fixedOverhead);
        }

        [Test]
        public void Factor_FromSuccessfulRound_IsPromptTokensOverEstimate()
        {
            var calibration = new ContextCalibration();

            calibration.RecordSuccessfulRound(promptTokens: 900, envelopeEstimate: 600);

            Assert.That(calibration.Factor, Is.EqualTo(1.5).Within(0.001));
        }

        [Test]
        public void Factor_IsClampedToTheCredibleRange()
        {
            var calibration = new ContextCalibration();

            calibration.RecordSuccessfulRound(promptTokens: 10, envelopeEstimate: 1000);
            Assert.That(calibration.Factor, Is.EqualTo(ContextCalibration.MinFactor));

            calibration.RecordSuccessfulRound(promptTokens: 100000, envelopeEstimate: 10);
            Assert.That(calibration.Factor, Is.EqualTo(ContextCalibration.MaxFactor));
        }

        [Test]
        public void Factor_IgnoresUnusableMeasurements()
        {
            var calibration = new ContextCalibration();

            calibration.RecordSuccessfulRound(promptTokens: null, envelopeEstimate: 500);
            calibration.RecordSuccessfulRound(promptTokens: 500, envelopeEstimate: 0);
            calibration.RecordSuccessfulRound(promptTokens: 0, envelopeEstimate: 500);

            Assert.That(calibration.Factor, Is.EqualTo(1.0));
        }

        [Test]
        public void Factor_IsDroppedWhenTheModelChanges()
        {
            var calibration = new ContextCalibration();
            calibration.ResetForModel("model-a");
            calibration.RecordSuccessfulRound(promptTokens: 900, envelopeEstimate: 600);

            calibration.ResetForModel("model-a");
            Assert.That(calibration.Factor, Is.EqualTo(1.5).Within(0.001), "the same model keeps its coefficient");

            calibration.ResetForModel("model-b");
            Assert.That(calibration.Factor, Is.EqualTo(1.0), "a different tokenizer must not inherit the coefficient");
        }

        [Test]
        public void Window_FromTheProviderErrorWinsOverTheProfile()
        {
            var budget = Create(errorContext: 10000, profileContext: 32768);

            Assert.That(budget.ContextLength, Is.EqualTo(10000),
                "llama.cpp reports the real window; the profile is frequently larger than what is actually served");
        }

        [Test]
        public void Window_FallsBackToTheProfileThenToTheLastKnownValue()
        {
            Assert.That(Create(profileContext: 8192).ContextLength, Is.EqualTo(8192));
            Assert.That(Create(fallbackContext: 4096).ContextLength, Is.EqualTo(4096));
        }

        [Test]
        public void OutputReserve_DefaultsToFifteenPercentOfTheWindow()
        {
            var budget = Create(profileContext: 10000);

            Assert.That(budget.OutputReserve, Is.EqualTo(1500));
        }

        [Test]
        public void OutputReserve_UsesMaxOutputTokensWhenKnown()
        {
            var budget = Create(profileContext: 10000, maxOutputTokens: 2048);

            Assert.That(budget.OutputReserve, Is.EqualTo(2048));
        }

        [Test]
        public void Margin_IsLargerWhileUncalibrated()
        {
            var uncalibrated = Create(profileContext: 10000);

            var calibration = new ContextCalibration();
            calibration.RecordSuccessfulRound(900, 900); // k = 1.0 is still "measured", but let us use a real ratio
            calibration.RecordSuccessfulRound(800, 700); // k ≈ 1.14

            var calibrated = Create(calibration, profileContext: 10000);

            Assert.That(uncalibrated.Margin, Is.GreaterThan(calibrated.Margin),
                "without a coefficient the estimator cannot be trusted, so the margin must be wider");
        }

        [Test]
        public void HistoryLimit_SubtractsTheFixedOverhead()
        {
            var budget = Create(profileContext: 10000, maxOutputTokens: 1000, fixedOverhead: 800);

            Assert.That(budget.EnvelopeLimit, Is.EqualTo(10000 - 1000 - budget.Margin));
            Assert.That(budget.HistoryLimit, Is.EqualTo(budget.EnvelopeLimit - 800));
        }

        [Test]
        public void Real_AppliesTheCalibrationCoefficient()
        {
            var calibration = new ContextCalibration();
            calibration.RecordSuccessfulRound(promptTokens: 1200, envelopeEstimate: 800); // k = 1.5

            var budget = Create(calibration, profileContext: 10000);

            Assert.That(budget.Real(100), Is.EqualTo(150));
        }

        [Test]
        public void SummaryInput_LeavesRoomForTheAnswerAndThePrompt()
        {
            var budget = Create(profileContext: 10000);

            Assert.That(budget.SummaryOutput, Is.GreaterThan(0));
            Assert.That(budget.SummaryInputLimit, Is.GreaterThan(0));
            Assert.That(budget.SummaryOutput + budget.SummaryInputLimit, Is.LessThan(budget.ContextLength),
                "the summary envelope must fit the window with room for the margin");
        }

        [Test]
        public void RecordOverflowError_KeepsTheWindowHint()
        {
            var calibration = new ContextCalibration();

            calibration.RecordOverflowError(promptTokens: 9000, contextLength: 8192);

            Assert.That(calibration.ErrorContextLength, Is.EqualTo(8192));
        }

        [Test]
        public void ApiErrorParser_ReadsLlamaCppSizeHints()
        {
            const string body = "{\"error\":{\"message\":\"exceeds the available context size\","
                                + "\"type\":\"exceed_context_size_error\",\"n_prompt_tokens\":9000,\"n_ctx\":8192}}";

            var info = ApiErrorParser.ParseErrorBody(body);

            Assert.That(info.PromptTokens, Is.EqualTo(9000));
            Assert.That(info.ContextLength, Is.EqualTo(8192));
        }

        [Test]
        public void ApiErrorParser_ReadsSizeHintsFromTheRootOfTheBody()
        {
            const string body = "{\"message\":\"prompt is too long\",\"n_prompt_tokens\":\"5000\",\"n_ctx\":\"4096\"}";

            var info = ApiErrorParser.ParseErrorBody(body);

            Assert.That(info.PromptTokens, Is.EqualTo(5000));
            Assert.That(info.ContextLength, Is.EqualTo(4096));
        }

        [Test]
        public void ApiErrorParser_LeavesHintsNullWhenAbsent()
        {
            var info = ApiErrorParser.ParseErrorBody("{\"error\":{\"message\":\"Invalid API key\"}}");

            Assert.That(info.PromptTokens, Is.Null);
            Assert.That(info.ContextLength, Is.Null);
        }

        [Test]
        public void EnvelopeEstimator_CountsSystemPromptMessagesAndToolDefinitions()
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage("system", "you are a helpful assistant"),
                new ChatMessage("user", "hello")
            };

            var tools = new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Name = "read_file_lines",
                    Description = "Reads lines from a file",
                    Parameters = new ToolParameters
                    {
                        Type = "object",
                        Properties = new Dictionary<string, ToolDetails>
                        {
                            { "file_path", new ToolDetails { Type = "string", Description = "Path" } }
                        }
                    }
                }
            };

            int withoutTools = EnvelopeEstimator.Estimate(messages, null);
            int withTools = EnvelopeEstimator.Estimate(messages, tools);

            Assert.That(withoutTools, Is.GreaterThan(0));
            Assert.That(withTools, Is.GreaterThan(withoutTools),
                "tool definitions travel with every request and are not part of the history");
        }

        [Test]
        public void HistoryLimit_SubtractsTheRealFixedOverhead()
        {
            var calibration = new ContextCalibration();
            calibration.RecordSuccessfulRound(promptTokens: 1200, envelopeEstimate: 800); // k = 1.5

            var budget = Create(calibration, profileContext: 10000, maxOutputTokens: 1000, fixedOverhead: 800);

            Assert.That(budget.HistoryLimit, Is.EqualTo(budget.EnvelopeLimit - 1200),
                "the overhead is an estimate, so it has to be converted before it leaves the real window");
        }

        [Test]
        public void SummaryEnvelope_FitsTheWindowInTheProvidersTokens()
        {
            var calibration = new ContextCalibration();
            calibration.RecordSuccessfulRound(promptTokens: 1800, envelopeEstimate: 600); // k = 3.0, the worst case

            var budget = Create(calibration, profileContext: 10000);

            Assert.That(budget.SummaryInputLimit + budget.SummaryOutput + budget.Margin,
                Is.LessThanOrEqualTo(budget.ContextLength),
                "the summary prompt is subtracted in real tokens, so a large coefficient cannot push the call over the window");
        }

        [Test]
        public void RecordOverflowError_DerivesTheCoefficientFromTheRejectedRequest()
        {
            var calibration = new ContextCalibration();

            calibration.RecordOverflowError(promptTokens: 900, contextLength: 8192, envelopeEstimate: 600);

            Assert.That(calibration.Factor, Is.EqualTo(1.5).Within(0.001),
                "the first overflow of a session must not be judged with k = 1: the provider just measured the envelope");
            Assert.That(calibration.ErrorContextLength, Is.EqualTo(8192));
        }

        [Test]
        public void RecordOverflowError_WithoutAnEnvelopeLeavesTheCoefficientAlone()
        {
            var calibration = new ContextCalibration();

            calibration.RecordOverflowError(promptTokens: 900, contextLength: 8192);

            Assert.That(calibration.Factor, Is.EqualTo(1.0));
            Assert.That(calibration.ErrorContextLength, Is.EqualTo(8192), "the window hint is kept for diagnostics either way");
        }
    }
}
