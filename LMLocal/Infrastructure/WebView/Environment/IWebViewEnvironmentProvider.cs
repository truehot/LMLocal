using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace LMLocal.Infrastructure.WebView.Environment
{
    /// <summary>
    /// Provides the process-wide <see cref="CoreWebView2Environment"/>.
    /// </summary>
    internal interface IWebViewEnvironmentProvider
    {
        /// <summary>
        /// Returns the cached shared environment, creating it lazily on first call.
        /// </summary>
        Task<CoreWebView2Environment> GetEnvironmentAsync(CancellationToken ct);
    }
}
