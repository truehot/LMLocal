using LMLocal.Application.ChatSessionStream;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    /// <summary>
    /// Real error bodies of the providers this app talks to. Each case documents how a provider actually reports an
    /// overflow, so the detector can be checked against reality rather than against invented JSON.
    /// </summary>
    [TestFixture]
    public class ProviderErrorFixtureTests
    {
        /// <summary>OpenAI and OpenAI-compatible gateways.</summary>
        private const string OpenAiBody =
            "{\"error\":{\"message\":\"This model's maximum context length is 8192 tokens. However, your messages " +
            "resulted in 9000 tokens.\",\"type\":\"invalid_request_error\",\"param\":\"messages\",\"code\":\"context_length_exceeded\"}}";

        /// <summary>llama.cpp / LM Studio: size hints in the body, no numeric code.</summary>
        private const string LlamaCppBody =
            "{\"error\":{\"code\":500,\"message\":\"The request exceeds the available context size. Try increasing " +
            "the context size or enable context shift.\",\"type\":\"exceed_context_size_error\",\"n_prompt_tokens\":9000,\"n_ctx\":8192}}";

        /// <summary>vLLM.</summary>
        private const string VllmBody =
            "{\"object\":\"error\",\"message\":\"This model's maximum context length is 4096 tokens. However, you " +
            "requested 5000 tokens.\",\"type\":\"BadRequestError\",\"param\":null,\"code\":400}";

        /// <summary>OpenRouter wrapping an upstream overflow.</summary>
        private const string OpenRouterBody =
            "{\"error\":{\"message\":\"Provider returned error\",\"code\":502,\"metadata\":{\"provider_name\":\"Together\"," +
            "\"raw\":\"maximum context length exceeded\"}}}";

        /// <summary>Groq.</summary>
        private const string GroqBody =
            "{\"error\":{\"message\":\"Please reduce the length of the messages or completion.\",\"type\":" +
            "\"invalid_request_error\",\"code\":\"context_length_exceeded\"}}";

        /// <summary>A gateway that sends no message at all, only a code.</summary>
        private const string BareCodeBody =
            "{\"error\":{\"code\":\"context_length_exceeded\"}}";

        [TestCase(OpenAiBody)]
        [TestCase(LlamaCppBody)]
        [TestCase(VllmBody)]
        [TestCase(OpenRouterBody)]
        [TestCase(GroqBody)]
        [TestCase(BareCodeBody)]
        public void OverflowBodies_AreRecognisedAsOverflow(string body)
        {
            var info = ApiErrorParser.ParseErrorBody(body);

            var result = new StreamCompletionResult
            {
                FinishReason = "error",
                ErrorMessage = info.Message,
                ErrorCode = info.RawCode,
                ErrorType = info.Type,
                HttpStatusCode = 400
            };

            Assert.That(ContextOverflowDetector.IsContextOverflow(result), Is.True,
                "provider body was not recognised: {0}", body);
        }

        [Test]
        public void LlamaCppBody_ProvidesWindowAndPromptSize()
        {
            var info = ApiErrorParser.ParseErrorBody(LlamaCppBody);

            // These two numbers are the most precise calibration available: they say what the window really is and
            // what the request really weighed.
            Assert.That(info.ContextLength, Is.EqualTo(8192));
            Assert.That(info.PromptTokens, Is.EqualTo(9000));
        }

        [Test]
        public void OpenAiBody_ProvidesStructuredCodeButNoSizeHints()
        {
            var info = ApiErrorParser.ParseErrorBody(OpenAiBody);

            Assert.That(info.RawCode, Is.EqualTo("context_length_exceeded"));
            Assert.That(info.Type, Is.EqualTo("invalid_request_error"));
            Assert.That(info.ContextLength, Is.Null, "OpenAI does not report the window in the error");
        }

        [Test]
        public void OpenRouterBody_KeepsUpstreamTextAndProvider()
        {
            var info = ApiErrorParser.ParseErrorBody(OpenRouterBody);

            // "Provider returned error" alone says nothing; the metadata carries the real reason.
            Assert.That(info.Message, Is.EqualTo("maximum context length exceeded"));
            Assert.That(info.Provider, Is.EqualTo("Together"));
        }

        [Test]
        public void VllmBody_IsRecognisedByTextBecauseTheCodeIsNumeric()
        {
            var info = ApiErrorParser.ParseErrorBody(VllmBody);

            Assert.That(info.Code, Is.EqualTo(400));
            Assert.That(info.RawCode, Is.Null);

            var result = new StreamCompletionResult
            {
                FinishReason = "error",
                ErrorMessage = info.Message,
                HttpStatusCode = 400
            };

            Assert.That(ContextOverflowDetector.IsContextOverflow(result), Is.True,
                "a numeric code carries no context information, so the text marker has to do the work");
        }

        [Test]
        public void BareCodeBody_IsRecognisedThroughTheStructuredCodeOnly()
        {
            var info = ApiErrorParser.ParseErrorBody(BareCodeBody);

            var result = new StreamCompletionResult
            {
                FinishReason = "error",
                ErrorMessage = info.Message,
                ErrorCode = info.RawCode,
                HttpStatusCode = 400
            };

            Assert.That(ContextOverflowDetector.IsContextOverflow(result), Is.True);
            Assert.That(info.RawBody, Is.Not.Null, "the unrecognised body is still available for the log");
        }

        [Test]
        public void RateLimitBody_IsNotMistakenForOverflow()
        {
            const string body =
                "{\"error\":{\"message\":\"Rate limit reached for requests\",\"type\":\"requests\",\"code\":\"rate_limit_exceeded\"}}";

            var info = ApiErrorParser.ParseErrorBody(body);

            var result = new StreamCompletionResult
            {
                FinishReason = "error",
                ErrorMessage = info.Message,
                ErrorCode = info.RawCode,
                HttpStatusCode = 429
            };

            Assert.That(ContextOverflowDetector.IsContextOverflow(result), Is.False);
        }
    }
}
