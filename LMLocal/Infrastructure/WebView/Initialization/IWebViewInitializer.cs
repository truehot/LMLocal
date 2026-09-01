using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace LMLocal.Infrastructure.WebView.Initialization
{
    /// <summary>
    /// Orchestrates the full WebView2 initialization for the main tool window: settings/tools load, background MCP refresh, shared environment,
    /// </summary>
    internal interface IWebViewInitializer
    {
        /// <summary>
        /// Initializes <paramref name="chatBrowser"/> and returns its <see cref="CoreWebView2"/>.
        /// </summary>
        Task<CoreWebView2> InitializeAsync(WebView2 chatBrowser, CancellationToken ct);
    }
}
