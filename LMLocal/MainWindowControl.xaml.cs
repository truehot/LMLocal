using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.WebView.Initialization;
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
        private bool _disposed;
        private readonly IWebViewInitializer _initializer;
        private readonly AsyncLazy<CoreWebView2> _webViewLazy;
        private readonly CancellationTokenSource _initCts = new CancellationTokenSource();

        internal MainWindowControl(IWebViewInitializer initializer)
        {
            this.InitializeComponent();
            _initializer = initializer ?? throw new ArgumentNullException(nameof(initializer));

            chatBrowser.GotFocus += OnGotFocus;

            _webViewLazy = new AsyncLazy<CoreWebView2>(
                () => _initializer.InitializeAsync(chatBrowser, _initCts.Token),
                ThreadHelper.JoinableTaskFactory);
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
            catch (ObjectDisposedException ex)
            {
                InternalLogger.Info($"WebView2 control was disposed before the page finished loading: {ex.Message}");
            }
            catch (Exception ex)
            {
                InternalLogger.Error("WebView2 initialization failed.", ex);
            }
        }

        private void OnGotFocus(object sender, RoutedEventArgs e)
        {
            if (chatBrowser?.CoreWebView2 != null && _webViewLazy.IsValueFactoryCompleted)
            {
                _ = chatBrowser.CoreWebView2.ExecuteScriptAsync("window.lmApi?.focusInput();");
            }
        }

        public void SendKeyToWebView(int keyCode, bool shift)
        {
            if (chatBrowser?.CoreWebView2 == null)
                return;

            string keyName = GetKeyName(keyCode);
            if (keyName == null)
                return;

            string script = $"window.lmApi?.moveCaret('{keyName}', {(shift ? "true" : "false")});";
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
            catch (OperationCanceledException)
            {
                InternalLogger.Info("InjectTextIntoInputAsync skipped: the window is closing/disposed.");
                return;
            }
            catch (ObjectDisposedException)
            {
                InternalLogger.Info("InjectTextIntoInputAsync skipped: the window is closing/disposed.");
                return;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("WebView2 initialization failed.", ex);
                return;
            }

            string escaped = JsonConvert.SerializeObject(markdownText);
            string script = $"window.lmApi.setInputText({escaped});";
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
            catch (OperationCanceledException)
            {
                InternalLogger.Info("InjectTextAndSendAsync skipped: the window is closing/disposed.");
                return;
            }
            catch (ObjectDisposedException)
            {
                InternalLogger.Info("InjectTextAndSendAsync skipped: the window is closing/disposed.");
                return;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("WebView2 initialization failed.", ex);
                return;
            }

            string escaped = JsonConvert.SerializeObject(markdownText);
            string escapedTabId = instructionTabId != null ? JsonConvert.SerializeObject(instructionTabId) : "null";

            string script = $"window.lmApi.injectAndSend({escaped}, {escapedTabId});";
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
            chatBrowser.GotFocus -= OnGotFocus;

            try { _initCts?.Cancel(); }
            catch (ObjectDisposedException ex)
            {
                InternalLogger.Info($"WebView2 init token already disposed during window dispose: {ex.Message}");
            }

            _initCts?.Dispose();

            if (chatBrowser == null)
            {
                GC.SuppressFinalize(this);
                return;
            }

            if (_webViewLazy.IsValueFactoryCompleted)
            {
                DisposeWebView();
            }
            else
            {
                InternalLogger.Info("WebView2 initialization not completed; control will be disposed after it settles.");
                _ = DisposeWebViewAfterInitSettlesAsync();
            }

            GC.SuppressFinalize(this);
        }

        private void DisposeWebView()
        {
            if (chatBrowser == null)
                return;

            try
            {
                chatBrowser.Dispose();
            }
            catch (Exception ex)
            {
                InternalLogger.Error("Failed to dispose WebView2 control.", ex);
            }
        }

        private async Task DisposeWebViewAfterInitSettlesAsync()
        {
            try
            {
                await _webViewLazy.GetValueAsync(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                InternalLogger.Info("WebView2 init settled with cancellation; disposing control.");
            }
            catch (Exception ex)
            {
                InternalLogger.Info($"WebView2 init settled with an error while disposing; disposing control anyway: {ex.Message}");
            }

            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                DisposeWebView();
            }
            catch (Exception ex)
            {
                InternalLogger.Error("Failed to dispose WebView2 control.", ex);
            }
        }
    }
}
