using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSession;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using LMLocal.Infrastructure.WebView;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class WebViewBridgeTests
    {
        [Test]
        public async Task ExecutePromptAsync_InvalidOrEmptyRequest_DoesNotStartSession()
        {
            var mockScript = new Mock<IWebViewScriptExecutor>();
            var mockActiveDoc = new Mock<IGetActiveDocument>();
            var mockSession = new Mock<ISessionManager>();
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();
            var mockSnapshotManager = new Mock<ISnapshotManager>();

            var bridge = new WebViewBridge(mockScript.Object, mockActiveDoc.Object, mockSession.Object, mockHistoryManager.Object, mockCompactor.Object, mockSnapshotManager.Object);

            await bridge.ExecutePromptAsync(null).ConfigureAwait(false);
            await bridge.ExecutePromptAsync("").ConfigureAwait(false);

            await bridge.ExecutePromptAsync("null").ConfigureAwait(false);

            mockSession.Verify(s => s.TryStartSessionAsync(It.IsAny<GenerateStreamContext>(), It.IsAny<Func<WebView2ScriptMessage, Task>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ExecutePromptAsync_ValidRequest_IncludesContentAndStartsSession()
        {
            var mockScript = new Mock<IWebViewScriptExecutor>();
            var mockActiveDoc = new Mock<IGetActiveDocument>();
            var mockSession = new Mock<ISessionManager>();
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();
            var mockSnapshotManager = new Mock<ISnapshotManager>();

            mockActiveDoc.Setup(a => a.GetContentAsync()).ReturnsAsync("file content");

            GenerateStreamContext capturedContext = null;
            mockSession.Setup(s => s.TryStartSessionAsync(It.IsAny<GenerateStreamContext>(), It.IsAny<Func<WebView2ScriptMessage, Task>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .Callback<GenerateStreamContext, Func<WebView2ScriptMessage, Task>, CancellationToken>((ctx, onMsg, ct) => capturedContext = ctx);

            var bridge = new WebViewBridge(mockScript.Object, mockActiveDoc.Object, mockSession.Object, mockHistoryManager.Object, mockCompactor.Object, mockSnapshotManager.Object);

            var req = new LMLocal.Models.ExecutePromptRequest { Prompt = "hello", IncludeContent = true, AdditionalPrompt = "add", ModelId = "m1" };
            var json = req.ToJson();

            await bridge.ExecutePromptAsync(json).ConfigureAwait(false);

            mockActiveDoc.Verify(a => a.GetContentAsync(), Times.Once);
            mockSession.Verify(s => s.TryStartSessionAsync(It.IsAny<GenerateStreamContext>(), It.IsAny<Func<WebView2ScriptMessage, Task>>(), It.IsAny<CancellationToken>()), Times.Once);

            Assert.That(capturedContext, Is.Not.Null);
            Assert.That(capturedContext.Prompt, Is.EqualTo("hello"));
            Assert.That(capturedContext.ActiveDocumentContent, Is.EqualTo("file content"));
            Assert.That(capturedContext.AdditionalPrompt, Is.EqualTo("add"));
            Assert.That(capturedContext.ModelId, Is.EqualTo("m1"));
        }

        [Test]
        public async Task ResetAndStop_InvokeSessionManager()
        {
            var mockScript = new Mock<IWebViewScriptExecutor>();
            var mockActiveDoc = new Mock<IGetActiveDocument>();
            var mockSession = new Mock<ISessionManager>();
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();
            var mockSnapshotManager = new Mock<ISnapshotManager>();

            mockSession.SetupGet(s => s.IsSessionRunning).Returns(false);

            var bridge = new WebViewBridge(mockScript.Object, mockActiveDoc.Object, mockSession.Object, mockHistoryManager.Object, mockCompactor.Object, mockSnapshotManager.Object);

            var reset = await bridge.ResetHistoryWithActionAsync("none").ConfigureAwait(false);
            Assert.That(reset, Is.True);
            mockHistoryManager.Verify(h => h.Clear(), Times.Once);

            await bridge.StopExecutionAsync().ConfigureAwait(false);
            mockSession.Verify(s => s.TryStopSession(), Times.Once);
        }

        [Test]
        public async Task ResetHistoryWithAction_WhenSessionRunning_ReturnsFalse()
        {
            var mockScript = new Mock<IWebViewScriptExecutor>();
            var mockActiveDoc = new Mock<IGetActiveDocument>();
            var mockSession = new Mock<ISessionManager>();
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();
            var mockSnapshotManager = new Mock<ISnapshotManager>();

            mockSession.SetupGet(s => s.IsSessionRunning).Returns(true);

            var bridge = new WebViewBridge(mockScript.Object, mockActiveDoc.Object, mockSession.Object, mockHistoryManager.Object, mockCompactor.Object, mockSnapshotManager.Object);

            var reset = await bridge.ResetHistoryWithActionAsync("none").ConfigureAwait(false);
            Assert.That(reset, Is.False);
            mockHistoryManager.Verify(h => h.Clear(), Times.Never);
        }

        [Test]
        public async Task ResetHistoryWithAction_LastPrompt_CallsMoveLastExchange()
        {
            var mockScript = new Mock<IWebViewScriptExecutor>();
            var mockActiveDoc = new Mock<IGetActiveDocument>();
            var mockSession = new Mock<ISessionManager>();
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();
            var mockSnapshotManager = new Mock<ISnapshotManager>();

            mockSession.SetupGet(s => s.IsSessionRunning).Returns(false);

            var bridge = new WebViewBridge(mockScript.Object, mockActiveDoc.Object, mockSession.Object, mockHistoryManager.Object, mockCompactor.Object, mockSnapshotManager.Object);

            var reset = await bridge.ResetHistoryWithActionAsync("last-prompt").ConfigureAwait(false);
            Assert.That(reset, Is.True);
            mockHistoryManager.Verify(h => h.MoveLastExchangeToNewSession(), Times.Once);
            mockHistoryManager.Verify(h => h.Clear(), Times.Never);
        }

        [Test]
        public async Task ResetHistoryWithAction_LastExchange_CallsConsolidateLastExchange()
        {
            var mockScript = new Mock<IWebViewScriptExecutor>();
            var mockActiveDoc = new Mock<IGetActiveDocument>();
            var mockSession = new Mock<ISessionManager>();
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();
            var mockSnapshotManager = new Mock<ISnapshotManager>();

            mockSession.SetupGet(s => s.IsSessionRunning).Returns(false);

            var bridge = new WebViewBridge(mockScript.Object, mockActiveDoc.Object, mockSession.Object, mockHistoryManager.Object, mockCompactor.Object, mockSnapshotManager.Object);

            var reset = await bridge.ResetHistoryWithActionAsync("last-exchange").ConfigureAwait(false);
            Assert.That(reset, Is.True);
            mockHistoryManager.Verify(h => h.ConsolidateLastExchange(), Times.Once);
            mockHistoryManager.Verify(h => h.Clear(), Times.Never);
        }

        [Test]
        public async Task SummarizeAndCompactAsync_SessionRunning_ReturnsFalse()
        {
            var mockScript = new Mock<IWebViewScriptExecutor>();
            var mockActiveDoc = new Mock<IGetActiveDocument>();
            var mockSession = new Mock<ISessionManager>();
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();
            var mockSnapshotManager = new Mock<ISnapshotManager>();

            mockSession.SetupGet(s => s.IsSessionRunning).Returns(true);

            var bridge = new WebViewBridge(mockScript.Object, mockActiveDoc.Object, mockSession.Object, mockHistoryManager.Object, mockCompactor.Object, mockSnapshotManager.Object);

            var result = await bridge.SummarizeAndCompactAsync("model1").ConfigureAwait(false);
            Assert.That(result, Is.False);
        }

        [Test]
        public async Task SummarizeAndCompactAsync_NoModel_ReturnsFalse()
        {
            var mockScript = new Mock<IWebViewScriptExecutor>();
            var mockActiveDoc = new Mock<IGetActiveDocument>();
            var mockSession = new Mock<ISessionManager>();
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();
            var mockSnapshotManager = new Mock<ISnapshotManager>();

            mockSession.SetupGet(s => s.IsSessionRunning).Returns(false);

            var bridge = new WebViewBridge(mockScript.Object, mockActiveDoc.Object, mockSession.Object, mockHistoryManager.Object, mockCompactor.Object, mockSnapshotManager.Object);

            var result = await bridge.SummarizeAndCompactAsync(null).ConfigureAwait(false);
            Assert.That(result, Is.False);
        }

        [Test]
        public async Task SummarizeAndCompactAsync_EmptyHistory_ReturnsTrue()
        {
            var mockScript = new Mock<IWebViewScriptExecutor>();
            var mockActiveDoc = new Mock<IGetActiveDocument>();
            var mockSession = new Mock<ISessionManager>();
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();
            var mockSnapshotManager = new Mock<ISnapshotManager>();

            mockSession.SetupGet(s => s.IsSessionRunning).Returns(false);
            mockHistoryManager.Setup(h => h.GetHistoryCopy()).Returns(new List<ChatMessage>());

            var bridge = new WebViewBridge(mockScript.Object, mockActiveDoc.Object, mockSession.Object, mockHistoryManager.Object, mockCompactor.Object, mockSnapshotManager.Object);

            var result = await bridge.SummarizeAndCompactAsync("model1").ConfigureAwait(false);
            Assert.That(result, Is.True);
            mockHistoryManager.Verify(h => h.Clear(), Times.Once);
        }

        [Test]
        public async Task SummarizeAndCompactAsync_Success_AddsPair()
        {
            var mockScript = new Mock<IWebViewScriptExecutor>();
            var mockActiveDoc = new Mock<IGetActiveDocument>();
            var mockSession = new Mock<ISessionManager>();
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();
            var mockSnapshotManager = new Mock<ISnapshotManager>();

            mockSession.SetupGet(s => s.IsSessionRunning).Returns(false);

            mockHistoryManager.Setup(h => h.GetHistoryCopy()).Returns(new List<ChatMessage>
            {
                new ChatMessage("user", "hello"),
                new ChatMessage("assistant", "hi")
            });

            mockCompactor.Setup(c => c.SummarizeAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), "model1", It.IsAny<CancellationToken>()))
                .ReturnsAsync("compacted summary");

            var bridge = new WebViewBridge(mockScript.Object, mockActiveDoc.Object, mockSession.Object, mockHistoryManager.Object, mockCompactor.Object, mockSnapshotManager.Object);

            var result = await bridge.SummarizeAndCompactAsync("model1").ConfigureAwait(false);
            Assert.That(result, Is.True);

            mockHistoryManager.Verify(h => h.Clear(), Times.Once);
            mockHistoryManager.Verify(h => h.AddUserMessage(
                It.Is<string>(s => s.Contains("Provide a brief summary")), null), Times.Once);
            mockHistoryManager.Verify(h => h.AddAssistantMessage(
                "compacted summary", null), Times.Once);
        }
    }
}
