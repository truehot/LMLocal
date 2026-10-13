using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Application.SubAgents;
using LMLocal.Application.Tool;
using LMLocal.Core.Exceptions;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Persistence;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.SubAgents
{
    /// <summary>
    /// Guards the user-facing error text surfaced by <see cref="SubAgentsService"/>:
    /// raw exception messages must be routed through <c>ExceptionFormatter</c>, so the
    /// generic .NET wrapper ("An error occurred while sending the request.") never leaks
    /// to the chat when the local LLM server is not running.
    /// </summary>
    [TestFixture]
    public class SubAgentsServiceTests
    {
        private const string GenericWrapper = "An error occurred while sending the request";

        private Mock<ISettingsManager> _settingsManagerMock;
        private Mock<IFileSystem> _fileSystemMock;
        private Mock<IStreamingRoundService> _roundServiceMock;
        private Mock<IToolExecutionManager> _toolExecutionManagerMock;
        private Mock<IToolQueueProvider> _queueProviderMock;
        private Mock<IToolCallLoopDetector> _loopDetectorMock;
        private Mock<IChatPersistenceFactory> _persistenceFactoryMock;
        private Mock<IChatPersistenceService> _persistenceMock;

        [SetUp]
        public void SetUp()
        {
            _settingsManagerMock = new Mock<ISettingsManager>();
            _settingsManagerMock.SetupGet(s => s.LocalAppDataFolder).Returns("LMLocalChat.Tests");
            _settingsManagerMock.SetupGet(s => s.SystemPrompt).Returns("system");

            _fileSystemMock = new Mock<IFileSystem>();
            _roundServiceMock = new Mock<IStreamingRoundService>();
            _toolExecutionManagerMock = new Mock<IToolExecutionManager>();
            _queueProviderMock = new Mock<IToolQueueProvider>();
            _loopDetectorMock = new Mock<IToolCallLoopDetector>();
            _persistenceFactoryMock = new Mock<IChatPersistenceFactory>();
            _persistenceMock = new Mock<IChatPersistenceService>();

            _queueProviderMock
                .Setup(q => q.GetSubAgentQueue(It.IsAny<SubAgentRunRequest>()))
                .Returns(ToolQueue.ForSubAgent("agent", new List<ToolDefinition>()));

            _persistenceMock
                .Setup(p => p.MarkNewSessionAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _persistenceFactoryMock
                .Setup(f => f.Create(It.IsAny<string>()))
                .Returns(_persistenceMock.Object);
        }

        private SubAgentsService CreateService()
        {
            return new SubAgentsService(
                _settingsManagerMock.Object,
                _fileSystemMock.Object,
                _roundServiceMock.Object,
                () => _toolExecutionManagerMock.Object,
                _queueProviderMock.Object,
                _loopDetectorMock.Object,
                _persistenceFactoryMock.Object);
        }

        private static SubAgentRunRequest BuildRequest()
        {
            return new SubAgentRunRequest
            {
                AgentName = "agent",
                Prompt = "do it",
                Model = "m"
            };
        }

        private void SetupRoundThrows(Exception exception)
        {
            _roundServiceMock
                .Setup(r => r.RunAsync(It.IsAny<StreamRoundRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);
        }

        [Test]
        public async Task ExecutePromptAsync_UnreachableProvider_ErrorExposesInnerCauseInsteadOfGenericWrapper()
        {
            var inner = new InvalidOperationException(
                "No connection could be made because the target machine actively refused it 127.0.0.1:1234");
            SetupRoundThrows(new HttpRequestException(GenericWrapper, inner));

            var response = await CreateService().ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Error, Does.Contain("actively refused it"),
                "The inner cause must be surfaced to the user.");
            Assert.That(response.Error, Does.Not.Contain(GenericWrapper),
                "The generic .NET wrapper must not leak into the user-facing error.");
        }

        [Test]
        public async Task ExecutePromptAsync_ApiException_KeepsParsedProviderMessage()
        {
            SetupRoundThrows(new ApiException(
                new ApiErrorInfo { Message = "Model not found" },
                404));

            var response = await CreateService().ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Error, Is.EqualTo("Provider API error: Model not found"));
        }
    }
}
