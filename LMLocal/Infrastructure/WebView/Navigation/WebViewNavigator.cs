using System;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;

namespace LMLocal.Infrastructure.WebView.Navigation
{
    /// <summary>
    /// Orchestrates page loading: navigate, wait for completion and then fire <c>window.lmInit()</c> — all in one place, immediately after a successful load.
    /// </summary>
    internal sealed class WebViewNavigator : IWebViewNavigator
    {
        public async Task LoadAsync(IWebView2Page page, string url, CancellationToken ct)
        {
            if (page == null)
                throw new ArgumentNullException(nameof(page));
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("URL must not be empty.", nameof(url));

            ct.ThrowIfCancellationRequested();

            bool success = await page.NavigateAndWaitAsync(url, ct);
            if (!success)
                throw new InvalidOperationException("WebView2 navigation failed");

            // lmInit right after the page finished loading.
            try
            {
                _ = page.ExecuteScriptAsync("window.lmInit()");
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"lmInit script failed: {ex.Message}");
            }
        }
    }
}
