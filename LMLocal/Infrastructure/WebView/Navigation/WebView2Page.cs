using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace LMLocal.Infrastructure.WebView.Navigation
{
    /// <summary>
    /// Production implementation of <see cref="IWebView2Page"/> backed by <see cref="CoreWebView2"/>.
    /// </summary>
    internal sealed class WebView2Page : IWebView2Page, IDisposable
    {
        private readonly CoreWebView2 _core;
        private readonly string _appUri;
        private readonly IExternalUriLauncher _externalUriLauncher;

        private bool _disposed;

        public WebView2Page(CoreWebView2 core, string appUri, IExternalUriLauncher externalUriLauncher)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));

            if (string.IsNullOrWhiteSpace(appUri))
                throw new ArgumentException("Application URI must not be empty.", nameof(appUri));

            _appUri = appUri;
            _externalUriLauncher = externalUriLauncher ?? throw new ArgumentNullException(nameof(externalUriLauncher));

            _core.NavigationStarting += OnNavigationStarting;
            _core.FrameNavigationStarting += OnFrameNavigationStarting;
            _core.NewWindowRequested += OnNewWindowRequested;
        }

        public async Task<bool> NavigateAndWaitAsync(string url, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnNavCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                _core.NavigationCompleted -= OnNavCompleted;
                tcs.TrySetResult(e.IsSuccess);
            }

            _core.NavigationCompleted += OnNavCompleted;
            try
            {
                _core.Navigate(url);

                using (ct.Register(() =>
                {
                    _core.NavigationCompleted -= OnNavCompleted;
                    tcs.TrySetCanceled();
                }))
                {
                    return await tcs.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                _core.NavigationCompleted -= OnNavCompleted;
            }
        }

        public Task<string> ExecuteScriptAsync(string script) => _core.ExecuteScriptAsync(script);

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _core.NavigationStarting -= OnNavigationStarting;
            _core.FrameNavigationStarting -= OnFrameNavigationStarting;
            _core.NewWindowRequested -= OnNewWindowRequested;
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            var decision = WebView2NavigationGuard.Decide(
                e.NavigationKind,
                e.Uri,
                isFrame: false,
                appUri: _appUri);

            if (decision == WebView2NavigationDecision.Allow)
                return;

            e.Cancel = true;

            if (decision == WebView2NavigationDecision.CancelAndOpenExternally)
            {
                OpenExternally(e.Uri);
            }
        }

        private void OnFrameNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (WebView2NavigationGuard.Decide(
                    e.NavigationKind,
                    e.Uri,
                    isFrame: true,
                    appUri: _appUri) != WebView2NavigationDecision.Allow)
            {
                e.Cancel = true;
            }
        }

        private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            if (!WebView2NavigationGuard.ShouldOpenExternally(e.Uri, _appUri))
                return;

            e.Handled = true;
            OpenExternally(e.Uri);
        }

        /// <summary>
        /// Hands the target over to the default system handler.
        /// </summary>
        private void OpenExternally(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
                return;

            // The launcher reports its own failures, so the result carries nothing for the policy here.
            _ = _externalUriLauncher.TryOpen(uri);
        }
    }
}


