using System;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSession;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;

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
        private readonly IChatHistoryManager _chatHistoryManager;
        private readonly IHistoryCompactor _historyCompactor;
        private readonly ISnapshotManager _snapshotManager;

        public WebViewBridgeFactory(
            IGetActiveDocument activeDocumentTool,
            ISessionManager sessionManager,
            IChatHistoryManager chatHistoryManager,
            IHistoryCompactor historyCompactor,
            ISnapshotManager snapshotManager)
        {
            _activeDocumentTool = activeDocumentTool ?? throw new ArgumentNullException(nameof(activeDocumentTool));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _chatHistoryManager = chatHistoryManager ?? throw new ArgumentNullException(nameof(chatHistoryManager));
            _historyCompactor = historyCompactor ?? throw new ArgumentNullException(nameof(historyCompactor));
            _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
        }

        public IWebViewBridge CreateBridge(Microsoft.Web.WebView2.Core.CoreWebView2 coreWebView2)
        {
            if (coreWebView2 == null)
                throw new ArgumentNullException(nameof(coreWebView2));

            var scriptExecutor = new WebViewScriptExecutor(coreWebView2);

            return new WebViewBridge(scriptExecutor, _activeDocumentTool, _sessionManager, _chatHistoryManager, _historyCompactor, _snapshotManager);
        }
    }
}
