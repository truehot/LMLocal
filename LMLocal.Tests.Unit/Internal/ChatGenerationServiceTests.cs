using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Chat;
using LMLocal.Application.Autocompletions;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.LlmApi.Responses;
using LMLocal.Infrastructure.Settings;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class ChatGenerationServiceTests
    {
        private Mock<IOpenApiAdapter> _clientMock;
        private Mock<IChatHistoryManager> _historyMock;
        private Mock<IHistoryCompactor> _compactorMock;
        private Mock<ISettingsManager> _settingsMock;
        private Mock<IStreamProcessor> _mockProcessor;
        private Mock<IStreamProcessorFactory> _mockFactory;
        private ChatStreamService _service;

        [SetUp]
        public void SetUp()
        {
            _clientMock = new Mock<IOpenApiAdapter>();
            _historyMock = new Mock<IChatHistoryManager>();
            _compactorMock = new Mock<IHistoryCompactor>();
            _settingsMock = new Mock<ISettingsManager>();
            _settingsMock.Setup(s => s.WindowSeconds).Returns(5);
            _settingsMock.Setup(s => s.BatchIntervalMs).Returns(100);
            _settingsMock.Setup(s => s.Current).Returns(new AppSettings());

            var activeModelContext = new ActiveModelContext();
            _mockProcessor = new Mock<IStreamProcessor>();
            // Ensure the processor returns a non-null completion result to avoid NullReference during awaits in service code
            _mockProcessor.Setup(p => p.ProcessStreamAsync(It.IsAny<System.IO.Stream>(), It.IsAny<CancellationToken>(), It.IsAny<Func<TextStreamChunk, TokenGenerationStats, Task>>(), It.IsAny<int>()))
                .ReturnsAsync(new StreamCompletionResult { ContentResponse = "", WasCancelled = false });

            _mockFactory = new Mock<IStreamProcessorFactory>();
            _mockFactory.Setup(f => f.Create(It.IsAny<System.Threading.CancellationTokenSource>())).Returns(_mockProcessor.Object);

            _service = new ChatStreamService(_clientMock.Object, _historyMock.Object, _settingsMock.Object, _mockFactory.Object);
        }


        private class BlockingStream : Stream
        {
            private readonly SemaphoreSlim _sem = new SemaphoreSlim(0, 1);

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => 0;
            public override long Position { get => 0; set => throw new NotSupportedException(); }

            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                try
                {
                    await _sem.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }

                return 0;
            }

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public void ReleaseAndClose()
            {
                try { _sem.Release(); } catch { }
            }

            protected override void Dispose(bool disposing)
            {
                try { _sem.Release(); } catch { }
                base.Dispose(disposing);
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }

        private class FakeClient : IOpenApiAdapter
        {
            private readonly Stream _stream;
            public FakeClient(Stream s) => _stream = s;

            public Task<string> ListModelsRawAsync(string endpoint, string baseUrl, string apiKey, CancellationToken cancellationToken)
                => Task.FromResult(string.Empty);

            public Task<StreamingResponse> SendChatStreamingAsync(MessageContext messageContext, ModelContext modelContext, CancellationToken cancellationToken)
            {
                var response = new System.Net.Http.HttpResponseMessage();
                var request = new System.Net.Http.HttpRequestMessage();
                var content = new System.Net.Http.StringContent("", System.Text.Encoding.UTF8, "application/json");
                var streaming = new StreamingResponse(_stream, response, request, content);
                return Task.FromResult(streaming);
            }

            public Task<SendChatResponse> SendChatAsync(MessageContext messageContext, ModelContext modelContext, CancellationToken cancellationToken)
                => Task.FromResult<SendChatResponse>(null);


            public Task<string> SendCompletionAsync(CompletionContext context, CancellationToken cancellationToken)
                => Task.FromResult(string.Empty);
        }

        private class DummyHistory : IChatHistoryManager
        {
            public void AddUserMessage(string text, string activeDocumentContent = null) { }
            public void AddAssistantMessage(string text, IReadOnlyList<ToolCallRecord> toolCalls) { }
            public void Clear() { }
            public IReadOnlyList<ChatMessage> GetHistoryCopy() => new List<ChatMessage>();
            public bool ReplaceHistory(string summary, IEnumerable<ChatMessage> recent, int expectedSize) => true;
            public List<ChatMessage> BuildUserMessagesWithHistory(string additionalSystemPrompt = null) => new List<ChatMessage>();
            public void AddToolExecutionResultMessages(IEnumerable<ChatMessage> messages) { }
            public Task<List<ChatMessage>> LoadLastSessionAsync() => Task.FromResult(new List<ChatMessage>());
            public void EnsureHistoryNormalized() { }
            public void SetPendingAssistant(string text, IReadOnlyList<ToolCallRecord> toolCalls) { }
            public void MoveLastExchangeToNewSession() { }
            public void ConsolidateLastExchange() { }
        }

        private class DummyCompactor : IHistoryCompactor
        {
            public Task CompactIfNeededAsync(string modelId, CancellationToken token) => Task.CompletedTask;
            public bool NeedsCompaction() => false;
            public Task<string> SummarizeAsync(IReadOnlyList<ChatMessage> history, string modelId, CancellationToken ct) => Task.FromResult<string>(null);
        }

        [Test]
        public async Task GenerateStreamAsync_StopExecution_Completes()
        {
            var blocking = new BlockingStream();
            var client = new FakeClient(blocking);
            var history = new DummyHistory();

            var settingsMock = new Mock<ISettingsManager>();
            settingsMock.Setup(s => s.WindowSeconds).Returns(5);
            settingsMock.Setup(s => s.BatchIntervalMs).Returns(100);
            settingsMock.Setup(s => s.Current).Returns(new AppSettings());

            var mockProcessor = new Mock<IStreamProcessor>();
            // Ensure the processor returns a non-null completion result to avoid NullReference during awaits in service code
            mockProcessor.Setup(p => p.ProcessStreamAsync(It.IsAny<System.IO.Stream>(), It.IsAny<CancellationToken>(), It.IsAny<Func<TextStreamChunk, TokenGenerationStats, Task>>(), It.IsAny<int>()))
                .ReturnsAsync(new StreamCompletionResult { ContentResponse = "", WasCancelled = false });

            var mockFactory = new Mock<IStreamProcessorFactory>();
            mockFactory.Setup(f => f.Create(It.IsAny<System.Threading.CancellationTokenSource>())).Returns(mockProcessor.Object);
            var svc = new ChatStreamService(client, history, settingsMock.Object, mockFactory.Object);

            var context = new GenerateStreamContext
            {
                Prompt = "hi",
                ActiveDocumentContent = null,
                AdditionalPrompt = "stop_extra",
                ModelId = null
            };

            var cts = new CancellationTokenSource();
            var genTask = svc.GenerateStreamAsync(context, null, (c, s) => Task.CompletedTask, completion => Task.CompletedTask, cts.Token);

            await Task.Delay(50);

            cts.Cancel();

            blocking.ReleaseAndClose();

            var completed = await Task.WhenAny(genTask, Task.Delay(3000)) == genTask;
            Assert.That(completed, Is.True, "GenerateStreamAsync should complete after cancellation");
            Assert.That(genTask.IsFaulted, Is.False, genTask.Exception?.ToString());
        }

        [Test]
        public async Task GenerateStreamAsync_AddsUserMessage_AndAssistantMessage()
        {
            var messages = new List<ChatMessage>();
            _historyMock.Setup(h => h.BuildUserMessagesWithHistory(It.IsAny<string>())).Returns(messages);

            var mockStream = new MemoryStream();
            var mockResponse = new System.Net.Http.HttpResponseMessage();
            var mockRequest = new System.Net.Http.HttpRequestMessage();
            var mockContent = new System.Net.Http.StringContent("");
            var streamingResponse = new StreamingResponse(mockStream, mockResponse, mockRequest, mockContent);
            _clientMock.Setup(c => c.SendChatStreamingAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(streamingResponse);

            _historyMock.Setup(h => h.AddUserMessage(It.IsAny<string>(), null));
            _compactorMock.Setup(c => c.CompactIfNeededAsync(It.IsAny<string>(), CancellationToken.None)).Returns(Task.CompletedTask);

            Task onChunk(TextStreamChunk chunk, TokenGenerationStats t) => Task.CompletedTask;

            var context = new GenerateStreamContext
            {
                Prompt = "prompt",
                ActiveDocumentContent = null,
                AdditionalPrompt = "extra",
                ModelId = null
            };

            await _service.GenerateStreamAsync(context, null, onChunk, completion => Task.CompletedTask, CancellationToken.None);

            _historyMock.Verify(h => h.AddUserMessage("prompt", null), Times.Once);
        }

        [Test]
        public async Task GenerateStreamAsync_ToolRound_AddsToolResultsNotUserMessage()
        {
            var messages = new List<ChatMessage>();
            _historyMock.Setup(h => h.BuildUserMessagesWithHistory(It.IsAny<string>())).Returns(messages);

            var mockStream = new MemoryStream();
            var mockResponse = new System.Net.Http.HttpResponseMessage();
            var mockRequest = new System.Net.Http.HttpRequestMessage();
            var mockContent = new System.Net.Http.StringContent("");
            var streamingResponse = new StreamingResponse(mockStream, mockResponse, mockRequest, mockContent);
            _clientMock.Setup(c => c.SendChatStreamingAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(streamingResponse);

            var toolResults = new List<ToolResultMessage>
            {
                new ToolResultMessage { ToolCallId = "c1", Result = "result1" },
                new ToolResultMessage { ToolCallId = "c2", Result = "result2" }
            };

            var context = new GenerateStreamContext
            {
                Prompt = "ignored_for_tool_round",
                ActiveDocumentContent = null,
                AdditionalPrompt = null,
                ModelId = null
            };

            Task onChunk(TextStreamChunk chunk, TokenGenerationStats t) => Task.CompletedTask;
            await _service.GenerateStreamAsync(context, toolResults, onChunk, completion => Task.CompletedTask, CancellationToken.None);

            // Tool round: BuildUserMessagesWithHistory called (AdditionalPrompt = null)
            _historyMock.Verify(h => h.BuildUserMessagesWithHistory(null), Times.Once);

            // Tool round: AddToolExecutionResultMessages called (not AddUserMessage)
            _historyMock.Verify(h => h.AddToolExecutionResultMessages(It.IsAny<IEnumerable<ChatMessage>>()), Times.Once);
            _historyMock.Verify(h => h.AddUserMessage(It.IsAny<string>(), It.IsAny<string>()), Times.Never);

            // Assistant message saved with result
            _historyMock.Verify(h => h.AddAssistantMessage(It.IsAny<string>(), It.IsAny<IReadOnlyList<ToolCallRecord>>()), Times.Once);
        }

        [Test]
        public async Task GenerateStreamAsync_SavesAssistantWithToolCalls_WhenProcessorReturnsThem()
        {
            var messages = new List<ChatMessage>();
            _historyMock.Setup(h => h.BuildUserMessagesWithHistory(It.IsAny<string>())).Returns(messages);

            var mockStream = new MemoryStream();
            var mockResponse = new System.Net.Http.HttpResponseMessage();
            var mockRequest = new System.Net.Http.HttpRequestMessage();
            var mockContent = new System.Net.Http.StringContent("");
            var streamingResponse = new StreamingResponse(mockStream, mockResponse, mockRequest, mockContent);
            _clientMock.Setup(c => c.SendChatStreamingAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(streamingResponse);

            var toolCalls = new List<ToolCallRecord>
            {
                new ToolCallRecord { CallId = "c1", FunctionName = "find", ArgumentsJson = "{}" }
            };
            _mockProcessor.Setup(p => p.ProcessStreamAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>(),
                    It.IsAny<Func<TextStreamChunk, TokenGenerationStats, Task>>(), It.IsAny<int>()))
                .ReturnsAsync(new StreamCompletionResult { ContentResponse = "using tool", ToolCalls = toolCalls.AsReadOnly() });

            var context = new GenerateStreamContext { Prompt = "find files", ModelId = null };
            Task onChunk(TextStreamChunk chunk, TokenGenerationStats t) => Task.CompletedTask;

            await _service.GenerateStreamAsync(context, null, onChunk, completion => Task.CompletedTask, CancellationToken.None);

            // Assistant with tool calls is pending (not yet committed)
            _historyMock.Verify(h => h.SetPendingAssistant(
                "using tool",
                It.Is<IReadOnlyList<ToolCallRecord>>(tc => tc.Count == 1 && tc[0].FunctionName == "find")),
                Times.Once);
            _historyMock.Verify(h => h.AddAssistantMessage(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<ToolCallRecord>>()),
                Times.Never);
        }

        [Test]
        public async Task GenerateStreamAsync_WasCancelled_DoesNotSaveAssistantMessage()
        {
            var messages = new List<ChatMessage>();
            _historyMock.Setup(h => h.BuildUserMessagesWithHistory(It.IsAny<string>())).Returns(messages);

            var mockStream = new MemoryStream();
            var mockResponse = new System.Net.Http.HttpResponseMessage();
            var mockRequest = new System.Net.Http.HttpRequestMessage();
            var mockContent = new System.Net.Http.StringContent("");
            var streamingResponse = new StreamingResponse(mockStream, mockResponse, mockRequest, mockContent);
            _clientMock.Setup(c => c.SendChatStreamingAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(streamingResponse);

            // Processor returns WasCancelled = true — simulating user hitting Stop
            _mockProcessor.Setup(p => p.ProcessStreamAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>(),
                    It.IsAny<Func<TextStreamChunk, TokenGenerationStats, Task>>(), It.IsAny<int>()))
                .ReturnsAsync(new StreamCompletionResult
                {
                    ContentResponse = "partial response",
                    WasCancelled = true,
                    ToolCalls = new List<ToolCallRecord>
                    {
                        new ToolCallRecord { CallId = "c1", FunctionName = "search", ArgumentsJson = "{}" }
                    }.AsReadOnly()
                });

            var context = new GenerateStreamContext { Prompt = "query", ModelId = null };
            Task onChunk(TextStreamChunk chunk, TokenGenerationStats t) => Task.CompletedTask;

            var onCompleteCalled = false;
            await _service.GenerateStreamAsync(context, null, onChunk, completion =>
            {
                onCompleteCalled = true;
                Assert.That(completion.WasCancelled, Is.True);
                return Task.CompletedTask;
            }, CancellationToken.None);

            Assert.That(onCompleteCalled, Is.True, "onComplete should still be invoked even when cancelled");

            // Core assertion: assistant message must NOT be saved to history when cancelled
            _historyMock.Verify(h => h.AddAssistantMessage(
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<ToolCallRecord>>()),
                Times.Never);
            _historyMock.Verify(h => h.SetPendingAssistant(
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<ToolCallRecord>>()),
                Times.Never);

            // User message is added before the streaming call, so it's already in history
            _historyMock.Verify(h => h.AddUserMessage(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [Test]
        public async Task GenerateStreamAsync_WithError_DoesNotSaveAssistantMessage()
        {
            var messages = new List<ChatMessage>();
            _historyMock.Setup(h => h.BuildUserMessagesWithHistory(It.IsAny<string>())).Returns(messages);

            var mockStream = new MemoryStream();
            var mockResponse = new System.Net.Http.HttpResponseMessage();
            var mockRequest = new System.Net.Http.HttpRequestMessage();
            var mockContent = new System.Net.Http.StringContent("");
            var streamingResponse = new StreamingResponse(mockStream, mockResponse, mockRequest, mockContent);
            _clientMock.Setup(c => c.SendChatStreamingAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(streamingResponse);

            // Processor returns ErrorMessage — simulating SSE error event
            _mockProcessor.Setup(p => p.ProcessStreamAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>(),
                    It.IsAny<Func<TextStreamChunk, TokenGenerationStats, Task>>(), It.IsAny<int>()))
                .ReturnsAsync(new StreamCompletionResult
                {
                    ContentResponse = "partial content",
                    ErrorMessage = "Rate limit exceeded",
                    ErrorType = "server_error",
                    ErrorCode = "rate_limit_exceeded",
                    WasCancelled = false,
                    ToolCalls = new List<ToolCallRecord>().AsReadOnly()
                });

            var context = new GenerateStreamContext { Prompt = "query", ModelId = null };
            Task onChunk(TextStreamChunk chunk, TokenGenerationStats t) => Task.CompletedTask;

            var onCompleteCalled = false;
            await _service.GenerateStreamAsync(context, null, onChunk, completion =>
            {
                onCompleteCalled = true;
                Assert.That(completion.ErrorMessage, Is.EqualTo("Rate limit exceeded"));
                return Task.CompletedTask;
            }, CancellationToken.None);

            Assert.That(onCompleteCalled, Is.True);

            // Core assertion: assistant message must NOT be saved to history when error
            _historyMock.Verify(h => h.AddAssistantMessage(
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<ToolCallRecord>>()),
                Times.Never);
            _historyMock.Verify(h => h.SetPendingAssistant(
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<ToolCallRecord>>()),
                Times.Never);
        }
    }
}
