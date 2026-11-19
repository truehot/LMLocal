using System;
using System.Threading.Tasks;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Core.Exceptions;
using LMLocal.Core.Models;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Internal
{
    /// <summary>
    /// Classification and delay computation of <see cref="RateLimitRetryPolicy"/>.
    /// The policy is checked directly so that the precedence rules are pinned down independently of the retry loop.
    /// </summary>
    [TestFixture]
    public class RateLimitRetryPolicyTests
    {
        private const int DefaultCap = 300;

        private static ApiException Create(
            int? status,
            double? hint = null,
            string rawCode = null,
            string type = null,
            int? bodyCode = null)
        {
            var info = new ApiErrorInfo
            {
                Message = "provider error",
                Code = bodyCode ?? status,
                RawCode = rawCode,
                Type = type,
                RetryAfterSeconds = hint,
                RetryAfterSource = hint.HasValue ? RetryAfterSource.Header : RetryAfterSource.None
            };

            return new ApiException(info, status);
        }

        // --- classification ---

        [Test]
        public void IsRateLimit_Http429_IsTrue()
        {
            Assert.That(RateLimitRetryPolicy.IsRateLimit(Create(429)), Is.True);
        }

        [Test]
        public void IsRateLimit_Http429WithQuotaMarker_IsFalse()
        {
            // A standing quota problem is reported through the same status, but waiting never helps.
            Assert.That(RateLimitRetryPolicy.IsRateLimit(Create(429, rawCode: "insufficient_quota")), Is.False);
            Assert.That(RateLimitRetryPolicy.IsRateLimit(Create(429, type: "quota_exceeded")), Is.False);
        }

        [Test]
        public void IsRateLimit_TransientMarkerOn503_IsTrue()
        {
            Assert.That(RateLimitRetryPolicy.IsRateLimit(Create(503, type: "rate_limit_error")), Is.True);
            Assert.That(RateLimitRetryPolicy.IsRateLimit(Create(503, rawCode: "rate_limit_exceeded")), Is.True);
        }

        [Test]
        public void IsRateLimit_Http503WithRetryAfterOnly_IsFalse()
        {
            // Guards against ApiErrorInfo.IsRateLimit, which is broad enough to accept this.
            var ex = Create(503, hint: 30);

            Assert.That(ex.ErrorInfo.IsRateLimit, Is.True, "precondition: the broad flag accepts it");
            Assert.That(RateLimitRetryPolicy.IsRateLimit(ex), Is.False);
        }

        [Test]
        public void IsRateLimit_Http400WithMarkerAndHint_IsFalse()
        {
            var ex = Create(400, hint: 30, type: "rate_limit");

            Assert.That(RateLimitRetryPolicy.IsRateLimit(ex), Is.False);
        }

        [Test]
        public void IsRateLimit_PersistentQuotaMarkers_AreFalse()
        {
            Assert.That(RateLimitRetryPolicy.IsRateLimit(Create(400, rawCode: "quota_exceeded")), Is.False);
            Assert.That(RateLimitRetryPolicy.IsRateLimit(Create(400, type: "insufficient_quota")), Is.False);
        }

        [Test]
        public void IsRateLimit_NullException_IsFalse()
        {
            Assert.That(RateLimitRetryPolicy.IsRateLimit(null), Is.False);
        }

        // --- delay ---

        [Test]
        public void TryGetDelay_Hint_AddsJitterWithinTwentyPercent()
        {
            var ex = Create(429, hint: 30);

            Assert.That(RateLimitRetryPolicy.TryGetDelay(ex, DefaultCap, out TimeSpan delay, out RetryAfterSource source), Is.True);
            Assert.That(delay.TotalSeconds, Is.GreaterThanOrEqualTo(30).And.LessThan(36));
            Assert.That(source, Is.EqualTo(RetryAfterSource.Header));
        }

        [Test]
        public void TryGetDelay_NoHint_UsesJitteredFallback()
        {
            var ex = Create(429);

            Assert.That(RateLimitRetryPolicy.TryGetDelay(ex, DefaultCap, out TimeSpan delay, out RetryAfterSource source), Is.True);
            Assert.That(delay.TotalSeconds, Is.GreaterThanOrEqualTo(10).And.LessThan(12));
            Assert.That(source, Is.EqualTo(RetryAfterSource.Fallback));
        }

        [Test]
        public void TryGetDelay_HintAboveCap_IsNotRetried()
        {
            var ex = Create(429, hint: 600);

            Assert.That(RateLimitRetryPolicy.TryGetDelay(ex, DefaultCap, out _, out _), Is.False,
                "the server asks for more than the maximum acceptable wait, so the original error must be surfaced");
        }

        [Test]
        public void TryGetDelay_HintEqualToCap_WaitsExactlyCap()
        {
            var ex = Create(429, hint: DefaultCap);

            Assert.That(RateLimitRetryPolicy.TryGetDelay(ex, DefaultCap, out TimeSpan delay, out _), Is.True);
            Assert.That(delay.TotalSeconds, Is.EqualTo(DefaultCap), "the cap is a hard ceiling, so no jitter is added");
        }

        [Test]
        public void TryGetDelay_ZeroHint_RetriesImmediately()
        {
            var ex = Create(429, hint: 0);

            Assert.That(RateLimitRetryPolicy.TryGetDelay(ex, DefaultCap, out TimeSpan delay, out _), Is.True);
            Assert.That(delay, Is.EqualTo(TimeSpan.Zero));
        }

        [Test]
        public void TryGetDelay_BodySourceIsPreserved()
        {
            var info = new ApiErrorInfo { Message = "reuse a key", Code = 429, RetryAfterSeconds = 12, RetryAfterSource = RetryAfterSource.Body };
            var ex = new ApiException(info, 429);

            Assert.That(RateLimitRetryPolicy.TryGetDelay(ex, DefaultCap, out TimeSpan delay, out RetryAfterSource source), Is.True);
            Assert.That(delay.TotalSeconds, Is.GreaterThanOrEqualTo(12).And.LessThan(14.4));
            Assert.That(source, Is.EqualTo(RetryAfterSource.Body));
        }

        [Test]
        public void TryGetDelay_NonPositiveCap_IsClampedToOneSecond()
        {
            // Zero and negative configured values must not turn into an instant retry.
            var ex = Create(429);

            Assert.That(RateLimitRetryPolicy.TryGetDelay(ex, 0, out TimeSpan fromZero, out _), Is.True);
            Assert.That(fromZero.TotalSeconds, Is.EqualTo(1));
            Assert.That(RateLimitRetryPolicy.TryGetDelay(ex, -5, out TimeSpan fromNegative, out _), Is.True);
            Assert.That(fromNegative.TotalSeconds, Is.EqualTo(1));
        }

        [Test]
        public void TryGetDelay_HugeCap_IsClampedToAnHour()
        {
            var ex = Create(429, hint: 2);

            Assert.That(RateLimitRetryPolicy.TryGetDelay(ex, 99999, out TimeSpan delay, out _), Is.True);
            Assert.That(delay.TotalSeconds, Is.GreaterThanOrEqualTo(2).And.LessThan(2.4),
                "the configured ceiling must be clamped, not honoured literally");
        }

        [Test]
        public void MaxAttempts_AllowsExactlyOneRetry()
        {
            Assert.That(RateLimitRetryPolicy.MaxAttempts, Is.EqualTo(2));
        }

        [Test]
        public void Clamp_BoundsBothEnds()
        {
            Assert.That(RateLimitRetryPolicy.Clamp(-1, 1, 3600), Is.EqualTo(1));
            Assert.That(RateLimitRetryPolicy.Clamp(0, 1, 3600), Is.EqualTo(1));
            Assert.That(RateLimitRetryPolicy.Clamp(300, 1, 3600), Is.EqualTo(300));
            Assert.That(RateLimitRetryPolicy.Clamp(99999, 1, 3600), Is.EqualTo(3600));
        }
    }
}
