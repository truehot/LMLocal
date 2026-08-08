using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using Microsoft.VisualStudio.Shell;

namespace LMLocal.Infrastructure.WebView.Controllers
{
    /// <summary>
    /// Bridge controller for host-level operations: clipboard access and window focus.
    /// Exposed to the WebView2 page as the "host" host object.
    /// </summary>
    public interface IWebViewHostController
    {
        /// <summary>Copies the given text to the clipboard.</summary>
        Task<bool> CopyToClipboardAsync(string text);

        /// <summary>Restores OS-level focus to the WebView2 host, then puts the caret in the user input textarea.</summary>
        Task FocusAsync();

        /// <summary>
        /// Configures the delegate that restores focus to the WebView2 host control.
        /// </summary>
        void ConfigureFocus(Func<Task> focusAction);
    }

    [ComVisible(true)]
    public class WebViewHostController : IWebViewHostController
    {
        private Func<Task> _focusAction;

        /// <summary>
        /// Configures the delegate that restores focus to the WebView2 host control.
        /// </summary>
        public void ConfigureFocus(Func<Task> focusAction)
        {
            _focusAction = focusAction ?? throw new ArgumentNullException(nameof(focusAction));
        }

        /// <summary>
        /// Copies the specified text to the clipboard.
        /// </summary>
        public async Task<bool> CopyToClipboardAsync(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) return false;
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                System.Windows.Clipboard.SetText(text);
                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("CopyToClipboardAsync failed", ex);
                return false;
            }
        }

        /// <summary>
        /// Restores focus to the WebView2 host and the user input textarea.
        /// </summary>
        public async Task FocusAsync()
        {
            if (_focusAction == null)
            {
                InternalLogger.Warn("WebViewHostController: FocusAsync called but no focus action is configured.");
                return;
            }

            try
            {
                await _focusAction().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InternalLogger.Error("FocusAsync failed", ex);
            }
        }
    }
}
