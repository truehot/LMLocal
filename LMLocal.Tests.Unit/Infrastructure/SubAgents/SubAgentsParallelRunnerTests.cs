using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.SubAgents;
using LMLocal.Application.Tool;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.SubAgents;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.SubAgents
{
    [TestFixture]
    public class SubAgentsParallelRunnerTests
    {
        private SubAgentsParallelRunner _runner;

        [SetUp]
        public void SetUp() => _runner = new SubAgentsParallelRunner();

        private static ToolCallRecord Call(string name, string callId = null)
            => new ToolCallRecord { FunctionName = name, CallId = callId ?? name, ArgumentsJson = "{}" };

        private static void TrackMax(ref int max, int value)
        {
            int prev;
            do
            {
                prev = Volatile.Read(ref max);
                if (value <= prev)
                    return;
            }
            while (Interlocked.CompareExchange(ref max, value, prev) != prev);
        }

        [Test]
        public async Task RunAsync_EmptyList_ReturnsEmptyResult()
        {
            var result = await _runner.RunAsync(new List<ToolCallRecord>(), (c, ct) => Task.FromResult(new ToolExecutionResult()), 2, CancellationToken.None);

            Assert.That(result.Results, Is.Empty);
            Assert.That(result.Succeeded, Is.EqualTo(0));
            Assert.That(result.Failed, Is.EqualTo(0));
        }

        [Test]
        public async Task RunAsync_RunsCallsConcurrently()
        {
            int active = 0;
            int max = 0;

            var calls = new List<ToolCallRecord> { Call("a"), Call("b"), Call("c") };

            var result = await _runner.RunAsync(calls, async (call, ct) =>
            {
                var now = Interlocked.Increment(ref active);
                TrackMax(ref max, now);
                await Task.Delay(120, ct).ConfigureAwait(false);
                Interlocked.Decrement(ref active);
                return new ToolExecutionResult { ToolName = call.FunctionName, Result = call.FunctionName };
            }, 3, CancellationToken.None);

            Assert.That(max, Is.GreaterThanOrEqualTo(2), "Calls should overlap (run concurrently).");

            Assert.That(result.Results.Count, Is.EqualTo(3));
        }

        [Test]
        public async Task RunAsync_BoundsConcurrencyToMaxParallel()
        {
            int active = 0;
            int max = 0;
            int completed = 0;

            var calls = Enumerable.Range(0, 5).Select(i => Call("a" + i)).ToList();

            var result = await _runner.RunAsync(calls, async (call, ct) =>
            {
                var now = Interlocked.Increment(ref active);
                TrackMax(ref max, now);
                await Task.Delay(60, ct).ConfigureAwait(false);
                Interlocked.Decrement(ref active);
                Interlocked.Increment(ref completed);
                return new ToolExecutionResult { ToolName = call.FunctionName, Result = call.FunctionName };
            }, 2, CancellationToken.None);

            Assert.That(max, Is.EqualTo(2), "Five calls with maxParallel = 2 must run 2 -> 2 -> 1 (at most 2 in flight).");
            Assert.That(completed, Is.EqualTo(5), "All calls must eventually run.");
            Assert.That(result.Results.Count, Is.EqualTo(5));
        }

        [Test]
        public async Task RunAsync_PreservesInputOrder_IndependentOfCompletionOrder()
        {
            var calls = new List<ToolCallRecord> { Call("slow"), Call("fast") };

            var result = await _runner.RunAsync(calls, async (call, ct) =>
            {
                await Task.Delay(call.FunctionName == "slow" ? 150 : 10, ct).ConfigureAwait(false);
                return new ToolExecutionResult { ToolName = call.FunctionName, Result = call.FunctionName };
            }, 2, CancellationToken.None);

            Assert.That(result.Results[0].ToolName, Is.EqualTo("slow"));
            Assert.That(result.Results[1].ToolName, Is.EqualTo("fast"));
        }

        [Test]
        public async Task RunAsync_OneCallFails_GroupStillCompletes()
        {
            var calls = new List<ToolCallRecord> { Call("ok"), Call("bad") };

            var result = await _runner.RunAsync(calls, (call, ct) =>
            {
                if (call.FunctionName == "bad")
                    return Task.FromResult(new ToolExecutionResult { ToolName = call.FunctionName, Error = "boom" });

                return Task.FromResult(new ToolExecutionResult { ToolName = call.FunctionName, Result = "ok" });
            }, 2, CancellationToken.None);

            Assert.That(result.Results.Count, Is.EqualTo(2));
            Assert.That(result.Results[0].Error, Is.Null.Or.Empty);
            Assert.That(result.Results[1].Error, Is.EqualTo("boom"));
            Assert.That(result.Succeeded, Is.EqualTo(1));
            Assert.That(result.Failed, Is.EqualTo(1));
            Assert.That(result.Errors, Has.Count.EqualTo(1));
            Assert.That(result.Errors[0], Is.EqualTo("boom"));
        }

        [Test]
        public async Task RunAsync_OneCallThrows_IsCapturedAsError()
        {
            var calls = new List<ToolCallRecord> { Call("boom"), Call("ok") };

            var result = await _runner.RunAsync(calls, (call, ct) =>
            {
                if (call.FunctionName == "boom")
                    throw new InvalidOperationException("boom msg");

                return Task.FromResult(new ToolExecutionResult { ToolName = call.FunctionName, Result = "ok" });
            }, 2, CancellationToken.None);

            Assert.That(result.Results.Count, Is.EqualTo(2));
            Assert.That(result.Results[0].Error, Does.Contain("boom msg"));
            Assert.That(result.Results[1].Error, Is.Null.Or.Empty);
        }

        [Test]
        public void RunAsync_Cancellation_Propagates()
        {
            var calls = new List<ToolCallRecord> { Call("a"), Call("b") };

            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(50);

                Assert.CatchAsync<OperationCanceledException>(() =>
                    _runner.RunAsync(calls, async (call, ct) =>
                    {
                        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                        return new ToolExecutionResult();
                    }, 2, cts.Token));
            }
        }

        [Test]
        public void RunAsync_MaxParallelLessThanOne_Throws()
        {
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                _runner.RunAsync(
                    new List<ToolCallRecord> { Call("a") },
                    (c, ct) => Task.FromResult(new ToolExecutionResult()),
                    0,
                    CancellationToken.None));
        }

        [Test]
        public void RunAsync_NullCalls_Throws()
        {
            Assert.ThrowsAsync<ArgumentNullException>(() =>
                _runner.RunAsync(null, (c, ct) => Task.FromResult(new ToolExecutionResult()), 2, CancellationToken.None));
        }

        [Test]
        public void RunAsync_NullWork_Throws()
        {
            Assert.ThrowsAsync<ArgumentNullException>(() =>
                _runner.RunAsync(new List<ToolCallRecord> { Call("a") }, null, 2, CancellationToken.None));
        }
    }
}
