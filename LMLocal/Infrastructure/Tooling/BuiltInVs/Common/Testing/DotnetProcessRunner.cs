using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Testing
{
    /// <summary>
    /// Result of a `dotnet` process run.
    /// </summary>
    internal sealed class DotnetProcessResult
    {
        public int ExitCode { get; set; }
        public string StdOut { get; set; }
        public string StdErr { get; set; }
        public bool Cancelled { get; set; }
        public bool TimedOut { get; set; }
    }

    /// <summary>
    /// Runs `dotnet <arguments>` with a timeout, capturing stdout/stderr via async events with a bounded buffer so huge logs cannot exhaust memory.
    /// </summary>
    internal static class DotnetProcessRunner
    {
        /// <summary>
        /// Default cap (chars) for each of stdout/stderr. Beyond this the stream is truncated.
        /// </summary>
        public const int DefaultMaxOutputSize = 1 * 1024 * 1024;

        public static async Task<DotnetProcessResult> RunAsync(
            string arguments,
            string workingDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            int maxOutputSize = DefaultMaxOutputSize)
        {
            if (string.IsNullOrWhiteSpace(arguments))
                throw new ArgumentException("Arguments must be a non-empty string.", nameof(arguments));
            if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
                throw new ArgumentException($"Working directory does not exist: {workingDirectory}", nameof(workingDirectory));
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than zero.");
            if (maxOutputSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxOutputSize), "maxOutputSize must be greater than zero.");

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                var stdoutBuffer = new StringBuilder();
                var stderrBuffer = new StringBuilder();
                bool stdoutTruncated = false;
                bool stderrTruncated = false;

                process.OutputDataReceived += (s, e) => AppendCapped(stdoutBuffer, e.Data, maxOutputSize, ref stdoutTruncated);
                process.ErrorDataReceived += (s, e) => AppendCapped(stderrBuffer, e.Data, maxOutputSize, ref stderrTruncated);

                try
                {
                    process.Start();
                }
                catch (Exception ex)
                {
                    return new DotnetProcessResult
                    {
                        ExitCode = -1,
                        StdErr = $"Failed to start 'dotnet': {ex.Message}",
                        StdOut = string.Empty
                    };
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                bool cancelled = false;
                bool timedOut = false;

                using (var timeoutCts = new CancellationTokenSource(timeout))
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                {
                    try
                    {
                        await Task.Run(
                            () =>
                            {
                                while (!process.WaitForExit(3000))
                                    linkedCts.Token.ThrowIfCancellationRequested();
                            },
                            linkedCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = cancellationToken.IsCancellationRequested;
                        timedOut = !cancelled;

                        TryKill(process);
                    }
                }

                if (process.HasExited)
                {
                    try { process.WaitForExit(); }
                    catch (Exception ex) { InternalLogger.Warn($"DotnetProcessRunner: WaitForExit flush failed: {ex.Message}"); }
                }

                string stdout = FinalizeOutput(stdoutBuffer, stdoutTruncated, maxOutputSize);
                string stderr = FinalizeOutput(stderrBuffer, stderrTruncated, maxOutputSize);
                int exitCode = process.HasExited ? process.ExitCode : -1;

                return new DotnetProcessResult
                {
                    ExitCode = cancelled || timedOut ? -1 : exitCode,
                    StdOut = stdout,
                    StdErr = stderr,
                    Cancelled = cancelled,
                    TimedOut = timedOut
                };
            }
        }

        private static void TryKill(Process process)
        {
            if (process.HasExited) return;

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "taskkill",
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
                InternalLogger.Warn($"DotnetProcessRunner: taskkill failed, falling back to Process.Kill: {ex.Message}");
                try { if (!process.HasExited) process.Kill(); }
                catch (Exception ex2) { InternalLogger.Warn($"DotnetProcessRunner: Process.Kill failed: {ex2.Message}"); }
            }

            try { process.WaitForExit(3000); }
            catch (Exception ex) { InternalLogger.Warn($"DotnetProcessRunner: WaitForExit after kill failed: {ex.Message}"); }
        }

        private static void AppendCapped(StringBuilder buffer, string data, int maxOutputSize, ref bool truncated)
        {
            if (string.IsNullOrEmpty(data) || truncated) return;
            if (buffer.Length + data.Length + Environment.NewLine.Length > maxOutputSize)
            {
                truncated = true;
                return;
            }
            buffer.AppendLine(data);
        }

        private static string FinalizeOutput(StringBuilder buffer, bool truncated, int maxOutputSize)
        {
            string text = buffer.ToString();
            return truncated
                ? text + Environment.NewLine + $"[output truncated at {maxOutputSize} chars]"
                : text;
        }
    }
}
