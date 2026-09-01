using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace LMLocal.Infrastructure.WebView.Environment
{
    /// <summary>
    /// Creates <see cref="CoreWebView2Environment"/> instances for a given user data folder.
    /// </summary>
    internal interface ICoreWebView2EnvironmentFactory
    {
        /// <summary>
        /// Creates a WebView2 environment using the specified user data folder.
        /// </summary>
        Task<CoreWebView2Environment> CreateAsync(string userDataFolder, CancellationToken ct);
    }
}
