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
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.Mcp;
using LMLocal.Infrastructure.Tooling.Mcp.Abstractions;
using LMLocal.Infrastructure.WebView;
using LMLocal.Infrastructure.WebView.Controllers;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;

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
        private readonly AsyncLazy<CoreWebView2> _webViewLazy;
        private readonly CancellationTokenSource _initCts = new CancellationTokenSource();

        public MainWindowControl()
        {
            this.InitializeComponent();
            this.Focusable = true;

            _webViewLazy = new AsyncLazy<CoreWebView2>(
                () => InitializeWebViewAsync(_initCts.Token),
                ThreadHelper.JoinableTaskFactory);

            this.Loaded += OnControlLoaded;
        }

        private async Task<CoreWebView2> InitializeWebViewAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // --- Load settings & tools config ---------------------------------
            var settingsManager = ServiceConfiguration.GetService<ISettingsManager>();
            await settingsManager.LoadAsync();

            var toolsConfigManager = ServiceConfiguration.GetService<IToolsConfigManager>();
            await toolsConfigManager.LoadAsync();

            // --- MCP background init (fire-and-forget with ct) ----------------
            _ = Task.Run(async () =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var mcpToolManager = ServiceConfiguration.GetService<IMcpToolManager>();
                    var configManager = ServiceConfiguration.GetService<IMcpConfigManager>();
                    var config = await configManager.GetAsync(CancellationToken.None);
                    if (config != null)
                    {
                        await mcpToolManager.RefreshServersAsync(config, CancellationToken.None);
                    }
                }
                catch (OperationCanceledException) { /* Dispose */ }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"MCP background init failed: {ex.Message}");
                }
            }, ct);

            var webViewBridgeFactory = ServiceConfiguration.GetService<IWebViewBridgeFactory>();

            // --- Switch to UI thread (WebView2 requires it) -------------------
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            ct.ThrowIfCancellationRequested();

            // --- Shared WebView2 environment ---------------------------------
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

            ct.ThrowIfCancellationRequested();
            await chatBrowser.EnsureCoreWebView2Async(sharedEnvironment);

            // --- Configure CoreWebView2 --------------------------------------
            string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string resourcesPath = Path.Combine(assemblyDir, "Resources");
            chatBrowser.CoreWebView2.SetVirtualHostNameToFolderMapping(
                settingsManager.VirtualHostName,
                resourcesPath,
                CoreWebView2HostResourceAccessKind.Allow
            );

            var bridge = webViewBridgeFactory.CreateBridge(chatBrowser.CoreWebView2);
            chatBrowser.CoreWebView2.AddHostObjectToScript("bridge", bridge);

            chatBrowser.CoreWebView2.AddHostObjectToScript("instructions", ServiceConfiguration.GetService<IInstructionsController>());
            chatBrowser.CoreWebView2.AddHostObjectToScript("providers", ServiceConfiguration.GetService<IProvidersController>());
            chatBrowser.CoreWebView2.AddHostObjectToScript("tools", ServiceConfiguration.GetService<IToolsController>());
            chatBrowser.CoreWebView2.AddHostObjectToScript("settings", ServiceConfiguration.GetService<ISettingsController>());
            chatBrowser.CoreWebView2.AddHostObjectToScript("mcp", ServiceConfiguration.GetService<IMcpController>());
            chatBrowser.CoreWebView2.AddHostObjectToScript("models", ServiceConfiguration.GetService<IModelsController>());
            chatBrowser.CoreWebView2.AddHostObjectToScript("autocompletions", ServiceConfiguration.GetService<IAutocompletionsController>());
            chatBrowser.CoreWebView2.AddHostObjectToScript("chatSession", ServiceConfiguration.GetService<IChatSessionController>());

            chatBrowser.HorizontalAlignment = HorizontalAlignment.Stretch;
            chatBrowser.VerticalAlignment = VerticalAlignment.Stretch;

#if !DEBUG
            chatBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            chatBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
