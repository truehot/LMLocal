using LMLocal.Core.Common;
using Microsoft.Web.WebView2.Core;
using System;
using System.Threading.Tasks;

namespace LMLocal.Infrastructure.WebView
{

    /// <summary>
    /// Executes JavaScript code or posts messages in a WebView2 control.
    /// </summary>
    internal interface IWebViewScriptExecutor
    {
        Task PostMessageAsJsonAsync(object message);
    }

    internal class WebViewScriptExecutor : IWebViewScriptExecutor
    {
        private readonly CoreWebView2 _coreWebView2;

        public WebViewScriptExecutor(CoreWebView2 coreWebView2)
        {
            _coreWebView2 = coreWebView2;
        }

        public async Task PostMessageAsJsonAsync(object message)
        {
            try
            {
                string json = message.ToJson();
                await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_coreWebView2 != null)
                {
                    _coreWebView2.PostWebMessageAsJson(json);
                }
                else
                {
                    InternalLogger.Warn("WebView2ScriptExecutor: CoreWebView2 is null, cannot post message.");
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error("WebView2ScriptExecutor.PostMessageAsJsonAsync failed", ex);
            }
        }
    }
}
