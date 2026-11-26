using System.Collections.Generic;
using System.Linq;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.SubAgents;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.SubAgents
{
    [TestFixture]
    public class SubAgentsParallelPolicyTests
    {
        private Mock<ISubAgentsCatalog> _catalogMock;
        private Mock<ISettingsManager> _settingsManagerMock;
        private Mock<IBuiltInVsToolProvider> _builtInToolsMock;
        private SubAgentsParallelPolicy _policy;

        [SetUp]
        public void SetUp()
        {
            _catalogMock = new Mock<ISubAgentsCatalog>();
            _settingsManagerMock = new Mock<ISettingsManager>();
            _builtInToolsMock = new Mock<IBuiltInVsToolProvider>();

            _settingsManagerMock.SetupGet(s => s.Current)
                .Returns(new AppSettings { EnableAiTools = true, EnableSubAgents = true });

            // Allowed tools default to unknown / not registered.
            _builtInToolsMock.Setup(t => t.ToolExists(It.IsAny<string>())).Returns(false);

            _policy = new SubAgentsParallelPolicy(
                _catalogMock.Object,
                _settingsManagerMock.Object,
                _builtInToolsMock.Object);
        }

        private void SetAgents(params SubAgentDefinition[] agents)
            => _catalogMock.Setup(c => c.GetEnabledAgents()).Returns(agents.ToList());

        private void RegisterReadOnlyTool(string name)
        {
            _builtInToolsMock.Setup(t => t.ToolExists(name)).Returns(true);
            _builtInToolsMock.Setup(t => t.GetToolAccessLevel(name)).Returns(ToolAccessLevel.ReadOnly);
        }

        private void RegisterTool(string name, ToolAccessLevel access)
        {
            _builtInToolsMock.Setup(t => t.ToolExists(name)).Returns(true);
            _builtInToolsMock.Setup(t => t.GetToolAccessLevel(name)).Returns(access);
        }

        private static SubAgentDefinition Agent(string id, bool parallel, params string[] allowedTools)
        {
            return new SubAgentDefinition
            {
                Id = id,
                Description = id,
                CustomBaseUrl = "http://localhost:1234",
                Model = "m",
                Parallel = parallel,
                AllowedTools = allowedTools.ToList()
            };
        }

        // =====================================================================
        // IsParallelEnabled
        // =====================================================================

        [Test]
        public void IsParallelEnabled_ParallelAgentWithReadOnlyTools_ReturnsTrue()
        {
            RegisterReadOnlyTool("read_file_lines");
            SetAgents(Agent("researcher", parallel: true, "read_file_lines"));

            Assert.That(_policy.IsParallelEnabled("researcher"), Is.True);
        }

        [Test]
        public void IsParallelEnabled_TrimsName()
        {
            SetAgents(Agent("researcher", parallel: true));

            Assert.That(_policy.IsParallelEnabled("  researcher  "), Is.True);
        }

        [Test]
        public void IsParallelEnabled_ReasoningOnlyAgent_ReturnsTrue()
        {
            SetAgents(Agent("researcher", parallel: true));

            Assert.That(_policy.IsParallelEnabled("researcher"), Is.True);
        }

        [Test]
        public void IsParallelEnabled_AgentParallelFalse_ReturnsFalse()
        {
            SetAgents(Agent("researcher", parallel: false));

            Assert.That(_policy.IsParallelEnabled("researcher"), Is.False);
        }

        [Test]
        public void IsParallelEnabled_UnknownTool_ReturnsFalse()
        {
            SetAgents(Agent("researcher", parallel: true));

            Assert.That(_policy.IsParallelEnabled("ghost"), Is.False);
        }

        [Test]
        public void IsParallelEnabled_NullOrEmpty_ReturnsFalse()
        {
            Assert.That(_policy.IsParallelEnabled(null), Is.False);
            Assert.That(_policy.IsParallelEnabled("   "), Is.False);
        }

        [Test]
        public void IsParallelEnabled_AiToolsDisabled_ReturnsFalse()
        {
            _settingsManagerMock.SetupGet(s => s.Current)
                .Returns(new AppSettings { EnableAiTools = false, EnableSubAgents = true });
            SetAgents(Agent("researcher", parallel: true));

            Assert.That(_policy.IsParallelEnabled("researcher"), Is.False);
        }

        [Test]
        public void IsParallelEnabled_SubAgentsDisabled_ReturnsFalse()
        {
            _settingsManagerMock.SetupGet(s => s.Current)
                .Returns(new AppSettings { EnableAiTools = true, EnableSubAgents = false });
            SetAgents(Agent("researcher", parallel: true));

            Assert.That(_policy.IsParallelEnabled("researcher"), Is.False);
        }

        [Test]
        public void IsParallelEnabled_WriteToolInAllowedTools_ReturnsFalse()
        {
            RegisterTool("replace_file_content", ToolAccessLevel.FullAccess);
            SetAgents(Agent("editor", parallel: true, "replace_file_content"));

            Assert.That(_policy.IsParallelEnabled("editor"), Is.False);
        }

        [Test]
        public void IsParallelEnabled_ExecutionToolInAllowedTools_ReturnsFalse()
        {
            RegisterTool("run_tests", ToolAccessLevel.Execution);
            SetAgents(Agent("tester", parallel: true, "run_tests"));

            Assert.That(_policy.IsParallelEnabled("tester"), Is.False);
        }

        [Test]
        public void IsParallelEnabled_UnknownAllowedTool_IsIgnored()
        {
            RegisterReadOnlyTool("read_file_lines");
            SetAgents(Agent("researcher", parallel: true, "read_file_lines", "some_unknown_tool"));

            Assert.That(_policy.IsParallelEnabled("researcher"), Is.True);
        }

        // =====================================================================
        // ResolveMaxParallel — resolution order: agent -> top-level -> default
        // =====================================================================

        [Test]
        public void ResolveMaxParallel_AgentValueWins()
        {
            var agent = Agent("researcher", parallel: true);
            agent.MaxParallel = 4;
            SetAgents(agent);
            _catalogMock.Setup(c => c.TryGetSnapshot()).Returns(new SubAgentsConfig { MaxParallel = 8 });

            Assert.That(_policy.ResolveMaxParallel("researcher"), Is.EqualTo(4));
        }

        [Test]
        public void ResolveMaxParallel_NoAgentValue_UsesTopLevel()
        {
            SetAgents(Agent("researcher", parallel: true));
            _catalogMock.Setup(c => c.TryGetSnapshot()).Returns(new SubAgentsConfig { MaxParallel = 4 });

            Assert.That(_policy.ResolveMaxParallel("researcher"), Is.EqualTo(4));
        }

        [Test]
        public void ResolveMaxParallel_NeitherAgentNorTopLevel_UsesDefault()
        {
            SetAgents(Agent("researcher", parallel: true));
            _catalogMock.Setup(c => c.TryGetSnapshot()).Returns(new SubAgentsConfig());

            Assert.That(_policy.ResolveMaxParallel("researcher"), Is.EqualTo(SubAgentsConfig.DefaultMaxParallel));
        }

        [Test]
        public void ResolveMaxParallel_AgentNonPositive_FallsBackToTopLevel()
        {
            var agent = Agent("researcher", parallel: true);
            agent.MaxParallel = 0;
            SetAgents(agent);
            _catalogMock.Setup(c => c.TryGetSnapshot()).Returns(new SubAgentsConfig { MaxParallel = 3 });

            Assert.That(_policy.ResolveMaxParallel("researcher"), Is.EqualTo(3));
        }

        [Test]
        public void ResolveMaxParallel_AgentAboveLimit_IsClampedToLimit()
        {
            var agent = Agent("researcher", parallel: true);
            agent.MaxParallel = SubAgentsConfig.MaxParallelLimit + 4;
            SetAgents(agent);
            _catalogMock.Setup(c => c.TryGetSnapshot()).Returns(new SubAgentsConfig());

            Assert.That(_policy.ResolveMaxParallel("researcher"), Is.EqualTo(SubAgentsConfig.MaxParallelLimit));
        }

        [Test]
        public void ResolveMaxParallel_TopLevelAboveLimit_IsClampedToLimit()
        {
            SetAgents(Agent("researcher", parallel: true));
            _catalogMock.Setup(c => c.TryGetSnapshot()).Returns(new SubAgentsConfig { MaxParallel = 20 });

            Assert.That(_policy.ResolveMaxParallel("researcher"), Is.EqualTo(SubAgentsConfig.MaxParallelLimit));
        }

        [Test]
        public void ResolveMaxParallel_ExactlyLimit_IsKept()
        {
            var agent = Agent("researcher", parallel: true);
            agent.MaxParallel = SubAgentsConfig.MaxParallelLimit;
            SetAgents(agent);
            _catalogMock.Setup(c => c.TryGetSnapshot()).Returns(new SubAgentsConfig());

            Assert.That(_policy.ResolveMaxParallel("researcher"), Is.EqualTo(SubAgentsConfig.MaxParallelLimit));
        }

        [Test]
        public void ResolveMaxParallel_UnknownAgent_UsesDefault()
        {
            _catalogMock.Setup(c => c.TryGetSnapshot()).Returns(new SubAgentsConfig());

            Assert.That(_policy.ResolveMaxParallel("ghost"), Is.EqualTo(SubAgentsConfig.DefaultMaxParallel));
        }

        // =====================================================================
        // Pipelines in a parallel group of the main round (2A)
        // A pipeline joins the group only when it opted in (or the root did), every step agent is read-only, and it
        // has no parallel step of its own; otherwise two writing pipelines could run at the same time.
        // =====================================================================

        private void SetPipelines(params SubAgentPipelineDefinition[] pipelines)
            => _catalogMock.Setup(c => c.GetEnabledPipelines()).Returns(pipelines.ToList());

        private void SetRoot(bool parallel, int? maxParallel = null)
            => _catalogMock.Setup(c => c.TryGetSnapshot())
                .Returns(new SubAgentsConfig { Parallel = parallel, MaxParallel = maxParallel });

        private static SubAgentPipelineDefinition Pipeline(string id, bool? parallel, params string[] agents)
        {
            return new SubAgentPipelineDefinition
            {
                Id = id,
                Description = id,
                Parallel = parallel,
                Steps = agents.Select(a => new SubAgentPipelineStep { Agent = a, Prompt = "{{task}}" }).ToList()
            };
        }

        [Test]
        public void IsParallelEnabled_PipelineOptedInWithReadOnlyAgents_ReturnsTrue()
        {
            RegisterReadOnlyTool("read_file_lines");
            SetAgents(Agent("explorer", parallel: false, "read_file_lines"), Agent("thinker", parallel: false));
            SetPipelines(Pipeline("review_flow", true, "explorer", "thinker"));
            SetRoot(parallel: false);

            Assert.That(_policy.IsParallelEnabled("review_flow"), Is.True,
                "the parallel flag of the agents does not matter, only the one of the pipeline");
        }

        [Test]
        public void IsParallelEnabled_PipelineWithoutFlag_FollowsTheRoot()
        {
            SetAgents(Agent("thinker", parallel: false));
            SetPipelines(Pipeline("review_flow", null, "thinker"));

            SetRoot(parallel: false);
            Assert.That(_policy.IsParallelEnabled("review_flow"), Is.False, "no flag and no root flag: sequential");

            SetRoot(parallel: true);
            Assert.That(_policy.IsParallelEnabled("review_flow"), Is.True, "no flag: the root value applies");
        }

        [Test]
        public void IsParallelEnabled_PipelineExplicitFalse_WinsOverTheRoot()
        {
            SetAgents(Agent("thinker", parallel: false));
            SetPipelines(Pipeline("review_flow", false, "thinker"));
            SetRoot(parallel: true);

            Assert.That(_policy.IsParallelEnabled("review_flow"), Is.False);
        }

        [Test]
        public void IsParallelEnabled_PipelineWithAWriteAgent_ReturnsFalse()
        {
            RegisterReadOnlyTool("read_file_lines");
            RegisterTool("replace_file_lines", ToolAccessLevel.FullAccess);
            SetAgents(Agent("explorer", parallel: true, "read_file_lines"), Agent("editor", parallel: true, "replace_file_lines"));
            SetPipelines(Pipeline("fix_flow", true, "explorer", "editor"));
            SetRoot(parallel: true);

            Assert.That(_policy.IsParallelEnabled("fix_flow"), Is.False,
                "one writing step is enough to keep the pipeline out of a parallel group");
        }

        [Test]
        public void IsParallelEnabled_PipelineWithAnExecutionAgent_ReturnsFalse()
        {
            RegisterTool("run_tests", ToolAccessLevel.Execution);
            SetAgents(Agent("tester", parallel: true, "run_tests"));
            SetPipelines(Pipeline("test_flow", true, "tester"));
            SetRoot(parallel: false);

            Assert.That(_policy.IsParallelEnabled("test_flow"), Is.False);
        }

        [Test]
        public void IsParallelEnabled_PipelineWithAMissingOrDisabledAgent_ReturnsFalse()
        {
            // GetEnabledAgents holds only enabled agents, so a disabled one is "not found" here.
            SetAgents(Agent("thinker", parallel: true));
            SetPipelines(Pipeline("review_flow", true, "thinker", "ghost"));
            SetRoot(parallel: false);

            Assert.That(_policy.IsParallelEnabled("review_flow"), Is.False);
        }

        [Test]
        public void IsParallelEnabled_PipelineWithItsOwnParallelStep_ReturnsFalse()
        {
            SetAgents(Agent("left", parallel: false), Agent("right", parallel: false));
            var pipeline = new SubAgentPipelineDefinition
            {
                Id = "fan_out",
                Description = "fan_out",
                Parallel = true,
                Steps = new List<SubAgentPipelineStep>
                {
                    new SubAgentPipelineStep
                    {
                        Parallel = new List<SubAgentPipelineStep>
                        {
                            new SubAgentPipelineStep { Agent = "left", Prompt = "{{task}}" },
                            new SubAgentPipelineStep { Agent = "right", Prompt = "{{task}}" }
                        }
                    }
                }
            };
            SetPipelines(pipeline);
            SetRoot(parallel: false);

            Assert.That(_policy.IsParallelEnabled("fan_out"), Is.False,
                "a group inside a group would multiply the concurrent runs by the two widths");
        }

        [Test]
        public void IsParallelEnabled_NameOfAnAgentAndAPipeline_TheAgentWins()
        {
            SetAgents(Agent("shared", parallel: false), Agent("thinker", parallel: false));
            SetPipelines(Pipeline("shared", true, "thinker"));
            SetRoot(parallel: false);

            Assert.That(_policy.IsParallelEnabled("shared"), Is.False,
                "the agent 'shared' is sequential; the pipeline of the same name must not override it");
        }

        [Test]
        public void IsParallelEnabled_Pipeline_SubAgentsDisabled_ReturnsFalse()
        {
            _settingsManagerMock.SetupGet(s => s.Current)
                .Returns(new AppSettings { EnableAiTools = true, EnableSubAgents = false });
            SetAgents(Agent("thinker", parallel: false));
            SetPipelines(Pipeline("review_flow", true, "thinker"));
            SetRoot(parallel: false);

            Assert.That(_policy.IsParallelEnabled("review_flow"), Is.False);
        }

        [Test]
        public void ResolveMaxParallel_Pipeline_PipelineValueThenRootThenDefault()
        {
            SetAgents(Agent("thinker", parallel: false));
            var pipeline = Pipeline("review_flow", true, "thinker");
            SetPipelines(pipeline);

            pipeline.MaxParallel = 5;
            SetRoot(parallel: false, maxParallel: 3);
            Assert.That(_policy.ResolveMaxParallel("review_flow"), Is.EqualTo(5), "the pipeline value wins");

            pipeline.MaxParallel = null;
            Assert.That(_policy.ResolveMaxParallel("review_flow"), Is.EqualTo(3), "then the root value");

            SetRoot(parallel: false);
            Assert.That(_policy.ResolveMaxParallel("review_flow"), Is.EqualTo(SubAgentsConfig.DefaultMaxParallel),
                "then the default");

            pipeline.MaxParallel = SubAgentsConfig.MaxParallelLimit + 3;
            Assert.That(_policy.ResolveMaxParallel("review_flow"), Is.EqualTo(SubAgentsConfig.MaxParallelLimit),
                "and never above the limit");
        }
    }
}
