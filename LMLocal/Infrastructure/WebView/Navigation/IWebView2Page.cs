using System.Threading;
using System.Threading.Tasks;

namespace LMLocal.Infrastructure.WebView.Navigation
{
    /// <summary>
    /// Thin abstraction over <see cref="Microsoft.Web.WebView2.Core.CoreWebView2"/>
    /// covering only what page loading needs: navigation, waiting for completion and script execution.
    /// </summary>
    internal interface IWebView2Page
    {
        /// <summary>
        /// Navigates to <paramref name="url"/> and waits for the top-level navigation to complete.
        /// </summary>
        Task<bool> NavigateAndWaitAsync(string url, CancellationToken ct);

        /// <summary>
        /// Executes JavaScript in the page and returns its result as JSON-encoded string.
        /// </summary>
        Task<string> ExecuteScriptAsync(string script);
    }
}
