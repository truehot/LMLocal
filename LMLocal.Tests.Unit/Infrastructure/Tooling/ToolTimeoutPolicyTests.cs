using System;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.Abstractions;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling
{
    /// <summary>
    /// Guards the single place where the timeout arithmetic lives: a tool declares only its default, the caller may cut
    /// it down, and the watchdog is always the default plus the grace.
    /// </summary>
    [TestFixture]
    public class ToolTimeoutPolicyTests
    {
        private static ToolExecutionLimits Limits(int defaultSeconds, int? graceSeconds = null)
        {
            return new ToolExecutionLimits(
                TimeSpan.FromSeconds(defaultSeconds),
                graceSeconds.HasValue ? TimeSpan.FromSeconds(graceSeconds.Value) : (TimeSpan?)null);
        }

        [Test]
        public void ResolveExternal_NullLimits_ReturnsNull()
        {
            Assert.That(ToolTimeoutPolicy.ResolveExternal(null), Is.Null,
                "A tool that declares nothing keeps the caller's own default.");
        }

        [Test]
        public void ResolveExternal_AddsDefaultGrace()
        {
            Assert.That(
                ToolTimeoutPolicy.ResolveExternal(Limits(600)),
                Is.EqualTo(TimeSpan.FromSeconds(610)),
                "The watchdog must outlast the tool's own timeout by the declared grace.");
        }

        [Test]
        public void ResolveExternal_ExplicitGrace_WinsOverDefault()
        {
            Assert.That(
                ToolTimeoutPolicy.ResolveExternal(Limits(600, graceSeconds: 3)),
                Is.EqualTo(TimeSpan.FromSeconds(603)));
        }

        [Test]
        public void ResolveApplied_NullLimits_ReturnsNull()
        {
            Assert.That(ToolTimeoutPolicy.ResolveApplied(null, TimeSpan.FromSeconds(10)), Is.Null);
        }

        [Test]
        public void ResolveApplied_NoAvailable_ReturnsDeclaredDefault()
        {
            Assert.That(ToolTimeoutPolicy.ResolveApplied(Limits(600), null), Is.EqualTo(TimeSpan.FromSeconds(600)));
        }

        [Test]
        public void ResolveApplied_AvailableAboveDefault_ReturnsDefault()
        {
            Assert.That(
                ToolTimeoutPolicy.ResolveApplied(Limits(600), TimeSpan.FromSeconds(900)),
                Is.EqualTo(TimeSpan.FromSeconds(600)),
                "The caller can only take time away; the tool never waits longer than it declares.");
        }

        [Test]
        public void ResolveApplied_AvailableBelowDefault_ReturnsAvailableMinusGrace()
        {
            Assert.That(
                ToolTimeoutPolicy.ResolveApplied(Limits(600), TimeSpan.FromSeconds(94)),
                Is.EqualTo(TimeSpan.FromSeconds(84)),
                "The tool's own teardown grace comes out of the caller-provided budget too.");
        }

        [Test]
        public void ResolveApplied_BudgetThirtySeconds_LeavesTwentyAfterGrace()
        {
            Assert.That(
                ToolTimeoutPolicy.ResolveApplied(Limits(600), TimeSpan.FromSeconds(30)),
                Is.EqualTo(TimeSpan.FromSeconds(20)));
        }

        [Test]
        public void IsBudgetExhausted_NullLimitsOrAvailable_IsFalse()
        {
            Assert.That(ToolTimeoutPolicy.IsBudgetExhausted(null, TimeSpan.FromSeconds(1)), Is.False);
            Assert.That(ToolTimeoutPolicy.IsBudgetExhausted(Limits(600), null), Is.False);
        }

        [Test]
        public void IsBudgetExhausted_AboveGrace_IsFalse()
        {
            Assert.That(ToolTimeoutPolicy.IsBudgetExhausted(Limits(600), TimeSpan.FromSeconds(11)), Is.False);
        }

        [TestCase(10)]
        [TestCase(0)]
        [TestCase(-5)]
        public void IsBudgetExhausted_AtOrBelowGrace_IsTrue(int seconds)
        {
            // Grace (10 s) is reserved for the tool's own teardown, so the tool has nothing to run with.
            Assert.That(ToolTimeoutPolicy.IsBudgetExhausted(Limits(600), TimeSpan.FromSeconds(seconds)), Is.True);
        }

        [Test]
        public void ToWholeSeconds_RoundsToNearestSecond()
        {
            Assert.That(ToolTimeoutPolicy.ToWholeSeconds(TimeSpan.FromSeconds(90.4)), Is.EqualTo(90));
            Assert.That(ToolTimeoutPolicy.ToWholeSeconds(TimeSpan.FromSeconds(90.6)), Is.EqualTo(91));
        }
    }
}
