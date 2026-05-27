using System.Runtime.InteropServices;
using LMLocal.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;

namespace LMLocal.Tests.Unit.Infrastructure.Vs
{
    /// <summary>
    /// Test implementation of UI thread guard.
    /// Gracefully handles UI thread checks in test scenarios where thread constraints may not apply.
    /// </summary>
    internal sealed class TestUiThreadGuard : IUiThreadGuard
    {
        /// <summary>
        /// Test implementation - simply logs and doesn't enforce thread checks.
        /// Allows tests to run on background threads.
        /// </summary>
        public void EnsureOnUIThread()
        {
            // In test mode, simply log without enforcing thread check
            InternalLogger.Debug("UI thread check skipped in test mode with injected provider");
        }
    }
}
