using System;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Tool;
using LMLocal.Core.Models;

namespace LMLocal.Application.SubAgents
{
    /// <summary>Turns the live token stats of a SubAgent run into throttled token events, cumulative over its rounds.</summary>
    internal sealed class SubAgentTokenReporter
    {
        /// <summary>Minimum gap between two live events.</summary>
        internal static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(250);

        private readonly IProgress<ToolActivityEvent> _progress;
        private readonly ITimeProvider _clock;

        private long? _lastReportTicks;
        private int _finishedTokens;
        private int _roundTokens;
        private double _speed;

        /// <summary>Without a clock every live event is sent.</summary>
        internal SubAgentTokenReporter(IProgress<ToolActivityEvent> progress, ITimeProvider clock)
        {
            _progress = progress;
            _clock = clock;
        }

        /// <summary>True when the run has a sink; without one no event is ever built.</summary>
        internal bool IsEnabled
        {
            get { return _progress != null; }
        }

        /// <summary>Tokens of the run so far: the finished rounds plus the round in progress.</summary>
        internal int TotalTokens
        {
            get { return _finishedTokens + _roundTokens; }
        }

        /// <summary>Live stats of the round in progress, sent at most once per <see cref="MinInterval"/>.</summary>
        internal void Report(TokenGenerationStats stats)
        {
            if (_progress == null)
                return;

            _roundTokens = Math.Max(_roundTokens, stats.TotalTokens);

            if (stats.TokensPerSecond > 0)
                _speed = stats.TokensPerSecond;

            if (_clock != null)
            {
                long now = _clock.UtcNowTicks();
                if (_lastReportTicks.HasValue && now - _lastReportTicks.Value < MinInterval.Ticks)
                    return;

                _lastReportTicks = now;
            }

            ReportNow();
        }

        /// <summary>Closes the round with the server's exact count when known (it includes tool-call arguments); speed drops to 0.</summary>
        internal void EndRound(int? completionTokens = null)
        {
            if (_progress == null)
                return;

            if (completionTokens.HasValue && completionTokens.Value > 0)
                _roundTokens = completionTokens.Value;

            _finishedTokens += _roundTokens;
            _roundTokens = 0;
            _speed = 0;
            _lastReportTicks = null;

            ReportNow();
        }

        private void ReportNow()
        {
            _progress.Report(new ToolActivityEvent
            {
                Tokens = TotalTokens,
                TokensPerSecond = _speed
            });
        }
    }
}
