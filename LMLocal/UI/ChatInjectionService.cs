using System;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace LMLocal
{
    /// <summary>
    /// UI-layer implementation of <see cref="IChatInjectionService"/>.
    /// </summary>
    internal sealed class ChatInjectionService : IChatInjectionService
    {
        private static readonly object _syncLock = new object();
        private static IChatInjectionService _instance;

        private readonly AsyncPackage _package;

        private ChatInjectionService(AsyncPackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
        }

        /// <summary>
        /// The current instance.
        /// </summary>
        public static IChatInjectionService Instance
        {
            get
            {
                var instance = _instance ?? throw new InvalidOperationException(
                        "ChatInjectionService is not initialized. " +
                        "Call ChatInjectionService.Initialize(package) during package initialization.");
                return instance;
            }
        }

        /// <summary>
        /// Creates the singleton instance.
        /// </summary>
        public static void Initialize(AsyncPackage package)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));

            lock (_syncLock)
            {
                if (_instance == null)
                {
                    _instance = new ChatInjectionService(package);
                }
            }
        }

        /// <summary>
        /// Drops the singleton instance. Called on package disposal.
        /// </summary>
        public static void Reset()
        {
            lock (_syncLock)
            {
                _instance = null;
            }
        }

        /// <inheritdoc />
        public async Task InjectPromptAsync(string markdownText)
        {
            MainWindow mainWindow = await FindAndShowMainWindowAsync().ConfigureAwait(false);
            if (mainWindow == null)
                return;

            await mainWindow.InjectPromptAsync(markdownText + "\n\n");
        }

        /// <inheritdoc />
        public async Task InjectAndAutoSendAsync(string markdownText, string instructionTabId = null)
        {
            MainWindow mainWindow = await FindAndShowMainWindowAsync().ConfigureAwait(false);
            if (mainWindow == null)
                return;

            await mainWindow.InjectAndAutoSendAsync(markdownText + "\n\n", instructionTabId);
        }

        /// <summary>
        /// Finds or creates the chat tool window and brings it to the front.
        /// </summary>
        private async Task<MainWindow> FindAndShowMainWindowAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

            ToolWindowPane window = _package.FindToolWindow(typeof(MainWindow), 0, false)
                                    ?? _package.FindToolWindow(typeof(MainWindow), 0, true);

            if (window?.Frame is IVsWindowFrame frame)
            {
                _ = frame.Show();
            }

            return window as MainWindow;
        }
    }
}
