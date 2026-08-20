using System.Linq;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Streaming;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class LlmSseParserExtendedTests
    {
        private LlmSseParser _parser;

        [SetUp]
        public void SetUp()
        {
            _parser = new LlmSseParser();
        }

        [Test]
        public void ExtractDeltas_ReturnsCompletion_OnDoneMarker()
        {
            var results = _parser.ExtractDeltas("data: [DONE]");
            Assert.That(results, Is.Not.Empty);
            var result = results[0];
            Assert.That(result is CompletionStreamChunk, Is.True);
            var completion = (CompletionStreamChunk)result;
            Assert.That(completion.FinishReason, Is.EqualTo("stop"));
        }

        [Test]
        public void ExtractDeltas_ReturnsEmpty_OnNonDataLine()
        {
            var results = _parser.ExtractDeltas("event: ping");
            Assert.That(results, Is.Empty);
        }

        [Test]
        public void ExtractDeltas_ReturnsEmpty_OnMalformedJson()
        {
            var results = _parser.ExtractDeltas("data: {not a json}");
            Assert.That(results, Is.Empty);
        }

        [Test]
        public void ExtractDeltas_ReturnsEmpty_WhenChoicesEmpty()
        {
            var json = "data: {\"choices\":[]}";
            var results = _parser.ExtractDeltas(json);
            Assert.That(results, Is.Empty);
        }

        [Test]
        public void ExtractDeltas_ReturnsTextChunk_ForContentDelta()
        {
            var json = "data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}";
            var results = _parser.ExtractDeltas(json);
            Assert.That(results, Is.Not.Empty);
            var result = results[0];
            Assert.That(result is TextStreamChunk, Is.True);
            var chunk = (TextStreamChunk)result;
            Assert.That(chunk.Kind, Is.EqualTo(ChunkKind.Content));
            Assert.That(chunk.Text, Is.EqualTo("hello"));
        }

        [Test]
        public void ExtractDeltas_ReturnsReasoningChunk_ForReasoningContent()
        {
            var json = "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"thinking\"}}]}";
            var results = _parser.ExtractDeltas(json);
            Assert.That(results, Is.Not.Empty);
            var result = results[0];
            Assert.That(result is TextStreamChunk, Is.True);
            var chunk = (TextStreamChunk)result;
            Assert.That(chunk.Kind, Is.EqualTo(ChunkKind.Reasoning));
            Assert.That(chunk.Text, Is.EqualTo("thinking"));
        }

        [Test]
        public void ExtractDeltas_HandlesOpenAiToolCall()
        {
            var meta = "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_123\",\"function\":{\"name\":\"get_weather\"}}]}}]}";
            var args = "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"{\\\"city\\\":\\\"London\\\"}\"}}]}}]}";

            var chunks1 = _parser.ExtractDeltas(meta);
            var chunks2 = _parser.ExtractDeltas(args);

            Assert.That(chunks1, Is.Not.Empty);
            Assert.That(chunks1[0] is ToolCallMetadataChunk);
            var toolMeta = (ToolCallMetadataChunk)chunks1[0];
            Assert.That(toolMeta.FunctionName, Is.EqualTo("get_weather"));
            Assert.That(toolMeta.CallId, Is.EqualTo("call_123"));

            Assert.That(chunks2, Is.Not.Empty);
            Assert.That(chunks2[0] is TextStreamChunk);
            var argsChunk = (TextStreamChunk)chunks2[0];
            Assert.That(argsChunk.Kind, Is.EqualTo(ChunkKind.ToolCallArguments));
            Assert.That(argsChunk.Text, Does.Contain("\"city\""));
        }

        [Test]
        public void ExtractDeltas_HandlesParallelToolCalls_IndicesPreserved()
        {
            var meta0 = "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"c0\",\"function\":{\"name\":\"f0\"}}]}}]}";
            var meta1 = "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":1,\"id\":\"c1\",\"function\":{\"name\":\"f1\"}}]}}]}";
            var args1 = "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":1,\"function\":{\"arguments\":\"{\\\"a\\\":1}\"}}]}}]}";
            var args0 = "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"{\\\"b\\\":2}\"}}]}}]}";

            var r0 = _parser.ExtractDeltas(meta0)[0];
            var r1 = _parser.ExtractDeltas(meta1)[0];
            var r2 = _parser.ExtractDeltas(args1)[0];
            var r3 = _parser.ExtractDeltas(args0)[0];

            Assert.That(r0 is ToolCallMetadataChunk);
            Assert.That(r1 is ToolCallMetadataChunk);
            Assert.That(r2 is TextStreamChunk);
            Assert.That(r3 is TextStreamChunk);

            var ta1 = (TextStreamChunk)r2;
            var ta0 = (TextStreamChunk)r3;
            Assert.That(ta1.ToolCallIndex, Is.EqualTo(1));
            Assert.That(ta0.ToolCallIndex, Is.EqualTo(0));
        }

        [Test]
        public void ExtractDeltas_ReturnsCompletion_OnFinishReason()
        {
            var json = "data: {\"choices\":[{\"finish_reason\":\"tool_calls\",\"delta\":{}}]}";
            var results = _parser.ExtractDeltas(json);
            Assert.That(results, Is.Not.Empty);
            var result = results[0];
            Assert.That(result is CompletionStreamChunk, Is.True);
            var c = (CompletionStreamChunk)result;
            Assert.That(c.FinishReason, Is.EqualTo("tool_calls"));
        }

        [Test]
        public void ExtractDeltas_ReturnsCompletion_WithUsageAndFingerprint()
        {
            var json = "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":2,\"total_tokens\":12},\"system_fingerprint\":\"abc\"}";
            var results = _parser.ExtractDeltas(json);
            Assert.That(results, Is.Not.Empty);
            var result = results[0];
            Assert.That(result is CompletionStreamChunk, Is.True);
            var c = (CompletionStreamChunk)result;
            Assert.That(c.TotalTokens, Is.EqualTo(12));
            Assert.That(c.PromptTokens, Is.EqualTo(10));
            Assert.That(c.CompletionTokens, Is.EqualTo(2));
            Assert.That(c.SystemFingerprint, Is.EqualTo("abc"));
        }

        [Test]
        public void ExtractDeltas_ReturnsContentChunk_ForRefusalFromDelta()
        {
            var json = "data: {\"choices\":[{\"delta\":{\"refusal\":\"I refuse\"}}]}";
            var results = _parser.ExtractDeltas(json);
            Assert.That(results, Is.Not.Empty);
            var result = results[0];
            Assert.That(result is TextStreamChunk, Is.True);
            var chunk = (TextStreamChunk)result;
            Assert.That(chunk.Kind, Is.EqualTo(ChunkKind.Content));
            Assert.That(chunk.Text, Is.EqualTo("I refuse"));
        }

        [Test]
        public void ExtractDeltas_PreservesContentWhenUsageInSameChunk()
        {
            var json = "data: {\"choices\":[{\"delta\":{\"content\":\"x\"}}],\"usage\":{\"total_tokens\":5}}";
            var results = _parser.ExtractDeltas(json);
            Assert.That(results, Is.Not.Empty);
            var result = results[0];
            Assert.That(result is TextStreamChunk, Is.True);
            var chunk = (TextStreamChunk)result;
            Assert.That(chunk.Kind, Is.EqualTo(ChunkKind.Content));
            Assert.That(chunk.Text, Is.EqualTo("x"));
        }

        [Test]
        public void ExtractDeltas_ReturnsCompletion_WithUsageWhenFinishReasonPresent()
        {
            // DeepSeek format: finish_reason + usage in the same chunk
            var json = "data: {\"choices\":[{\"finish_reason\":\"stop\",\"delta\":{\"content\":\"\"}}],\"usage\":{\"prompt_tokens\":72313,\"completion_tokens\":13,\"total_tokens\":72326,\"completion_tokens_details\":{\"reasoning_tokens\":8},\"prompt_tokens_details\":{\"cached_tokens\":72192},\"prompt_cache_hit_tokens\":72192,\"prompt_cache_miss_tokens\":121},\"system_fingerprint\":\"fp_test\"}";
            var results = _parser.ExtractDeltas(json);
            Assert.That(results, Is.Not.Empty);
            var result = results[0];
            Assert.That(result is CompletionStreamChunk, Is.True);
            var c = (CompletionStreamChunk)result;
            Assert.That(c.FinishReason, Is.EqualTo("stop"));
            Assert.That(c.TotalTokens, Is.EqualTo(72326));
            Assert.That(c.PromptTokens, Is.EqualTo(72313));
            Assert.That(c.CompletionTokens, Is.EqualTo(13));
            Assert.That(c.ReasoningTokens, Is.EqualTo(8));
            Assert.That(c.SystemFingerprint, Is.EqualTo("fp_test"));
        }

        [Test]
        public void ExtractDeltas_ReturnsContentChunk_ForRefusalAndUsageInSameChunk()
        {
            var json = "data: {\"choices\":[{\"delta\":{\"refusal\":\"I cannot do that\"}}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":1,\"total_tokens\":11}}";
            var results = _parser.ExtractDeltas(json);
            Assert.That(results, Is.Not.Empty);
            var result = results[0];
            Assert.That(result is TextStreamChunk, Is.True);
            var chunk = (TextStreamChunk)result;
            Assert.That(chunk.Kind, Is.EqualTo(ChunkKind.Content));
            Assert.That(chunk.Text, Is.EqualTo("I cannot do that"));
        }

        [Test]
        public void ExtractDeltas_AccumulatesMultiChunkRefusal_AsContent()
        {
            // Multi-token refusal: each fragment is a separate TextStreamChunk(Content),
            // which StreamProcessor accumulates into fullResponse.
            var r1 = _parser.ExtractDeltas(@"data: {""choices"":[{""delta"":{""refusal"":""I'm sorry,""}}]}");
            var r2 = _parser.ExtractDeltas(@"data: {""choices"":[{""delta"":{""refusal"":"" I can't""}}]}");
            var r3 = _parser.ExtractDeltas(@"data: {""choices"":[{""delta"":{""refusal"":"" help.""}}]}");

            Assert.That(r1.Count, Is.EqualTo(1));
            Assert.That(r2.Count, Is.EqualTo(1));
            Assert.That(r3.Count, Is.EqualTo(1));

            Assert.That(r1[0], Is.TypeOf<TextStreamChunk>());
            Assert.That(r2[0], Is.TypeOf<TextStreamChunk>());
            Assert.That(r3[0], Is.TypeOf<TextStreamChunk>());

            var text = string.Concat(
                ((TextStreamChunk)r1[0]).Text,
                ((TextStreamChunk)r2[0]).Text,
                ((TextStreamChunk)r3[0]).Text);

            Assert.That(text, Is.EqualTo("I'm sorry, I can't help."));
        }

        [Test]
        public void ExtractStreamContent_HandlesMissingDeltaSafely()
        {
            var json = "data: {\"choices\":[{}]}";
            var results = _parser.ExtractDeltas(json);
            Assert.That(results, Is.Empty);
        }

        [Test]
        public void ExtractDeltas_ReturnsErrorChunk_OnOpenAiError()
        {
            var json = "data: {\"error\":{\"message\":\"Cannot find model\",\"type\":\"invalid_request_error\",\"code\":\"model_not_found\"}}";
            var results = _parser.ExtractDeltas(json);

            Assert.That(results, Is.Not.Empty);
            Assert.That(results[0] is ErrorStreamChunk);
            var err = (ErrorStreamChunk)results[0];
            Assert.That(err.Message, Is.EqualTo("Cannot find model"));
            Assert.That(err.ErrorType, Is.EqualTo("invalid_request_error"));
            Assert.That(err.ErrorCode, Is.EqualTo("model_not_found"));
        }

        [Test]
        public void ExtractDeltas_ReturnsErrorChunk_WithDuplicateMessageField()
        {
            // LM Studio format: message duplicated at root level
            var json = "data: {\"error\":{\"message\":\"Cannot find model\"},\"message\":\"Cannot find model\"}";
            var results = _parser.ExtractDeltas(json);

            Assert.That(results, Is.Not.Empty);
            Assert.That(results[0] is ErrorStreamChunk);
            var err = (ErrorStreamChunk)results[0];
            Assert.That(err.Message, Does.Contain("Cannot find model"));
        }

        [Test]
        public void ExtractDeltas_ErrorChunk_TakesPriorityOverChoices()
        {
            // Error + choices in same JSON — error must win
            var json = "data: {\"error\":{\"message\":\"Rate limit exceeded\"},\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}";
            var results = _parser.ExtractDeltas(json);

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0] is ErrorStreamChunk);
            var err = (ErrorStreamChunk)results[0];
            Assert.That(err.Message, Is.EqualTo("Rate limit exceeded"));
        }

        [Test]
        public void ExtractDeltas_ErrorChunk_ClearsBufferedToolCall()
        {
            // Partial tool call in buffer — error should clear it, not flush it
            var line1 = "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"<tool_call>Search\"}}]}";
            var line2 = "data: {\"error\":{\"message\":\"Server error\"}}";

            _parser.ExtractDeltas(line1); // buffers the incomplete tool call
            var results2 = _parser.ExtractDeltas(line2);

            Assert.That(results2.Count, Is.EqualTo(1));
            Assert.That(results2[0] is ErrorStreamChunk);

            // After error, parser should be clean — no leftover buffer
            var line3 = "data: {\"choices\":[{\"delta\":{\"content\":\"fresh\"}}]}";
            var results3 = _parser.ExtractDeltas(line3);
            Assert.That(results3.Count, Is.EqualTo(1));
            Assert.That(results3[0] is TextStreamChunk);
            Assert.That(((TextStreamChunk)results3[0]).Text, Is.EqualTo("fresh"));
        }

        [Test]
        public void ExtractDeltas_ParsesCachedTokens_FromPromptTokensDetails()
        {
            // DeepSeek format: cached_tokens inside prompt_tokens_details (+ duplicate at usage root)
            var json = @"data: {""choices"":[{""finish_reason"":""stop"",""delta"":{}}],""usage"":{""prompt_tokens"":7545,""completion_tokens"":446,""total_tokens"":7991,""prompt_tokens_details"":{""cached_tokens"":4224},""prompt_cache_hit_tokens"":4224,""prompt_cache_miss_tokens"":3321}}";
            var results = _parser.ExtractDeltas(json);

            Assert.That(results, Is.Not.Empty);
            Assert.That(results[0], Is.TypeOf<CompletionStreamChunk>());
            var c = (CompletionStreamChunk)results[0];
            Assert.That(c.CachedTokens, Is.EqualTo(4224));
        }

    }
}
