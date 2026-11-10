using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.ChatSession;
using LMLocal.Core.Common;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace LMLocal.Infrastructure.VisualStudio
{
    /// <summary>
    /// Guards Visual Studio against being closed by accident while an AI request is running.
    /// </summary>
    internal interface ICloseWhileGeneratingGuard : IDisposable
    {
        /// <summary>
        /// Starts listening for close requests of the Visual Studio main window.
        /// Must be called on the UI thread.
        /// </summary>
        void Initialize();
    }

    /// <summary>
    /// Intercepts close requests of the Visual Studio main window while an AI request is running and
    /// asks the user how to proceed: continue working, cancel the work and stay, or exit immediately.
    /// </summary>

    internal sealed class CloseWhileGeneratingGuard : ICloseWhileGeneratingGuard
    {
        private const int WM_CLOSE = 0x0010;
        private const int WM_QUERYENDSESSION = 0x0011;

        private const string DialogTitle = "LM Local";
        private const string MainInstruction = "An AI request is still running.";
        private const string ContentText = "Closing Visual Studio now would cancel the current work.\r\nWhat would you like to do?";

        private const string CaptionContinueWork = "Keep working";
        private const string CaptionCancelAndStay = "Stop request";
        private const string CaptionCancelAndExit = "Stop and exit";

        private readonly ISessionManager _sessionManager;
        private readonly ISettingsManager _settingsManager;

        private IVsShell _shell;
        private BroadcastMessageEvents _broadcastSink;
        private uint _broadcastCookie;

        private System.Windows.Application _application;
        private Window _mainWindow;

        // Win32 subclassing of the main window's HWND. Visual Studio closes its frame natively via WM_CLOSE,
        // which does not reliably raise the WPF Closing event on the hosted main window.
        private IntPtr _subclassHwnd = IntPtr.Zero;
        private IntPtr _originalWndProc = IntPtr.Zero;
        private WndProcDelegate _wndProc;

        // Prevents a second prompt while one is open, and remembers for which session an exit was approved.
        private bool _isHandling;

        // Exit approval is bound to one session: the next session gets a new id and is protected again.
        private long _exitApprovedSessionId = -1;
        private bool _disposed;

        public CloseWhileGeneratingGuard(ISessionManager sessionManager, ISettingsManager settingsManager)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        }

        public void Initialize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed) return;

            try
            {
                _shell = Package.GetGlobalService(typeof(SVsShell)) as IVsShell;
                if (_shell != null)
                {
                    _broadcastSink = new BroadcastMessageEvents(this);
                    int hr = _shell.AdviseBroadcastMessages(_broadcastSink, out _broadcastCookie);
                    if (Microsoft.VisualStudio.ErrorHandler.Failed(hr))
                    {
                        InternalLogger.Warn($"CloseWhileGeneratingGuard: AdviseBroadcastMessages failed (0x{hr:X8}); session-end interception is disabled.");
                        _broadcastCookie = 0;
                    }
                }

                AttachMainWindow();
            }
            catch (Exception ex)
            {
                InternalLogger.Error("CloseWhileGeneratingGuard: Failed to initialize the close guard.", ex);
            }
        }

        #region Main window (Window.Closing + WM_CLOSE subclass)

        /// <summary>
        /// Subscribes to the close events of the Visual Studio main window.
        /// </summary>
        private void AttachMainWindow()
        {
            _application = System.Windows.Application.Current;
            if (_application == null)
            {
                InternalLogger.Warn("CloseWhileGeneratingGuard: No WPF application instance found; only session-end notifications remain active.");
                return;
            }

            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnAnyWindowLoaded),
                handledEventsToo: true);

            SubscribeToWindow(_application.MainWindow);
        }

        private void OnAnyWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (_disposed || _application == null) return;

            try
            {
                if (sender is Window window && ReferenceEquals(window, _application.MainWindow))
                    SubscribeToWindow(window);
            }
            catch (Exception ex)
            {
                InternalLogger.Error("CloseWhileGeneratingGuard: Failed to subscribe to the main window.", ex);
            }
        }

        private void SubscribeToWindow(Window window)
        {
            if (window == null) return;

            if (!ReferenceEquals(window, _mainWindow))
            {
                UnsubscribeFromWindow();
                _mainWindow = window;
                _mainWindow.Closing += OnMainWindowClosing;
                InternalLogger.Info($"CloseWhileGeneratingGuard: Listening for close requests on '{window.GetType().Name}'.");
            }

            EnsureSubclass(window);
        }

        private void UnsubscribeFromWindow()
        {
            if (_mainWindow == null) return;

            try
            {
                _mainWindow.Closing -= OnMainWindowClosing;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"CloseWhileGeneratingGuard: Failed to detach from the main window: {ex.Message}");
            }
            finally
            {
                _mainWindow = null;
            }
        }

        private void OnMainWindowClosing(object sender, CancelEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var current = _application?.MainWindow;
            if (current != null && !ReferenceEquals(sender, current))
                return;

            InternalLogger.Info("CloseWhileGeneratingGuard: WPF Window.Closing fired.");

            if (TryVetoClose("Window.Closing", isSessionEnd: false))
                e.Cancel = true;
        }

        /// <summary>
        /// Subclasses the HWND of the given window so WM_CLOSE and WM_QUERYENDSESSION can be intercepted.
        /// </summary>
        private void EnsureSubclass(Window window)
        {
            if (_subclassHwnd != IntPtr.Zero || window == null) return;

            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;

                _wndProc = MainWindowProc;
                _originalWndProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProc));
                if (_originalWndProc == IntPtr.Zero)
                {
                    InternalLogger.Warn("CloseWhileGeneratingGuard: SetWindowLongPtr failed; WM_CLOSE interception is disabled.");
                    _wndProc = null;
                    return;
                }

                _subclassHwnd = hwnd;
                InternalLogger.Info($"CloseWhileGeneratingGuard: Subclassed main window HWND 0x{hwnd.ToInt64():X}.");
            }
            catch (Exception ex)
            {
                InternalLogger.Error("CloseWhileGeneratingGuard: Failed to subclass the Visual Studio main window.", ex);
                _wndProc = null;
                _originalWndProc = IntPtr.Zero;
            }
        }

        private void RestoreSubclass()
        {
            if (_subclassHwnd == IntPtr.Zero) return;

            try
            {
                IntPtr current = GetWindowLongPtr(_subclassHwnd, GWLP_WNDPROC);
                if (_wndProc == null || current != Marshal.GetFunctionPointerForDelegate(_wndProc))
                {
                    InternalLogger.Warn("CloseWhileGeneratingGuard: The main window was subclassed after us; leaving the pass-through procedure in place.");
                    return;
                }

                if (_originalWndProc != IntPtr.Zero)
                    SetWindowLongPtr(_subclassHwnd, GWLP_WNDPROC, _originalWndProc);
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"CloseWhileGeneratingGuard: Failed to restore the main window procedure: {ex.Message}");
                return;
            }

            _originalWndProc = IntPtr.Zero;
            _subclassHwnd = IntPtr.Zero;
            _wndProc = null;
        }

        private IntPtr MainWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (msg == WM_CLOSE && TryVetoClose("WM_CLOSE", isSessionEnd: false))
                return IntPtr.Zero;

            if (msg == WM_QUERYENDSESSION && !IsCriticalSessionEnd(lParam)
                && TryVetoClose("WM_QUERYENDSESSION (HWND)", isSessionEnd: true))
                return IntPtr.Zero;

            return CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
        }

        #endregion

        /// <summary>
        /// Handles an incoming close request.
        /// </summary>
        private bool TryVetoClose(string source, bool isSessionEnd)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_disposed) return false;

            if (_isHandling) return true;

            _isHandling = true;
            try
            {
                if (!_sessionManager.IsSessionRunning) return false;

                if (_exitApprovedSessionId == _sessionManager.CurrentSessionId) return false;

                if (!IsPreventionEnabled()) return false;

                InternalLogger.Info($"CloseWhileGeneratingGuard: '{source}' received while work is running.");

                CloseChoice choice;

                IntPtr blockReasonHwnd = isSessionEnd ? TryCreateShutdownBlockReason() : IntPtr.Zero;
                try
                {
                    choice = ShowCloseDialog();
                }
                finally
                {
                    if (blockReasonHwnd != IntPtr.Zero)
                        ShutdownBlockReasonDestroy(blockReasonHwnd);
                }

                InternalLogger.Info($"CloseWhileGeneratingGuard: User chose '{choice}'.");

                switch (choice)
                {
                    case CloseChoice.CancelAndExit:
                        _exitApprovedSessionId = _sessionManager.CurrentSessionId;
                        StopRunningWork();
                        return false;

                    case CloseChoice.CancelWorkAndStay:
                        StopRunningWork();
                        return true;

                    default:
                        return true;
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error("CloseWhileGeneratingGuard: Failed to handle the close request; allowing close.", ex);
                return false;
            }
            finally
            {
                _isHandling = false;
            }
        }

        private bool IsPreventionEnabled()
        {
            try
            {
                return _settingsManager.Current?.PreventCloseWhileGenerating ?? true;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        private void StopRunningWork()
        {
            try
            {
                _sessionManager.TryStopSession();
            }
            catch (Exception ex)
            {
                InternalLogger.Error("CloseWhileGeneratingGuard: Failed to cancel the running session.", ex);
            }
        }

        /// <summary>
        /// Registers the text Windows shows on its "these apps are preventing shutdown" screen.
        /// </summary>
        private IntPtr TryCreateShutdownBlockReason()
        {
            IntPtr hwnd = _subclassHwnd;
            if (hwnd == IntPtr.Zero)
            {
                Window owner = ResolveOwnerWindow();
                if (owner != null)
                    hwnd = new WindowInteropHelper(owner).Handle;
            }

            if (hwnd == IntPtr.Zero) return IntPtr.Zero;
            if (ShutdownBlockReasonCreate(hwnd, MainInstruction)) return hwnd;

            InternalLogger.Warn("CloseWhileGeneratingGuard: ShutdownBlockReasonCreate failed.");
            return IntPtr.Zero;
        }

        // ENDSESSION_CRITICAL: Windows ends the session regardless of the answer and does not wait for a prompt.
        private static bool IsCriticalSessionEnd(IntPtr lParam) => (lParam.ToInt64() & ENDSESSION_CRITICAL) != 0;

        #region Themed dialog

        /// <summary>
        /// Resolves the window the prompt should be owned by.
        /// </summary>
        private Window ResolveOwnerWindow()
        {
            if (_mainWindow != null && _mainWindow.IsLoaded)
                return _mainWindow;

            Window current = _application?.MainWindow;
            return current != null && current.IsLoaded ? current : null;
        }

        /// <summary>
        /// Modal three-button prompt that follows the current Visual Studio theme (Light / Dark / Blue / High Contrast).
        /// </summary>
        private CloseChoice ShowCloseDialog()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            CloseChoice choice = CloseChoice.ContinueWork;
            Window owner = ResolveOwnerWindow();

            var dialog = new DialogWindow
            {
                Title = DialogTitle,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                ShowInTaskbar = false,
                HasMinimizeButton = false,
                HasMaximizeButton = false,
                MinWidth = 420,
                MaxWidth = 560,
                Owner = owner,
                WindowStartupLocation = owner != null
                    ? WindowStartupLocation.CenterOwner
                    : WindowStartupLocation.CenterScreen
            };
            dialog.SetResourceReference(Control.BackgroundProperty, ThemedDialogColors.WindowPanelBrushKey);
            dialog.SetResourceReference(Control.ForegroundProperty, ThemedDialogColors.WindowPanelTextBrushKey);
            dialog.SetResourceReference(TextElement.ForegroundProperty, ThemedDialogColors.WindowPanelTextBrushKey);
            dialog.SetResourceReference(TextElement.FontFamilyProperty, VsFonts.EnvironmentFontFamilyKey);
            dialog.SetResourceReference(TextElement.FontSizeProperty, VsFonts.EnvironmentFontSizeKey);

            var root = new Grid { Margin = new Thickness(20, 20, 20, 16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var icon = new CrispImage
            {
                Moniker = KnownMonikers.StatusWarning,
                Width = 32,
                Height = 32,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 16, 0)
            };
            root.Children.Add(icon);

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Top };

            var header = new TextBlock
            {
                Text = MainInstruction,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            header.SetResourceReference(TextBlock.ForegroundProperty, ThemedDialogColors.HeaderTextBrushKey);
            header.SetResourceReference(TextBlock.FontSizeProperty, VsFonts.Environment122PercentFontSizeKey);
            texts.Children.Add(header);

            var body = new TextBlock { Text = ContentText, TextWrapping = TextWrapping.Wrap };
            body.SetResourceReference(TextBlock.ForegroundProperty, ThemedDialogColors.WindowPanelTextBrushKey);
            texts.Children.Add(body);
            Grid.SetColumn(texts, 1);
            root.Children.Add(texts);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };
            buttons.Children.Add(CreateDialogButton(dialog, CaptionContinueWork, isDefault: true, () => choice = CloseChoice.ContinueWork));
            buttons.Children.Add(CreateDialogButton(dialog, CaptionCancelAndStay, isDefault: false, () => choice = CloseChoice.CancelWorkAndStay));
            buttons.Children.Add(CreateDialogButton(dialog, CaptionCancelAndExit, isDefault: false, () => choice = CloseChoice.CancelAndExit));
            Grid.SetRow(buttons, 1);
            Grid.SetColumnSpan(buttons, 2);
            root.Children.Add(buttons);

            dialog.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Escape)
                    return;

                e.Handled = true;
                choice = CloseChoice.ContinueWork;
                dialog.Close();
            };

            dialog.Content = root;
            dialog.ShowModal();

            return choice;
        }

        private static Button CreateDialogButton(Window dialog, string caption, bool isDefault, Action onClick)
        {
            var button = new Button
            {
                Content = caption,
                MinWidth = 75,
                MinHeight = 23,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(10, 2, 10, 2),
                IsDefault = isDefault
            };

            button.SetResourceReference(FrameworkElement.StyleProperty, VsResourceKeys.ThemedDialogButtonStyleKey);
            button.Click += (s, e) =>
            {
                onClick();
                dialog.DialogResult = true;
            };
            return button;
        }

        #endregion

        #region Broadcast messages (WM_QUERYENDSESSION)

        /// <summary>
        /// Backup channel for session end.
        /// </summary>
        private int OnBroadcastMessage(int message, IntPtr lParam)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (message != WM_QUERYENDSESSION || IsCriticalSessionEnd(lParam)) return BroadcastContinue;

            return TryVetoClose("WM_QUERYENDSESSION", isSessionEnd: true) ? BroadcastStop : BroadcastContinue;
        }

        private sealed class BroadcastMessageEvents : IVsBroadcastMessageEvents
        {
            private readonly CloseWhileGeneratingGuard _owner;

            internal BroadcastMessageEvents(CloseWhileGeneratingGuard owner)
            {
                _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            }

            public int OnBroadcastMessage(uint msg, IntPtr wParam, IntPtr lParam)
            {
                ThreadHelper.ThrowIfNotOnUIThread(); // the shell broadcasts on the UI thread
                return _owner.OnBroadcastMessage((int)msg, lParam);
            }
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (!ThreadHelper.CheckAccess())
            {
                InternalLogger.Warn("CloseWhileGeneratingGuard: Dispose called off the UI thread; UI cleanup skipped.");
                return;
            }

            ThreadHelper.ThrowIfNotOnUIThread();
            ReleaseUiResources();
        }

        private void ReleaseUiResources()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                RestoreSubclass();
                UnsubscribeFromWindow();
                _application = null;

                if (_shell != null && _broadcastCookie != 0)
                    _shell.UnadviseBroadcastMessages(_broadcastCookie);
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"CloseWhileGeneratingGuard: Cleanup failed: {ex.Message}");
            }
            finally
            {
                _broadcastCookie = 0;
                _broadcastSink = null;
                _shell = null;
            }
        }

        private enum CloseChoice
        {
            ContinueWork,
            CancelWorkAndStay,
            CancelAndExit
        }

        #region Win32 interop (VS 2022+ is 64-bit only: SetWindowLongPtrW is always available)

        private const int GWLP_WNDPROC = -4;
        private const long ENDSESSION_CRITICAL = 0x40000000;

        // IVsBroadcastMessageEvents: S_OK = let the shell continue, S_FALSE = stop.
        private const int BroadcastContinue = Microsoft.VisualStudio.VSConstants.S_OK;
        private const int BroadcastStop = Microsoft.VisualStudio.VSConstants.S_FALSE;

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool ShutdownBlockReasonCreate(IntPtr hWnd, string pwszReason);

        [DllImport("user32.dll")]
        private static extern bool ShutdownBlockReasonDestroy(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        #endregion
    }
}
