using System;
using System.Collections.Generic;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.LlmApi.Requests;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Infrastructure.Tooling;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class ApiRequestBuilderTests
    {
        private Mock<ISettingsManager> _mockSettings;
        private Mock<IToolQueueProvider> _mockToolQueueProvider;

        [SetUp]
        public void SetUp()
        {
            _mockSettings = new Mock<ISettingsManager>();
            _mockSettings.Setup(s => s.Current).Returns(new AppSettings());
            _mockToolQueueProvider = new Mock<IToolQueueProvider>();
            _mockToolQueueProvider
                .Setup(p => p.GetMainQueue())
                .Returns(ToolQueue.Main(new List<ToolDefinition>()));
        }

        private ApiRequestBuilder CreateBuilder()
        {
            return new ApiRequestBuilder(_mockSettings.Object, _mockToolQueueProvider.Object);
        }

        [Test]
        public void BuildRequest_NullMessageContext_Throws()
        {
            var builder = CreateBuilder();

            Assert.That(
                () => builder.BuildRequest(null, new ModelContext("m"), true),
                Throws.ArgumentNullException);
        }

        [Test]
        public void BuildRequest_NullModelContext_Throws()
        {
            var builder = CreateBuilder();

            Assert.That(
                () => builder.BuildRequest(new MessageContext(new ChatMessage[0]), null, true),
                Throws.ArgumentNullException);
        }

        private void EnableMainTools(params string[] names)
        {
            _mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableAiTools = true });
            var definitions = new List<ToolDefinition>();
            foreach (var name in names)
                definitions.Add(new ToolDefinition { Name = name });
            _mockToolQueueProvider.Setup(p => p.GetMainQueue()).Returns(ToolQueue.Main(definitions));
        }

        private static MessageContext ReasoningHistory(out ChatMessage assistant)
        {
            assistant = new ChatMessage("assistant", "a") { ReasoningContent = "think" };
            return new MessageContext(new[] { new ChatMessage("user", "q"), assistant });
        }

        [Test]
        public void ReasoningEcho_WithTools_KeepsFieldAndCountsChars()
        {
            EnableMainTools("read_file_lines");
            var builder = CreateBuilder();
            var mc = ReasoningHistory(out ChatMessage assistant);

            var request = builder.BuildRequest(mc, new ModelContext("m", echoReasoning: true), stream: false);

            Assert.That(JObject.Parse(JsonConvert.SerializeObject(request))["messages"][1]["reasoning_content"].Value<string>(), Is.EqualTo("think"));
            Assert.That(request.ReasoningEchoChars, Is.EqualTo(5));
            Assert.That(JsonConvert.SerializeObject(request), Does.Not.Contain("ReasoningEchoChars"), "The counter must not reach the wire.");
            Assert.That(JsonConvert.SerializeObject(request.Messages[0]), Does.Not.Contain("reasoning_content"));
            Assert.That(assistant.ReasoningContent, Is.EqualTo("think"), "The history must stay untouched.");
        }

        [Test]
        public void ReasoningEcho_ProfileOptOut_NoFieldNoChars()
        {
            EnableMainTools("read_file_lines");
            var builder = CreateBuilder();
            var mc = ReasoningHistory(out _);

            var request = builder.BuildRequest(mc, new ModelContext("m"), stream: false);

            Assert.That(JsonConvert.SerializeObject(request), Does.Not.Contain("reasoning_content"));
            Assert.That(request.ReasoningEchoChars, Is.EqualTo(0));
        }

        [Test]
        public void ReasoningEcho_UseToolsFalse_NoField()
        {
            EnableMainTools("read_file_lines");
            var builder = CreateBuilder();
            var mc = ReasoningHistory(out _);

            var request = builder.BuildRequest(mc, new ModelContext("m", echoReasoning: true), stream: false, useTools: false);

            Assert.That(JsonConvert.SerializeObject(request), Does.Not.Contain("reasoning_content"));
            Assert.That(request.ReasoningEchoChars, Is.EqualTo(0));
        }

        [Test]
        public void ReasoningEcho_EmptyToolQueue_NoField()
        {
            EnableMainTools();
            var builder = CreateBuilder();
            var mc = ReasoningHistory(out _);

            var request = builder.BuildRequest(mc, new ModelContext("m", echoReasoning: true), stream: false);

            Assert.That(JsonConvert.SerializeObject(request), Does.Not.Contain("reasoning_content"));
            Assert.That(request.ReasoningEchoChars, Is.EqualTo(0));
        }

        [Test]
        public void ReasoningEcho_ExplicitToolsEmpty_NoField()
        {
            var builder = CreateBuilder();
            var mc = ReasoningHistory(out _);

            var request = builder.BuildRequest(mc, new ModelContext("m", echoReasoning: true), stream: false, new List<ToolDefinition>());

            Assert.That(JsonConvert.SerializeObject(request), Does.Not.Contain("reasoning_content"));
            Assert.That(request.ReasoningEchoChars, Is.EqualTo(0));
        }

        [Test]
        public void ReasoningEcho_ExplicitTools_KeepsField()
        {
            var builder = CreateBuilder();
            var mc = ReasoningHistory(out _);
            var tools = new List<ToolDefinition> { new ToolDefinition { Name = "read_file_lines" } };

            var request = builder.BuildRequest(mc, new ModelContext("m", echoReasoning: true), stream: false, tools);

            Assert.That(JObject.Parse(JsonConvert.SerializeObject(request))["messages"][1]["reasoning_content"].Value<string>(), Is.EqualTo("think"));
            Assert.That(request.ReasoningEchoChars, Is.EqualTo(5));
        }

        [Test]
        public void BuildRequest_SetsModelFromContext()
        {
            var builder = CreateBuilder();
            var mc = new MessageContext(new ChatMessage[0]);

            var result = builder.BuildRequest(mc, new ModelContext("gemini-pro"), stream: false);

            Assert.That(result.Model, Is.EqualTo("gemini-pro"));
        }

        [Test]
        public void BuildRequest_CopiesMessages()
        {
            var builder = CreateBuilder();
            var mc = new MessageContext(new[]
            {
                new ChatMessage("user", "hello"),
                new ChatMessage("assistant", "hi")
            });

            var result = builder.BuildRequest(mc, new ModelContext("m"), stream: false);

            Assert.That(result.Messages.Count, Is.EqualTo(2));
            Assert.That(result.Messages[0].Role, Is.EqualTo("user"));
            Assert.That(result.Messages[0].Content, Is.EqualTo("hello"));
        }

        [Test]
        public void BuildRequest_Streaming_SetsStreamAndOptions()
        {
            var builder = CreateBuilder();

            var result = builder.BuildRequest(
                new MessageContext(new ChatMessage[0]),
                new ModelContext("m"),
                stream: true);

            Assert.That(result.Stream, Is.True);
            Assert.That(result.StreamOptions, Is.Not.Null);
            Assert.That(result.StreamOptions.IncludeUsage, Is.True);
        }

        [Test]
        public void BuildRequest_NonStreaming_NoStreamOptions()
        {
            var builder = CreateBuilder();

            var result = builder.BuildRequest(
                new MessageContext(new ChatMessage[0]),
                new ModelContext("m"),
                stream: false);

            Assert.That(result.StreamOptions, Is.Null);
        }

        [Test]
        public void BuildRequest_ToolsDisabled_NoToolsInRequest()
        {
            _mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableAiTools = false });
            var builder = CreateBuilder();

            var result = builder.BuildRequest(
                new MessageContext(new ChatMessage[0]),
                new ModelContext("m"),
                stream: false,
                useTools: true);

            Assert.That(result.Tools, Is.Null);
            _mockToolQueueProvider.Verify(p => p.GetMainQueue(), Times.Never);
        }

        [Test]
        public void BuildRequest_UseToolsFalse_NoToolsEvenWhenEnabled()
        {
            _mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableAiTools = true });
            var builder = CreateBuilder();

            var result = builder.BuildRequest(
                new MessageContext(new ChatMessage[0]),
                new ModelContext("m"),
                stream: false,
                useTools: false);

            Assert.That(result.Tools, Is.Null);
            _mockToolQueueProvider.Verify(p => p.GetMainQueue(), Times.Never);
        }

        [Test]
        public void BuildRequest_MainQueueTools_AddedWhenEnabled()
        {
            _mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableAiTools = true });
            _mockToolQueueProvider
                .Setup(p => p.GetMainQueue())
                .Returns(ToolQueue.Main(new List<ToolDefinition> { new ToolDefinition { Name = "read_file_lines" } }));
            var builder = CreateBuilder();

            var result = builder.BuildRequest(
                new MessageContext(new ChatMessage[0]),
                new ModelContext("m"),
                stream: false,
                useTools: true);

            Assert.That(result.Tools, Is.Not.Null);
            Assert.That(result.Tools.Count, Is.EqualTo(1));
        }

        [Test]
        public void BuildRequest_ExplicitTools_AddsTools()
        {
            var builder = CreateBuilder();
            var tools = new List<ToolDefinition>
            {
                new ToolDefinition { Name = "read_file_lines" },
                new ToolDefinition { Name = "search_file_content" }
            };

            var result = builder.BuildRequest(
                new MessageContext(new ChatMessage[0]),
                new ModelContext("m"),
                stream: false,
                tools);

            Assert.That(result.Tools, Is.Not.Null);
            Assert.That(result.Tools.Count, Is.EqualTo(2));
        }

        [Test]
        public void BuildRequest_ModelParameters_AreMapped()
        {
            var builder = CreateBuilder();
            var model = new ModelContext(
                modelId: "m",
                temperature: 0.7,
                topP: 0.9,
                maxOutputTokens: 2048,
                presencePenalty: 0.5,
                frequencyPenalty: 0.3,
                reasoning: "high");

            var result = builder.BuildRequest(new MessageContext(new ChatMessage[0]), model, stream: false);

            Assert.That(result.Temperature, Is.EqualTo(0.7));
            Assert.That(result.TopP, Is.EqualTo(0.9));
            Assert.That(result.MaxCompletionTokens, Is.EqualTo(2048));
            Assert.That(result.PresencePenalty, Is.EqualTo(0.5));
            Assert.That(result.FrequencyPenalty, Is.EqualTo(0.3));
            Assert.That(result.ReasoningEffort, Is.EqualTo("high"));
        }

        // ================ Gemini extra_content echo-back ================

        private static ChatToolCall ExtraContentToolCall(string signature)
        {
            return new ChatToolCall
            {
                Id = "call_1",
                Name = "check_flight",
                Arguments = "{\"flight\":\"AA100\"}",
                ExtraContentJson = "{\"google\":{\"thought_signature\":\"" + signature + "\"}}"
            };
        }

        [Test]
        public void BuildRequest_ToolCallList_ProjectsExtraContentIntoWire()
        {
            var builder = CreateBuilder();
            var toolCalls = new List<ChatToolCall> { ExtraContentToolCall("sigA") };
            var chatMessage = new ChatMessage("assistant", null) { ToolCalls = toolCalls };

            var result = builder.BuildRequest(new MessageContext(new[] { chatMessage }), new ModelContext("m"), stream: false);

            Assert.That(JToken.DeepEquals(
                JToken.Parse("{\"google\":{\"thought_signature\":\"sigA\"}}"),
                result.Messages[0].ToolCalls[0].ExtraContent), Is.True);
        }

        [Test]
        public void SerializeRequest_ExtraContent_LandsInsideToolCall()
        {
            var builder = CreateBuilder();
            var toolCalls = new List<ChatToolCall> { ExtraContentToolCall("sigA") };
            var chatMessage = new ChatMessage("assistant", null) { ToolCalls = toolCalls };

            var result = builder.BuildRequest(new MessageContext(new[] { chatMessage }), new ModelContext("gemini-pro"), stream: false);
            var json = JsonConvert.SerializeObject(result);

            Assert.That(json, Does.Contain("\"extra_content\":{\"google\":{\"thought_signature\":\"sigA\"}}"));
        }

        [Test]
        public void SerializeRequest_NoExtraContent_FieldOmitted()
        {
            var builder = CreateBuilder();
            var toolCalls = new List<ChatToolCall>
            {
                new ChatToolCall
                {
                    Id = "call_1",
                    Name = "check_flight",
                    Arguments = "{}"
                }
            };
            var chatMessage = new ChatMessage("assistant", null) { ToolCalls = toolCalls };

            var result = builder.BuildRequest(new MessageContext(new[] { chatMessage }), new ModelContext("openai-model"), stream: false);
            var json = JsonConvert.SerializeObject(result);

            Assert.That(json, Does.Not.Contain("extra_content"));
        }

        // ================ Golden API payload ================

        /// <summary>
        /// Pins the full request body of an assistant message with tool calls; the expected JSON must not change.
        /// </summary>
        [Test]
        public void SerializeRequest_ToolCallMessages_GoldenWireShape()
        {
            var builder = CreateBuilder();
            var chatMessage = new ChatMessage("assistant", null)
            {
                ToolCalls = new List<ChatToolCall>
                {
                    new ChatToolCall
                    {
                        Id = "call_1",
                        Name = "check_flight",
                        Arguments = "{\"flight\":\"AA100\"}"
                    },
                    new ChatToolCall
                    {
                        Id = "call_2",
                        Name = "book_flight",
                        Arguments = "{}",
                        ExtraContentJson = "{\"google\":{\"thought_signature\":\"sigA\"}}"
                    }
                }
            };

            var request = builder.BuildRequest(new MessageContext(new[] { chatMessage }), new ModelContext("m"), stream: false);
            var body = JObject.Parse(JsonConvert.SerializeObject(request));

            var expected = JToken.Parse(@"{
                ""model"": ""m"",
                ""messages"": [
                    {
                        ""role"": ""assistant"",
                        ""content"": null,
                        ""tool_calls"": [
                            {
                                ""id"": ""call_1"",
                                ""type"": ""function"",
                                ""function"": { ""name"": ""check_flight"", ""arguments"": ""{\""flight\"":\""AA100\""}"" }
                            },
                            {
                                ""id"": ""call_2"",
                                ""type"": ""function"",
                                ""function"": { ""name"": ""book_flight"", ""arguments"": ""{}"" },
                                ""extra_content"": { ""google"": { ""thought_signature"": ""sigA"" } }
                            }
                        ]
                    }
                ],
                ""stream"": false
            }");

            Assert.That(JToken.DeepEquals(expected, body), Is.True,
                "tool call request body changed" + Environment.NewLine +
                "expected: " + expected.ToString() + Environment.NewLine +
                "actual:   " + body.ToString());
        }
    }
}
