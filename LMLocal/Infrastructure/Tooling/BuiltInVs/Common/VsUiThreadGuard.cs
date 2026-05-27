using Microsoft.VisualStudio.Shell;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common
{

    /// <summary>
    /// Abstraction for UI thread affinity checks.
    /// Allows for flexible thread safety validation, including test scenarios where thread checks may need to be suppressed.
    /// </summary>
    internal interface IUiThreadGuard
    {
        /// <summary>
        /// Ensures the current code is executing on the Visual Studio UI thread.
        /// In production, this will throw a COMException if not on the UI thread.
        /// In test scenarios with injected dependencies, this may be a no-op or handle exceptions gracefully.
        /// </summary>
        /// <exception cref="System.Runtime.InteropServices.COMException">Thrown when not on UI thread (production mode).</exception>
        void EnsureOnUIThread();
    }

    /// <summary>
    /// Production implementation of UI thread guard.
    /// Enforces strict UI thread affinity checks using Visual Studio's ThreadHelper.
    /// </summary>
    internal sealed class VsUiThreadGuard : IUiThreadGuard
    {
        /// <summary>
        /// Ensures the current code is executing on the Visual Studio UI thread.
        /// Throws a COMException if not on the UI thread.
        /// VSTHRD108: Thread affinity check is unconditional and required for VS service access.
        /// </summary>
        /// <exception cref="System.Runtime.InteropServices.COMException">Thrown when not on UI thread.</exception>
        public void EnsureOnUIThread()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
        }
    }
}
