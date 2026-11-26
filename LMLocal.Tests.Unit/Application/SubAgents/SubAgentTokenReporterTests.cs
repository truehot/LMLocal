using System;
using System.Collections.Generic;
using LMLocal.Application.SubAgents;
using LMLocal.Application.Tool;
using LMLocal.Core.Models;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.SubAgents
{
    /// <summary>Numbers the status bar shows for a SubAgent run: cumulative, throttled, speed reset after a round.</summary>
    [TestFixture]
    public class SubAgentTokenReporterTests
    {
        private sealed class Recorder : IProgress<ToolActivityEvent>
        {
            internal List<ToolActivityEvent> Events { get; } = new List<ToolActivityEvent>();

            public void Report(ToolActivityEvent value)
            {
                Events.Add(value);
            }
        }

        private FakeTimeProvider _clock;

        [SetUp]
        public void ResetClock()
        {
            _clock = new FakeTimeProvider(new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc));
        }

        private SubAgentTokenReporter CreateReporter(Recorder recorder)
        {
            return new SubAgentTokenReporter(recorder, _clock);
        }

        [Test]
        public void Report_FirstStats_AreSentAtOnce()
        {
            var recorder = new Recorder();
            var reporter = CreateReporter(recorder);

            reporter.Report(new TokenGenerationStats(10, 5.5));

            Assert.That(recorder.Events.Count, Is.EqualTo(1));
            Assert.That(recorder.Events[0].Tokens, Is.EqualTo(10));
            Assert.That(recorder.Events[0].TokensPerSecond, Is.EqualTo(5.5));
            Assert.That(recorder.Events[0].Step, Is.Null, "A token event must never look like a progress step");
            Assert.That(recorder.Events[0].AgentName, Is.Null, "A single run leaves naming to the UI");
        }

        [Test]
        public void Report_EventWithinInterval_IsThrottled()
        {
            var recorder = new Recorder();
            var reporter = CreateReporter(recorder);

            reporter.Report(new TokenGenerationStats(10, 5));

            _clock.Advance(SubAgentTokenReporter.MinInterval - TimeSpan.FromMilliseconds(1));
            reporter.Report(new TokenGenerationStats(20, 5));

            Assert.That(recorder.Events.Count, Is.EqualTo(1), "Two events in one interval would flicker the status bar");

            _clock.Advance(TimeSpan.FromSeconds(1));
            reporter.Report(new TokenGenerationStats(30, 5));

            Assert.That(recorder.Events.Count, Is.EqualTo(2));
            Assert.That(recorder.Events[1].Tokens, Is.EqualTo(30));
        }

        [Test]
        public void Report_WithoutClock_IsNotThrottled()
        {
            var recorder = new Recorder();
            var reporter = new SubAgentTokenReporter(recorder, null);

            reporter.Report(new TokenGenerationStats(10, 5));
            reporter.Report(new TokenGenerationStats(20, 5));

            Assert.That(recorder.Events.Count, Is.EqualTo(2));
        }

        [Test]
        public void Report_ContinuesFromTheFinishedRounds()
        {
            var recorder = new Recorder();
            var reporter = CreateReporter(recorder);

            reporter.Report(new TokenGenerationStats(10, 5));
            reporter.EndRound();

            reporter.Report(new TokenGenerationStats(4, 7));

            Assert.That(recorder.Events[recorder.Events.Count - 1].Tokens, Is.EqualTo(14),
                "A round that restarts at 4 tokens must continue from the 10 tokens of the previous one");
            Assert.That(recorder.Events[recorder.Events.Count - 1].TokensPerSecond, Is.EqualTo(7),
                "The first event of a new round is sent at once, not throttled by the previous round");
            Assert.That(reporter.TotalTokens, Is.EqualTo(14));
        }

        [Test]
        public void EndRound_SendsTheLastValueEvenWhenTheLiveEventWasThrottled()
        {
            var recorder = new Recorder();
            var reporter = CreateReporter(recorder);

            reporter.Report(new TokenGenerationStats(10, 5));

            _clock.Advance(TimeSpan.FromMilliseconds(10));
            reporter.Report(new TokenGenerationStats(25, 5));
            reporter.EndRound();

            Assert.That(recorder.Events.Count, Is.EqualTo(2));
            Assert.That(recorder.Events[1].Tokens, Is.EqualTo(25), "The dropped live value of the round must still reach the UI");
        }

        [Test]
        public void EndRound_ExactCount_ReplacesTheEstimateOfTheRound()
        {
            var recorder = new Recorder();
            var reporter = CreateReporter(recorder);

            reporter.Report(new TokenGenerationStats(10, 5));
            reporter.EndRound(35);
            reporter.EndRound();

            Assert.That(recorder.Events[1].Tokens, Is.EqualTo(35),
                "The server's count includes the tool-call arguments that never reach the live stats");
            Assert.That(reporter.TotalTokens, Is.EqualTo(35), "An empty round adds nothing");
        }

        [Test]
        public void EndRound_ToolCallOnlyRound_CountsTheServerTokens()
        {
            var recorder = new Recorder();
            var reporter = CreateReporter(recorder);

            reporter.EndRound(40);

            Assert.That(recorder.Events.Count, Is.EqualTo(1));
            Assert.That(recorder.Events[0].Tokens, Is.EqualTo(40), "A round with no text chunk still adds its tokens");
        }

        [Test]
        public void EndRound_ResetsTheSpeed()
        {
            var recorder = new Recorder();
            var reporter = CreateReporter(recorder);

            reporter.Report(new TokenGenerationStats(10, 95.3));
            reporter.EndRound();

            Assert.That(recorder.Events[recorder.Events.Count - 1].TokensPerSecond, Is.EqualTo(0),
                "Between rounds nothing is generated; the last speed would look frozen");
        }

        [Test]
        public void Report_WithoutSink_DoesNothing()
        {
            var reporter = new SubAgentTokenReporter(null, _clock);

            Assert.That(reporter.IsEnabled, Is.False);

            Assert.DoesNotThrow(() =>
            {
                reporter.Report(new TokenGenerationStats(10, 5));
                reporter.EndRound();
            });
        }
    }
}
