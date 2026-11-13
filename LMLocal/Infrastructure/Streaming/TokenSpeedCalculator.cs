using System;
using System.Collections.Generic;
using LMLocal.Infrastructure.Time;

namespace LMLocal.Infrastructure.Streaming
{
    /// <summary>
    /// Measures streamed generation speed in tokens per second: a windowed reading for the status bar and the average of
    /// those readings for the final badge. On per-token servers deltas equal tokens; on buffering providers that send several
    /// tokens per chunk the estimated token count is used instead.
    /// Must be fed and sampled from a single lock (see StreamProcessor), it is not thread-safe on its own.
    /// </summary>
    internal interface ITokenSpeedCalculator
    {
        /// <summary>
        /// Records one streamed delta (reasoning, content or tool-call arguments).
        /// </summary>
        void Add(int chars, double estimatedTokens);

        /// <summary>
        /// Approximate speed over the sliding window; 0 until a second delta arrives.
        /// </summary>
        double GetTokensPerSecond();

        /// <summary>
        /// Average of the positive readings returned by <see cref="GetTokensPerSecond"/> during this stream; 0 if none.
        /// </summary>
        double GetAverageTokensPerSecond();
    }

    /// <summary>
    /// Default <see cref="ITokenSpeedCalculator"/>: the windowed reading for the status bar and the average of those
    /// readings for the final badge. It only accumulates numbers; estimating tokens from text is the estimator's job.
    /// </summary>
    internal sealed class TokenSpeedCalculator : ITokenSpeedCalculator
    {
        /// <summary>Average chunk size above which the provider is treated as buffering (dozens of tokens per chunk).</summary>
        private const double ChunkedCharsPerDelta = 16;

        /// <summary>Deltas after which a per-token provider is assumed; a chunked provider latches earlier.</summary>
        private const int ModeDecisionMinDeltas = 5;

        private struct Point
        {
            public long Ticks;
            public int Deltas;
            public int Chars;
            public double Estimate;
        }

        private readonly List<Point> _window = new List<Point>();
        private readonly long _windowTicks;
        private readonly ITimeProvider _timeProvider;

        private Point _total;
        private bool _chunked;
        private bool _modeDecided;
        private double _speedSum;
        private int _speedCount;

        /// <summary>
        /// Creates a calculator.
        /// </summary>
        public TokenSpeedCalculator(int windowSeconds = 5, ITimeProvider timeProvider = null)
        {
            _timeProvider = timeProvider ?? new SystemTimeProvider();
            _windowTicks = TimeSpan.FromSeconds(windowSeconds).Ticks;
        }

        /// <summary>
        /// Records one streamed delta and advances the cumulative counters of the round.
        /// </summary>
        public void Add(int chars, double estimatedTokens)
        {
            if (chars <= 0)
                return;

            _total.Ticks = _timeProvider.UtcNowTicks();
            _total.Deltas++;
            _total.Chars += chars;
            _total.Estimate += estimatedTokens;

            _window.Add(_total);

            if (!_modeDecided)
            {
                if ((double)_total.Chars / _total.Deltas > ChunkedCharsPerDelta)
                {
                    _chunked = true;
                    _modeDecided = true;
                }
                else if (_total.Deltas >= ModeDecisionMinDeltas)
                {
                    _modeDecided = true;
                }
            }
        }

        /// <summary>
        /// Gets the speed over the sliding window; 0 until a second delta arrives.
        /// </summary>
        public double GetTokensPerSecond()
        {
            if (_window.Count == 0) return 0.0;

            long now = _timeProvider.UtcNowTicks();
            long cutoff = now - _windowTicks;

            int drop = 0;
            while (drop + 1 < _window.Count && _window[drop + 1].Ticks <= cutoff) drop++;
            if (drop > 0) _window.RemoveRange(0, drop);

            Point baseline = _window[0];
            double spanSeconds = TimeSpan.FromTicks(now - baseline.Ticks).TotalSeconds;

            if (spanSeconds <= 0.0) return 0.0;

            double units = _chunked
                ? _total.Estimate - baseline.Estimate
                : _total.Deltas - baseline.Deltas;

            double speed = units / spanSeconds;

            if (speed <= 0.0) return 0.0;

            _speedSum += speed;
            _speedCount++;

            return speed;
        }

        /// <summary>
        /// Gets the average of the positive readings taken by <see cref="GetTokensPerSecond"/> during this stream.
        /// </summary>
        public double GetAverageTokensPerSecond()
        {
            return _speedCount > 0 ? _speedSum / _speedCount : 0.0;
        }
    }
}
