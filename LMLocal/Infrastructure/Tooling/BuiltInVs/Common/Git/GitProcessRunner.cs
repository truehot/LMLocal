using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git
{
    /// <summary>Result of one git invocation. Output is captured as bytes so NUL-separated (-z) formats survive.</summary>
    internal sealed class GitProcessResult
    {
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; }
        public string StandardError { get; set; }

        /// <summary>Output is incomplete: over the byte cap, or still held open by a process git started after git exited.</summary>
        public bool Truncated { get; set; }
    }

    /// <summary>Indirection so operation tests can assert the exact argv without starting git.</summary>
    internal interface IGitProcessRunner
    {
        /// <summary>Cancellation (Stop or the tool's own time budget) kills the process tree and throws OperationCanceledException.</summary>
        Task<GitProcessResult> RunAsync(
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            int maxOutputBytes = GitProcessRunner.DefaultMaxOutputBytes);
    }

    /// <summary>
    /// Runs git with a fixed, read-only environment, a byte-accurate bounded output buffer and
    /// process-tree termination on cancellation. Unlike DotnetProcessRunner it never reads output line by line.
    /// </summary>
    internal sealed class GitProcessRunner : IGitProcessRunner
    {
        public const int DefaultMaxOutputBytes = 4 * 1024 * 1024;

        /// <summary>How long to keep reading after git exited or was killed; an orphaned child can hold the pipes much longer.</summary>
        private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

        private readonly GitExecutableLocator _locator;

        public GitProcessRunner(GitExecutableLocator locator = null)
        {
            _locator = locator ?? GitExecutableLocator.Shared;
        }

        public async Task<GitProcessResult> RunAsync(
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            int maxOutputBytes = DefaultMaxOutputBytes)
        {
            if (string.IsNullOrWhiteSpace(arguments))
                throw new ArgumentException("Arguments must be a non-empty string.", nameof(arguments));
            if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
                throw new ArgumentException($"Working directory does not exist: {workingDirectory}", nameof(workingDirectory));
            if (maxOutputBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxOutputBytes), "maxOutputBytes must be greater than zero.");

            cancellationToken.ThrowIfCancellationRequested();

            string gitPath;
            string locatorError;
            if (!_locator.TryGet(out gitPath, out locatorError))
                return new GitProcessResult { ExitCode = -1, StandardOutput = string.Empty, StandardError = locatorError };

            ProcessStartInfo startInfo = CreateStartInfo(gitPath, arguments, workingDirectory);

            using (var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true })
            {
                var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                process.Exited += (sender, e) => exited.TrySetResult(true);

                try
                {
                    if (!process.Start())
                        throw new InvalidOperationException("Process.Start returned false.");
                }
                catch (Exception ex)
                {
                    return new GitProcessResult { ExitCode = -1, StandardOutput = string.Empty, StandardError = $"Failed to start git: {ex.Message}" };
                }

                // A git command that asks for input must fail, never block on a hidden console.
                try { process.StandardInput.Close(); }
                catch (Exception ex) { InternalLogger.Debug($"git_read: closing stdin failed: {ex.Message}"); }

                var stdout = new CappedReader(process.StandardOutput.BaseStream, maxOutputBytes);
                var stderr = new CappedReader(process.StandardError.BaseStream, maxOutputBytes);
                Task reads = Task.WhenAll(stdout.Completion, stderr.Completion);

                // The end of git, not the end of its pipes: a child git started may keep them open after git is gone.
                Task cancelled = Task.Delay(Timeout.Infinite, cancellationToken);
                if (await Task.WhenAny(exited.Task, cancelled).ConfigureAwait(false) != exited.Task)
                {
                    ProcessTreeKiller.TryKill(process);
                    await WaitBoundedAsync(reads).ConfigureAwait(false);
                    throw new OperationCanceledException(cancellationToken);
                }

                bool drained = await WaitBoundedAsync(reads).ConfigureAwait(false);
                if (!drained)
                    InternalLogger.Warn("git_read: git exited but its output pipes stayed open; returning what was read.");

                return new GitProcessResult
                {
                    ExitCode = process.ExitCode,
                    StandardOutput = Decode(stdout.Snapshot()),
                    StandardError = Decode(stderr.Snapshot()),
                    Truncated = stdout.Truncated || !drained
                };
            }
        }

        internal static ProcessStartInfo CreateStartInfo(string gitPath, string arguments, string workingDirectory)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = gitPath,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            // Inherited GIT_* variables could redirect the repository, the index, the config or the diff program.
            var inherited = new List<string>();
            foreach (string key in startInfo.EnvironmentVariables.Keys)
            {
                if (key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase))
                    inherited.Add(key);
            }

            foreach (string key in inherited)
                startInfo.EnvironmentVariables.Remove(key);

            // No index writes, no credential prompts, no pager, no pathspec globbing.
            startInfo.EnvironmentVariables["GIT_OPTIONAL_LOCKS"] = "0";
            startInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            startInfo.EnvironmentVariables["GIT_LITERAL_PATHSPECS"] = "1";
            startInfo.EnvironmentVariables["GIT_PAGER"] = "cat";
            return startInfo;
        }

        /// <summary>True when both pipes reached end of stream within <see cref="DrainTimeout"/>.</summary>
        private static async Task<bool> WaitBoundedAsync(Task reads)
        {
            // reads are thread-pool pipe reads that never need the VS main thread, so they cannot deadlock.
#pragma warning disable VSTHRD003
            return await Task.WhenAny(reads, Task.Delay(DrainTimeout)).ConfigureAwait(false) == reads;
#pragma warning restore VSTHRD003
        }

        private static string Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;

            int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            return Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
        }

        /// <summary>
        /// Reads a pipe to its end, keeping at most the cap. The bytes read so far can be taken at any time, so an
        /// abandoned read (pipe held by an orphan) still yields its output; the loop itself ends when the pipe closes.
        /// </summary>
        private sealed class CappedReader
        {
            private readonly Stream _stream;
            private readonly int _maxBytes;
            private readonly MemoryStream _buffer = new MemoryStream();
            private readonly object _lock = new object();
            private bool _truncated;

            public CappedReader(Stream stream, int maxBytes)
            {
                _stream = stream;
                _maxBytes = maxBytes;
                Completion = ReadToEndAsync();
            }

            public Task Completion { get; }

            public bool Truncated
            {
                get { lock (_lock) return _truncated; }
            }

            public byte[] Snapshot()
            {
                lock (_lock)
                    return _buffer.ToArray();
            }

            private async Task ReadToEndAsync()
            {
                var chunk = new byte[8192];
                try
                {
                    int read;
                    while ((read = await _stream.ReadAsync(chunk, 0, chunk.Length).ConfigureAwait(false)) > 0)
                    {
                        lock (_lock)
                        {
                            int keep = (int)Math.Min(read, _maxBytes - _buffer.Length);
                            // In-memory copy under a lock, where await is not allowed.
#pragma warning disable VSTHRD103
                            if (keep > 0)
                                _buffer.Write(chunk, 0, keep);
#pragma warning restore VSTHRD103
                            if (keep < read)
                                _truncated = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    InternalLogger.Debug($"git_read: reading git output stopped: {ex.Message}");
                }
            }
        }
    }
}
