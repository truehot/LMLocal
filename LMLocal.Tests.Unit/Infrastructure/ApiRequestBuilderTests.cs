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

        private static ToolCall ExtraContentToolCall(string signature)
        {
            return new ToolCall
            {
                Id = "call_1",
                Type = "function",
                Function = new FunctionCallDetails { Name = "check_flight", Arguments = "{\"flight\":\"AA100\"}" },
                ExtraContent = JToken.Parse("{\"google\":{\"thought_signature\":\"" + signature + "\"}}")
            };
        }

        [Test]
        public void BuildRequest_PassthroughToolCallList_ExtraContentSameReference()
        {
            var builder = CreateBuilder();
            var toolCalls = new List<ToolCall> { ExtraContentToolCall("sigA") };
            var chatMessage = new ChatMessage("assistant", null) { ToolCalls = toolCalls };

            var result = builder.BuildRequest(new MessageContext(new[] { chatMessage }), new ModelContext("m"), stream: false);

            Assert.That(result.Messages[0].ToolCalls, Is.SameAs(toolCalls));
            Assert.That(toolCalls[0].ExtraContent["google"].Value<string>("thought_signature"), Is.EqualTo("sigA"));
        }

        [Test]
        public void SerializeRequest_ExtraContent_LandsInsideToolCall()
        {
            var builder = CreateBuilder();
            var toolCalls = new List<ToolCall> { ExtraContentToolCall("sigA") };
            var chatMessage = new ChatMessage("assistant", null) { ToolCalls = toolCalls };

            var result = builder.BuildRequest(new MessageContext(new[] { chatMessage }), new ModelContext("gemini-pro"), stream: false);
            var json = JsonConvert.SerializeObject(result);

            Assert.That(json, Does.Contain("\"extra_content\":{\"google\":{\"thought_signature\":\"sigA\"}}"));
        }

        [Test]
        public void SerializeRequest_NoExtraContent_FieldOmitted()
        {
            var builder = CreateBuilder();
            var toolCalls = new List<ToolCall>
            {
                new ToolCall
                {
                    Id = "call_1",
                    Type = "function",
                    Function = new FunctionCallDetails { Name = "check_flight", Arguments = "{}" }
                }
            };
            var chatMessage = new ChatMessage("assistant", null) { ToolCalls = toolCalls };

            var result = builder.BuildRequest(new MessageContext(new[] { chatMessage }), new ModelContext("openai-model"), stream: false);
            var json = JsonConvert.SerializeObject(result);

            Assert.That(json, Does.Not.Contain("extra_content"));
        }

        [Test]
        public void BuildRequest_JArrayToolCalls_ExtraContentRestored()
        {
            // JArray branch of ConvertToolCalls: ToObject<List<ToolCall>>() must restore ExtraContent.
            var builder = CreateBuilder();
            var array = JArray.Parse(
                "[{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"check_flight\",\"arguments\":\"{}\"},\"extra_content\":{\"google\":{\"thought_signature\":\"sigB\"}}}]");
            var chatMessage = new ChatMessage("assistant", null) { ToolCalls = array };

            var result = builder.BuildRequest(new MessageContext(new[] { chatMessage }), new ModelContext("m"), stream: false);

            var call = result.Messages[0].ToolCalls[0];
            Assert.That(call.ExtraContent, Is.Not.Null);
            Assert.That(call.ExtraContent["google"].Value<string>("thought_signature"), Is.EqualTo("sigB"));
        }
    }
}
