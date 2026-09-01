using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.Mcp;
using LMLocal.Infrastructure.Tooling.Mcp.Abstractions;
using LMLocal.Infrastructure.WebView.Environment;
using LMLocal.Infrastructure.WebView.Hosting;
using LMLocal.Infrastructure.WebView.Navigation;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace LMLocal.Infrastructure.WebView.Initialization
{
    /// <summary>
    /// Production implementation of <IWebViewInitializer>.
    /// </summary>
    internal sealed class WebViewInitializer : IWebViewInitializer
    {
        private readonly ISettingsManager _settingsManager;
        private readonly IToolsConfigManager _toolsConfigManager;
        private readonly IMcpToolManager _mcpToolManager;
        private readonly IMcpConfigManager _mcpConfigManager;
        private readonly IWebViewEnvironmentProvider _environmentProvider;
        private readonly IWebViewHostObjectRegistrar _hostObjectRegistrar;
        private readonly IWebViewNavigator _navigator;

        public WebViewInitializer(
            ISettingsManager settingsManager,
            IToolsConfigManager toolsConfigManager,
            IMcpToolManager mcpToolManager,
            IMcpConfigManager mcpConfigManager,
            IWebViewEnvironmentProvider environmentProvider,
            IWebViewHostObjectRegistrar hostObjectRegistrar,
            IWebViewNavigator navigator)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _toolsConfigManager = toolsConfigManager ?? throw new ArgumentNullException(nameof(toolsConfigManager));
            _mcpToolManager = mcpToolManager ?? throw new ArgumentNullException(nameof(mcpToolManager));
            _mcpConfigManager = mcpConfigManager ?? throw new ArgumentNullException(nameof(mcpConfigManager));
            _environmentProvider = environmentProvider ?? throw new ArgumentNullException(nameof(environmentProvider));
            _hostObjectRegistrar = hostObjectRegistrar ?? throw new ArgumentNullException(nameof(hostObjectRegistrar));
            _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        }

        public async Task<CoreWebView2> InitializeAsync(WebView2 chatBrowser, CancellationToken ct)
        {
            if (chatBrowser == null)
                throw new ArgumentNullException(nameof(chatBrowser));

            ct.ThrowIfCancellationRequested();

            // --- Load settings & tools config ---------------------------------
            await _settingsManager.LoadAsync().ConfigureAwait(false);
            await _toolsConfigManager.LoadAsync().ConfigureAwait(false);

            // --- MCP background init (fire-and-forget with ct) ----------------
            _ = Task.Run(async () =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var config = await _mcpConfigManager.GetAsync(CancellationToken.None).ConfigureAwait(false);
                    if (config != null)
                    {
                        await _mcpToolManager.RefreshServersAsync(config, CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    InternalLogger.Info("MCP background init cancelled.");
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"MCP background init failed: {ex.Message}");
                }
            }, ct);

            // --- Switch to UI thread (WebView2 requires it) -------------------
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            ct.ThrowIfCancellationRequested();

            // --- Shared WebView2 environment ---------------------------------
            CoreWebView2Environment sharedEnvironment = await _environmentProvider.GetEnvironmentAsync(ct);

            ct.ThrowIfCancellationRequested();
            await chatBrowser.EnsureCoreWebView2Async(sharedEnvironment);

            // --- Configure CoreWebView2 --------------------------------------
            string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string resourcesPath = Path.Combine(assemblyDir, "Resources");
            chatBrowser.CoreWebView2.SetVirtualHostNameToFolderMapping(
                _settingsManager.VirtualHostName,
                resourcesPath,
                CoreWebView2HostResourceAccessKind.DenyCors
            );

#if !DEBUG
            chatBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            chatBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
#endif

            // --- Register host objects (controllers) --------------
            _hostObjectRegistrar.Register(
                new WebViewHostObjectSink(chatBrowser.CoreWebView2),
                async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    chatBrowser.Focus();
                    await chatBrowser.CoreWebView2.ExecuteScriptAsync("window.lmApi?.focusInput();");
                });

            // --- Navigate, wait for load and fire lmInit ---------------------
            await _navigator.LoadAsync(
                new WebView2Page(chatBrowser.CoreWebView2),
                $"https://{_settingsManager.VirtualHostName}/app.html",
                ct);

            return chatBrowser.CoreWebView2;
        }
    }
}
