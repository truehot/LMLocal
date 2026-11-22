using System;
using System.Collections.Generic;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSession;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Application.Usage;
using LMLocal.Core.Models;
using LMLocal.Core.Models.Usage;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.Usage
{
    /// <summary>
    /// Covers the sums a request reports: tokens over every round, generation speed only over the measured rounds,
    /// context peak and the estimate that stands in for a round the provider reported nothing about.
    /// </summary>
    [TestFixture]
    public class UsageAccumulatorTests
    {
        private static StreamCompletionResult Round(int? prompt, int? completion, double tokensPerSecond = 0d, int toolCalls = 0)
        {
            var result = new StreamCompletionResult
            {
                ContentResponse = "text",
                TokensPerSecond = tokensPerSecond,
                StopReason = StreamStopReason.Completed
            };

            if (prompt.HasValue || completion.HasValue)
            {
                result.TokenUsage = new TokenUsageMetadata
                {
                    PromptTokens = prompt,
                    CompletionTokens = completion,
                    CachedTokens = prompt.HasValue ? prompt.Value / 2 : (int?)null
                };
            }

            if (toolCalls > 0)
            {
                var calls = new List<ToolCallRecord>();
                for (int i = 0; i < toolCalls; i++)
                    calls.Add(new ToolCallRecord { Index = i, FunctionName = "tool", ArgumentsJson = "{}" });

                result.ToolCalls = calls;
            }

            return result;
        }

        [Test]
        public void Add_SumsTokensRoundsAndToolCallsOverEveryRound()
        {
            var usage = new UsageAccumulator();

            usage.Add(new[]
            {
                Round(100, 10, toolCalls: 2),
                Round(150, 20, toolCalls: 1)
            });

            Assert.That(usage.Rounds, Is.EqualTo(2));
            Assert.That(usage.ToolCalls, Is.EqualTo(3));
            Assert.That(usage.Prompt, Is.EqualTo(250));
            Assert.That(usage.Completion, Is.EqualTo(30));
            Assert.That(usage.Cached, Is.EqualTo(125));
        }

        [Test]
        public void Add_ContextPeak_IsTheLargestPromptOfTheRounds()
        {
            var usage = new UsageAccumulator();

            usage.Add(Round(100, 10));
            usage.Add(Round(900, 20));

            Assert.That(usage.ContextPeak, Is.EqualTo(900));
        }

        [Test]
        public void Add_GenerationTime_IgnoresRoundsWithoutMeasuredSpeed()
        {
            var usage = new UsageAccumulator();

            // 100 tokens at 10 t/s = 10 s, 100 tokens at 20 t/s = 5 s.
            usage.Add(Round(1000, 100, tokensPerSecond: 10d));
            usage.Add(Round(1000, 100, tokensPerSecond: 20d));

            // This round has no measured speed: its tokens must not enter the numerator, or the speed would be too high.
            usage.Add(Round(1000, 100, tokensPerSecond: 0d));

            Assert.That(usage.GenCompletion, Is.EqualTo(200));
            Assert.That(usage.GenMs, Is.EqualTo(15000));
            Assert.That(usage.Completion, Is.EqualTo(300), "Every reported completion still counts as tokens.");
        }

        [Test]
        public void Add_FailedRound_IsNotEstimated()
        {
            var failed = Round(null, null);
            failed.StopReason = StreamStopReason.Error;
            failed.ErrorMessage = "provider stream error";

            var usage = new UsageAccumulator();
            usage.Add(failed);

            Assert.That(usage.Rounds, Is.EqualTo(1));
            Assert.That(usage.Estimated, Is.False, "A failed round has unknown numbers, not estimated ones.");
            Assert.That(usage.Completion, Is.Null);
        }

        [Test]
        public void Add_SuccessfulRoundWithoutUsage_IsEstimated()
        {
            var usage = new UsageAccumulator();
            usage.Add(Round(null, null));

            Assert.That(usage.Estimated, Is.True);
            Assert.That(usage.Completion, Is.GreaterThan(0), "The output size is estimated from the answer text.");
        }

        [Test]
        public void ApplyTo_CopiesTheAccumulatedNumbers()
        {
            var usage = new UsageAccumulator();
            usage.Add(Round(100, 10, tokensPerSecond: 10d));

            var record = new UsageRecord { Model = "qwen" };
            usage.ApplyTo(record);

            Assert.That(record.Prompt, Is.EqualTo(100));
            Assert.That(record.Completion, Is.EqualTo(10));
            Assert.That(record.ContextPeak, Is.EqualTo(100));
            Assert.That(record.Rounds, Is.EqualTo(1));
            Assert.That(record.TokensPerSecond, Is.EqualTo(10d).Within(0.01));
            Assert.That(record.Model, Is.EqualTo("qwen"), "Identity fields of the record are left alone.");
        }
    }

    /// <summary>
    /// Covers the mapping of the chat outcomes onto the values the usage file stores.
    /// </summary>
    [TestFixture]
    public class UsageOutcomeMapperTests
    {
        [Test]
        public void MapSessionTerminal_CompletedRequest_IsOk()
        {
            UsageOutcome outcome;
            string error;

            UsageOutcomeMapper.MapSessionTerminal(
                ChatSessionState.CompactingHistory, new List<StreamCompletionResult>(), null, null, out outcome, out error);

            Assert.That(outcome, Is.EqualTo(UsageOutcome.Ok));
            Assert.That(error, Is.Null);
        }

        [Test]
        public void MapSessionTerminal_UserStop_IsCancelled()
        {
            UsageOutcome outcome;
            string error;

            UsageOutcomeMapper.MapSessionTerminal(
                ChatSessionState.Cancelled, new List<StreamCompletionResult>(), null, null, out outcome, out error);

            Assert.That(outcome, Is.EqualTo(UsageOutcome.Cancelled));
        }

        [Test]
        public void MapSessionTerminal_LengthStop_IsIncomplete()
        {
            var last = new StreamCompletionResult { FinishReason = "length", ErrorMessage = "length limit reached" };
            UsageOutcome outcome;
            string error;

            UsageOutcomeMapper.MapSessionTerminal(
                ChatSessionState.Error, new[] { last }, null, last.ErrorMessage, out outcome, out error);

            Assert.That(outcome, Is.EqualTo(UsageOutcome.Incomplete));
            Assert.That(error, Is.EqualTo("length limit reached"));
        }

        [Test]
        public void MapSessionTerminal_Failure_KeepsTheFirstLineOfTheError()
        {
            var exception = new InvalidOperationException("first line\r\nsecond line");
            UsageOutcome outcome;
            string error;

            UsageOutcomeMapper.MapSessionTerminal(
                ChatSessionState.Error, new List<StreamCompletionResult>(), exception, null, out outcome, out error);

            Assert.That(outcome, Is.EqualTo(UsageOutcome.Error));
            Assert.That(error, Is.EqualTo("first line"));
        }

        [Test]
        public void MapCompaction_OnlyAUserStopIsACancellation()
        {
            Assert.That(UsageOutcomeMapper.MapCompaction(CompactionOutcome.Cancelled), Is.EqualTo(UsageOutcome.Cancelled));
            Assert.That(UsageOutcomeMapper.MapCompaction(CompactionOutcome.Failed), Is.EqualTo(UsageOutcome.Ok));
            Assert.That(UsageOutcomeMapper.MapCompaction(CompactionOutcome.Success), Is.EqualTo(UsageOutcome.Ok));
        }

        [Test]
        public void MapStreamRound_UnexpectedCancelOfASubagentIsAFailure()
        {
            Assert.That(UsageOutcomeMapper.MapStreamRound(StreamRoundOutcome.Completed), Is.EqualTo(UsageOutcome.Ok));
            Assert.That(UsageOutcomeMapper.MapStreamRound(StreamRoundOutcome.Incomplete), Is.EqualTo(UsageOutcome.Incomplete));
            Assert.That(UsageOutcomeMapper.MapStreamRound(StreamRoundOutcome.Cancelled), Is.EqualTo(UsageOutcome.Error));
            Assert.That(UsageOutcomeMapper.MapStreamRound(StreamRoundOutcome.Error), Is.EqualTo(UsageOutcome.Error));
        }

        [Test]
        public void FirstLine_TrimsAndCapsTheErrorText()
        {
            Assert.That(UsageOutcomeMapper.FirstLine("  a\nb  "), Is.EqualTo("a"));
            Assert.That(UsageOutcomeMapper.FirstLine("   "), Is.Null);
            Assert.That(UsageOutcomeMapper.FirstLine(new string('x', 400)).Length, Is.EqualTo(300));
        }
    }
}
