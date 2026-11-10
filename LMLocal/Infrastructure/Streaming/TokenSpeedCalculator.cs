using System;
using System.Collections.Generic;
using LMLocal.Infrastructure.Time;

namespace LMLocal.Infrastructure.Streaming
{
    /// <summary>
    /// Streaming speed in deltas per second (equals tokens/s on per-token servers) over a sliding window that starts at the first delta.
    /// </summary>
    internal interface ITokenSpeedCalculator
    {
        void Update(int totalDeltas);
        double GetTokensPerSecond();
        double GetAverageTokensPerSecond();
    }

    internal sealed class TokenSpeedCalculator : ITokenSpeedCalculator
    {
        private readonly Queue<(long ticksUtc, int deltas)> _window = new Queue<(long, int)>();
        private readonly long _windowTicks;
        private int _currentDeltas;
        private readonly ITimeProvider _timeProvider;
        private double _speedSum;
        private int _speedCount;

        public TokenSpeedCalculator(int windowSeconds = 5, ITimeProvider timeProvider = null)
        {
            _timeProvider = timeProvider ?? new SystemTimeProvider();
            _windowTicks = TimeSpan.FromSeconds(windowSeconds).Ticks;
        }

        /// <summary>
        /// Records the running count of received deltas; the first call starts the window, so the wait for the first delta is not counted.
        /// </summary>
        public void Update(int totalDeltas)
        {
            if (totalDeltas != _currentDeltas)
            {
                _currentDeltas = totalDeltas;
                _window.Enqueue((_timeProvider.UtcNowTicks(), totalDeltas));
            }
        }

        /// <summary>
        /// Gets the deltas per second over the sliding window; 0 until a second delta arrives.
        /// </summary>
        public double GetTokensPerSecond()
        {
            if (_window.Count == 0) return 0.0;

            long now = _timeProvider.UtcNowTicks();
            long cutoff = now - _windowTicks;

            // Keep at least one entry as the baseline for the delta.
            while (_window.Count > 1 && _window.Peek().ticksUtc < cutoff)
            {
                _window.Dequeue();
            }

            var (ticksUtc, deltas) = _window.Peek();
            double spanSeconds = TimeSpan.FromTicks(now - ticksUtc).TotalSeconds;

            if (spanSeconds <= 0) return 0.0;

            double speed = (_currentDeltas - deltas) / spanSeconds;

            if (speed > 0)
            {
                _speedSum += speed;
                _speedCount++;
            }

            return speed;
        }

        /// <summary>
        /// Gets the average of the positive samples taken by GetTokensPerSecond during this stream.
        /// </summary>
        public double GetAverageTokensPerSecond()
        {
            return _speedCount > 0 ? _speedSum / _speedCount : 0.0;
        }
    }
}
