using System.Collections.Generic;
using LMLocal.Application.SubAgents;
using LMLocal.Application.Tool;
using LMLocal.Core.Models;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.SubAgents
{
    [TestFixture]
    public class SubAgentRecoveryPolicyTests
    {
        private const int ManyRounds = 20;

        private static SubAgentRecoveryPolicy Policy(bool loopRecoveryEnabled = true, int maxRounds = ManyRounds)
            => new SubAgentRecoveryPolicy(new ToolCallLoopDetector(), loopRecoveryEnabled, maxRounds);

        private static StreamCompletionResult Calls(string arguments)
        {
            return new StreamCompletionResult
            {
                StopReason = StreamStopReason.Completed,
                ToolCalls = new[] { new ToolCallRecord { CallId = "c", FunctionName = "find_files", ArgumentsJson = arguments } }
            };
        }

        private static StreamCompletionResult Aborted()
        {
            return new StreamCompletionResult
            {
                StopReason = StreamStopReason.ToolCallAborted,
                ToolCalls = new[] { new ToolCallRecord { CallId = "c", FunctionName = "find_files", ArgumentsJson = "{}", IsInvalid = true } }
            };
        }

        [Test]
        public void DifferentCalls_Proceed()
        {
            var policy = Policy();

            Assert.That(policy.Evaluate(Calls("{\"a\":1}"), 0).Action, Is.EqualTo(RecoveryAction.Proceed));
            Assert.That(policy.Evaluate(Calls("{\"a\":2}"), 1).Action, Is.EqualTo(RecoveryAction.Proceed));
        }

        [Test]
        public void Malformed_RecoversTwice_ThenFails()
        {
            var policy = Policy();

            var first = policy.Evaluate(Aborted(), 0);
            var second = policy.Evaluate(Aborted(), 1);
            var third = policy.Evaluate(Aborted(), 2);

            Assert.That(first.Action, Is.EqualTo(RecoveryAction.Recover));
            Assert.That(first.Nudge, Is.EqualTo(SubAgentRecoveryPolicy.MalformedToolCallNudge));
            Assert.That(first.Note, Is.EqualTo("SubAgent recovered from a malformed tool call (attempt 1 of 2)."));
            Assert.That(second.Action, Is.EqualTo(RecoveryAction.Recover));
            Assert.That(second.Note, Is.EqualTo("SubAgent recovered from a malformed tool call (attempt 2 of 2)."));
            Assert.That(third.Action, Is.EqualTo(RecoveryAction.Fail));
            Assert.That(third.Error, Is.EqualTo("SubAgent stopped: the model produced malformed tool calls 3 times."));
        }

        [Test]
        public void Malformed_InLastRound_Fails()
        {
            var decision = Policy(maxRounds: 3).Evaluate(Aborted(), 2);

            Assert.That(decision.Action, Is.EqualTo(RecoveryAction.Fail));
            Assert.That(decision.Error, Is.EqualTo("SubAgent stopped: the model produced malformed tool calls 1 times."));
        }

        [Test]
        public void Repeat_FirstMatchProceeds_SecondConsecutiveMatchRecovers()
        {
            var policy = Policy();

            Assert.That(policy.Evaluate(Calls("{}"), 0).Action, Is.EqualTo(RecoveryAction.Proceed));
            Assert.That(policy.Evaluate(Calls("{}"), 1).Action, Is.EqualTo(RecoveryAction.Proceed));
            var decision = policy.Evaluate(Calls("{}"), 2);

            Assert.That(decision.Action, Is.EqualTo(RecoveryAction.Recover));
            Assert.That(decision.Nudge, Is.EqualTo(SubAgentRecoveryPolicy.LoopRecoveryNudge));
            Assert.That(decision.Note, Is.EqualTo("SubAgent recovered from a repeated tool call loop (attempt 1 of 2)."));
        }

        [Test]
        public void Repeat_WithLoopRecoveryDisabled_Fails()
        {
            var policy = Policy(loopRecoveryEnabled: false);

            policy.Evaluate(Calls("{}"), 0);
            policy.Evaluate(Calls("{}"), 1);
            var decision = policy.Evaluate(Calls("{}"), 2);

            Assert.That(decision.Action, Is.EqualTo(RecoveryAction.Fail));
            Assert.That(decision.Error, Is.EqualTo("SubAgent detected a repeated tool call loop and stopped."));
        }

        [Test]
        public void Repeat_InLastRound_Fails()
        {
            var policy = Policy(maxRounds: 3);

            policy.Evaluate(Calls("{}"), 0);
            policy.Evaluate(Calls("{}"), 1);

            Assert.That(policy.Evaluate(Calls("{}"), 2).Action, Is.EqualTo(RecoveryAction.Fail));
        }

        [Test]
        public void DifferentCall_ResetsDuplicateCounter()
        {
            var policy = Policy();

            policy.Evaluate(Calls("{}"), 0);
            policy.Evaluate(Calls("{}"), 1);
            policy.Evaluate(Calls("{\"other\":1}"), 2);
            policy.Evaluate(Calls("{}"), 3);

            Assert.That(policy.Evaluate(Calls("{}"), 4).Action, Is.EqualTo(RecoveryAction.Proceed),
                "one match after a different call is not a loop yet");
            Assert.That(policy.Evaluate(Calls("{}"), 5).Action, Is.EqualTo(RecoveryAction.Recover));
        }

        [Test]
        public void AfterRecovery_PreviousCallsAreForgotten()
        {
            var policy = Policy();

            policy.Evaluate(Calls("{}"), 0);
            policy.Evaluate(Calls("{}"), 1);
            Assert.That(policy.Evaluate(Calls("{}"), 2).Action, Is.EqualTo(RecoveryAction.Recover));

            Assert.That(policy.Evaluate(Calls("{}"), 3).Action, Is.EqualTo(RecoveryAction.Proceed), "nothing to compare with after recovery");
            Assert.That(policy.Evaluate(Calls("{}"), 4).Action, Is.EqualTo(RecoveryAction.Proceed));
            var decision = policy.Evaluate(Calls("{}"), 5);
            Assert.That(decision.Action, Is.EqualTo(RecoveryAction.Recover));
            Assert.That(decision.Note, Is.EqualTo("SubAgent recovered from a repeated tool call loop (attempt 2 of 2)."));
        }

        [Test]
        public void Repeat_RecoversTwice_ThenFails()
        {
            var policy = Policy();
            int round = 0;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                policy.Evaluate(Calls("{}"), round++);
                policy.Evaluate(Calls("{}"), round++);
                Assert.That(policy.Evaluate(Calls("{}"), round++).Action, Is.EqualTo(RecoveryAction.Recover));
            }

            policy.Evaluate(Calls("{}"), round++);
            policy.Evaluate(Calls("{}"), round++);
            Assert.That(policy.Evaluate(Calls("{}"), round).Action, Is.EqualTo(RecoveryAction.Fail));
        }

        // ---- Final answer with unresolved ref markers ----------------------------------------

        [Test]
        public void Answer_WithoutUnresolvedMarkers_Proceeds()
        {
            var policy = Policy();

            Assert.That(policy.EvaluateAnswer(null, 0).Action, Is.EqualTo(RecoveryAction.Proceed));
            Assert.That(policy.EvaluateAnswer(new string[0], 0).Action, Is.EqualTo(RecoveryAction.Proceed));
        }

        [Test]
        public void Answer_WithUnresolvedMarkers_RecoversOnce_ThenProceeds()
        {
            var policy = Policy();
            var markers = new[] { "@src\\A.cs:210-247", "@src\\B.cs:5" };

            var first = policy.EvaluateAnswer(markers, 1);
            var second = policy.EvaluateAnswer(markers, 2);

            Assert.That(first.Action, Is.EqualTo(RecoveryAction.Recover));
            Assert.That(first.Note, Is.EqualTo("SubAgent recovered from unresolved ref markers (attempt 1 of 1)."));
            Assert.That(first.Nudge, Does.StartWith("[AUTOMATED GUARD: UNRESOLVED REF MARKERS]\n"));
            Assert.That(first.Nudge, Does.Not.Contain("\r"), "one line ending in the whole nudge");
            Assert.That(first.Nudge, Does.Contain("cannot be expanded:\n@src\\A.cs:210-247\n@src\\B.cs:5\nFor each marker"));
            Assert.That(first.Nudge, Does.Contain("@src\\A.cs:210-247"));
            Assert.That(first.Nudge, Does.Contain("@src\\B.cs:5"));
            Assert.That(second.Action, Is.EqualTo(RecoveryAction.Proceed), "the answer goes out as it is after the one attempt");
        }

        [Test]
        public void Answer_WithUnresolvedMarkers_InLastRound_Proceeds()
        {
            var decision = Policy(maxRounds: 3).EvaluateAnswer(new[] { "@a.cs:1" }, 2);

            Assert.That(decision.Action, Is.EqualTo(RecoveryAction.Proceed));
        }

        [Test]
        public void Answer_Recovery_DoesNotUseOtherAttempts()
        {
            var policy = Policy();

            Assert.That(policy.EvaluateAnswer(new[] { "@a.cs:1" }, 0).Action, Is.EqualTo(RecoveryAction.Recover));
            Assert.That(policy.Evaluate(Aborted(), 1).Note, Is.EqualTo("SubAgent recovered from a malformed tool call (attempt 1 of 2)."));
        }

        [Test]
        public void UnresolvedRefsNudge_ListsAtMostTheLimit()
        {
            var markers = new List<string>();
            for (int i = 1; i <= SubAgentRecoveryPolicy.UnresolvedRefsListLimit + 3; i++)
                markers.Add("@f.cs:" + i);

            var nudge = SubAgentRecoveryPolicy.BuildUnresolvedRefsNudge(markers);

            Assert.That(nudge, Does.Contain("@f.cs:" + SubAgentRecoveryPolicy.UnresolvedRefsListLimit + "\r\n").Or.Contain("@f.cs:" + SubAgentRecoveryPolicy.UnresolvedRefsListLimit + "\n"));
            Assert.That(nudge, Does.Not.Contain("@f.cs:" + (SubAgentRecoveryPolicy.UnresolvedRefsListLimit + 1)));
            Assert.That(nudge, Does.Contain("(and 3 more)"));
            Assert.That(nudge, Does.EndWith("Then give the complete final answer again."));
        }

        [Test]
        public void Malformed_IsCheckedBeforeRepeat_AndCountersAreSeparate()
        {
            var policy = Policy();

            policy.Evaluate(Calls("{}"), 0);
            policy.Evaluate(Calls("{}"), 1);

            var malformed = policy.Evaluate(Aborted(), 2);
            Assert.That(malformed.Action, Is.EqualTo(RecoveryAction.Recover));
            Assert.That(malformed.Nudge, Is.EqualTo(SubAgentRecoveryPolicy.MalformedToolCallNudge));

            policy.Evaluate(Calls("{}"), 3);
            policy.Evaluate(Calls("{}"), 4);
            var loop = policy.Evaluate(Calls("{}"), 5);
            Assert.That(loop.Note, Is.EqualTo("SubAgent recovered from a repeated tool call loop (attempt 1 of 2)."),
                "a malformed recovery does not use a loop attempt");
        }
    }
}
