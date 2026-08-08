using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.VisualStudio.Shell;

namespace LMLocal
{
    /// <summary>
    /// This class implements the tool window exposed by this package and hosts a user control.
    /// </summary>
    [Guid("ae3b51e3-5a57-49b6-baf2-ed10eda982cc")]
    public class MainWindow : ToolWindowPane
    {
        private readonly MainWindowControl _control;

        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// </summary>
        public MainWindow() : base(null)
        {
            this.Caption = "LM Local Chat - AI Assistant";

            _control = new MainWindowControl();
            this.Content = _control;

            _control.PreviewKeyDown += OnControlPreviewKeyDown;
        }

        /// <summary>
        /// Injects markdown text into the chat input field. Used by the "Ask LM Local" context menu command.
        /// </summary>
        public async Task InjectPromptAsync(string markdownText)
        {
            await _control.InjectTextIntoInputAsync(markdownText);
        }

        private void OnControlPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Handle Home, End, Left, Right arrow keys
            if (e.Key == Key.Home || e.Key == Key.End || e.Key == Key.Left || e.Key == Key.Right)
            {
                int keyCode = GetKeyCode(e.Key);
                bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

                _control?.SendKeyToWebView(keyCode, shift);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Injects markdown text and automatically sends it.
        /// Used by code analysis commands (Review, Fix, Refactor, etc.).
        /// </summary>
        public async Task InjectAndAutoSendAsync(string markdownText, string instructionTabId = null)
        {
            await _control.InjectTextAndSendAsync(markdownText, instructionTabId);
        }

        private int GetKeyCode(Key key)
        {
            if (key == Key.Home) return 0x24;
            if (key == Key.End) return 0x23;
            if (key == Key.Left) return 0x25;
            if (key == Key.Right) return 0x27;
            return 0;
        }
    }
}
