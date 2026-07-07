using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSession;
using LMLocal.Application.ModelsList;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Instructions;
using LMLocal.Infrastructure.LlmApi.Responses;
using LMLocal.Infrastructure.Providers;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using LMLocal.Infrastructure.Tooling.Mcp;
using LMLocal.Infrastructure.Tooling.Mcp.Abstractions;
using LMLocal.Infrastructure.WebView;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class WebViewBridgeTests
    {
        [Test]
            public async Task ListModelsAsync_SetsActiveModelAndReturnsJson()
            {
                var mockSettings = new Mock<ISettingsManager>();
                mockSettings.SetupGet(s => s.RequestTimeoutSeconds).Returns(1);

                var unified = new UnifiedListModelsResponse();
                unified.Models.Add(new UnifiedModelInfo { Id = "model1", Name = "Model One" });

                var mockModelsListService = new Mock<IModelsListService>();
                mockModelsListService.Setup(o => o.ListModelsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string id, CancellationToken ct) =>
                    {
                        if (id == "model1")
                        {
                            unified.HasActiveModel = true;
                            unified.ActiveModel = unified.Models[0];
                            unified.Models[0].IsLoaded = true;
                        }
                        return Task.FromResult(unified);
                    });

                var mockScript = new Mock<IWebViewScriptExecutor>();
                var mockActiveDoc = new Mock<IGetActiveDocument>();
                var mockSession = new Mock<ISessionManager>();
                var mockActiveModelContext = new Mock<IActiveModelContext>();
                mockActiveModelContext.SetupGet(a => a.CurrentModelId).Returns("model1");
                var mockHistoryManager = new Mock<IChatHistoryManager>();
                var mockCompactor = new Mock<IHistoryCompactor>();

                var mockInstructions = new Mock<IInstructionsManager>();
                var mockMcp = new Mock<IMcpConfigManager>();
                var mockMcpToolManager = new Mock<IMcpToolManager>();
                var mockProvidersConfigManager = new Mock<IProvidersConfigManager>();
                var mockBuiltInVsToolProvider = new Mock<IBuiltInVsToolProvider>();
                var mockToolsConfigManager = new Mock<IToolsConfigManager>();
                var mockSnapshotManager = new Mock<ISnapshotManager>();
                var bridge = new WebViewBridge(mockSettings.Object, mockModelsListService.Object, mockScript.Object, mockInstructions.Object, mockMcp.Object, mockMcpToolManager.Object, mockProvidersConfigManager.Object, mockBuiltInVsToolProvider.Object, mockToolsConfigManager.Object, mockActiveDoc.Object, mockSession.Object, mockActiveModelContext.Object, mockHistoryManager.Object, mockCompactor.Object, mockSnapshotManager.Object);

                var json = await bridge.ListModelsAsync().ConfigureAwait(false);

                Assert.That(json, Is.Not.Null.And.Not.Empty);

                var parsed = JsonConvert.DeserializeObject<UnifiedListModelsResponse>(json);
                Assert.That(parsed, Is.Not.Null);
                Assert.That(parsed.HasActiveModel, Is.True);
                Assert.That(parsed.ActiveModel, Is.Not.Null);
                Assert.That(parsed.ActiveModel.Id, Is.EqualTo("model1"));
            }

            [Test]
            public async Task ListModelsAsync_WhenAdapterThrows_ReturnsErrorJson()
            {
                var mockSettings = new Mock<ISettingsManager>();
                mockSettings.SetupGet(s => s.RequestTimeoutSeconds).Returns(1);

                var mockModelsListService = new Mock<IModelsListService>();
                mockModelsListService.Setup(o => o.ListModelsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("boom"));

                var mockHistoryManager = new Mock<IChatHistoryManager>();
                var mockCompactor = new Mock<IHistoryCompactor>();
                var mockInstructions = new Mock<IInstructionsManager>();
                var mockMcp = new Mock<IMcpConfigManager>();
                var mockMcpToolManager = new Mock<IMcpToolManager>();
                var mockProvidersConfigManager = new Mock<IProvidersConfigManager>();
                var mockBuiltInVsToolProvider = new Mock<IBuiltInVsToolProvider>();
                var mockToolsConfigManager = new Mock<IToolsConfigManager>();
                var mockSnapshotManager = new Mock<ISnapshotManager>();
                var bridge = new WebViewBridge(mockSettings.Object, mockModelsListService.Object, new Mock<IWebViewScriptExecutor>().Object, mockInstructions.Object, mockMcp.Object, mockMcpToolManager.Object, mockProvidersConfigManager.Object, mockBuiltInVsToolProvider.Object, mockToolsConfigManager.Object, new Mock<IGetActiveDocument>().Object, new Mock<ISessionManager>().Object, new Mock<IActiveModelContext>().Object, mockHistoryManager.Object, mockCompactor.Object, mockSnapshotManager.Object);

                var json = await bridge.ListModelsAsync().ConfigureAwait(false);

                Assert.That(json, Does.Contain("Failed to list models"));
            }

            [Test]
        public async Task ExecutePromptAsync_InvalidOrEmptyRequest_DoesNotStartSession()
        {
            var mockSettings = new Mock<ISettingsManager>();
            var mockModelsListService = new Mock<IModelsListService>();
            var mockScript = new Mock<IWebViewScriptExecutor>();
            var mockActiveDoc = new Mock<IGetActiveDocument>();
            var mockSession = new Mock<ISessionManager>();
            var mockActiveModelContext = new Mock<IActiveModelContext>();
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();

            var mockInstructions = new Mock<IInstructionsManager>();
            var mockMcp = new Mock<IMcpConfigManager>();
            var mockMcpToolManager = new Mock<IMcpToolManager>();
            var mockProvidersConfigManager = new Mock<IProvidersConfigManager>();
            var mockBuiltInVsToolProvider = new Mock<IBuiltInVsToolProvider>();
            var mockToolsConfigManager = new Mock<IToolsConfigManager>();
            var mockSnapshotManager = new Mock<ISnapshotManager>();
            var bridge = new WebViewBridge(mockSettings.Object, mockModelsListService.Object, mockScript.Object, mockInstructions.Object, mockMcp.Object, mockMcpToolManager.Object, mockProvidersConfigManager.Object, mockBuiltInVsToolProvider.Object, mockToolsConfigManager.Object, mockActiveDoc.Object, mockSession.Object, mockActiveModelContext.Object, mockHistoryManager.Object, mockCompactor.Object, mockSnapshotManager.Object);

            await bridge.ExecutePromptAsync(null).ConfigureAwait(false);
            await bridge.ExecutePromptAsync("").ConfigureAwait(false);

            await bridge.ExecutePromptAsync("null").ConfigureAwait(false);

            mockSession.Verify(s => s.TryStartSessionAsync(It.IsAny<GenerateStreamContext>(), It.IsAny<Func<WebView2ScriptMessage, Task>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ExecutePromptAsync_ValidRequest_IncludesContentAndStartsSession()
        {
            var mockSettings = new Mock<ISettingsManager>();
            var mockModelsListService = new Mock<IModelsListService>();
            var mockScript = new Mock<IWebViewScriptExecutor>();
            var mockActiveDoc = new Mock<IGetActiveDocument>();
            var mockSession = new Mock<ISessionManager>();
            var mockActiveModelContext = new Mock<IActiveModelContext>();
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();

            mockActiveDoc.Setup(a => a.GetContentAsync()).ReturnsAsync("file content");

            GenerateStreamContext capturedContext = null;
            mockSession.Setup(s => s.TryStartSessionAsync(It.IsAny<GenerateStreamContext>(), It.IsAny<Func<WebView2ScriptMessage, Task>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .Callback<GenerateStreamContext, Func<WebView2ScriptMessage, Task>, CancellationToken>((ctx, onMsg, ct) => capturedContext = ctx);

            var mockInstructions = new Mock<IInstructionsManager>();
            var mockMcp = new Mock<IMcpConfigManager>();
            var mockMcpToolManager = new Mock<IMcpToolManager>();
            var mockProvidersConfigManager = new Mock<IProvidersConfigManager>();
            var mockBuiltInVsToolProvider = new Mock<IBuiltInVsToolProvider>();
            var mockToolsConfigManager = new Mock<IToolsConfigManager>();
            var mockSnapshotManager = new Mock<ISnapshotManager>();
            var bridge = new WebViewBridge(mockSettings.Object, mockModelsListService.Object, mockScript.Object, mockInstructions.Object, mockMcp.Object, mockMcpToolManager.Object, mockProvidersConfigManager.Object, mockBuiltInVsToolProvider.Object, mockToolsConfigManager.Object, mockActiveDoc.Object, mockSession.Object, mockActiveModelContext.Object, mockHistoryManager.Object, mockCompactor.Object, mockSnapshotManager.Object);

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
        public async Task SetActiveModelAsync_ValidAndInvalidBehaviors()
        {
            var mockSettings = new Mock<ISettingsManager>();
            var mockModelsListService = new Mock<IModelsListService>();
            var mockScript = new Mock<IWebViewScriptExecutor>();
            var mockActiveDoc = new Mock<IGetActiveDocument>();
            var mockSession = new Mock<ISessionManager>();
            var mockActiveModelContext = new Mock<IActiveModelContext>();
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();

            var mockInstructions = new Mock<IInstructionsManager>();
            var mockMcp = new Mock<IMcpConfigManager>();
            var mockMcpToolManager = new Mock<IMcpToolManager>();
            var mockProvidersConfigManager = new Mock<IProvidersConfigManager>();
            var mockBuiltInVsToolProvider = new Mock<IBuiltInVsToolProvider>();
            var mockToolsConfigManager = new Mock<IToolsConfigManager>();
            var mockSnapshotManager = new Mock<ISnapshotManager>();
            var bridge = new WebViewBridge(mockSettings.Object, mockModelsListService.Object, mockScript.Object, mockInstructions.Object, mockMcp.Object, mockMcpToolManager.Object, mockProvidersConfigManager.Object, mockBuiltInVsToolProvider.Object, mockToolsConfigManager.Object, mockActiveDoc.Object, mockSession.Object, mockActiveModelContext.Object, mockHistoryManager.Object, mockCompactor.Object, mockSnapshotManager.Object);

            var res1 = await bridge.SetActiveModelAsync(null, 0).ConfigureAwait(false);
            Assert.That(res1, Is.False);

            var res2 = await bridge.SetActiveModelAsync("modelX", 0).ConfigureAwait(false);
            Assert.That(res2, Is.True);
            mockActiveModelContext.Verify(a => a.SetActiveModel("modelX", 16384), Times.Once);

            var res3 = await bridge.SetActiveModelAsync("modelY", 2000).ConfigureAwait(false);
            Assert.That(res3, Is.True);
            mockActiveModelContext.Verify(a => a.SetActiveModel("modelY", 2000), Times.Once);
        }

        [Test]
        public async Task ResetAndStop_InvokeSessionManager()
        {
            var mockSettings = new Mock<ISettingsManager>();
            var mockModelsListService = new Mock<IModelsListService>();
            var mockScript = new Mock<IWebViewScriptExecutor>();
            var mockActiveDoc = new Mock<IGetActiveDocument>();
            var mockSession = new Mock<ISessionManager>();
            var mockActiveModelContext = new Mock<IActiveModelContext>();
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();

            mockSession.SetupGet(s => s.IsSessionRunning).Returns(false);

            var mockInstructions = new Mock<IInstructionsManager>();
            var mockMcp = new Mock<IMcpConfigManager>();
            var mockMcpToolManager = new Mock<IMcpToolManager>();
            var mockProvidersConfigManager = new Mock<IProvidersConfigManager>();
            var mockBuiltInVsToolProvider = new Mock<IBuiltInVsToolProvider>();
            var mockToolsConfigManager = new Mock<IToolsConfigManager>();
            var mockSnapshotManager = new Mock<ISnapshotManager>();
            var bridge = new WebViewBridge(mockSettings.Object, mockModelsListService.Object, mockScript.Object, mockInstructions.Object, mockMcp.Object, mockMcpToolManager.Object, mockProvidersConfigManager.Object, mockBuiltInVsToolProvider.Object, mockToolsConfigManager.Object, mockActiveDoc.Object, mockSession.Object, mockActiveModelContext.Object, mockHistoryManager.Object, mockCompactor.Object, mockSnapshotManager.Object);

            var reset = await bridge.ResetHistoryWithActionAsync("none").ConfigureAwait(false);
            Assert.That(reset, Is.True);
            mockHistoryManager.Verify(h => h.Clear(), Times.Once);

            await bridge.StopExecutionAsync().ConfigureAwait(false);
            mockSession.Verify(s => s.TryStopSession(), Times.Once);
        }

        [Test]
        public async Task ResetHistoryWithAction_WhenSessionRunning_ReturnsFalse()
        {
            var mockSettings = new Mock<ISettingsManager>();
            var mockModelsListService = new Mock<IModelsListService>();
            var mockScript = new Mock<IWebViewScriptExecutor>();
            var mockActiveDoc = new Mock<IGetActiveDocument>();
            var mockSession = new Mock<ISessionManager>();
            var mockActiveModelContext = new Mock<IActiveModelContext>();
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();

            mockSession.SetupGet(s => s.IsSessionRunning).Returns(true);

            var mockInstructions = new Mock<IInstructionsManager>();
            var mockMcp = new Mock<IMcpConfigManager>();
            var mockMcpToolManager = new Mock<IMcpToolManager>();
            var mockProvidersConfigManager = new Mock<IProvidersConfigManager>();
            var mockBuiltInVsToolProvider = new Mock<IBuiltInVsToolProvider>();
            var mockToolsConfigManager = new Mock<IToolsConfigManager>();
            var mockSnapshotManager = new Mock<ISnapshotManager>();
            var bridge = new WebViewBridge(mockSettings.Object, mockModelsListService.Object, mockScript.Object, mockInstructions.Object, mockMcp.Object, mockMcpToolManager.Object, mockProvidersConfigManager.Object, mockBuiltInVsToolProvider.Object, mockToolsConfigManager.Object, mockActiveDoc.Object, mockSession.Object, mockActiveModelContext.Object, mockHistoryManager.Object, mockCompactor.Object, mockSnapshotManager.Object);

            var reset = await bridge.ResetHistoryWithActionAsync("none").ConfigureAwait(false);
            Assert.That(reset, Is.False);
            mockHistoryManager.Verify(h => h.Clear(), Times.Never);
        }

        [Test]
        public async Task ResetHistoryWithAction_LastPrompt_CallsMoveLastExchange()
        {
            var mockSettings = new Mock<ISettingsManager>();
            var mockModelsListService = new Mock<IModelsListService>();
            var mockScript = new Mock<IWebViewScriptExecutor>();
            var mockActiveDoc = new Mock<IGetActiveDocument>();
            var mockSession = new Mock<ISessionManager>();
            var mockActiveModelContext = new Mock<IActiveModelContext>();
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();

            mockSession.SetupGet(s => s.IsSessionRunning).Returns(false);

            var mockInstructions = new Mock<IInstructionsManager>();
            var mockMcp = new Mock<IMcpConfigManager>();
            var mockMcpToolManager = new Mock<IMcpToolManager>();
            var mockProvidersConfigManager = new Mock<IProvidersConfigManager>();
            var mockBuiltInVsToolProvider = new Mock<IBuiltInVsToolProvider>();
            var mockToolsConfigManager = new Mock<IToolsConfigManager>();
            var mockSnapshotManager = new Mock<ISnapshotManager>();
            var bridge = new WebViewBridge(mockSettings.Object, mockModelsListService.Object, mockScript.Object, mockInstructions.Object, mockMcp.Object, mockMcpToolManager.Object, mockProvidersConfigManager.Object, mockBuiltInVsToolProvider.Object, mockToolsConfigManager.Object, mockActiveDoc.Object, mockSession.Object, mockActiveModelContext.Object, mockHistoryManager.Object, mockCompactor.Object, mockSnapshotManager.Object);

            var reset = await bridge.ResetHistoryWithActionAsync("last-prompt").ConfigureAwait(false);
            Assert.That(reset, Is.True);
            mockHistoryManager.Verify(h => h.MoveLastExchangeToNewSession(), Times.Once);
            mockHistoryManager.Verify(h => h.Clear(), Times.Never);
        }

        [Test]
        public async Task GetProvidersAsync_ReturnsProviderTypes()
        {
            var mockSettings = new Mock<ISettingsManager>();
            var mockModelsListService = new Mock<IModelsListService>();
            var mockScript = new Mock<IWebViewScriptExecutor>();
            var mockActiveDoc = new Mock<IGetActiveDocument>();
            var mockSession = new Mock<ISessionManager>();
            var mockActiveModelContext = new Mock<IActiveModelContext>();
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();

            var mockInstructions = new Mock<IInstructionsManager>();
            var mockMcp = new Mock<IMcpConfigManager>();
            var mockMcpToolManager = new Mock<IMcpToolManager>();
            var mockBuiltInVsToolProvider = new Mock<IBuiltInVsToolProvider>();
            var mockToolsConfigManager = new Mock<IToolsConfigManager>();
            var mockSnapshotManager = new Mock<ISnapshotManager>();

            var defaultConfig = new ProvidersConfigFile
            {
                DefaultProviders = new System.Collections.Generic.List<CustomProvider>
                {
                    new CustomProvider { Id = 0, ProviderName = "Test", ProviderType = "lmstudio" }
                },
                Providers = new System.Collections.Generic.List<CustomProvider>()
            };

            var mockProvidersConfigManager = new Mock<IProvidersConfigManager>();
            mockProvidersConfigManager
                .Setup(m => m.GetAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(defaultConfig);

            var bridge = new WebViewBridge(mockSettings.Object, mockModelsListService.Object, mockScript.Object,
                mockInstructions.Object, mockMcp.Object, mockMcpToolManager.Object,
                mockProvidersConfigManager.Object, mockBuiltInVsToolProvider.Object,
                mockToolsConfigManager.Object, mockActiveDoc.Object, mockSession.Object,
                mockActiveModelContext.Object, mockHistoryManager.Object, mockCompactor.Object, mockSnapshotManager.Object);

            var json = await bridge.GetProvidersAsync().ConfigureAwait(false);

            Assert.That(json, Is.Not.Null.And.Not.Empty);
            Assert.That(json, Does.Contain("\"providerTypes\""));
        }

        [Test]
        public async Task SummarizeAndCompactAsync_SessionRunning_ReturnsFalse()
        {
            var mockSettings = new Mock<ISettingsManager>();
            var mockModelsListService = new Mock<IModelsListService>();
            var mockScript = new Mock<IWebViewScriptExecutor>();
            var mockActiveDoc = new Mock<IGetActiveDocument>();
            var mockSession = new Mock<ISessionManager>();
            var mockActiveModelContext = new Mock<IActiveModelContext>();
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();

            mockSession.SetupGet(s => s.IsSessionRunning).Returns(true);

            var bridge = new WebViewBridge(mockSettings.Object, mockModelsListService.Object, mockScript.Object,
                new Mock<IInstructionsManager>().Object, new Mock<IMcpConfigManager>().Object,
                new Mock<IMcpToolManager>().Object, new Mock<IProvidersConfigManager>().Object,
                new Mock<IBuiltInVsToolProvider>().Object, new Mock<IToolsConfigManager>().Object,
                mockActiveDoc.Object, mockSession.Object, mockActiveModelContext.Object,
                mockHistoryManager.Object, mockCompactor.Object, new Mock<ISnapshotManager>().Object);

            var result = await bridge.SummarizeAndCompactAsync("model1").ConfigureAwait(false);
            Assert.That(result, Is.False);
        }

        [Test]
        public async Task SummarizeAndCompactAsync_NoModel_ReturnsFalse()
        {
            var mockSettings = new Mock<ISettingsManager>();
            var mockSession = new Mock<ISessionManager>();
            mockSession.SetupGet(s => s.IsSessionRunning).Returns(false);

            var bridge = new WebViewBridge(mockSettings.Object, new Mock<IModelsListService>().Object,
                new Mock<IWebViewScriptExecutor>().Object, new Mock<IInstructionsManager>().Object,
                new Mock<IMcpConfigManager>().Object, new Mock<IMcpToolManager>().Object,
                new Mock<IProvidersConfigManager>().Object, new Mock<IBuiltInVsToolProvider>().Object,
                new Mock<IToolsConfigManager>().Object, new Mock<IGetActiveDocument>().Object,
                mockSession.Object, new Mock<IActiveModelContext>().Object,
                new Mock<IChatHistoryManager>().Object, new Mock<IHistoryCompactor>().Object,
                new Mock<ISnapshotManager>().Object);

            var result = await bridge.SummarizeAndCompactAsync(null).ConfigureAwait(false);
            Assert.That(result, Is.False);
        }

        [Test]
        public async Task SummarizeAndCompactAsync_EmptyHistory_ReturnsTrue()
        {
            var mockSettings = new Mock<ISettingsManager>();
            var mockSession = new Mock<ISessionManager>();
            mockSession.SetupGet(s => s.IsSessionRunning).Returns(false);

            var mockHistoryManager = new Mock<IChatHistoryManager>();
            mockHistoryManager.Setup(h => h.GetHistoryCopy()).Returns(new List<ChatMessage>());

            var bridge = new WebViewBridge(mockSettings.Object, new Mock<IModelsListService>().Object,
                new Mock<IWebViewScriptExecutor>().Object, new Mock<IInstructionsManager>().Object,
                new Mock<IMcpConfigManager>().Object, new Mock<IMcpToolManager>().Object,
                new Mock<IProvidersConfigManager>().Object, new Mock<IBuiltInVsToolProvider>().Object,
                new Mock<IToolsConfigManager>().Object, new Mock<IGetActiveDocument>().Object,
                mockSession.Object, new Mock<IActiveModelContext>().Object,
                mockHistoryManager.Object, new Mock<IHistoryCompactor>().Object,
                new Mock<ISnapshotManager>().Object);

            var result = await bridge.SummarizeAndCompactAsync("model1").ConfigureAwait(false);
            Assert.That(result, Is.True);
            mockHistoryManager.Verify(h => h.Clear(), Times.Once);
        }

        [Test]
        public async Task SummarizeAndCompactAsync_Success_AddsPair()
        {
            var mockSettings = new Mock<ISettingsManager>();
            var mockSession = new Mock<ISessionManager>();
            mockSession.SetupGet(s => s.IsSessionRunning).Returns(false);

            var mockHistoryManager = new Mock<IChatHistoryManager>();
            mockHistoryManager.Setup(h => h.GetHistoryCopy()).Returns(new List<ChatMessage>
            {
                new ChatMessage("user", "hello"),
                new ChatMessage("assistant", "hi")
            });

            var mockCompactor = new Mock<IHistoryCompactor>();
            mockCompactor.Setup(c => c.SummarizeAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), "model1", It.IsAny<CancellationToken>()))
                .ReturnsAsync("compacted summary");

            var bridge = new WebViewBridge(mockSettings.Object, new Mock<IModelsListService>().Object,
                new Mock<IWebViewScriptExecutor>().Object, new Mock<IInstructionsManager>().Object,
                new Mock<IMcpConfigManager>().Object, new Mock<IMcpToolManager>().Object,
                new Mock<IProvidersConfigManager>().Object, new Mock<IBuiltInVsToolProvider>().Object,
                new Mock<IToolsConfigManager>().Object, new Mock<IGetActiveDocument>().Object,
                mockSession.Object, new Mock<IActiveModelContext>().Object,
                mockHistoryManager.Object, mockCompactor.Object,
                new Mock<ISnapshotManager>().Object);

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