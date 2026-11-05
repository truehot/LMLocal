using System.Collections.Generic;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Core.Models;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class ContextOverflowDetectorTests
    {
        private static StreamCompletionResult Result(
            string finishReason = null,
            string errorMessage = null,
            string errorCode = null,
            string errorType = null,
            int? httpStatusCode = null)
        {
            return new StreamCompletionResult
            {
                ContentResponse = "text",
                FinishReason = finishReason,
                ErrorMessage = errorMessage,
                ErrorCode = errorCode,
                ErrorType = errorType,
                HttpStatusCode = httpStatusCode,
                ToolCalls = new List<ToolCallRecord>()
            };
        }

        [Test]
        public void Null_ReturnsFalse()
        {
            Assert.That(ContextOverflowDetector.IsContextOverflow(null), Is.False);
        }

        [Test]
        public void ContextLengthExceededCode_ReturnsTrue_WithoutTextMarker()
        {
            var result = Result(errorCode: "context_length_exceeded", errorMessage: "Request failed.");

            Assert.That(ContextOverflowDetector.IsContextOverflow(result), Is.True);
        }

        [Test]
        public void ExceedContextSizeErrorType_ReturnsTrue_WithoutTextMarker()
        {
            var result = Result(errorType: "exceed_context_size_error", errorMessage: "Request failed.");

            Assert.That(ContextOverflowDetector.IsContextOverflow(result), Is.True);
        }

        [Test]
        public void StructuralCode_IsCaseInsensitive()
        {
            var result = Result(errorCode: "Context_Length_Exceeded");

            Assert.That(ContextOverflowDetector.IsContextOverflow(result), Is.True);
        }

        [Test]
        public void StructuralCode_Wins_OverServerErrorStatus()
        {
            // Proxies may wrap an upstream overflow into 502/504 while keeping the error body.
            var result = Result(errorType: "exceed_context_size_error", httpStatusCode: 502);

            Assert.That(ContextOverflowDetector.IsContextOverflow(result), Is.True);
        }

        [Test]
        public void PayloadTooLarge_ReturnsTrue()
        {
            var result = Result(httpStatusCode: 413, errorMessage: "Payload too large");

            Assert.That(ContextOverflowDetector.IsContextOverflow(result), Is.True);
        }

        [TestCase("This model's maximum context length is 8192 tokens")]
        [TestCase("the request exceeds the available context size")]
        [TestCase("n_keep is larger than the context window")]
        [TestCase("prompt is too long for this model")]
        public void TextMarker_ReturnsTrue(string message)
        {
            var result = Result(errorMessage: message, httpStatusCode: 400);

            Assert.That(ContextOverflowDetector.IsContextOverflow(result), Is.True);
        }

        [TestCase("Please try again later.")]
        [TestCase(null)]
        public void UnrelatedText_ReturnsFalse(string message)
        {
            var result = Result(errorMessage: message, httpStatusCode: 400);

            Assert.That(ContextOverflowDetector.IsContextOverflow(result), Is.False);
        }

        [Test]
        public void LengthFinishReason_WithoutUsage_IsNotTreatedAsOverflow()
        {
            // "length" alone says nothing about why the answer stopped. Without usage there is no way to tell an
            // exhausted answer budget from an exhausted window, and compacting on a guess spends an LLM call for
            // nothing while leaving the answer truncated anyway.
            var result = Result(finishReason: "length", errorMessage: "Response truncated — token limit reached.");

            Assert.That(ContextOverflowDetector.IsContextOverflow(result), Is.False);
        }

        [Test]
        public void LengthFinishReason_CompletionNearOutputCap_IsNotTreatedAsOverflow()
        {
            var result = Result(finishReason: "length");
            result.TokenUsage = new TokenUsageMetadata { PromptTokens = 1200, CompletionTokens = 2000 };

            Assert.That(ContextOverflowDetector.IsContextOverflow(result, maxOutputTokens: 2048, contextLength: 32000),
                Is.False, "the answer budget ran out, the window did not");
        }

        [Test]
        public void LengthFinishReason_WindowNearlyFull_IsTreatedAsOverflow()
        {
            var result = Result(finishReason: "length");
            result.TokenUsage = new TokenUsageMetadata { PromptTokens = 9500, CompletionTokens = 500 };

            Assert.That(ContextOverflowDetector.IsContextOverflow(result, maxOutputTokens: 2048, contextLength: 10000),
                Is.True, "prompt plus completion filled the window");
        }

        [Test]
        public void LengthFinishReason_FullWindowWins_OverOutputCap()
        {
            // Both limits look reached; a full window explains the stop, so compaction is the right response.
            var result = Result(finishReason: "length");
            result.TokenUsage = new TokenUsageMetadata { PromptTokens = 9600, CompletionTokens = 400 };

            Assert.That(ContextOverflowDetector.IsContextOverflow(result, maxOutputTokens: 400, contextLength: 10000),
                Is.True);
        }

        [Test]
        public void LengthFinishReason_UsageWellBelowBothLimits_IsNotTreatedAsOverflow()
        {
            var result = Result(finishReason: "length");
            result.TokenUsage = new TokenUsageMetadata { PromptTokens = 100, CompletionTokens = 50 };

            Assert.That(ContextOverflowDetector.IsContextOverflow(result, contextLength: 32000), Is.False);
        }

        [Test]
        public void ContentFilterFinishReason_ReturnsFalse()
        {
            var result = Result(finishReason: "content_filter", errorMessage: "Response blocked by content filter.");

            Assert.That(ContextOverflowDetector.IsContextOverflow(result), Is.False);
        }

        [TestCase(401)]
        [TestCase(403)]
        [TestCase(408)]
        [TestCase(429)]
        public void RejectStatuses_ReturnFalse_EvenWithTextMarker(int status)
        {
            var result = Result(errorMessage: "maximum context length exceeded", httpStatusCode: status);

            Assert.That(ContextOverflowDetector.IsContextOverflow(result), Is.False);
        }

        [TestCase(500)]
        [TestCase(502)]
        [TestCase(503)]
        [TestCase(504)]
        public void ServerErrorStatus_WithoutStructuralMarker_ReturnsFalse(int status)
        {
            var result = Result(errorMessage: "maximum context length exceeded", httpStatusCode: status);

            Assert.That(ContextOverflowDetector.IsContextOverflow(result), Is.False);
        }

        [Test]
        public void ServerErrorStatus_WithStructuralCode_ReturnsTrue()
        {
            var result = Result(errorCode: "context_length_exceeded", httpStatusCode: 504);

            Assert.That(ContextOverflowDetector.IsContextOverflow(result), Is.True);
        }

        [Test]
        public void SuccessfulResult_ReturnsFalse()
        {
            var result = Result(finishReason: "stop");

            Assert.That(ContextOverflowDetector.IsContextOverflow(result), Is.False);
        }
    }
}
