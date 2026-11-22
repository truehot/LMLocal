using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.VisualStudio.Shell;

namespace LMLocal.Infrastructure.WebView.Controllers
{
    /// <summary>
    /// Bridge controller for host-level operations: clipboard access, window focus and the debugger stop banner.
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

        /// <summary>
        /// Collects the context of the current debugger stop, builds the prompt and sends it to the chat.
        /// </summary>
        Task<bool> ExplainDebugStopAsync();
    }

    [ComVisible(true)]
    public class WebViewHostController : IWebViewHostController
    {
        private readonly IDebuggerContextService _debuggerContextService;
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly Func<IChatInjectionService> _chatInjection;

        private Func<Task> _focusAction;

        internal WebViewHostController(
            IDebuggerContextService debuggerContextService,
            IVsDependencies vsDependencies,
            IPathResolver pathResolver,
            Func<IChatInjectionService> chatInjection)
        {
            _debuggerContextService = debuggerContextService ?? throw new ArgumentNullException(nameof(debuggerContextService));
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _chatInjection = chatInjection ?? throw new ArgumentNullException(nameof(chatInjection));
        }

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

        /// <summary>
        /// Reads the full stop context now, because none of it survives leaving Break Mode, and sends it.
        /// </summary>
        public async Task<bool> ExplainDebugStopAsync()
        {
            try
            {
                DebugStopContext context = await _debuggerContextService.CollectContextAsync();

                if (context?.Info == null)
                {
                    InternalLogger.Info("ExplainDebugStop: nothing was collected — Visual Studio already left Break Mode.");
                    return false;
                }

                string prompt = DebugStopPromptBuilder.Build(context, CreatePathConverter());
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    InternalLogger.Warn("ExplainDebugStop: the prompt is empty — nothing to send.");
                    return false;
                }

                await _chatInjection().InjectAndAutoSendAsync(prompt);
                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("ExplainDebugStopAsync failed", ex);
                return false;
            }
        }

        /// <summary>
        /// The prompt shows paths relative to the solution, the same way the code commands do.
        /// </summary>
        private Func<string, string> CreatePathConverter()
        {
            string solutionDirectory = null;

            try
            {
                solutionDirectory = _vsDependencies.GetSolutionDirectory();
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"ExplainDebugStop: cannot read the solution directory: {ex.Message}");
            }

            return fullPath => ToRelativePath(fullPath, solutionDirectory);
        }

        /// <summary>
        /// A path outside the solution — or an unknown solution directory — stays absolute.
        /// </summary>
        private string ToRelativePath(string fullPath, string solutionDirectory)
        {
            if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(solutionDirectory))
                return fullPath;

            if (_pathResolver.TryGetRelativePath(fullPath, solutionDirectory, out string relative)
                && !string.IsNullOrWhiteSpace(relative))
            {
                return relative;
            }

            return fullPath;
        }
    }
}
