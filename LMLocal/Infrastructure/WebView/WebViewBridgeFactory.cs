using System;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSession;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using LMLocal.Infrastructure.WebView.Messaging;

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
        private readonly IGetActiveDocument _activeDocumentTool;
        private readonly ISessionManager _sessionManager;
        private readonly IChatHistoryService _chatHistoryService;
        private readonly ISnapshotManager _snapshotManager;

        public WebViewBridgeFactory(
            IGetActiveDocument activeDocumentTool,
            ISessionManager sessionManager,
            IChatHistoryService chatHistoryService,
            ISnapshotManager snapshotManager)
        {
            _activeDocumentTool = activeDocumentTool ?? throw new ArgumentNullException(nameof(activeDocumentTool));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _chatHistoryService = chatHistoryService ?? throw new ArgumentNullException(nameof(chatHistoryService));
            _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
        }

        public IWebViewBridge CreateBridge(Microsoft.Web.WebView2.Core.CoreWebView2 coreWebView2)
        {
            if (coreWebView2 == null)
                throw new ArgumentNullException(nameof(coreWebView2));

            var scriptExecutor = new WebViewScriptExecutor(coreWebView2);

            return new WebViewBridge(scriptExecutor, _activeDocumentTool, _sessionManager, _chatHistoryService, _snapshotManager);
        }
    }
}
