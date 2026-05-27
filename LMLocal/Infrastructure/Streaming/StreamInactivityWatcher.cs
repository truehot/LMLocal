using System;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Common;

namespace LMLocal.Infrastructure.Streaming
{
    /// <summary>
    /// Watches for inactivity and cancels the operation when the configured timeout (in seconds) is exceeded.
    /// Can be used for streams, HTTP requests, MCP calls, or any long-running operation.
    /// </summary>
    internal interface IStreamInactivityWatcher
    {
        bool IsTimeout { get; }
        void SignalCompletion();
        void SignalActivity();
        Task WatchAsync(CancellationToken cancellationToken);
    }

    internal sealed class StreamInactivityWatcher : IStreamInactivityWatcher
    {
        private readonly int _timeoutSeconds;
        private readonly int _delayMilliseconds;
        private volatile bool _isCompleted = false;
        private readonly CancellationTokenSource _cts;
        private volatile bool _isTimeout = false;
        private long _lastActivityMs;

        public bool IsTimeout => _isTimeout;

        public StreamInactivityWatcher(CancellationTokenSource cts, int timeoutSeconds, int delayMilliseconds = 1000)
        {
            _cts = cts ?? throw new ArgumentNullException(nameof(cts));

            if (timeoutSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "Timeout must be a positive number of seconds.");
            }
            if (delayMilliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(delayMilliseconds), "Delay must be a positive number of milliseconds.");
            }
            _timeoutSeconds = timeoutSeconds;
            _delayMilliseconds = delayMilliseconds;
            _lastActivityMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public void SignalCompletion()
        {
            _isCompleted = true;
        }

        /// <summary>
        /// Signal that activity occurred (data received). Updates last activity timestamp.
        /// </summary>
        public void SignalActivity()
        {
            Interlocked.Exchange(ref _lastActivityMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        /// <summary>
        /// Watch for inactivity using SignalActivity() calls.
        /// </summary>
        public async Task WatchAsync(CancellationToken cancellationToken)
        {
            long timeoutMs = _timeoutSeconds * 1000L;
            SignalActivity();
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (_isCompleted)
                    {
                        InternalLogger.Debug("Activity monitoring completed, inactivity watcher exiting");
                        return;
                    }

                    await Task.Delay(_delayMilliseconds, cancellationToken).ConfigureAwait(false);

                    long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    long lastActivityMs = Interlocked.Read(ref _lastActivityMs);
                    long timeSinceLastActivity = nowMs - lastActivityMs;

                    if (timeSinceLastActivity > timeoutMs)
                    {
                        InternalLogger.Error($"Inactivity timeout ({timeSinceLastActivity}ms) exceeded. No activity for {_timeoutSeconds} seconds.");
                        _isTimeout = true;
                        _isCompleted = true;
                        _cts?.Cancel();
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                InternalLogger.Debug($"Cancellation requested for inactivity watcher. Exiting.");
            }
        }
    }

    internal sealed class NoopStreamInactivityWatcher : IStreamInactivityWatcher
    {
        public bool IsTimeout => false;

        public void SignalCompletion() { }

        public void SignalActivity() { }

        public Task WatchAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
