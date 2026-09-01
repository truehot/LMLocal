using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace LMLocal.Infrastructure.WebView.Navigation
{
    /// <summary>
    /// Production implementation of <see cref="IWebView2Page"/> backed by <see cref="CoreWebView2"/>.
    /// </summary>
    internal sealed class WebView2Page : IWebView2Page
    {
        private readonly CoreWebView2 _core;

        public WebView2Page(CoreWebView2 core)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));

            _core.NavigationStarting += OnNavigationStarting;
            _core.FrameNavigationStarting += OnFrameNavigationStarting;
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

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (WebView2NavigationGuard.ShouldCancel(e.NavigationKind, isFrame: false))
            {
                e.Cancel = true;
            }
        }

        private void OnFrameNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (WebView2NavigationGuard.ShouldCancel(e.NavigationKind, isFrame: true))
            {
                e.Cancel = true;
            }
        }
    }
}
