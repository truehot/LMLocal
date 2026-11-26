using System;
using System.Collections.Generic;
using LMLocal.Application.SubAgents;
using LMLocal.Application.Tool;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.SubAgents
{
    /// <summary>Token events of a pipeline: total of the whole call, agent of the step, never a step line.</summary>
    [TestFixture]
    public class SubAgentPipelineProgressTests
    {
        private sealed class Recorder : IProgress<ToolActivityEvent>
        {
            internal List<ToolActivityEvent> Events { get; } = new List<ToolActivityEvent>();

            internal ToolActivityEvent Last
            {
                get { return Events[Events.Count - 1]; }
            }

            public void Report(ToolActivityEvent value)
            {
                Events.Add(value);
            }
        }

        [Test]
        public void Step_TokenEvent_IsNotTurnedIntoAStepLine()
        {
            var recorder = new Recorder();
            var step = new SubAgentPipelineProgress(recorder).BeginStep("1/2 analyze", "Plan critic");

            step.Report(new ToolActivityEvent { Tokens = 120, TokensPerSecond = 42.5 });

            Assert.That(recorder.Events.Count, Is.EqualTo(1));
            Assert.That(recorder.Last.Tokens, Is.EqualTo(120));
            Assert.That(recorder.Last.Step, Is.Null, "The step line of the card must stay untouched");
            Assert.That(recorder.Last.Message, Is.Null.Or.Empty);
            Assert.That(recorder.Last.AgentName, Is.EqualTo("Plan critic"));
        }

        [Test]
        public void Step_ToolEvent_StillProducesAStepLine()
        {
            var recorder = new Recorder();
            var step = new SubAgentPipelineProgress(recorder).BeginStep("1/2 analyze", "Plan critic");

            step.Report(new ToolActivityEvent { ToolName = "find_files" });

            Assert.That(recorder.Last.Step, Is.EqualTo(1));
            Assert.That(recorder.Last.Message, Is.EqualTo("[1/2 analyze] Plan critic · find_files"));
            Assert.That(recorder.Last.Tokens, Is.Null);
        }

        [Test]
        public void Tokens_AreSummedOverTheStepsOfThePipeline()
        {
            var recorder = new Recorder();
            var progress = new SubAgentPipelineProgress(recorder);
            var first = progress.BeginStep("1/2 analyze", "Analyzer");
            var second = progress.BeginStep("2/2 critique", "Critic");

            first.Report(new ToolActivityEvent { Tokens = 100, TokensPerSecond = 10 });
            first.Report(new ToolActivityEvent { Tokens = 100, TokensPerSecond = 0 });
            second.Report(new ToolActivityEvent { Tokens = 30, TokensPerSecond = 7 });

            Assert.That(recorder.Last.Tokens, Is.EqualTo(130));
            Assert.That(recorder.Last.TokensPerSecond, Is.EqualTo(7), "A finished step closed its speed at 0");
            Assert.That(recorder.Last.AgentName, Is.EqualTo("Critic"));
        }

        [Test]
        public void Tokens_AreSummedOverTheBranchesOfAParallelStep()
        {
            var recorder = new Recorder();
            var group = new SubAgentPipelineProgress(recorder).BeginGroup("1/1 review", 2);
            var left = group.CreateBranchProgress("Reviewer");
            var right = group.CreateBranchProgress("Plan critic");

            left.Report(new ToolActivityEvent { Tokens = 100, TokensPerSecond = 40 });
            right.Report(new ToolActivityEvent { Tokens = 50, TokensPerSecond = 20 });

            Assert.That(recorder.Last.Tokens, Is.EqualTo(150));
            Assert.That(recorder.Last.TokensPerSecond, Is.EqualTo(60), "Both branches are running, so both speeds add up");
            Assert.That(recorder.Last.AgentName, Is.EqualTo("Plan critic"));
        }

        [Test]
        public void Tokens_OfAFailedAttempt_StayInTheTotalOfARetry()
        {
            var recorder = new Recorder();
            var step = new SubAgentPipelineProgress(recorder).BeginStep("1/1 plan", "Planner");

            step.ReportStart(null);
            step.Report(new ToolActivityEvent { Tokens = 300, TokensPerSecond = 0 });

            step.ReportStart("retry 2/2");
            step.Report(new ToolActivityEvent { Tokens = 40, TokensPerSecond = 9 });

            Assert.That(recorder.Last.Tokens, Is.EqualTo(340), "The retry restarts at 0; the 300 tokens of the first attempt stay");
        }
    }
}
