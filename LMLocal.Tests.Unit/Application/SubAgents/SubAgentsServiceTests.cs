using System;
using System.Collections.Generic;
using System.Linq;
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
using LMLocal.Infrastructure.SubAgents;
using LMLocal.Tests.Unit.Internal.TestHelpers;
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

        private SubAgentsService CreateService(ISubAgentFailureLogger failureLogger = null)
        {
            return new SubAgentsService(
                _settingsManagerMock.Object,
                _fileSystemMock.Object,
                _roundServiceMock.Object,
                () => _toolExecutionManagerMock.Object,
                _queueProviderMock.Object,
                _loopDetectorMock.Object,
                _persistenceFactoryMock.Object,
                failureLogger ?? NullSubAgentFailureLogger.Instance);
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

        [Test]
        public async Task ExecutePromptAsync_LoopDetected_LogsTheRepeatedCallsAsRecentCalls()
        {
            var logger = new RecordingFailureLogger();
            SetupRepeatedToolCalls("read_file_lines", "{\"path\":\"a.cs\"}");
            AllowTool("read_file_lines");

            _loopDetectorMock
                .Setup(d => d.AreSameToolCalls(
                    It.IsAny<IReadOnlyList<ToolCallRecord>>(),
                    It.IsAny<IReadOnlyList<ToolCallRecord>>()))
                .Returns(true);

            _toolExecutionManagerMock
                .Setup(t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<ToolQueue>()))
                .ReturnsAsync(new ToolExecutionResult
                {
                    ToolId = "call_1",
                    ToolName = "read_file_lines",
                    Result = "file text"
                });

            var response = await CreateService(logger).ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Error, Does.Contain("repeated tool call loop"));

            Assert.That(logger.Stage, Is.EqualTo("Final"));
            Assert.That(logger.RecentCalls, Is.Not.Null);
            Assert.That(
                logger.RecentCalls.Select(c => c.ToolName),
                Is.EqualTo(new[] { "read_file_lines", "read_file_lines" }),
                "The rounds that repeated the same call are the reason for the failure.");
            Assert.That(logger.RecentCalls.Select(c => c.Round), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(logger.RecentCalls.Last().ArgumentsJson, Does.Contain("a.cs"));
            Assert.That(logger.RecentCalls.Last().ResultText, Is.EqualTo("file text"));
        }

        [Test]
        public async Task ExecutePromptAsync_RejectedToolCall_IsLoggedWithErrorAndWithoutResult()
        {
            var logger = new RecordingFailureLogger();
            SetupRepeatedToolCalls("write_file", "{\"path\":\"a.cs\"}");

            // The queue has no definitions, so every call is rejected before execution.
            var response = await CreateService(logger).ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Error, Does.Contain("maximum rounds"));

            Assert.That(logger.RecentCalls, Has.Count.EqualTo(3),
                "Only the last three attempts are carried into the record.");
            Assert.That(logger.RecentCalls.Select(c => c.ToolName), Is.All.EqualTo("write_file"));
            Assert.That(logger.RecentCalls.Select(c => c.Error), Is.All.Contains("not allowed"),
                "A rejected call is an attempt and must be visible with its error.");
            Assert.That(logger.RecentCalls.Select(c => c.ResultText), Is.All.Null);

            _toolExecutionManagerMock.Verify(
                t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<ToolQueue>()),
                Times.Never,
                "A tool that is not in the SubAgent queue must never be executed.");
        }

        private void SetupRepeatedToolCalls(string toolName, string argumentsJson)
        {
            var call = new ToolCallRecord
            {
                CallId = "call_1",
                FunctionName = toolName,
                ArgumentsJson = argumentsJson
            };

            _roundServiceMock
                .Setup(r => r.RunAsync(It.IsAny<StreamRoundRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StreamRoundResult
                {
                    Outcome = StreamRoundOutcome.ToolCalls,
                    Stream = new StreamCompletionResult { ToolCalls = new[] { call } }
                });
        }

        private void AllowTool(params string[] toolNames)
        {
            var definitions = toolNames.Select(name => new ToolDefinition { Name = name }).ToList();

            _queueProviderMock
                .Setup(q => q.GetSubAgentQueue(It.IsAny<SubAgentRunRequest>()))
                .Returns(ToolQueue.ForSubAgent("agent", definitions));
        }

        /// <summary>
        /// Captures what <see cref="SubAgentsService"/> hands to the failure logger.
        /// </summary>
        private sealed class RecordingFailureLogger : ISubAgentFailureLogger
        {
            public string Stage { get; private set; }

            public List<SubAgentRecentCall> RecentCalls { get; private set; }

            public Task LogAsync(SubAgentFailureRecord record, CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public Task LogFromRequestAsync(
                SubAgentRunRequest request,
                string stage,
                string error,
                string runId = null,
                int? round = null,
                Guid? chatSessionId = null,
                Exception exception = null,
                IReadOnlyList<SubAgentRecentCall> recentCalls = null,
                CancellationToken cancellationToken = default)
            {
                Stage = stage;
                RecentCalls = recentCalls?.ToList();

                return Task.CompletedTask;
            }

            public Task LogFromToolCallAsync(
                ToolCallRecord call,
                string stage,
                string error,
                Exception exception = null,
                CancellationToken cancellationToken = default)
                => Task.CompletedTask;
        }
    }
}
