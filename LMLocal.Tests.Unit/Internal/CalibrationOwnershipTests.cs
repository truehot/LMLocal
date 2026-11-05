using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.LlmApi.Responses;
using LMLocal.Infrastructure.ModelsConfig;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    /// <summary>
    /// A round may only touch the calibration of its own conversation: the chat records and resets, a subagent does neither.
    /// </summary>
    [TestFixture]
    public class CalibrationOwnershipTests
    {
        private const string ChatModel = "chat-model";
        private const string SubAgentModel = "sub-agent-model";

        private Mock<IStreamProcessor> _processor;
        private Mock<IModelsConfigManager> _modelsConfig;

        [SetUp]
        public void SetUp()
        {
            _processor = new Mock<IStreamProcessor>();
            _processor
                .Setup(p => p.ProcessStreamAsync(
                    It.IsAny<Stream>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<System.Func<TextStreamChunk, TokenGenerationStats, Task>>(),
                    It.IsAny<int>()))
                .ReturnsAsync(new StreamCompletionResult { ContentResponse = "" });

            _modelsConfig = new Mock<IModelsConfigManager>();
            _modelsConfig
                .Setup(m => m.GetAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ModelsConfigFile { Models = new List<ModelDefinition>() });
        }

        private StreamingRoundService CreateRoundService(Mock<IOpenApiAdapter> client)
        {
            var settings = new Mock<ISettingsManager>();
            settings.Setup(s => s.BatchIntervalMs).Returns(100);

            var factory = new Mock<IStreamProcessorFactory>();
            factory.Setup(f => f.Create(It.IsAny<CancellationTokenSource>())).Returns(_processor.Object);

            return new StreamingRoundService(client.Object, settings.Object, factory.Object, _modelsConfig.Object);
        }

        private static Mock<IOpenApiAdapter> CreateClient()
        {
            var client = new Mock<IOpenApiAdapter>();
            client
                .Setup(c => c.SendChatStreamingAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StreamingResponse(
                    new MemoryStream(),
                    new System.Net.Http.HttpResponseMessage(),
                    new System.Net.Http.HttpRequestMessage(),
                    new System.Net.Http.StringContent("")));
            return client;
        }

        private void ReturnPromptTokens(int promptTokens)
        {
            _processor
                .Setup(p => p.ProcessStreamAsync(
                    It.IsAny<Stream>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<System.Func<TextStreamChunk, TokenGenerationStats, Task>>(),
                    It.IsAny<int>()))
                .ReturnsAsync(new StreamCompletionResult
                {
                    ContentResponse = "",
                    TokenUsage = new TokenUsageMetadata { PromptTokens = promptTokens }
                });
        }

        private static List<ChatMessage> OnePrompt()
        {
            return new List<ChatMessage> { new ChatMessage("user", "a prompt that produces a measurable envelope") };
        }

        private static ContextCalibration SeededForChat()
        {
            var calibration = new ContextCalibration();
            calibration.ResetForModel(ChatModel);
            calibration.RecordSuccessfulRound(promptTokens: 1500, envelopeEstimate: 1000);
            calibration.RecordOverflowError(promptTokens: 9000, contextLength: 8192);
            return calibration;
        }

        private async Task RunChatRoundAsync(ContextCalibration calibration, string modelId)
        {
            var history = new Mock<IChatHistoryManager>();
            history.Setup(h => h.BuildUserMessagesWithHistory(It.IsAny<string>())).Returns(OnePrompt());

            var service = new ChatStreamService(CreateRoundService(CreateClient()), history.Object, calibration);

            await service.GenerateStreamAsync(
                new GenerateStreamContext { Prompt = "prompt", ModelId = modelId },
                null,
                (chunk, stats) => Task.CompletedTask,
                completion => Task.CompletedTask,
                CancellationToken.None);
        }

        [Test]
        public async Task ChatRound_RecordsItsCalibration()
        {
            var calibration = new ContextCalibration();
            calibration.ResetForModel(ChatModel);

            int estimate = EnvelopeEstimator.Estimate(OnePrompt(), null);
            Assert.That(estimate, Is.GreaterThan(0), "the fixture must produce a measurable envelope");
            ReturnPromptTokens(estimate * 2);

            await RunChatRoundAsync(calibration, ChatModel);

            Assert.That(calibration.Factor, Is.EqualTo(2.0).Within(0.001),
                "the chat owns the calibration and must record what the provider reported");
        }

        [Test]
        public async Task SubAgentRound_LeavesTheChatCalibrationUntouched()
        {
            var calibration = SeededForChat();
            ReturnPromptTokens(5000);

            var history = new Mock<IChatHistoryManager>();
            history.Setup(h => h.BuildUserMessagesWithHistory(It.IsAny<string>())).Returns(OnePrompt());

            await CreateRoundService(CreateClient()).RunAsync(
                new StreamRoundRequest
                {
                    History = history.Object,
                    Prompt = "subagent prompt",
                    ModelId = SubAgentModel
                },
                CancellationToken.None);

            Assert.That(calibration.Factor, Is.EqualTo(1.5).Within(0.001),
                "a subagent round must neither reset the coefficient nor overwrite it with its own model's ratio");
            Assert.That(calibration.ErrorContextLength, Is.EqualTo(8192),
                "nor replace the window the chat learned from its own overflow");
        }

        [Test]
        public async Task ChatRound_ForAnotherModel_ResetsTheCoefficient()
        {
            var calibration = SeededForChat();

            await RunChatRoundAsync(calibration, "another-chat-model");

            Assert.That(calibration.Factor, Is.EqualTo(1.0),
                "a different tokenizer must not inherit the coefficient");
            Assert.That(calibration.ErrorContextLength, Is.Null,
                "nor a window measured with the previous model");
        }
    }
}
