using LMLocal.Core.Models;
using LMLocal.Infrastructure.Streaming;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class LlmSseParserExtendedTests
    {
        [Test]
        public void ExtractDelta_ReturnsCompletion_OnDoneMarker()
        {
            var result = LlmSseParser.ExtractDelta("data: [DONE]");
            Assert.That(result, Is.Not.Null);
            Assert.That(result is CompletionStreamChunk, Is.True);
            var completion = (CompletionStreamChunk)result;
            Assert.That(completion.FinishReason, Is.EqualTo("stop"));
        }

        [Test]
        public void ExtractDelta_ReturnsNull_OnNonDataLine()
        {
            var result = LlmSseParser.ExtractDelta("event: ping");
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ExtractDelta_ReturnsNull_OnMalformedJson()
        {
            var result = LlmSseParser.ExtractDelta("data: {not a json}");
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ExtractDelta_ReturnsNull_WhenChoicesEmpty()
        {
            var json = "data: {\"choices\":[]}";
            var result = LlmSseParser.ExtractDelta(json);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ExtractDelta_ReturnsTextChunk_ForContentDelta()
        {
            var json = "data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}";
            var result = LlmSseParser.ExtractDelta(json);
            Assert.That(result, Is.Not.Null);
            Assert.That(result is TextStreamChunk, Is.True);
            var chunk = (TextStreamChunk)result;
            Assert.That(chunk.Kind, Is.EqualTo(ChunkKind.Content));
            Assert.That(chunk.Text, Is.EqualTo("hello"));
        }

        [Test]
        public void ExtractDelta_ReturnsReasoningChunk_ForReasoningContent()
        {
            var json = "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"thinking...\"}}]}";
            var result = LlmSseParser.ExtractDelta(json);
            Assert.That(result, Is.Not.Null);
            Assert.That(result is TextStreamChunk, Is.True);
            var chunk = (TextStreamChunk)result;
            Assert.That(chunk.Kind, Is.EqualTo(ChunkKind.Reasoning));
            Assert.That(chunk.Text, Is.EqualTo("thinking..."));
        }

        [Test]
        public void ExtractDelta_DetectsNemotronXmlToolCall_FragmentOpenTag()
        {
            var json = "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"<tool_call>\"}}]}";
            var result = LlmSseParser.ExtractDelta(json);
            Assert.That(result, Is.Not.Null);
            Assert.That(result is TextStreamChunk, Is.True);
            var chunk = (TextStreamChunk)result;
            Assert.That(chunk.Kind, Is.EqualTo(ChunkKind.ToolCallArguments));
            Assert.That(chunk.ToolCallIndex, Is.EqualTo(0));
        }

        [Test]
        public void ExtractDelta_EmitsToolCallMetadata_ForOpenAiFormat()
        {
            var json = "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":1,\"id\":\"call123\",\"function\":{\"name\":\"search\"}}]}}]}";
            var result = LlmSseParser.ExtractDelta(json);
            Assert.That(result, Is.Not.Null);
            Assert.That(result is ToolCallMetadataChunk, Is.True);
            var meta = (ToolCallMetadataChunk)result;
            Assert.That(meta.Index, Is.EqualTo(1));
            Assert.That(meta.CallId, Is.EqualTo("call123"));
            Assert.That(meta.FunctionName, Is.EqualTo("search"));
        }

        [Test]
        public void ExtractDelta_EmitsToolCallArguments_ForOpenAiFormat()
        {
            var json = "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":1,\"function\":{\"arguments\":\"{\\\"q\\\":\\\"a\\\"}\"}}]}}]}";
            var result = LlmSseParser.ExtractDelta(json);
            Assert.That(result, Is.Not.Null);
            Assert.That(result is TextStreamChunk, Is.True);
            var chunk = (TextStreamChunk)result;
            Assert.That(chunk.Kind, Is.EqualTo(ChunkKind.ToolCallArguments));
            Assert.That(chunk.ToolCallIndex, Is.EqualTo(1));
            Assert.That(chunk.Text, Does.Contain("\"q\":\"a\""));
        }

        [Test]
        public void ExtractDelta_HandlesParallelToolCalls_IndicesPreserved()
        {
            var meta0 = "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"c0\",\"function\":{\"name\":\"f0\"}}]}}]}";
            var meta1 = "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":1,\"id\":\"c1\",\"function\":{\"name\":\"f1\"}}]}}]}";
            var args1 = "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":1,\"function\":{\"arguments\":\"{\\\"a\\\":1}\"}}]}}]}";
            var args0 = "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"{\\\"b\\\":2}\"}}]}}]}";

            var r0 = LlmSseParser.ExtractDelta(meta0);
            var r1 = LlmSseParser.ExtractDelta(meta1);
            var r2 = LlmSseParser.ExtractDelta(args1);
            var r3 = LlmSseParser.ExtractDelta(args0);

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
        public void ExtractDelta_ReturnsCompletion_OnFinishReason()
        {
            var json = "data: {\"choices\":[{\"finish_reason\":\"tool_calls\",\"delta\":{}}]}";
            var result = LlmSseParser.ExtractDelta(json);
            Assert.That(result, Is.Not.Null);
            Assert.That(result is CompletionStreamChunk, Is.True);
            var c = (CompletionStreamChunk)result;
            Assert.That(c.FinishReason, Is.EqualTo("tool_calls"));
        }

        [Test]
        public void ExtractDelta_ReturnsCompletion_WithUsageAndFingerprint()
        {
            var json = "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":2,\"total_tokens\":12},\"system_fingerprint\":\"abc\"}";
            var result = LlmSseParser.ExtractDelta(json);
            Assert.That(result, Is.Not.Null);
            Assert.That(result is CompletionStreamChunk, Is.True);
            var c = (CompletionStreamChunk)result;
            Assert.That(c.TotalTokens, Is.EqualTo(12));
            Assert.That(c.PromptTokens, Is.EqualTo(10));
            Assert.That(c.CompletionTokens, Is.EqualTo(2));
            Assert.That(c.SystemFingerprint, Is.EqualTo("abc"));
        }

        [Test]
        public void ExtractDelta_ReturnsCompletion_WithRefusalFromDelta()
        {
            var json = "data: {\"choices\":[{\"delta\":{\"refusal\":\"I refuse\"}}]}";
            var result = LlmSseParser.ExtractDelta(json);
            Assert.That(result, Is.Not.Null);
            Assert.That(result is CompletionStreamChunk, Is.True);
            var c = (CompletionStreamChunk)result;
            Assert.That(c.Refusal, Is.EqualTo("I refuse"));
        }

        [Test]
        public void ExtractDelta_PreservesContentWhenUsageInSameChunk()
        {
            var json = "data: {\"choices\":[{\"delta\":{\"content\":\"x\"}}],\"usage\":{\"total_tokens\":5}}";
            var result = LlmSseParser.ExtractDelta(json);
            Assert.That(result, Is.Not.Null);
            Assert.That(result is TextStreamChunk, Is.True);
            var chunk = (TextStreamChunk)result;
            Assert.That(chunk.Kind, Is.EqualTo(ChunkKind.Content));
            Assert.That(chunk.Text, Is.EqualTo("x"));
        }

        [Test]
        public void ExtractStreamContent_HandlesMissingDeltaSafely()
        {
            var json = "data: {\"choices\":[{}]}";
            var result = LlmSseParser.ExtractDelta(json);
            Assert.That(result, Is.Null);
        }
    }
}
