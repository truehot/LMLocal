using LMLocal.Core.Models;
using LMLocal.Infrastructure.Streaming;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class LlmSseParserTests
    {
        private LlmSseParser _parser;

        [SetUp]
        public void SetUp()
        {
            _parser = new LlmSseParser();
        }

        [Test]
        public void ExtractDeltas_ReturnsCompletion_OnDoneLine()
        {
            var results = _parser.ExtractDeltas("data: [DONE]");
            Assert.That(results, Is.Not.Empty);
            var result = results[0];
            Assert.That(result is CompletionStreamChunk, Is.True);
            var completion = (CompletionStreamChunk)result;
            Assert.That(completion.FinishReason, Is.EqualTo("stop"));
        }

        [Test]
        public void ExtractDeltas_ParsesDeltaContent()
        {
            var json = "data: {\"choices\":[{\"delta\":{\"content\":\"hi\"}}]}";
            var results = _parser.ExtractDeltas(json);
            Assert.That(results, Is.Not.Empty);
            var result = results[0];
            Assert.That(result is TextStreamChunk, Is.True);
            var chunk = (TextStreamChunk)result;
            Assert.That(chunk.Text, Is.EqualTo("hi"));
            Assert.That(chunk.Kind, Is.EqualTo(ChunkKind.Content));
        }

        [Test]
        public void ExtractDeltas_ReturnsUsageInCompletionChunk()
        {
            var json = "data: {\"choices\":[],\"usage\":{\"total_tokens\":42}}";
            var results = _parser.ExtractDeltas(json);
            Assert.That(results, Is.Not.Empty);
            var result = results[0];
            Assert.That(result is CompletionStreamChunk, Is.True);
            var completion = (CompletionStreamChunk)result;
            Assert.That(completion.TotalTokens, Is.EqualTo(42));
        }

        [Test]
        public void ExtractDeltas_ReturnsEmpty_OnMalformedJson()
        {
            var results = _parser.ExtractDeltas("data: {not a json}");
            Assert.That(results, Is.Empty);
        }

        [Test]
        public void ExtractDeltas_ReturnsEmpty_WhenNoChoices()
        {
            var json = "data: {\"choices\":[]}";
            var results = _parser.ExtractDeltas(json);
            Assert.That(results, Is.Empty);
        }
    }
}
