using System;
using LMLocal.Core.Models;

namespace LMLocal.Application.ChatSessionStream
{
    /// <summary>
    /// Phase of a rate-limit wait notification.
    /// </summary>
    internal enum RateLimitWaitPhase
    {
        /// <summary>The wait is about to start; the delay still lies ahead.</summary>
        Begin = 0,

        /// <summary>The wait is over; the retry is about to be sent (or the session was cancelled meanwhile).</summary>
        End = 1
    }

    /// <summary>
    /// Notification about a rate-limit wait.
    /// </summary>
    internal readonly struct RateLimitWaitInfo
    {
        public RateLimitWaitInfo(
            string waitId,
            int attempt,
            int maxAttempts,
            TimeSpan delay,
            RetryAfterSource source,
            RateLimitWaitPhase phase)
        {
            WaitId = waitId;
            Attempt = attempt;
            MaxAttempts = maxAttempts;
            Delay = delay;
            Source = source;
            Phase = phase;
        }

        /// <summary>Identifier of this wait; the UI clears a banner only when the id matches.</summary>
        public string WaitId { get; }

        /// <summary>1-based number of the attempt that starts after the wait.</summary>
        public int Attempt { get; }

        /// <summary>Total attempts allowed for the round.</summary>
        public int MaxAttempts { get; }

        /// <summary>How long the round is going to wait.</summary>
        public TimeSpan Delay { get; }

        /// <summary>Where the delay came from (server body, server header or policy fallback).</summary>
        public RetryAfterSource Source { get; }

        /// <summary>Whether this notification opens or closes the wait.</summary>
        public RateLimitWaitPhase Phase { get; }
    }
}
