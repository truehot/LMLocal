using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Common;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace LMLocal.Infrastructure.VisualStudio
{
    /// <summary>
    /// Package-side view of <see cref="TaskbarAttentionNotifier"/>: the package hands over its own
    /// <see cref="JoinableTaskFactory"/>, so fire-and-forget work is tracked and joined when the package is disposed.
    /// </summary>
    internal interface ITaskbarAttentionNotifier : IAttentionNotifier
    {
        /// <summary>
        /// Supplies the package's task factory and disposal token. Until it is called, attention requests are ignored.
        /// </summary>
        void Initialize(JoinableTaskFactory joinableTaskFactory, CancellationToken disposalToken);
    }

    /// <summary>
    /// Flashes the Visual Studio taskbar button when a chat session finishes, so a result that arrives while the user
    /// is working in another application is noticed.
    /// </summary>
    internal sealed class TaskbarAttentionNotifier : ITaskbarAttentionNotifier
    {
        private readonly ISettingsManager _settingsManager;

        private volatile JoinableTaskFactory _joinableTaskFactory;
        private CancellationToken _disposalToken;

        public TaskbarAttentionNotifier(ISettingsManager settingsManager)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        }

        public void Initialize(JoinableTaskFactory joinableTaskFactory, CancellationToken disposalToken)
        {
            _disposalToken = disposalToken;
            _joinableTaskFactory = joinableTaskFactory ?? throw new ArgumentNullException(nameof(joinableTaskFactory));
        }

        /// <summary>
        /// Requests attention. Safe to call from any thread; never throws.
        /// </summary>
        public void RequestAttention()
        {
            try
            {
                JoinableTaskFactory jtf = _joinableTaskFactory;
                if (jtf == null)
                {
                    InternalLogger.Warn("TaskbarAttentionNotifier: Not initialized by the package; the taskbar button is not flashed.");
                    return;
                }

                if (!IsEnabled()) return;

                CancellationToken disposalToken = _disposalToken;

                _ = jtf.RunAsync(async () =>
                {
                    try
                    {
                        await jtf.SwitchToMainThreadAsync(disposalToken);
                        FlashTaskbar();
                    }
                    catch (OperationCanceledException)
                    {
                        // Visual Studio is shutting down: nothing to flash.
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Warn($"TaskbarAttentionNotifier: Failed to flash the taskbar button: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"TaskbarAttentionNotifier: Failed to request attention: {ex.Message}");
            }
        }

        private bool IsEnabled()
        {
            try
            {
                return _settingsManager.Current?.FlashTaskbarOnCompletion ?? true;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        private void FlashTaskbar()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            IntPtr hwnd = ResolveMainWindowHandle();
            if (hwnd == IntPtr.Zero)
            {
                InternalLogger.Warn("TaskbarAttentionNotifier: The main window handle is not available; the taskbar button is not flashed.");
                return;
            }

            if (IsVisualStudioInForeground(hwnd))
            {
                InternalLogger.Info("TaskbarAttentionNotifier: Visual Studio is in the foreground; the taskbar button is not flashed.");
                return;
            }

            var info = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                hwnd = hwnd,
                dwFlags = FLASHW_TRAY | FLASHW_TIMERNOFG,
                uCount = 0,
                dwTimeout = 0
            };

            FlashWindowEx(ref info);

            InternalLogger.Info("TaskbarAttentionNotifier: Flashing the taskbar button until Visual Studio is activated.");
        }

        /// <summary>
        /// Resolves the HWND of the Visual Studio main window, the same way the close guard does.
        /// </summary>
        private static IntPtr ResolveMainWindowHandle()
        {
            System.Windows.Window mainWindow = System.Windows.Application.Current?.MainWindow;
            return mainWindow == null ? IntPtr.Zero : new WindowInteropHelper(mainWindow).Handle;
        }

        /// <summary>
        /// True when the foreground window belongs to this Visual Studio instance.
        /// </summary>
        private static bool IsVisualStudioInForeground(IntPtr mainWindow)
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;

            GetWindowThreadProcessId(foreground, out uint processId);

            return processId == (uint)CurrentProcessId || GetAncestor(foreground, GA_ROOTOWNER) == mainWindow;
        }

        #region Win32 interop

        private static readonly int CurrentProcessId = GetCurrentProcessId();

        private static int GetCurrentProcessId()
        {
            using (Process process = Process.GetCurrentProcess())
            {
                return process.Id;
            }
        }

        private const uint FLASHW_TRAY = 0x00000002;
        private const uint FLASHW_TIMERNOFG = 0x0000000C;
        private const uint GA_ROOTOWNER = 3;

        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        [DllImport("user32.dll")]
        private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        #endregion
    }
}
