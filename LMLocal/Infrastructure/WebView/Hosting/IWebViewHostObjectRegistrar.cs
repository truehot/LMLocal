using System;
using System.Threading.Tasks;

namespace LMLocal.Infrastructure.WebView.Hosting
{
    /// <summary>
    /// Registers the fixed set of host objects exposed to the WebView2 page via <c>AddHostObjectToScript</c>.
    /// </summary>
    internal interface IWebViewHostObjectRegistrar
    {
        /// <summary>
        /// Registers all host objects (bridge + controllers) into the given sink.
        /// </summary>
        void Register(IWebViewHostObjectSink sink, Func<Task> focusAction);
    }
}
