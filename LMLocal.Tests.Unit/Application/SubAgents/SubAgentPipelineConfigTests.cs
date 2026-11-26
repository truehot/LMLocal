using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.SubAgents;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using LMLocal.Tests.Unit.Infrastructure;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.SubAgents
{
    [TestFixture]
    public class SubAgentPipelineConfigTests
    {
        private static readonly JsonSerializerSettings WriteSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        private static SubAgentDefinition Agent(string id, bool enabled = true, bool? exposed = null)
        {
            return new SubAgentDefinition
            {
                Id = id,
                Description = "Agent " + id,
                Model = "m",
                CustomBaseUrl = "http://localhost:1234",
                Enabled = enabled,
                Exposed = exposed
            };
        }

        private static SubAgentPipelineStep Step(string agent, string prompt, string id = null)
        {
            return new SubAgentPipelineStep
            {
                Id = id,
                Agent = agent,
                Prompt = prompt
            };
        }

        private static SubAgentPipelineDefinition Pipeline(string id, params SubAgentPipelineStep[] steps)
        {
            return new SubAgentPipelineDefinition
            {
                Id = id,
                Description = "Pipeline " + id,
                Steps = steps.ToList()
            };
        }

        private static SubAgentsConfig Config(params SubAgentDefinition[] agents)
        {
            var config = new SubAgentsConfig();
            config.Agents.AddRange(agents);
            return config;
        }

        private static SubAgentsConfigManager CreateManager(InMemoryFileSystem fs, params string[] builtInToolNames)
        {
            var settings = new Mock<ISettingsManager>();
            settings.Setup(s => s.LocalAppDataFolder).Returns("LMLocalChatUnitPipelines");

            var builtIn = new Mock<IBuiltInVsToolProvider>();
            builtIn.Setup(b => b.GetAllToolDefinitionsUnfiltered())
                .Returns(builtInToolNames.Select(n => new ToolDefinition { Name = n }).ToList());

            return new SubAgentsConfigManager(fs, settings.Object, builtIn.Object);
        }

        private static string ManagerFilePath()
        {
            return Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "LMLocalChatUnitPipelines",
                "subagents.json");
        }

        private static void WriteConfig(InMemoryFileSystem fs, string json)
        {
            fs.WriteAllBytesAsync(ManagerFilePath(), System.Text.Encoding.UTF8.GetBytes(json)).GetAwaiter().GetResult();
        }

        // =====================================================================
        // Model defaults and round-trip
        // =====================================================================

        [Test]
        public void IsEnabled_MissingKey_MeansTrue()
        {
            var pipeline = JsonConvert.DeserializeObject<SubAgentPipelineDefinition>("{ \"id\": \"p\", \"description\": \"d\" }");

            Assert.That(pipeline.IsEnabled, Is.True);
        }

        [Test]
        public void IsEnabled_False_MeansFalse()
        {
            var pipeline = JsonConvert.DeserializeObject<SubAgentPipelineDefinition>("{ \"id\": \"p\", \"enabled\": false }");

            Assert.That(pipeline.IsEnabled, Is.False);
        }

        [Test]
        public void IsExposed_MissingKey_MeansTrue()
        {
            var agent = JsonConvert.DeserializeObject<SubAgentDefinition>("{ \"id\": \"a\" }");

            Assert.That(agent.IsExposed, Is.True);
        }

        [Test]
        public void IsExposed_False_MeansFalse()
        {
            var agent = JsonConvert.DeserializeObject<SubAgentDefinition>("{ \"id\": \"a\", \"exposed\": false }");

            Assert.That(agent.IsExposed, Is.False);
        }

        [Test]
        public void Serialize_ConfigWithoutPipelines_DoesNotWriteTheKey()
        {
            var json = JsonConvert.SerializeObject(Config(Agent("a")), WriteSettings);

            Assert.That(json, Does.Not.Contain("\"pipelines\""));
        }

        [Test]
        public void Serialize_AgentWithoutExposed_DoesNotWriteTheKey()
        {
            var json = JsonConvert.SerializeObject(Config(Agent("a")), WriteSettings);

            Assert.That(json, Does.Not.Contain("\"exposed\""));
        }

        [Test]
        public void RoundTrip_KeepsPipelinesAndExposed()
        {
            var config = Config(Agent("a", exposed: false));
            config.Pipelines = new List<SubAgentPipelineDefinition>
            {
                Pipeline("review_flow", Step("a", "{{task}}", "first"), Step("a", "{{prev}}", "second"))
            };
            config.Pipelines[0].Output = SubAgentPipelineDefinition.OutputAll;
            config.Pipelines[0].Steps[0].Retries = 2;

            var json = JsonConvert.SerializeObject(config, WriteSettings);
            var parsed = JsonConvert.DeserializeObject<SubAgentsConfig>(json);

            Assert.That(parsed.Agents[0].IsExposed, Is.False);
            Assert.That(parsed.Pipelines, Has.Count.EqualTo(1));
            Assert.That(parsed.Pipelines[0].Id, Is.EqualTo("review_flow"));
            Assert.That(parsed.Pipelines[0].OutputsAllSteps, Is.True);
            Assert.That(parsed.Pipelines[0].Steps, Has.Count.EqualTo(2));
            Assert.That(parsed.Pipelines[0].Steps[0].Retries, Is.EqualTo(2));
            Assert.That(parsed.Pipelines[0].Steps[1].Id, Is.EqualTo("second"));
        }

        [Test]
        public void Clone_CopiesPipelinesAndExposed()
        {
            var config = Config(Agent("a", exposed: false));
            config.Pipelines = new List<SubAgentPipelineDefinition> { Pipeline("p", Step("a", "{{task}}", "s1")) };

            var clone = config.Clone();
            clone.Pipelines[0].Steps[0].Prompt = "changed";

            Assert.That(clone.Agents[0].Exposed, Is.False);
            Assert.That(clone.Pipelines, Has.Count.EqualTo(1));
            Assert.That(clone.Pipelines[0].Steps[0].Id, Is.EqualTo("s1"));
            Assert.That(config.Pipelines[0].Steps[0].Prompt, Is.EqualTo("{{task}}"), "the clone must not share the step with the original");
        }

        [Test]
        public void Clone_ParallelStep_DeepCopiesBranches()
        {
            var group = new SubAgentPipelineStep
            {
                Parallel = new List<SubAgentPipelineStep> { Step("a", "{{task}}", "left"), Step("a", "{{task}}", "right") },
                OnBranchError = SubAgentPipelineStep.BranchErrorContinue
            };
            var config = Config(Agent("a"));
            config.Pipelines = new List<SubAgentPipelineDefinition> { Pipeline("p", group) };

            var clone = config.Clone();
            clone.Pipelines[0].Steps[0].Parallel[0].Prompt = "changed";

            Assert.That(clone.Pipelines[0].Steps[0].Parallel, Has.Count.EqualTo(2));
            Assert.That(clone.Pipelines[0].Steps[0].ContinuesOnBranchError, Is.True);
            Assert.That(group.Parallel[0].Prompt, Is.EqualTo("{{task}}"));
        }

        // =====================================================================
        // Model validation
        // =====================================================================

        [Test]
        public void Validate_MissingId_IsAnError()
        {
            var pipeline = Pipeline(null, Step("a", "{{task}}"));

            Assert.That(pipeline.Validate(), Has.Some.Contains("'id' is required"));
        }

        [Test]
        public void Validate_MissingDescription_IsAnError()
        {
            var pipeline = Pipeline("p", Step("a", "{{task}}"));
            pipeline.Description = "  ";

            Assert.That(pipeline.Validate(), Has.Some.Contains("'description' is required"));
        }

        [Test]
        public void Validate_NoSteps_IsAnError()
        {
            var pipeline = Pipeline("p");

            Assert.That(pipeline.Validate(), Has.Some.Contains("at least one step"));
        }

        [Test]
        public void Validate_StepWithoutAgentAndPrompt_IsAnError()
        {
            var pipeline = Pipeline("p", new SubAgentPipelineStep());

            var errors = pipeline.Validate();

            Assert.That(errors, Has.Some.Contains("'agent' and 'prompt' are required"));
        }

        [Test]
        public void Validate_PrevInTheFirstStep_IsAnError()
        {
            var pipeline = Pipeline("p", Step("a", "{{prev}}"), Step("a", "{{prev}}"));

            Assert.That(pipeline.Validate(), Has.Some.Contains("'{{prev}}' is not available in the first step"));
        }

        [Test]
        public void Validate_DuplicatePipelineIds_IsAnError()
        {
            var config = Config(Agent("a"));
            config.Pipelines = new List<SubAgentPipelineDefinition>
            {
                Pipeline("p", Step("a", "{{task}}")),
                Pipeline("p", Step("a", "{{task}}"))
            };

            Assert.That(config.Validate(), Has.Some.Contains("duplicate pipeline id 'p'"));
        }

        [Test]
        public void Validate_DuplicateStepIds_IsAnError()
        {
            var pipeline = Pipeline("p", Step("a", "{{task}}", "s"), Step("a", "{{prev}}", "s"));

            Assert.That(pipeline.Validate(), Has.Some.Contains("duplicate step id 's'"));
        }

        [Test]
        public void Validate_StepRefToALaterStep_IsAnError()
        {
            var pipeline = Pipeline("p", Step("a", "{{steps.second}}"), Step("a", "{{prev}}", "second"));

            Assert.That(pipeline.Validate(), Has.Some.Contains("must reference an earlier step"));
        }

        [Test]
        public void Validate_StepRefToAnEarlierStep_IsAllowed()
        {
            var pipeline = Pipeline("p", Step("a", "{{task}}", "first"), Step("a", "{{steps.first}}"));

            Assert.That(pipeline.Validate(), Is.Empty);
        }

        [Test]
        public void Validate_RetriesOutOfRange_IsAnError()
        {
            var step = Step("a", "{{task}}");
            step.Retries = 4;
            var pipeline = Pipeline("p", step);

            Assert.That(pipeline.Validate(), Has.Some.Contains("'retries' must be between 0 and 3"));
        }

        [Test]
        public void Deserialize_RemovedRetryOnField_IsIgnored()
        {
            const string json = "{ \"id\": \"p\", \"description\": \"P\", \"steps\": [ { \"agent\": \"a\", \"prompt\": \"{{task}}\", \"retries\": 1, \"retryOn\": [\"timeout\"] } ] }";

            var pipeline = JsonConvert.DeserializeObject<SubAgentPipelineDefinition>(json);

            Assert.That(pipeline.Steps[0].Retries, Is.EqualTo(1));
            Assert.That(pipeline.Validate(), Is.Empty);
        }

        [Test]
        public void Validate_UnknownOutputValue_IsAnError()
        {
            var pipeline = Pipeline("p", Step("a", "{{task}}"));
            pipeline.Output = "everything";

            Assert.That(pipeline.Validate(), Has.Some.Contains("'output' must be 'last' or 'all'"));
        }

        [Test]
        public void Validate_NestedParallelGroup_IsAnError()
        {
            var inner = new SubAgentPipelineStep
            {
                Parallel = new List<SubAgentPipelineStep> { Step("a", "{{task}}") }
            };
            var group = new SubAgentPipelineStep
            {
                Parallel = new List<SubAgentPipelineStep> { inner }
            };
            var pipeline = Pipeline("p", group);

            Assert.That(pipeline.Validate(), Has.Some.Contains("nested parallel groups are not allowed"));
        }

        [Test]
        public void Validate_AgentAndParallelTogether_IsAnError()
        {
            var group = new SubAgentPipelineStep
            {
                Agent = "a",
                Prompt = "{{task}}",
                Parallel = new List<SubAgentPipelineStep> { Step("a", "{{task}}") }
            };
            var pipeline = Pipeline("p", group);

            Assert.That(pipeline.Validate(), Has.Some.Contains("not both"));
        }

        // =====================================================================
        // Manager validation
        // =====================================================================

        [Test]
        public void ManagerValidate_PipelineIdCollidingWithAgent_IsAnError()
        {
            var manager = CreateManager(new InMemoryFileSystem());
            var config = Config(Agent("review_flow"));
            config.Pipelines = new List<SubAgentPipelineDefinition> { Pipeline("review_flow", Step("review_flow", "{{task}}")) };

            Assert.That(manager.Validate(config), Has.Some.Contains("collides with a SubAgent id"));
        }

        [Test]
        public void ManagerValidate_PipelineIdCollidingWithBuiltInTool_IsAnError()
        {
            var manager = CreateManager(new InMemoryFileSystem(), "read_file_lines");
            var config = Config(Agent("a"));
            config.Pipelines = new List<SubAgentPipelineDefinition> { Pipeline("read_file_lines", Step("a", "{{task}}")) };

            Assert.That(manager.Validate(config), Has.Some.Contains("collides with a built-in tool name"));
        }

        [Test]
        public void ManagerValidate_UnknownStepAgent_IsAnError()
        {
            var manager = CreateManager(new InMemoryFileSystem());
            var config = Config(Agent("a"));
            config.Pipelines = new List<SubAgentPipelineDefinition> { Pipeline("p", Step("ghost", "{{task}}")) };

            Assert.That(manager.Validate(config), Has.Some.Contains("references unknown SubAgent 'ghost'"));
        }

        [Test]
        public void ManagerValidate_AllowedToolsReferencingAPipeline_IsAnError()
        {
            var manager = CreateManager(new InMemoryFileSystem());
            var agent = Agent("a");
            agent.AllowedTools = new List<string> { "p" };
            var config = Config(agent);
            config.Pipelines = new List<SubAgentPipelineDefinition> { Pipeline("p", Step("a", "{{task}}")) };

            Assert.That(manager.Validate(config), Has.Some.Contains("references the pipeline 'p'"));
        }

        [Test]
        public void ManagerValidate_DisabledStepAgent_IsNotAnError()
        {
            var manager = CreateManager(new InMemoryFileSystem());
            var config = Config(Agent("a", enabled: false));
            config.Pipelines = new List<SubAgentPipelineDefinition> { Pipeline("p", Step("a", "{{task}}")) };

            Assert.That(manager.Validate(config), Is.Empty, "turning an agent off in the dialog must not break saving");
        }

        // =====================================================================
        // Catalog and enabled flags
        // =====================================================================

        [Test]
        public async Task GetEnabledPipelines_ReturnsEnabledPipelinesOnly()
        {
            var fs = new InMemoryFileSystem();
            var manager = CreateManager(fs);
            WriteConfig(fs, @"{
                ""agents"": [ { ""id"": ""a"", ""description"": ""A"", ""customBaseUrl"": ""http://localhost:1234"", ""model"": ""m"" } ],
                ""pipelines"": [
                    { ""id"": ""on"", ""description"": ""d"", ""steps"": [ { ""agent"": ""a"", ""prompt"": ""{{task}}"" } ] },
                    { ""id"": ""off"", ""description"": ""d"", ""enabled"": false, ""steps"": [ { ""agent"": ""a"", ""prompt"": ""{{task}}"" } ] }
                ]
            }");

            await manager.RefreshAsync();

            var pipelines = manager.GetEnabledPipelines();

            Assert.That(pipelines.Select(p => p.Id), Is.EquivalentTo(new[] { "on" }));
        }

        [Test]
        public async Task UpdateEnabledFlagsAsync_AppliesPipelineFlagsAndKeepsEverythingElse()
        {
            var fs = new InMemoryFileSystem();
            var manager = CreateManager(fs);
            WriteConfig(fs, @"{
                ""temperature"": 0.1,
                ""agents"": [ { ""id"": ""a"", ""description"": ""A"", ""customBaseUrl"": ""http://localhost:1234"", ""model"": ""m"" } ],
                ""pipelines"": [
                    { ""id"": ""p"", ""description"": ""d"", ""steps"": [ { ""agent"": ""a"", ""prompt"": ""{{task}}"" } ] }
                ]
            }");

            var errors = await manager.UpdateEnabledFlagsAsync(
                new List<SubAgentEnabledFlag>(),
                cancellationToken: CancellationToken.None,
                pipelineFlags: new List<SubAgentEnabledFlag> { new SubAgentEnabledFlag { Id = "p", Enabled = false } });

            Assert.That(errors, Is.Empty);

            var config = await manager.GetAsync();
            Assert.That(config.Pipelines[0].IsEnabled, Is.False);
            Assert.That(config.Pipelines[0].Steps, Has.Count.EqualTo(1), "the raw pipeline definition must survive the save");
            Assert.That(config.Temperature, Is.EqualTo(0.1));
        }
    }
}
