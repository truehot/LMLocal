using LMLocal.Core.Models;
using LMLocal.Infrastructure.Streaming;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class LlmSseParserTests
    {
        [Test]
        public void ExtractDelta_ReturnsCompletion_OnDoneLine()
        {
            var result = LlmSseParser.ExtractDelta("data: [DONE]");
            Assert.That(result, Is.Not.Null);
            Assert.That(result is CompletionStreamChunk, Is.True);
            var completion = (CompletionStreamChunk)result;
            Assert.That(completion.FinishReason, Is.EqualTo("stop"));
        }

        [Test]
        public void ExtractDelta_ParsesDeltaContent()
        {
            var json = "data: {\"choices\":[{\"delta\":{\"content\":\"hi\"}}]}";
            var result = LlmSseParser.ExtractDelta(json);
            Assert.That(result, Is.Not.Null);
            Assert.That(result is TextStreamChunk, Is.True);
            var chunk = (TextStreamChunk)result;
            Assert.That(chunk.Text, Is.EqualTo("hi"));
            Assert.That(chunk.Kind, Is.EqualTo(ChunkKind.Content));
        }

        [Test]
        public void ExtractDelta_ReturnsUsageInCompletionChunk()
        {
            var json = "data: {\"choices\":[],\"usage\":{\"total_tokens\":42}}";
            var result = LlmSseParser.ExtractDelta(json);
            Assert.That(result, Is.Not.Null);
            Assert.That(result is CompletionStreamChunk, Is.True);
            var completion = (CompletionStreamChunk)result;
            Assert.That(completion.TotalTokens, Is.EqualTo(42));
        }

        [Test]
        public void ExtractDelta_ReturnsNull_OnMalformedJson()
        {
            var result = LlmSseParser.ExtractDelta("data: {not a json}");
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ExtractDelta_ReturnsNull_WhenNoChoices()
        {
            var json = "data: {\"choices\":[]}";
            var result = LlmSseParser.ExtractDelta(json);
            Assert.That(result, Is.Null);
        }
    }
}
