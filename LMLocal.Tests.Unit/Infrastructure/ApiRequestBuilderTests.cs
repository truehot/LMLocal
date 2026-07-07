using System.Collections.Generic;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.Tooling;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class ApiRequestBuilderTests
    {
        private Mock<ISettingsManager> _mockSettings;
        private Mock<ICompositeToolFactory> _mockToolFactory;

        [SetUp]
        public void SetUp()
        {
            _mockSettings = new Mock<ISettingsManager>();
            _mockSettings.Setup(s => s.Current).Returns(new AppSettings());
            _mockToolFactory = new Mock<ICompositeToolFactory>();
        }

        [Test]
        public void BuildRequest_NullMessageContext_Throws()
        {
            var builder = new ApiRequestBuilder(_mockSettings.Object, _mockToolFactory.Object);

            Assert.That(
                () => builder.BuildRequest(null, new ModelContext("m"), true),
                Throws.ArgumentNullException);
        }

        [Test]
        public void BuildRequest_NullModelContext_Throws()
        {
            var builder = new ApiRequestBuilder(_mockSettings.Object, _mockToolFactory.Object);

            Assert.That(
                () => builder.BuildRequest(new MessageContext(new ChatMessage[0]), null, true),
                Throws.ArgumentNullException);
        }

        [Test]
        public void BuildRequest_SetsModelFromContext()
        {
            var builder = new ApiRequestBuilder(_mockSettings.Object, _mockToolFactory.Object);
            var mc = new MessageContext(new ChatMessage[0]);

            var result = builder.BuildRequest(mc, new ModelContext("gemini-pro"), stream: false);

            Assert.That(result.Model, Is.EqualTo("gemini-pro"));
        }

        [Test]
        public void BuildRequest_CopiesMessages()
        {
            var builder = new ApiRequestBuilder(_mockSettings.Object, _mockToolFactory.Object);
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
            var builder = new ApiRequestBuilder(_mockSettings.Object, _mockToolFactory.Object);

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
            var builder = new ApiRequestBuilder(_mockSettings.Object, _mockToolFactory.Object);

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
            var builder = new ApiRequestBuilder(_mockSettings.Object, _mockToolFactory.Object);

            var result = builder.BuildRequest(
                new MessageContext(new ChatMessage[0]),
                new ModelContext("m"),
                stream: false,
                useTools: true);

            Assert.That(result.Tools, Is.Null);
        }

        [Test]
        public void BuildRequest_UseToolsFalse_NoToolsEvenWhenEnabled()
        {
            _mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableAiTools = true });
            var builder = new ApiRequestBuilder(_mockSettings.Object, _mockToolFactory.Object);

            var result = builder.BuildRequest(
                new MessageContext(new ChatMessage[0]),
                new ModelContext("m"),
                stream: false,
                useTools: false);

            Assert.That(result.Tools, Is.Null);
        }

        [Test]
        public void BuildRequest_ModelParameters_AreMapped()
        {
            var builder = new ApiRequestBuilder(_mockSettings.Object, _mockToolFactory.Object);
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
    }
}
