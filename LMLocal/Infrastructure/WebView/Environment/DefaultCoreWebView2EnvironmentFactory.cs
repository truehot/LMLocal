using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace LMLocal.Infrastructure.WebView.Environment
{
    /// <summary>
    /// Production implementation of <see cref="ICoreWebView2EnvironmentFactory"/>
    /// backed by <see cref="CoreWebView2Environment.CreateAsync(string, string)"/>.
    /// </summary>
    internal sealed class DefaultCoreWebView2EnvironmentFactory : ICoreWebView2EnvironmentFactory
    {
        public Task<CoreWebView2Environment> CreateAsync(string userDataFolder, CancellationToken ct)
            => CoreWebView2Environment.CreateAsync(null, userDataFolder);
    }
}
