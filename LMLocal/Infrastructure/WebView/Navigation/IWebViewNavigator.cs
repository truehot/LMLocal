using System.Threading;
using System.Threading.Tasks;

namespace LMLocal.Infrastructure.WebView.Navigation
{
    /// <summary>
    /// Loads the application page into the WebView2 and signals the page that the host bridge is ready by invoking <c>window.lmInit()</c> right after a successful load.
    /// </summary>
    internal interface IWebViewNavigator
    {
        /// <summary>
        /// Navigates to <paramref name="url"/> and waits for the page to load.
        /// </summary>
        Task LoadAsync(IWebView2Page page, string url, CancellationToken ct);
    }
}
