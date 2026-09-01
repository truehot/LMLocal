using Microsoft.Web.WebView2.Core;

namespace LMLocal.Infrastructure.WebView.Hosting
{
    /// <summary>
    /// Abstraction over <see cref="CoreWebView2.AddHostObjectToScript"/> so that
    /// <see cref="IWebViewHostObjectRegistrar"/> can be unit-tested without a real WebView2 core.
    /// </summary>
    internal interface IWebViewHostObjectSink
    {
        /// <summary>
        /// The underlying WebView2 core. Used to create the JS bridge; null in unit tests.
        /// </summary>
        CoreWebView2 Core { get; }

        /// <summary>
        /// Exposes a managed object to the WebView2 page under the given name.
        /// </summary>
        void AddHostObject(string name, object obj);
    }
}
