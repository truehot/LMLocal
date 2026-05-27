using LMLocal.Infrastructure.Mcp;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Mcp
{
    [TestFixture]
    public class SseStreamParserTests
    {
        [Test]
        public void TryParseSseLine_ReturnsNull_ForNullLine()
        {
            var result = SseStreamParser.TryParseSseLine(null);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void TryParseSseLine_ReturnsNull_ForEmptyLine()
        {
            var result = SseStreamParser.TryParseSseLine("");
            Assert.That(result, Is.Null);
        }

        [Test]
        public void TryParseSseLine_ReturnsNull_ForWhitespaceLine()
        {
            var result = SseStreamParser.TryParseSseLine("   ");
            Assert.That(result, Is.Null);
        }

        [Test]
        public void TryParseSseLine_ParsesCommentLine()
        {
            var result = SseStreamParser.TryParseSseLine(": keep-alive");
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(SseMessageType.Comment));
            Assert.That(result.RawData, Is.EqualTo(": keep-alive"));
        }

        [Test]
        public void TryParseSseLine_ParsesEventLine()
        {
            var result = SseStreamParser.TryParseSseLine("event: message");
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(SseMessageType.Event));
            Assert.That(result.EventType, Is.EqualTo("message"));
        }

        [Test]
        public void TryParseSseLine_ParsesDataLine_WithValidJson()
        {
            var result = SseStreamParser.TryParseSseLine("data: {\"id\":1,\"result\":\"test\"}");
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(SseMessageType.Data));
            Assert.That(result.RawData, Is.EqualTo("{\"id\":1,\"result\":\"test\"}"));
            Assert.That(result.ParsedData, Is.Not.Null);
        }

        [Test]
        public void TryParseSseLine_ParsesDataLine_WithInvalidJson()
        {
            var result = SseStreamParser.TryParseSseLine("data: {not json}");
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(SseMessageType.Data));
            Assert.That(result.RawData, Is.EqualTo("{not json}"));
            Assert.That(result.ParsedData, Is.Null);
        }

        [Test]
        public void TryParseSseLine_ParsesDoneMarker()
        {
            var result = SseStreamParser.TryParseSseLine("data: [DONE]");
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(SseMessageType.Done));
            Assert.That(result.RawData, Is.EqualTo("[DONE]"));
        }

        [Test]
        public void TryParseSseLine_ParsesDataLine_WithEmptyData()
        {
            var result = SseStreamParser.TryParseSseLine("data: ");
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(SseMessageType.Data));
            Assert.That(result.RawData, Is.Empty);
        }

        [Test]
        public void IsSseMessage_ReturnsFalse_ForNullLine()
        {
            Assert.That(SseStreamParser.IsSseMessage(null), Is.False);
        }

        [Test]
        public void IsSseMessage_ReturnsFalse_ForEmptyLine()
        {
            Assert.That(SseStreamParser.IsSseMessage(""), Is.False);
        }

        [Test]
        public void IsSseMessage_ReturnsTrue_ForDataLine()
        {
            Assert.That(SseStreamParser.IsSseMessage("data: test"), Is.True);
        }

        [Test]
        public void IsSseMessage_ReturnsTrue_ForEventLine()
        {
            Assert.That(SseStreamParser.IsSseMessage("event: test"), Is.True);
        }

        [Test]
        public void IsSseMessage_ReturnsTrue_ForCommentLine()
        {
            Assert.That(SseStreamParser.IsSseMessage(": test"), Is.True);
        }

        [Test]
        public void IsSseMessage_ReturnsFalse_ForRegularLine()
        {
            Assert.That(SseStreamParser.IsSseMessage("regular line"), Is.False);
        }

        [Test]
        public void ExtractSseData_ReturnsNull_ForNonDataLine()
        {
            Assert.That(SseStreamParser.ExtractSseData("event: test"), Is.Null);
        }

        [Test]
        public void ExtractSseData_ReturnsData_ForDataLine()
        {
            var result = SseStreamParser.ExtractSseData("data: {\"test\":true}");
            Assert.That(result, Is.EqualTo("{\"test\":true}"));
        }

        [Test]
        public void ExtractSseData_TrimsWhitespace()
        {
            var result = SseStreamParser.ExtractSseData("data:   {\"test\":true}   ");
            Assert.That(result, Is.EqualTo("{\"test\":true}"));
        }

        [Test]
        public void ExtractSseEventType_ReturnsNull_ForNonEventLine()
        {
            Assert.That(SseStreamParser.ExtractSseEventType("data: test"), Is.Null);
        }

        [Test]
        public void ExtractSseEventType_ReturnsEventType_ForEventLine()
        {
            var result = SseStreamParser.ExtractSseEventType("event: my_event");
            Assert.That(result, Is.EqualTo("my_event"));
        }

        [Test]
        public void ExtractSseEventType_TrimsWhitespace()
        {
            var result = SseStreamParser.ExtractSseEventType("event:   my_event   ");
            Assert.That(result, Is.EqualTo("my_event"));
        }

        [Test]
        public void TryParseSseLine_HandlesMultipleColonsInData()
        {
            var result = SseStreamParser.TryParseSseLine("data: {\"url\":\"http://example.com\"}");
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(SseMessageType.Data));
            Assert.That(result.ParsedData, Is.Not.Null);
        }

        [Test]
        public void TryParseSseLine_ExtractsCorrectJsonFromComplexData()
        {
            var json = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"tools\":[]}}";
            var result = SseStreamParser.TryParseSseLine($"data: {json}");
            Assert.That(result, Is.Not.Null);
            Assert.That(result.ParsedData, Is.Not.Null);
            Assert.That(result.ParsedData["id"].ToString(), Is.EqualTo("1"));
        }
    }
}
