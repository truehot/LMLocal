using System;
using System.Diagnostics;
using LMLocal.Core.Common;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common
{
    /// <summary>
    /// Terminates a timed-out/cancelled process together with its children (git can spawn helpers).
    /// Shared by the git process runner; the existing dotnet runner keeps its own behaviour.
    /// </summary>
    internal static class ProcessTreeKiller
    {
        public static void TryKill(Process process)
        {
            if (process == null || process.HasExited)
                return;

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = ExecutablePath.InSystemDirectory("taskkill.exe"),
                    Arguments = $"/PID {process.Id} /T /F",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var killer = Process.Start(startInfo))
                {
                    if (killer != null && !killer.WaitForExit(3000))
                        killer.Kill();
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("ProcessTreeKiller: taskkill failed, falling back to Process.Kill", ex);
                try { if (!process.HasExited) process.Kill(); }
                catch (Exception ex2) { InternalLogger.Warn("ProcessTreeKiller: Process.Kill failed", ex2); }
            }

            try { process.WaitForExit(3000); }
            catch (Exception ex) { InternalLogger.Warn("ProcessTreeKiller: WaitForExit after kill failed", ex); }
        }
    }
}
