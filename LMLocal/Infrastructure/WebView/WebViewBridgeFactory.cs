using System;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSession;
using LMLocal.Application.ModelsList;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Instructions;
using LMLocal.Infrastructure.Providers;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using LMLocal.Infrastructure.Tooling.Mcp;
using LMLocal.Infrastructure.Tooling.Mcp.Abstractions;

namespace LMLocal.Infrastructure.WebView
{
    /// <summary>
    /// Factory for creating WebViewBridge instances with all dependencies injected.
    /// </summary>
    public interface IWebViewBridgeFactory
    {
        IWebViewBridge CreateBridge(Microsoft.Web.WebView2.Core.CoreWebView2 coreWebView2);
    }

    internal class WebViewBridgeFactory : IWebViewBridgeFactory
    {
        private readonly ISettingsManager _settingsManager;
        private readonly IModelsListService _modelsListService;
        private readonly IInstructionsManager _instructionsManager;
        private readonly IMcpConfigManager _mcpConfigManager;
        private readonly IMcpToolManager _mcpToolManager;
        private readonly IProvidersConfigManager _providersConfigManager;
        private readonly IBuiltInVsToolProvider _builtInVsToolProvider;
        private readonly IToolsConfigManager _toolsConfigManager;
        private readonly IGetActiveDocument _activeDocumentTool;
        private readonly ISessionManager _sessionManager;
        private readonly IActiveModelContext _activeModelContext;
        private readonly IChatHistoryManager _chatHistoryManager;
        private readonly IHistoryCompactor _historyCompactor;
        private readonly ISnapshotManager _snapshotManager;

        public WebViewBridgeFactory(
            ISettingsManager settingsManager,
            IModelsListService modelsListService,
            IInstructionsManager instructionsManager,
            IMcpConfigManager mcpConfigManager,
            IMcpToolManager mcpToolManager,
            IProvidersConfigManager providersConfigManager,
            IBuiltInVsToolProvider builtInVsToolProvider,
            IToolsConfigManager toolsConfigManager,
            IGetActiveDocument activeDocumentTool,
            ISessionManager sessionManager,
            IActiveModelContext activeModelContext,
            IChatHistoryManager chatHistoryManager,
            IHistoryCompactor historyCompactor,
            ISnapshotManager snapshotManager)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _modelsListService = modelsListService ?? throw new ArgumentNullException(nameof(modelsListService));
            _instructionsManager = instructionsManager ?? throw new ArgumentNullException(nameof(instructionsManager));
            _mcpConfigManager = mcpConfigManager ?? throw new ArgumentNullException(nameof(mcpConfigManager));
            _mcpToolManager = mcpToolManager ?? throw new ArgumentNullException(nameof(mcpToolManager));
            _providersConfigManager = providersConfigManager ?? throw new ArgumentNullException(nameof(providersConfigManager));
            _builtInVsToolProvider = builtInVsToolProvider ?? throw new ArgumentNullException(nameof(builtInVsToolProvider));
            _toolsConfigManager = toolsConfigManager ?? throw new ArgumentNullException(nameof(toolsConfigManager));
            _activeDocumentTool = activeDocumentTool ?? throw new ArgumentNullException(nameof(activeDocumentTool));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _activeModelContext = activeModelContext ?? throw new ArgumentNullException(nameof(activeModelContext));
            _chatHistoryManager = chatHistoryManager ?? throw new ArgumentNullException(nameof(chatHistoryManager));
            _historyCompactor = historyCompactor ?? throw new ArgumentNullException(nameof(historyCompactor));
            _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
        }

        public IWebViewBridge CreateBridge(Microsoft.Web.WebView2.Core.CoreWebView2 coreWebView2)
        {
            if (coreWebView2 == null)
                throw new ArgumentNullException(nameof(coreWebView2));

            var scriptExecutor = new WebViewScriptExecutor(coreWebView2);

            return new WebViewBridge(_settingsManager, _modelsListService, scriptExecutor, _instructionsManager, _mcpConfigManager, _mcpToolManager, _providersConfigManager, _builtInVsToolProvider, _toolsConfigManager, _activeDocumentTool, _sessionManager, _activeModelContext, _chatHistoryManager, _historyCompactor, _snapshotManager);
        }
    }
}