#endif
            chatBrowser.CoreWebView2.NavigationStarting += OnNavigationStarting;
            chatBrowser.GotFocus += onGotFocus;

            // --- Navigate and wait for page load -----------------------------
            var navTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnNavCompleted(object s, CoreWebView2NavigationCompletedEventArgs e)
            {
                chatBrowser.CoreWebView2.NavigationCompleted -= OnNavCompleted;
                if (e.IsSuccess)
                {
                    try { _ = chatBrowser.CoreWebView2.ExecuteScriptAsync("window.lmInit()"); }
                    catch (Exception ex) { InternalLogger.Warn($"lmInit script failed: {ex.Message}"); }
                }
                navTcs.TrySetResult(e.IsSuccess);
            }

            chatBrowser.CoreWebView2.NavigationCompleted += OnNavCompleted;

            // Cleanup the handler if cancellation is requested while waiting.
            CancellationTokenRegistration ctReg = ct.Register(() =>
            {
                chatBrowser.CoreWebView2.NavigationCompleted -= OnNavCompleted;
                navTcs.TrySetCanceled();
            });

            try
            {
                ct.ThrowIfCancellationRequested();
                string html = await GetHtmlFromResourceAsync(settingsManager.HtmlResourcePath).ConfigureAwait(false);
                chatBrowser.NavigateToString(html);

                if (!await navTcs.Task)
                    throw new InvalidOperationException("WebView2 navigation failed");
            }
            finally
            {
                ctReg.Dispose();
            }

            return chatBrowser.CoreWebView2;
        }

        private void OnControlLoaded(object sender, RoutedEventArgs e)
        {
            _ = OnControlLoadedAsync();
        }

        private async Task OnControlLoadedAsync()
        {
            try
            {
                await _webViewLazy.GetValueAsync(_initCts.Token);
            }
            catch (OperationCanceledException)
            {
                InternalLogger.Info("Dispose was called before the page finished loading.");
            }
            catch (Exception ex)
            {
                InternalLogger.Error("WebView2 initialization failed.", ex);
            }
        }

        private void onGotFocus(object sender, RoutedEventArgs e)
        {
            if (chatBrowser?.CoreWebView2 != null && _webViewLazy.IsValueFactoryCompleted)
            {
                _ = chatBrowser.CoreWebView2.ExecuteScriptAsync("document.getElementById('userInput')?.focus()");
            }
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (e.NavigationKind != CoreWebView2NavigationKind.NewDocument)
            {
                e.Cancel = true;
            }
        }

        private async Task<string> GetHtmlFromResourceAsync(string resourceName)
        {
            var uri = new Uri($"/{Assembly.GetExecutingAssembly().GetName().Name};component/{resourceName}", UriKind.Relative);
            var streamInfo = System.Windows.Application.GetResourceStream(uri) ?? throw new InvalidOperationException("Resource not found: " + resourceName);
            using (var reader = new StreamReader(streamInfo.Stream))
            {
                return await reader.ReadToEndAsync();
            }
        }
        public void SendKeyToWebView(int keyCode, bool shift)
        {
            if (chatBrowser?.CoreWebView2 == null)
                return;

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
        /// Injects markdown text into the chat input (userInput textarea) via JavaScript.
        /// </summary>
        public async Task InjectTextIntoInputAsync(string markdownText)
        {
            CoreWebView2 core;
            try
            {
                core = await _webViewLazy.GetValueAsync(_initCts.Token);
            }
            catch (Exception ex)
            {
                InternalLogger.Error("WebView2 initialization failed.", ex);
                return;
            }

            string escaped = JsonConvert.SerializeObject(markdownText);
            string script = $@"
(function() {{
    const el = document.getElementById('userInput');
    if (!el) return;
    el.value = {escaped};
    el.style.height = 'auto';
    el.style.height = el.scrollHeight + 'px';
    el.dispatchEvent(new Event('input', {{ bubbles: true }}));
    el.focus();
    el.setSelectionRange(el.value.length, el.value.length);
    const wrapper = el.closest('.input-wrapper');
    if (wrapper) wrapper.classList.add('expanded');
}})();
";
            await core.ExecuteScriptAsync(script);
        }

        /// <summary>
        /// Injects markdown text into the chat input and automatically clicks Send.
        /// </summary>
        public async Task InjectTextAndSendAsync(string markdownText, string instructionTabId = null)
        {
            CoreWebView2 core;
            try
            {
                core = await _webViewLazy.GetValueAsync(_initCts.Token);
            }
            catch (Exception ex)
            {
                InternalLogger.Error("WebView2 initialization failed.", ex);
                return;
            }

            string escaped = JsonConvert.SerializeObject(markdownText);
            string escapedTabId = instructionTabId != null ? JsonConvert.SerializeObject(instructionTabId) : "null";

            // JS execution: select instruction tab (if specified), inject text, resize, then click Send
            string script = $@"
(function() {{
    const tabId = {escapedTabId};
    if (tabId) {{
        const item = document.querySelector('.dropdown-item[data-value=""' + tabId + '""]');
        if (item) {{
            item.click();
        }}
    }}
    const el = document.getElementById('userInput');
    if (!el) return;
    el.value = {escaped};
    el.style.height = 'auto';
    el.style.height = el.scrollHeight + 'px';
    el.dispatchEvent(new Event('input', {{ bubbles: true }}));
    const wrapper = el.closest('.input-wrapper');
    if (wrapper) wrapper.classList.add('expanded');
    const btn = document.getElementById('mainBtn');
    if (btn) btn.click();
}})();
";
            await core.ExecuteScriptAsync(script);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            this.Loaded -= OnControlLoaded;

            try { _initCts?.Cancel(); } catch (ObjectDisposedException) { }

            _initCts?.Dispose();

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
