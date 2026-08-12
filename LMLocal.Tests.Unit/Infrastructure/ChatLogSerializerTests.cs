using System;
using System.Collections.Generic;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi.Requests;
using LMLocal.Infrastructure.Persistence;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class ChatLogSerializerTests
    {
        private static readonly DateTime UtcNow = new DateTime(2025, 2, 1, 14, 30, 0, DateTimeKind.Utc);

        [Test]
        public void BuildFileName_FormatsHourlyFileName()
        {
            var result = ChatLogSerializer.BuildFileName(UtcNow, "chat_");

            Assert.That(result, Is.EqualTo("20250201_14_chat_.jsonl"));
        }

        [Test]
        public void BuildMessageLine_ContainsAllRequiredFields()
        {
            var message = new ChatMessage("user", "hello");

            var line = ChatLogSerializer.BuildMessageLine(message, "session-1", UtcNow);
            var obj = JObject.Parse(line.Trim());

            Assert.That(obj.Value<string>("type"), Is.EqualTo("message"));
            Assert.That(obj.Value<string>("session_id"), Is.EqualTo("session-1"));
            Assert.That(obj.Value<string>("role"), Is.EqualTo("user"));
            Assert.That(obj.Value<string>("content"), Is.EqualTo("hello"));
            // Timestamp must be written in ISO 8601 round-trip format. JObject.Parse converts such
            // strings to DateTime (culture-dependent), so assert on the raw serialized line.
            Assert.That(line, Does.Contain("\"timestamp\":\"2025-02-01T14:30:00.0000000Z\""));
        }

        [Test]
        public void BuildMessageLine_WithToolCalls_SerializesToolCallsArray()
        {
            var toolCalls = new List<ToolCall>
            {
                new ToolCall
                {
                    Id = "call_1",
                    Type = "function",
                    Function = new FunctionCallDetails { Name = "search", Arguments = "{\"q\":\"x\"}" }
                }
            };
            var message = new ChatMessage("assistant", "using tools") { ToolCalls = toolCalls };

            var line = ChatLogSerializer.BuildMessageLine(message, "session-1", UtcNow);
            var obj = JObject.Parse(line.Trim());

            Assert.That(obj["tool_calls"].Type, Is.EqualTo(JTokenType.Array));
            Assert.That(obj["tool_calls"][0].Value<string>("id"), Is.EqualTo("call_1"));
            Assert.That(obj["tool_calls"][0]["function"].Value<string>("name"), Is.EqualTo("search"));
        }

        [Test]
        public void BuildSessionStartMarker_ContainsTypeSessionIdAndTimestamp()
        {
            var line = ChatLogSerializer.BuildSessionStartMarker("session-9", UtcNow);
            var obj = JObject.Parse(line.Trim());

            Assert.That(obj.Value<string>("type"), Is.EqualTo("session_start"));
            Assert.That(obj.Value<string>("session_id"), Is.EqualTo("session-9"));
            Assert.That(line, Does.Contain("\"timestamp\":\"2025-02-01T14:30:00.0000000Z\""));
        }

        [Test]
        public void ParseChatMessage_ValidLine_ReturnsMessage()
        {
            var obj = JObject.Parse(
                "{\"type\":\"message\",\"session_id\":\"s1\",\"role\":\"user\",\"content\":\"hi\",\"timestamp\":\"2025-02-01T14:00:00Z\"}");

            var result = ChatLogSerializer.ParseChatMessage(obj);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Role, Is.EqualTo("user"));
            Assert.That(result.Content, Is.EqualTo("hi"));
        }

        [Test]
        public void ParseChatMessage_MissingRole_DefaultsToUnknown()
        {
            var obj = JObject.Parse("{\"content\":\"hi\"}");

            var result = ChatLogSerializer.ParseChatMessage(obj);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Role, Is.EqualTo("unknown"));
        }

        [Test]
        public void ParseChatMessage_MalformedToolCalls_ReturnsNull()
        {
            var obj = JObject.Parse("{\"role\":\"assistant\",\"tool_calls\":\"not-an-array\"}");

            var result = ChatLogSerializer.ParseChatMessage(obj);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void ParseChatMessage_WithToolCalls_ReturnsListOfToolCall()
        {
            var obj = JObject.Parse(
                "{\"role\":\"assistant\",\"tool_calls\":[{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"search\",\"arguments\":\"{}\"}}]}");

            var result = ChatLogSerializer.ParseChatMessage(obj);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.ToolCalls, Is.InstanceOf<List<ToolCall>>());

            var toolCalls = (List<ToolCall>)result.ToolCalls;
            Assert.That(toolCalls.Count, Is.EqualTo(1));
            Assert.That(toolCalls[0].Id, Is.EqualTo("call_1"));
            Assert.That(toolCalls[0].Type, Is.EqualTo("function"));
            Assert.That(toolCalls[0].Function.Name, Is.EqualTo("search"));
            Assert.That(toolCalls[0].Function.Arguments, Is.EqualTo("{}"));
        }

        [Test]
        public void TruncatePrompt_BelowBoundary_ReturnsAsIs()
        {
            var content = new string('x', 199);

            Assert.That(ChatLogSerializer.TruncatePrompt(content), Is.EqualTo(content));
        }

        [Test]
        public void TruncatePrompt_AtBoundary_ReturnsAsIs()
        {
            var content = new string('x', 200);

            Assert.That(ChatLogSerializer.TruncatePrompt(content), Is.EqualTo(content));
        }

        [Test]
        public void TruncatePrompt_AboveBoundary_TruncatesWithEllipsis()
        {
            var content = new string('x', 201);

            var result = ChatLogSerializer.TruncatePrompt(content);

            Assert.That(result.Length, Is.EqualTo(200 + 3));
            Assert.That(result.StartsWith(new string('x', 200)));
            Assert.That(result.EndsWith("..."));
        }

        [TestCase(null)]
        [TestCase("")]
        public void TruncatePrompt_NullOrEmpty_ReturnsEmpty(string content)
        {
            Assert.That(ChatLogSerializer.TruncatePrompt(content), Is.EqualTo(string.Empty));
        }

        [Test]
        public void BuildMessageLine_WithImageParts_WritesPlaceholderNotBase64()
        {
            var parts = new List<ContentPart>
            {
                new ContentPart { Type = "text", Text = "describe" },
                new ContentPart { Type = "image_url", ImageUrl = new ImageUrlInfo { Url = "data:image/png;base64,AAAA", Detail = "auto" } },
                new ContentPart { Type = "image_url", ImageUrl = new ImageUrlInfo { Url = "data:image/png;base64,BBBB", Detail = "auto" } }
            };
            var message = new ChatMessage("user", parts);

            var line = ChatLogSerializer.BuildMessageLine(message, "session-1", UtcNow);
            var obj = JObject.Parse(line.Trim());

            Assert.That(obj.Value<string>("content"), Is.EqualTo("[2 images attached - not available in this session]"));
            Assert.That(line, Does.Not.Contain("data:image"));
        }

        [Test]
        public void BuildMessageLine_WithTextOnlyParts_KeepsContentArray()
        {
            var parts = new List<ContentPart>
            {
                new ContentPart { Type = "text", Text = "describe" }
            };
            var message = new ChatMessage("user", parts);

            var line = ChatLogSerializer.BuildMessageLine(message, "session-1", UtcNow);
            var obj = JObject.Parse(line.Trim());

            Assert.That(obj["content"].Type, Is.EqualTo(JTokenType.Array));
            Assert.That(obj["content"][0].Value<string>("type"), Is.EqualTo("text"));
            Assert.That(obj["content"][0].Value<string>("text"), Is.EqualTo("describe"));
        }

        [Test]
        public void BuildMessageLine_WithSingleImage_WritesSingularPlaceholder()
        {
            var parts = new List<ContentPart>
            {
                new ContentPart { Type = "image_url", ImageUrl = new ImageUrlInfo { Url = "data:image/png;base64,AAAA", Detail = "auto" } }
            };
            var message = new ChatMessage("user", parts);

            var line = ChatLogSerializer.BuildMessageLine(message, "session-1", UtcNow);
            var obj = JObject.Parse(line.Trim());

            Assert.That(obj.Value<string>("content"), Is.EqualTo("[1 image attached - not available in this session]"));
            Assert.That(line, Does.Not.Contain("data:image"));
        }
    }
}
