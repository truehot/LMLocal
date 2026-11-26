using System;
using LMLocal.Core.Exceptions;
using LMLocal.Core.Models;

namespace LMLocal.Application.ChatSessionStream
{
    /// <summary>
    /// Classification and delay computation for transient provider rate limits.
    /// </summary>
    internal static class RateLimitRetryPolicy
    {
        /// <summary>Total attempts allowed per round: the original request plus exactly one retry.</summary>
        internal const int MaxAttempts = 2;

        /// <summary>Delay used when the provider reports a rate limit without any server hint.</summary>
        internal const int FallbackDelaySeconds = 10;

        /// <summary>
        /// Upper bound on how long a retry may wait, in seconds. A server hint above it is not retried at all: such a
        /// wait would stall the session, so the original error is surfaced to the user instead.
        /// </summary>
        internal const int MaxRetryDelaySeconds = 300;

        /// <summary>Lower bound applied to the configured maximum wait.</summary>
        internal const int MinMaxDelaySeconds = 1;

        /// <summary>Upper bound applied to the configured maximum wait.</summary>
        internal const int MaxMaxDelaySeconds = 3600;

        /// <summary>
        /// Fraction of the reported hint that may be added as jitter.
        /// </summary>
        private const double JitterRatio = 0.2;

        /// <summary>Errors that describe a standing quota problem; waiting cannot help.</summary>
        private static readonly string[] PersistentMarkers = { "quota_exceeded", "insufficient_quota" };

        /// <summary>Errors that describe throttling, including proxies that answer 503 instead of 429.</summary>
        private static readonly string[] TransientMarkers = { "rate_limit", "rate_limit_error", "rate_limit_exceeded" };

        private static readonly Random RandomSource = new Random();
        private static readonly object RandomLock = new object();

        /// <summary>
        /// True when the failure is a transient throttle worth one retry, applying an explicit precedence:
        /// persistent quota (never retried, overrides even 429) &gt; 429 &gt; 503 with a transient marker.
        /// </summary>
        public static bool IsRateLimit(ApiException ex)
        {
            if (ex == null)
                return false;

            var info = ex.ErrorInfo;
            return IsRateLimit(ex.HttpStatusCode ?? info?.Code, info?.RawCode, info?.Type);
        }

        /// <summary>
        /// Same classification for a finished round result: the fields BuildErrorResult copied from the API error.
        /// </summary>
        public static bool IsRateLimit(StreamCompletionResult result)
        {
            if (result == null)
                return false;

            return IsRateLimit(result.HttpStatusCode, result.ErrorCode, result.ErrorType);
        }

        private static bool IsRateLimit(int? status, string code, string type)
        {
            // 1. Persistent quota (billing or a daily cap): waiting cannot help, and this overrides even 429.
            if (HasMarker(code, type, PersistentMarkers))
                return false;

            // 2. Plain throttle.
            if (status == 429)
                return true;

            // 3. Reverse proxies and providers that report throttling as 503.
            if (status == 503 && HasMarker(code, type, TransientMarkers))
                return true;

            // 4. Everything else, including a 400 carrying a transient marker and a 503 carrying only Retry-After.
            return false;
        }

        /// <summary>
        /// Computes the wait before the retry.
        /// </summary>
        public static bool TryGetDelay(ApiException ex, int maxDelaySeconds, out TimeSpan delay, out RetryAfterSource source)
        {
            delay = TimeSpan.Zero;
            source = RetryAfterSource.Fallback;

            int cap = Clamp(maxDelaySeconds, MinMaxDelaySeconds, MaxMaxDelaySeconds);
            var info = ex?.ErrorInfo;
            double? hint = info?.RetryAfterSeconds;

            if (!hint.HasValue)
            {
                double fallback = FallbackDelaySeconds * (1.0 + NextRandom() * JitterRatio);
                delay = TimeSpan.FromSeconds(Math.Min(fallback, cap));
                source = RetryAfterSource.Fallback;
                return true;
            }

            double seconds = hint.Value;

            if (seconds > cap)
                return false;

            source = info.RetryAfterSource == RetryAfterSource.Header
                ? RetryAfterSource.Header
                : RetryAfterSource.Body;

            if (seconds >= cap)
            {
                delay = TimeSpan.FromSeconds(cap);
                return true;
            }

            double headroom = cap - seconds;
            double jitter = Math.Min(seconds * JitterRatio, headroom) * NextRandom();
            delay = TimeSpan.FromSeconds(seconds + jitter);
            return true;
        }

        /// <summary>Clamps a value into the inclusive range [<paramref name="min"/>, <paramref name="max"/>].</summary>
        internal static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;

            return value > max ? max : value;
        }

        private static bool HasMarker(string code, string type, string[] markers)
        {
            foreach (var marker in markers)
            {
                if (string.Equals(code, marker, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (string.Equals(type, marker, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static double NextRandom()
        {
            lock (RandomLock)
            {
                return RandomSource.NextDouble();
            }
        }
    }
}
