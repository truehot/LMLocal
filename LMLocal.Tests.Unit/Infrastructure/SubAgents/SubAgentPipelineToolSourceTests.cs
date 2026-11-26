using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.SubAgents;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.SubAgents;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.SubAgents
{
    [TestFixture]
    public class SubAgentPipelineToolSourceTests
    {
        private Mock<ISubAgentsCatalog> _catalog;
        private Mock<ISettingsManager> _settings;
        private Mock<ISubAgentPipelineRunner> _runner;
        private SubAgentsToolSource _source;

        [SetUp]
        public void SetUp()
        {
            _catalog = new Mock<ISubAgentsCatalog>();
            _settings = new Mock<ISettingsManager>();
            _runner = new Mock<ISubAgentPipelineRunner>();

            _settings.SetupGet(s => s.Current)
                .Returns(new AppSettings { EnableAiTools = true, EnableSubAgents = true, EnableAiWriteTools = false });

            _source = new SubAgentsToolSource(
                _catalog.Object,
                _settings.Object,
                () => new Mock<ISubAgentsService>().Object,
                _runner.Object);
        }

        private static SubAgentDefinition Agent(string id, int? timeoutSeconds)
        {
            return new SubAgentDefinition
            {
                Id = id,
                Description = "Agent " + id,
                Model = "m",
                CustomBaseUrl = "http://localhost:1234",
                Enabled = true,
                TimeoutSeconds = timeoutSeconds
            };
        }

        private void SetPipeline(SubAgentPipelineDefinition pipeline, params SubAgentDefinition[] agents)
        {
            var config = new SubAgentsConfig();
            config.Agents.AddRange(agents);
            config.Pipelines = new List<SubAgentPipelineDefinition> { pipeline };

            _catalog.Setup(c => c.TryGetSnapshot()).Returns(config);
            _catalog.Setup(c => c.GetEnabledAgents()).Returns(agents.Where(a => a.Enabled).ToList());
            _catalog.Setup(c => c.GetEnabledPipelines()).Returns(new List<SubAgentPipelineDefinition> { pipeline });
        }

        private static SubAgentPipelineDefinition Pipeline(params SubAgentPipelineStep[] steps)
        {
            var pipeline = new SubAgentPipelineDefinition { Id = "review_flow", Description = "Pipeline review_flow" };
            pipeline.Steps.AddRange(steps);
            return pipeline;
        }

        private static SubAgentPipelineStep Step(string agent, int? retries = null)
        {
            return new SubAgentPipelineStep
            {
                Agent = agent,
                Prompt = "{{task}}",
                Retries = retries
            };
        }

        [Test]
        public void ToolExists_ReturnsTrueForAPipeline()
        {
            SetPipeline(Pipeline(Step("a")), Agent("a", 100));

            Assert.That(_source.ToolExists("review_flow"), Is.True);
            Assert.That(_source.ToolExists("ghost"), Is.False);
        }

        [Test]
        public void GetDisplayName_UsesThePipelineDisplayName()
        {
            var pipeline = Pipeline(Step("a"));
            pipeline.DisplayName = "Review flow";
            SetPipeline(pipeline, Agent("a", 100));

            Assert.That(_source.GetDisplayName("review_flow"), Is.EqualTo("Review flow"));
        }

        [Test]
        public void GetToolTimeout_IsTheSumOfTheStepsPlusOneGrace()
        {
            SetPipeline(Pipeline(Step("a"), Step("b")), Agent("a", 100), Agent("b", 200));

            Assert.That(_source.GetToolTimeout("review_flow"), Is.EqualTo(TimeSpan.FromSeconds(320)));
        }

        [Test]
        public void GetToolTimeout_AnyStepWithoutTimeout_MakesTheCallUnlimited()
        {
            SetPipeline(Pipeline(Step("a"), Step("b")), Agent("a", 100), Agent("b", 0));

            Assert.That(_source.GetToolTimeout("review_flow"), Is.EqualTo(Timeout.InfiniteTimeSpan));
        }

        [TestCase(1, 123)]
        [TestCase(3, 136)]
        public void GetToolTimeout_AddsThePausesBeforeRetries(int retries, int expectedSeconds)
        {
            SetPipeline(Pipeline(Step("a", retries)), Agent("a", 100));

            // 100 s of the agent, the pauses 3 / 3+5+8 s, and one grace of 20 s.
            Assert.That(_source.GetToolTimeout("review_flow"), Is.EqualTo(TimeSpan.FromSeconds(expectedSeconds)));
        }

        [Test]
        public void GetToolTimeout_ParallelGroupCountsItsSlowestBranch()
        {
            var group = new SubAgentPipelineStep
            {
                Parallel = new List<SubAgentPipelineStep> { Step("a"), Step("b") }
            };

            SetPipeline(Pipeline(group), Agent("a", 100), Agent("b", 200));

            Assert.That(_source.GetToolTimeout("review_flow"), Is.EqualTo(TimeSpan.FromSeconds(220)));
        }

        [Test]
        public async Task ExecuteAsync_MissingStepAgent_FailsWithTheStepNumber()
        {
            SetPipeline(Pipeline(Step("ghost")), Agent("a", 100));

            var result = await _source.ExecuteAsync(
                "review_flow",
                new Dictionary<string, object> { { "task", "do it" } },
                CancellationToken.None);

            var response = (SubAgentsRunResponse)result;

            Assert.That(response.Success, Is.False);
            Assert.That(response.FailureKind, Is.EqualTo(SubAgentFailureKind.InvalidRequest));
            Assert.That(response.Error, Does.Contain("step 1"));
            Assert.That(response.Error, Does.Contain("not found or disabled"));
        }

        [Test]
        public async Task ExecuteAsync_ValidPipeline_RunsTheRunnerWithTheTask()
        {
            SetPipeline(Pipeline(Step("a")), Agent("a", 100));

            SubAgentPipelineRunRequest captured = null;
            _runner
                .Setup(r => r.RunAsync(It.IsAny<SubAgentPipelineRunRequest>(), It.IsAny<CancellationToken>()))
                .Returns((SubAgentPipelineRunRequest request, CancellationToken token) =>
                {
                    captured = request;
                    return Task.FromResult(new SubAgentsRunResponse { Success = true, Content = "done" });
                });

            var result = await _source.ExecuteAsync(
                "review_flow",
                new Dictionary<string, object> { { "task", "do it" } },
                CancellationToken.None);

            var response = (SubAgentsRunResponse)result;

            Assert.That(response.Success, Is.True);
            Assert.That(response.Content, Is.EqualTo("done"));
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured.Task, Is.EqualTo("do it"));
            Assert.That(captured.Steps, Has.Count.EqualTo(1));
            Assert.That(captured.Steps[0].Request.AgentName, Is.EqualTo("a"));
            Assert.That(captured.Steps[0].Request.Prompt, Is.Null, "the prompt is rendered by the runner");
        }

        [Test]
        public async Task ExecuteAsync_MissingTask_IsRejected()
        {
            SetPipeline(Pipeline(Step("a")), Agent("a", 100));

            var result = await _source.ExecuteAsync("review_flow", new Dictionary<string, object>(), CancellationToken.None);
            var response = (SubAgentsRunResponse)result;

            Assert.That(response.Success, Is.False);
            Assert.That(response.Error, Does.Contain("'task'"));
            _runner.Verify(r => r.RunAsync(It.IsAny<SubAgentPipelineRunRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ExecuteAsync_WhenSubAgentsAreDisabled_IsRejected()
        {
            _settings.SetupGet(s => s.Current)
                .Returns(new AppSettings { EnableAiTools = true, EnableSubAgents = false });
            SetPipeline(Pipeline(Step("a")), Agent("a", 100));

            var result = await _source.ExecuteAsync(
                "review_flow",
                new Dictionary<string, object> { { "task", "x" } },
                CancellationToken.None);

            Assert.That(((SubAgentsRunResponse)result).Success, Is.False);
        }

        [Test]
        public void GetToolTimeout_GroupBranchInheritsTheGroupRetries()
        {
            var group = new SubAgentPipelineStep
            {
                Retries = 2,
                Parallel = new List<SubAgentPipelineStep> { Step("a"), Step("b") }
            };

            SetPipeline(Pipeline(group), Agent("a", 100), Agent("b", 50));

            // Both branches inherit the retries of the group: 100 s + 3 s + 5 s is the slowest branch, plus 20 s grace.
            Assert.That(_source.GetToolTimeout("review_flow"), Is.EqualTo(TimeSpan.FromSeconds(128)));
        }

        [Test]
        public async Task ExecuteAsync_GroupBranchWithWriteTools_IsRejected()
        {
            var builtIn = new Mock<IBuiltInVsToolProvider>();
            builtIn.Setup(b => b.ToolExists("replace_file_content")).Returns(true);
            builtIn.Setup(b => b.GetToolAccessLevel("replace_file_content")).Returns(ToolAccessLevel.FullAccess);

            var source = new SubAgentsToolSource(
                _catalog.Object,
                _settings.Object,
                () => new Mock<ISubAgentsService>().Object,
                _runner.Object,
                builtIn.Object);

            var writer = Agent("writer", 100);
            writer.AllowedTools = new List<string> { "replace_file_content" };

            var group = new SubAgentPipelineStep
            {
                Parallel = new List<SubAgentPipelineStep> { Step("writer"), Step("a") }
            };

            SetPipeline(Pipeline(group), writer, Agent("a", 100));

            var result = await source.ExecuteAsync(
                "review_flow",
                new Dictionary<string, object> { { "task", "do it" } },
                CancellationToken.None);
            var response = (SubAgentsRunResponse)result;

            Assert.That(response.Success, Is.False);
            Assert.That(response.Error, Does.Contain("write tools"));
            _runner.Verify(
                r => r.RunAsync(It.IsAny<SubAgentPipelineRunRequest>(), It.IsAny<CancellationToken>()),
                Times.Never,
                "a group with a write branch must never start");
        }
    }
}
