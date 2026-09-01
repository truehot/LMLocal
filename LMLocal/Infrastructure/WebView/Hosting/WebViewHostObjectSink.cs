using System;
using Microsoft.Web.WebView2.Core;

namespace LMLocal.Infrastructure.WebView.Hosting
{
    /// <summary>
    /// Production implementation of <see cref="IWebViewHostObjectSink"/> backed by
    /// <see cref="CoreWebView2.AddHostObjectToScript(string, object)"/>.
    /// </summary>
    internal sealed class WebViewHostObjectSink : IWebViewHostObjectSink
    {
        private readonly CoreWebView2 _core;

        public WebViewHostObjectSink(CoreWebView2 core)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
        }

        public CoreWebView2 Core => _core;

        public void AddHostObject(string name, object obj)
            => _core.AddHostObjectToScript(name, obj);
    }
}
