using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common.Git
{
    /// <summary>Records argv and returns canned git output; no real git process in unit tests.</summary>
    internal sealed class FakeGitProcessRunner : IGitProcessRunner
    {
        public readonly List<string> Calls = new List<string>();
        public Func<string, GitProcessResult> Handler;
        public string DefaultOutput = string.Empty;
        public string DefaultError = string.Empty;
        public int DefaultExitCode = 0;

        /// <summary>A call whose argv contains this text hangs until it is cancelled (simulates a slow git).</summary>
        public string HangOn;

        public async Task<GitProcessResult> RunAsync(
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            int maxOutputBytes = GitProcessRunner.DefaultMaxOutputBytes)
        {
            Calls.Add(arguments);
            if (HangOn != null && arguments.Contains(HangOn))
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);

            GitProcessResult result = Handler != null
                ? Handler(arguments)
                : new GitProcessResult
                {
                    ExitCode = DefaultExitCode,
                    StandardOutput = DefaultOutput,
                    StandardError = DefaultError
                };

            return result ?? new GitProcessResult { ExitCode = -1, StandardError = "no canned result" };
        }

        public string LastCall => Calls.Count > 0 ? Calls[Calls.Count - 1] : null;
    }
}
