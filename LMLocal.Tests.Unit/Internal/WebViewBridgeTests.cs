using System;
using System.Collections.Generic;
using System.Linq;
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
using LMLocal.Infrastructure.WebView.Messaging;
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
            var mockHistoryService = new Mock<IChatHistoryService>();
            var mockSnapshotManager = new Mock<ISnapshotManager>();


            var bridge = new WebViewBridge(mockScript.Object, mockActiveDoc.Object, mockSession.Object, mockHistoryService.Object, mockSnapshotManager.Object);

            await bridge.ExecutePromptAsync(null).ConfigureAwait(false);
            await bridge.ExecutePromptAsync("").ConfigureAwait(false);

            await bridge.ExecutePromptAsync("null").ConfigureAwait(false);

            mockSession.Verify(s => s.TryStartSessionAsync(It.IsAny<GenerateStreamContext>(), It.IsAny<Func<WebView2ScriptMessage, Task>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ExecutePromptAsync_WithImages_PassesImagesToContext()
        {
            var mockScript = new Mock<IWebViewScriptExecutor>();
            var mockActiveDoc = new Mock<IGetActiveDocument>();
            var mockSession = new Mock<ISessionManager>();
            var mockHistoryService = new Mock<IChatHistoryService>();
            var mockSnapshotManager = new Mock<ISnapshotManager>();


            mockSession.Setup(s => s.TryStartSessionAsync(It.IsAny<GenerateStreamContext>(), It.IsAny<Func<WebView2ScriptMessage, Task>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var bridge = new WebViewBridge(mockScript.Object, mockActiveDoc.Object, mockSession.Object, mockHistoryService.Object, mockSnapshotManager.Object);

            await bridge.ExecutePromptAsync("{\"prompt\":\"describe\",\"images\":[\"data:image/png;base64,AAAA\"]}").ConfigureAwait(false);

            mockSession.Verify(s => s.TryStartSessionAsync(
                It.Is<GenerateStreamContext>(c => c.Images != null && c.Images.Count == 1 && c.Images[0] == "data:image/png;base64,AAAA"),
                It.IsAny<Func<WebView2ScriptMessage, Task>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task ExecutePromptAsync_WithTooManyImages_RejectsRequest()
        {
            var mockScript = new Mock<IWebViewScriptExecutor>();
            var mockActiveDoc = new Mock<IGetActiveDocument>();
            var mockSession = new Mock<ISessionManager>();
            var mockHistoryService = new Mock<IChatHistoryService>();
            var mockSnapshotManager = new Mock<ISnapshotManager>();


            var bridge = new WebViewBridge(mockScript.Object, mockActiveDoc.Object, mockSession.Object, mockHistoryService.Object, mockSnapshotManager.Object);

            var requestJson = "{\"prompt\":\"describe\",\"images\":[\"data:image/png;base64,AAAA\",\"data:image/png;base64,BBBB\"," +
                "\"data:image/png;base64,CCCC\",\"data:image/png;base64,DDDD\"]}";
            await bridge.ExecutePromptAsync(requestJson).ConfigureAwait(false);

            mockSession.Verify(s => s.TryStartSessionAsync(It.IsAny<GenerateStreamContext>(), It.IsAny<Func<WebView2ScriptMessage, Task>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ExecutePromptAsync_WithNonImageScheme_RejectsRequest()
        {
            var mockScript = new Mock<IWebViewScriptExecutor>();
            var mockActiveDoc = new Mock<IGetActiveDocument>();
            var mockSession = new Mock<ISessionManager>();
            var mockHistoryService = new Mock<IChatHistoryService>();
            var mockSnapshotManager = new Mock<ISnapshotManager>();


            var bridge = new WebViewBridge(mockScript.Object, mockActiveDoc.Object, mockSession.Object, mockHistoryService.Object, mockSnapshotManager.Object);

            await bridge.ExecutePromptAsync("{\"prompt\":\"describe\",\"images\":[\"data:text/plain;base64,AAAA\"]}").ConfigureAwait(false);

            mockSession.Verify(s => s.TryStartSessionAsync(It.IsAny<GenerateStreamContext>(), It.IsAny<Func<WebView2ScriptMessage, Task>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ExecutePromptAsync_ValidRequest_IncludesContentAndStartsSession()
        {
            var mockScript = new Mock<IWebViewScriptExecutor>();
            var mockActiveDoc = new Mock<IGetActiveDocument>();
            var mockSession = new Mock<ISessionManager>();
            var mockHistoryService = new Mock<IChatHistoryService>();
            var mockSnapshotManager = new Mock<ISnapshotManager>();


            mockActiveDoc.Setup(a => a.GetActiveDocumentInfoAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetActiveDocument.ActiveDocumentResponse
                {
                    FilePath = "src/Foo.cs",
                    Content = "file content",
                    Success = true
                });

            GenerateStreamContext capturedContext = null;
            mockSession.Setup(s => s.TryStartSessionAsync(It.IsAny<GenerateStreamContext>(), It.IsAny<Func<WebView2ScriptMessage, Task>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .Callback<GenerateStreamContext, Func<WebView2ScriptMessage, Task>, CancellationToken>((ctx, onMsg, ct) => capturedContext = ctx);

            var bridge = new WebViewBridge(mockScript.Object, mockActiveDoc.Object, mockSession.Object, mockHistoryService.Object, mockSnapshotManager.Object);

            var req = new LMLocal.Models.ExecutePromptRequest { Prompt = "hello", IncludeContent = true, AdditionalPrompt = "add", ModelId = "m1" };
            var json = req.ToJson();


            await bridge.ExecutePromptAsync(json).ConfigureAwait(false);


            mockActiveDoc.Verify(a => a.GetActiveDocumentInfoAsync(It.IsAny<CancellationToken>()), Times.Once);
            mockSession.Verify(s => s.TryStartSessionAsync(It.IsAny<GenerateStreamContext>(), It.IsAny<Func<WebView2ScriptMessage, Task>>(), It.IsAny<CancellationToken>()), Times.Once);

            Assert.That(capturedContext, Is.Not.Null);
            Assert.That(capturedContext.Prompt, Is.EqualTo("hello"));
            Assert.That(capturedContext.ActiveDocumentContent, Is.EqualTo("````csharp\n// file: src/Foo.cs\nfile content\n````"));
            Assert.That(capturedContext.AdditionalPrompt, Is.EqualTo("add"));
            Assert.That(capturedContext.ModelId, Is.EqualTo("m1"));
        }


        [Test]
        public async Task ResetAndStop_InvokeSessionManager()
        {
            var mockScript = new Mock<IWebViewScriptExecutor>();
            var mockActiveDoc = new Mock<IGetActiveDocument>();
            var mockSession = new Mock<ISessionManager>();
            var mockHistoryService = new Mock<IChatHistoryService>();
            var mockSnapshotManager = new Mock<ISnapshotManager>();


            mockSession.SetupGet(s => s.IsSessionRunning).Returns(false);
            mockHistoryService.Setup(h => h.ResetHistoryAsync("none")).ReturnsAsync(true);

            var bridge = new WebViewBridge(mockScript.Object, mockActiveDoc.Object, mockSession.Object, mockHistoryService.Object, mockSnapshotManager.Object);

            var reset = await bridge.ResetHistoryWithActionAsync("none").ConfigureAwait(false);
            Assert.That(reset, Is.True);
            mockHistoryService.Verify(h => h.ResetHistoryAsync("none"), Times.Once);

            await bridge.StopExecutionAsync().ConfigureAwait(false);
            mockSession.Verify(s => s.TryStopSession(), Times.Once);
        }


        [Test]
        public async Task ResetHistoryWithAction_DelegatesToHistoryService()
        {
            var mockScript = new Mock<IWebViewScriptExecutor>();
            var mockActiveDoc = new Mock<IGetActiveDocument>();
            var mockSession = new Mock<ISessionManager>();
            var mockHistoryService = new Mock<IChatHistoryService>();
            var mockSnapshotManager = new Mock<ISnapshotManager>();


            mockSession.SetupGet(s => s.IsSessionRunning).Returns(false);
            mockHistoryService.Setup(h => h.ResetHistoryAsync("last-prompt")).ReturnsAsync(true);

            var bridge = new WebViewBridge(mockScript.Object, mockActiveDoc.Object, mockSession.Object, mockHistoryService.Object, mockSnapshotManager.Object);

            var reset = await bridge.ResetHistoryWithActionAsync("last-prompt").ConfigureAwait(false);

            Assert.That(reset, Is.True);
            mockHistoryService.Verify(h => h.ResetHistoryAsync("last-prompt"), Times.Once);
        }


        [Test]
        public async Task SummarizeAndCompact_DelegatesToHistoryService()
        {
            var mockScript = new Mock<IWebViewScriptExecutor>();
            var mockActiveDoc = new Mock<IGetActiveDocument>();
            var mockSession = new Mock<ISessionManager>();
            var mockHistoryService = new Mock<IChatHistoryService>();
            var mockSnapshotManager = new Mock<ISnapshotManager>();


            mockHistoryService.Setup(h => h.SummarizeAndCompactAsync("model1")).ReturnsAsync(true);

            var bridge = new WebViewBridge(mockScript.Object, mockActiveDoc.Object, mockSession.Object, mockHistoryService.Object, mockSnapshotManager.Object);

            var result = await bridge.SummarizeAndCompactAsync("model1").ConfigureAwait(false);

            Assert.That(result, Is.True);
            mockHistoryService.Verify(h => h.SummarizeAndCompactAsync("model1"), Times.Once);
        }
    }
}
