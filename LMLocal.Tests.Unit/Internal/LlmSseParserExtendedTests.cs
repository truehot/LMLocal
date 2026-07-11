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
        public void ExtractDeltas_BuffersRaggedToolCall()
        {
            var line1 = "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"<tool_call>search_\"}}]}";
            var line2 = "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"file_content\"}}]}";
            var line3 = "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\" args\\n</tool_call>\"}}]}";

            var chunks1 = _parser.ExtractDeltas(line1);
            var chunks2 = _parser.ExtractDeltas(line2);
            var chunks3 = _parser.ExtractDeltas(line3);

            // First two should return nothing (buffering)
            Assert.That(chunks1, Is.Empty);
            Assert.That(chunks2, Is.Empty);

            // Third should return complete tool call block
            Assert.That(chunks3, Is.Not.Empty);
            Assert.That(chunks3[0] is TextStreamChunk);
            var toolCallBlock = (TextStreamChunk)chunks3[0];
            Assert.That(toolCallBlock.Kind, Is.EqualTo(ChunkKind.ToolCallRaw));
            Assert.That(toolCallBlock.Text, Does.Contain("<tool_call>"));
            Assert.That(toolCallBlock.Text, Does.Contain("</tool_call>"));
            Assert.That(toolCallBlock.Text, Does.Contain("search_file_content"));
        }

        [Test]
        public void ExtractDeltas_MixesReasoningWithToolCall()
        {
            var line1 = "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"The user wants to search. \"}}]}";
            var line2 = "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"<tool_call>Search\"}}]}";
            var line3 = "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\" *.cs\"}}]}";
            var line4 = "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"</tool_call>\"}}]}";
            var line5 = "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\" Done.\"}}]}";

            var chunks1 = _parser.ExtractDeltas(line1);
            var chunks2 = _parser.ExtractDeltas(line2);
            var chunks3 = _parser.ExtractDeltas(line3);
            var chunks4 = _parser.ExtractDeltas(line4);
            var chunks5 = _parser.ExtractDeltas(line5);

            // Line 1: Regular reasoning
            Assert.That(chunks1.Count, Is.EqualTo(1));
            Assert.That(chunks1[0] is TextStreamChunk);
            var reasoning1 = (TextStreamChunk)chunks1[0];
            Assert.That(reasoning1.Kind, Is.EqualTo(ChunkKind.Reasoning));
            Assert.That(reasoning1.Text, Is.EqualTo("The user wants to search. "));

            // Lines 2-3: Buffering tool call
            Assert.That(chunks2, Is.Empty);
            Assert.That(chunks3, Is.Empty);

            // Line 4: Tool call complete
            Assert.That(chunks4.Count, Is.EqualTo(1));
            Assert.That(chunks4[0] is TextStreamChunk);
            var toolCall = (TextStreamChunk)chunks4[0];
            Assert.That(toolCall.Kind, Is.EqualTo(ChunkKind.ToolCallRaw));
            Assert.That(toolCall.Text, Does.Contain("<tool_call>"));
            Assert.That(toolCall.Text, Does.Contain("</tool_call>"));

            // Line 5: Remaining reasoning
            Assert.That(chunks5.Count, Is.EqualTo(1));
            Assert.That(chunks5[0] is TextStreamChunk);
            var reasoning5 = (TextStreamChunk)chunks5[0];
            Assert.That(reasoning5.Kind, Is.EqualTo(ChunkKind.Reasoning));
            Assert.That(reasoning5.Text, Is.EqualTo(" Done."));
        }

        [Test]
        public void ExtractDeltas_FlushesBufferOnFinishReason()
        {
            // Simulate tool call followed by finish_reason
            var line1 = "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"<tool_call>Search\"}}]}";
            var line2 = "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\" pattern</tool_call> and more\"}}]}";
            var line3 = "data: {\"choices\":[{\"finish_reason\":\"stop\",\"delta\":{}}]}";

            var chunks1 = _parser.ExtractDeltas(line1);
            var chunks2 = _parser.ExtractDeltas(line2);
            var chunks3 = _parser.ExtractDeltas(line3);

            // Line 1: Incomplete tool call is buffered
            Assert.That(chunks1, Is.Empty);

            // Line 2: Complete tool call block is extracted as ToolCallRaw, remaining text as reasoning
            Assert.That(chunks2.Count, Is.EqualTo(2)); // tool call + remaining reasoning
            Assert.That(chunks2[0] is TextStreamChunk);
            var toolCall = (TextStreamChunk)chunks2[0];
            Assert.That(toolCall.Kind, Is.EqualTo(ChunkKind.ToolCallRaw));
            Assert.That(toolCall.Text, Does.Contain("<tool_call>"));
            Assert.That(toolCall.Text, Does.Contain("</tool_call>"));

            Assert.That(chunks2[1] is TextStreamChunk);
            var remaining = (TextStreamChunk)chunks2[1];
            Assert.That(remaining.Kind, Is.EqualTo(ChunkKind.Reasoning));
            Assert.That(remaining.Text, Is.EqualTo(" and more"));

            // Line 3: finish_reason should just add completion chunk
            Assert.That(chunks3.Count, Is.EqualTo(1)); // just completion chunk
            Assert.That(chunks3[0] is CompletionStreamChunk);
            var completion = (CompletionStreamChunk)chunks3[0];
            Assert.That(completion.FinishReason, Is.EqualTo("stop"));
        }

        [Test]
        public void ExtractDeltas_FlushesBufferOnDoneMarker()
        {
            var line1 = "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"<tool_call>Search\"}}]}";
            var line2 = "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\" args</tool_call> remaining text\"}}]}";
            var line3 = "data: [DONE]";

            var chunks1 = _parser.ExtractDeltas(line1);
            var chunks2 = _parser.ExtractDeltas(line2);
            var chunks3 = _parser.ExtractDeltas(line3);

            // Line 1: Incomplete tool call is buffered, nothing emitted
            Assert.That(chunks1, Is.Empty);

            // Line 2: Complete tool call is extracted as ToolCallRaw, remaining text is reasoning
            Assert.That(chunks2.Count, Is.EqualTo(2)); // tool call block + remaining reasoning
            Assert.That(chunks2[0] is TextStreamChunk);
            Assert.That(chunks2[0].Kind, Is.EqualTo(ChunkKind.ToolCallRaw));
            var toolCall = (TextStreamChunk)chunks2[0];
            Assert.That(toolCall.Text, Does.Contain("<tool_call>"));
            Assert.That(toolCall.Text, Does.Contain("</tool_call>"));

            Assert.That(chunks2[1] is TextStreamChunk);
            Assert.That(chunks2[1].Kind, Is.EqualTo(ChunkKind.Reasoning));
            var remaining = (TextStreamChunk)chunks2[1];
            Assert.That(remaining.Text, Is.EqualTo(" remaining text"));

            // Line 3: [DONE] just adds completion marker
            Assert.That(chunks3.Count, Is.EqualTo(1));
            Assert.That(chunks3[0] is CompletionStreamChunk);
            var completion = (CompletionStreamChunk)chunks3[0];
            Assert.That(completion.FinishReason, Is.EqualTo("stop"));
        }

        [Test]
        public void ParseNemotronToolCall_ShouldAccumulateCompleteBlock()
        {
            var parser = new LlmSseParser();
            var lines = new[]
            {
                "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"<tool_call>\"}}]}",
                "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"Read_Solution\"}}]}",
                "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"_Files<param\"}}]}",
                "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"eter=file_path>\"}}]}",
                "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"LMLocal/...\"}}]}",
                "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"</parameter></tool_call>\"}}]}",
            };

            var allChunks = lines.SelectMany(l => parser.ExtractDeltas(l)).ToList();

            // Should accumulate the complete tool_call block as ToolCallRaw
            var toolCallRawChunks = allChunks.Where(c => c.Kind == ChunkKind.ToolCallRaw).ToList();
            Assert.That(toolCallRawChunks.Count, Is.EqualTo(1));

            var toolCallBlock = (TextStreamChunk)toolCallRawChunks[0];
            Assert.That(toolCallBlock.Text, Does.StartWith("<tool_call>"));
            Assert.That(toolCallBlock.Text, Does.EndWith("</tool_call>"));
            Assert.That(toolCallBlock.Text, Does.Contain("Read_Solution_Files"));
            Assert.That(toolCallBlock.Text, Does.Contain("parameter=file_path"));
        }

        [Test]
        public void ExtractDeltas_HandlesMultipleToolCalls()
        {
            var line = "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"<tool_call>Func1 arg1</tool_call><tool_call>Func2 arg2</tool_call>\"}}]}";

            var chunks = _parser.ExtractDeltas(line);

            // Should have 2 raw tool call blocks
            Assert.That(chunks.Count, Is.EqualTo(2));
            Assert.That(chunks[0] is TextStreamChunk);
            Assert.That(chunks[0].Kind, Is.EqualTo(ChunkKind.ToolCallRaw));
            var toolCall1 = (TextStreamChunk)chunks[0];
            Assert.That(toolCall1.Text, Does.Contain("Func1"));

            Assert.That(chunks[1] is TextStreamChunk);
            Assert.That(chunks[1].Kind, Is.EqualTo(ChunkKind.ToolCallRaw));
            var toolCall2 = (TextStreamChunk)chunks[1];
            Assert.That(toolCall2.Text, Does.Contain("Func2"));
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
        public void ExtractDeltas_ReturnsCompletion_WithRefusalFromDelta()
        {
            var json = "data: {\"choices\":[{\"delta\":{\"refusal\":\"I refuse\"}}]}";
            var results = _parser.ExtractDeltas(json);
            Assert.That(results, Is.Not.Empty);
            var result = results[0];
            Assert.That(result is CompletionStreamChunk, Is.True);
            var c = (CompletionStreamChunk)result;
            Assert.That(c.Refusal, Is.EqualTo("I refuse"));
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
        public void ExtractDeltas_ReturnsCompletion_WithUsageAndRefusalInSameChunk()
        {
            var json = "data: {\"choices\":[{\"delta\":{\"refusal\":\"I cannot do that\"}}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":1,\"total_tokens\":11}}";
            var results = _parser.ExtractDeltas(json);
            Assert.That(results, Is.Not.Empty);
            var result = results[0];
            Assert.That(result is CompletionStreamChunk, Is.True);
            var c = (CompletionStreamChunk)result;
            Assert.That(c.Refusal, Is.EqualTo("I cannot do that"));
            Assert.That(c.TotalTokens, Is.EqualTo(11));
            Assert.That(c.PromptTokens, Is.EqualTo(10));
            Assert.That(c.CompletionTokens, Is.EqualTo(1));
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
    }
}
