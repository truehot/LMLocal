using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.DependencyInjection;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.Tooling.Mcp;
using LMLocal.Infrastructure.Tooling.Mcp.Abstractions;
using LMLocal.Infrastructure.WebView;
using Microsoft.VisualStudio.Shell;
using Microsoft.Web.WebView2.Core;

namespace LMLocal
{
    /// <summary>
    /// Interaction logic for MainWindowControl.
    /// </summary>
    public partial class MainWindowControl : UserControl, IDisposable
    {
        private static CoreWebView2Environment sharedEnvironment;
        private static readonly SemaphoreSlim _envLock = new SemaphoreSlim(1, 1);

        private bool _disposed;
        private bool _webViewInitialized;
        private bool _webViewInitializing;

        public MainWindowControl()
        {
            this.InitializeComponent();
            this.Focusable = true;
            this.Loaded += OnControlLoaded;
        }

        private async Task OnControlLoadedAsync(object sender, RoutedEventArgs e)
        {
            if (_webViewInitialized || _webViewInitializing)
            {
                return;
            }

            _webViewInitializing = true;

            //load settings first
            var settingsManager = ServiceConfiguration.GetService<ISettingsManager>();
            await settingsManager.LoadAsync();

            //init MCP in background 
            _ = Task.Run(async () =>
            {
                try
                {
                    var mcpToolManager = ServiceConfiguration.GetService<IMcpToolManager>();
                    var configManager = ServiceConfiguration.GetService<IMcpConfigManager>();
                    var config = await configManager.GetAsync(CancellationToken.None);
                    if (config != null)
                    {
                        await mcpToolManager.RefreshServersAsync(config, CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"MCP background init failed: {ex.Message}");
                }
            });

            var webViewBridgeFactory = ServiceConfiguration.GetService<IWebViewBridgeFactory>();

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {

                await _envLock.WaitAsync();

                try
                {
                    if (sharedEnvironment == null)
                    {
                        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                        string userDataFolder = Path.Combine(localAppData, settingsManager.LocalAppDataFolder, settingsManager.WebViewUserDataFolder);
                        Directory.CreateDirectory(userDataFolder);
                        sharedEnvironment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                    }
                }
                finally
                {
                    _envLock.Release();
                }

                await chatBrowser.EnsureCoreWebView2Async(sharedEnvironment);

                string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string resourcesPath = Path.Combine(assemblyDir, "Resources");
                chatBrowser.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    settingsManager.VirtualHostName,
                    resourcesPath,
                    CoreWebView2HostResourceAccessKind.Allow
                );

                var bridge = webViewBridgeFactory.CreateBridge(chatBrowser.CoreWebView2);
                chatBrowser.CoreWebView2.AddHostObjectToScript("bridge", bridge);

                chatBrowser.HorizontalAlignment = HorizontalAlignment.Stretch;
                chatBrowser.VerticalAlignment = VerticalAlignment.Stretch;

#if !DEBUG
                chatBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                chatBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
#endif

                chatBrowser.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

                string html = await GetHtmlFromResourceAsync(settingsManager.HtmlResourcePath);

                chatBrowser.NavigateToString(html);

                _webViewInitialized = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"WebView2 Error: {ex.Message}");
            }
            finally
            {
                _webViewInitializing = false;
            }
        }

        private void OnControlLoaded(object sender, RoutedEventArgs e)
        {
            _ = OnControlLoadedAsync(sender, e);
        }

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                chatBrowser.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                _ = chatBrowser.CoreWebView2.ExecuteScriptAsync("window.lmInit()");
            }
        }

        private async Task<string> GetHtmlFromResourceAsync(string resourceName)
        {
            var uri = new Uri($"/{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name};component/{resourceName}", UriKind.Relative);
            var streamInfo = System.Windows.Application.GetResourceStream(uri) ?? throw new InvalidOperationException("Resource not found: " + resourceName);
            using (var reader = new System.IO.StreamReader(streamInfo.Stream))
            {
                return await reader.ReadToEndAsync();
            }
        }

        public void SendKeyToWebView(int keyCode, bool shift)
        {
            if (chatBrowser?.CoreWebView2 == null || !_webViewInitialized)
                return;

            chatBrowser.Focus();

            string keyName = GetKeyName(keyCode);
            if (keyName == null)
                return;

            string script = $@"
    (function() {{
        const el = document.activeElement;
        if (!el || !('selectionStart' in el)) return;

        const isShift = {(shift ? "true" : "false")};
        const key = '{keyName}';

        const caret = el.selectionDirection === 'backward'
            ? el.selectionStart
            : el.selectionEnd;

        const text = el.value;
        const textLength = text.length;

        let anchor = el.dataset.selAnchor
            ? parseInt(el.dataset.selAnchor, 10)
            : caret;

        function getLineStart(pos) {{
            const idx = text.lastIndexOf('\n', pos - 1);
            return idx === -1 ? 0 : idx + 1;
        }}

        function getLineEnd(pos) {{
            const idx = text.indexOf('\n', pos);
            return idx === -1 ? textLength : idx;
        }}

        let newPos = caret;

        if (key === 'Home') {{
            newPos = getLineStart(caret);
        }} else if (key === 'End') {{
            newPos = getLineEnd(caret);
        }} else if (key === 'ArrowLeft') {{
            if (caret > 0) newPos = caret - 1;
        }} else if (key === 'ArrowRight') {{
            if (caret < textLength) newPos = caret + 1;
        }} else {{
            return;
        }}

        if (!isShift) {{
            delete el.dataset.selAnchor;
            el.setSelectionRange(newPos, newPos, 'none');
        }} else {{
            if (!el.dataset.selAnchor) {{
                el.dataset.selAnchor = anchor;
            }}
            const start = Math.min(anchor, newPos);
            const end   = Math.max(anchor, newPos);
            const direction = anchor <= newPos ? 'forward' : 'backward';
            el.setSelectionRange(start, end, direction);
        }}
    }})();
";
            _ = chatBrowser.CoreWebView2.ExecuteScriptAsync(script);
        }

        private string GetKeyName(int keyCode)
        {
            if (keyCode == 0x24) return "Home";
            if (keyCode == 0x23) return "End";
            if (keyCode == 0x25) return "ArrowLeft";
            if (keyCode == 0x27) return "ArrowRight";
            return null;
        }

        /// <summary>
        /// Releases all resources used by the <see cref="MainWindowControl"/> instance.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            this.Loaded -= OnControlLoaded;

            if (chatBrowser != null)
            {
                try
                {
                    chatBrowser.Dispose();
                }
                catch (Exception ex)
                {
                    InternalLogger.Error("Failed to dispose WebView2 control.", ex);
                }
            }
            GC.SuppressFinalize(this);
        }
    }
}
