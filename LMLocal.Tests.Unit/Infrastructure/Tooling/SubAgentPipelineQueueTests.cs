using System.Collections.Generic;
using System.Linq;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.SubAgents;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.SubAgents;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using LMLocal.Infrastructure.Tooling.Mcp.Abstractions;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling
{
    /// <summary>
    /// Main-queue policy for pipelines and for agents hidden with <c>exposed: false</c>.
    /// </summary>
    [TestFixture]
    public class SubAgentPipelineQueueTests
    {
        private Mock<ISettingsManager> _settings;
        private Mock<IBuiltInVsToolProvider> _builtIn;
        private Mock<IMcpToolManager> _mcp;
        private Mock<ISubAgentsCatalog> _catalog;
        private Mock<ISubAgentsParallelPolicy> _parallel;
        private ToolQueueProvider _provider;

        [SetUp]
        public void SetUp()
        {
            _settings = new Mock<ISettingsManager>();
            _builtIn = new Mock<IBuiltInVsToolProvider>();
            _mcp = new Mock<IMcpToolManager>();
            _catalog = new Mock<ISubAgentsCatalog>();
            _parallel = new Mock<ISubAgentsParallelPolicy>();

            _settings.SetupGet(s => s.Current)
                .Returns(new AppSettings { EnableAiTools = true, EnableSubAgents = true, EnableAiWriteTools = false });

            var readOnly = new ToolDefinition { Name = "read_file_lines" };
            var write = new ToolDefinition { Name = "replace_file_content" };

            _builtIn.Setup(b => b.GetAllToolDefinitions())
                .Returns(new List<ToolDefinition> { readOnly, write }.AsReadOnly());
            _builtIn.Setup(b => b.GetAllToolDefinitionsUnfiltered())
                .Returns(new List<ToolDefinition> { readOnly, write }.AsReadOnly());
            _builtIn.Setup(b => b.ToolExists("read_file_lines")).Returns(true);
            _builtIn.Setup(b => b.ToolExists("replace_file_content")).Returns(true);
            _builtIn.Setup(b => b.GetToolAccessLevel("read_file_lines")).Returns(ToolAccessLevel.ReadOnly);
            _builtIn.Setup(b => b.GetToolAccessLevel("replace_file_content")).Returns(ToolAccessLevel.FullAccess);

            _mcp.Setup(m => m.GetMcpToolDefinitions()).Returns(new List<ToolDefinition>().AsReadOnly());

            _provider = new ToolQueueProvider(
                _settings.Object,
                _builtIn.Object,
                _mcp.Object,
                _catalog.Object,
                _parallel.Object);
        }

        private static SubAgentDefinition Agent(string id, bool enabled = true, bool? exposed = null, params string[] allowedTools)
        {
            return new SubAgentDefinition
            {
                Id = id,
                Description = "Agent " + id,
                Model = "m",
                CustomBaseUrl = "http://localhost:1234",
                Enabled = enabled,
                Exposed = exposed,
                AllowedTools = allowedTools.ToList()
            };
        }

        private static SubAgentPipelineDefinition Pipeline(string id, bool enabled = true, params string[] agents)
        {
            var pipeline = new SubAgentPipelineDefinition
            {
                Id = id,
                Description = "Pipeline " + id,
                Enabled = enabled
            };

            foreach (var agent in agents)
            {
                pipeline.Steps.Add(new SubAgentPipelineStep { Agent = agent, Prompt = "{{task}}" });
            }

            return pipeline;
        }

        private void SetConfig(SubAgentsConfig config)
        {
            _catalog.Setup(c => c.TryGetSnapshot()).Returns(config);
        }

        private static SubAgentsConfig Config(params SubAgentDefinition[] agents)
        {
            var config = new SubAgentsConfig();
            config.Agents.AddRange(agents);
            return config;
        }

        private IEnumerable<string> QueueNames()
        {
            return _provider.GetMainQueue().Definitions.Select(d => d.Name);
        }

        [Test]
        public void HiddenAgent_IsNotOffered_ButStillOwnsItsTools()
        {
            SetConfig(Config(Agent("researcher", exposed: false, allowedTools: "read_file_lines")));

            var queue = _provider.GetMainQueue();
            var names = queue.Definitions.Select(d => d.Name).ToList();

            Assert.That(names, Does.Not.Contain("researcher"));
            Assert.That(names, Does.Not.Contain("read_file_lines"), "the hidden agent still owns the tool");
            Assert.That(queue.Allows("researcher"), Is.False);
        }

        [Test]
        public void SharedTool_StaysAvailableToTheModel()
        {
            var config = Config(Agent("researcher", exposed: false, allowedTools: "read_file_lines"));
            config.SharedTools = new List<string> { "read_file_lines" };
            SetConfig(config);

            Assert.That(QueueNames(), Does.Contain("read_file_lines"));
            Assert.That(QueueNames(), Does.Not.Contain("researcher"));
        }

        [Test]
        public void AvailablePipeline_IsOfferedWithTheTaskParameter()
        {
            var config = Config(Agent("explorer"), Agent("analyzer"));
            config.Pipelines = new List<SubAgentPipelineDefinition> { Pipeline("review_flow", true, "explorer", "analyzer") };
            SetConfig(config);

            var definition = _provider.GetMainQueue().Definitions.SingleOrDefault(d => d.Name == "review_flow");

            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.Description, Does.Contain("Pipeline review_flow"));
            Assert.That(definition.Parameters.Properties.Keys, Is.EquivalentTo(new[] { "task" }));
            Assert.That(definition.Parameters.Required, Is.EquivalentTo(new[] { "task" }));
        }

        [Test]
        public void PipelineWithHiddenStepAgent_IsStillOffered()
        {
            var config = Config(Agent("explorer", exposed: false), Agent("analyzer"));
            config.Pipelines = new List<SubAgentPipelineDefinition> { Pipeline("review_flow", true, "explorer", "analyzer") };
            SetConfig(config);

            Assert.That(QueueNames(), Does.Contain("review_flow"));
        }

        [Test]
        public void DisabledPipeline_IsNotOffered()
        {
            var config = Config(Agent("explorer"));
            config.Pipelines = new List<SubAgentPipelineDefinition> { Pipeline("review_flow", false, "explorer") };
            SetConfig(config);

            Assert.That(QueueNames(), Does.Not.Contain("review_flow"));
        }

        [Test]
        public void PipelineWithDisabledAgent_IsNotOffered()
        {
            var config = Config(Agent("explorer", enabled: false));
            config.Pipelines = new List<SubAgentPipelineDefinition> { Pipeline("review_flow", true, "explorer") };
            SetConfig(config);

            Assert.That(QueueNames(), Does.Not.Contain("review_flow"));
        }

        [Test]
        public void PipelineWithMissingAgent_IsNotOffered()
        {
            var config = Config(Agent("explorer"));
            config.Pipelines = new List<SubAgentPipelineDefinition> { Pipeline("review_flow", true, "explorer", "ghost") };
            SetConfig(config);

            Assert.That(QueueNames(), Does.Not.Contain("review_flow"));
        }

        [Test]
        public void PipelineWhoseAgentNeedsUnavailableTools_IsNotOffered()
        {
            // The write tool is filtered out in the read-only mode, so the agent is not visible and neither is the pipeline.
            var config = Config(Agent("writer", allowedTools: "replace_file_content"));
            config.Pipelines = new List<SubAgentPipelineDefinition> { Pipeline("write_flow", true, "writer") };
            SetConfig(config);

            Assert.That(QueueNames(), Does.Not.Contain("write_flow"));
        }

        [Test]
        public void PipelineCollidingWithAnAgentName_IsNotOffered()
        {
            var config = Config(Agent("review_flow"));
            config.Pipelines = new List<SubAgentPipelineDefinition> { Pipeline("review_flow", true, "review_flow") };
            SetConfig(config);

            var names = QueueNames().ToList();

            Assert.That(names, Does.Contain("review_flow"), "the agent wins the name");
            Assert.That(_provider.GetMainQueue().Definitions.Count(d => d.Name == "review_flow"), Is.EqualTo(1));
        }

        [Test]
        public void HiddenAgentCall_IsNotAllowedByTheMainQueue()
        {
            // ToolExecutionManager rejects exactly what the queue does not allow.
            SetConfig(Config(Agent("reviewer_subagent", exposed: false)));

            var queue = _provider.GetMainQueue();

            Assert.That(queue.Allows("reviewer_subagent"), Is.False);
        }

        [Test]
        public void ParallelGroupWithWriteBranch_IsNotOffered()
        {
            // Write mode is on, so the agent itself is visible: only the group rule can hide the pipeline.
            _settings.SetupGet(s => s.Current)
                .Returns(new AppSettings { EnableAiTools = true, EnableSubAgents = true, EnableAiWriteTools = true });

            var config = Config(
                Agent("writer", allowedTools: "replace_file_content"),
                Agent("reader", allowedTools: "read_file_lines"));

            var pipeline = new SubAgentPipelineDefinition { Id = "review_flow", Description = "Pipeline review_flow" };
            pipeline.Steps.Add(new SubAgentPipelineStep
            {
                Parallel = new List<SubAgentPipelineStep>
                {
                    new SubAgentPipelineStep { Agent = "writer", Prompt = "{{task}}" },
                    new SubAgentPipelineStep { Agent = "reader", Prompt = "{{task}}" }
                }
            });

            config.Pipelines = new List<SubAgentPipelineDefinition> { pipeline };
            SetConfig(config);

            Assert.That(QueueNames(), Does.Not.Contain("review_flow"));
        }

        [Test]
        public void SequentialStepWithWriteAgent_IsStillOffered()
        {
            _settings.SetupGet(s => s.Current)
                .Returns(new AppSettings { EnableAiTools = true, EnableSubAgents = true, EnableAiWriteTools = true });

            var config = Config(Agent("writer", allowedTools: "replace_file_content"));
            config.Pipelines = new List<SubAgentPipelineDefinition> { Pipeline("write_flow", true, "writer") };
            SetConfig(config);

            Assert.That(QueueNames(), Does.Contain("write_flow"),
                "a sequential step runs alone, so write tools are allowed there");
        }
    }
}
