using LMLocal.Core.Models;
using Newtonsoft.Json;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.SubAgents
{
    /// <summary>
    /// ReasoningEffort in the SubAgents config: top-level (global) default + per-agent value,
    /// inherited via ApplyDefaults when the agent has no value of its own, preserved by Clone,
    /// and omitted from subagents.json when absent (same as the other optional settings).
    /// </summary>
    [TestFixture]
    public class SubAgentsReasoningEffortTests
    {
        private static SubAgentDefinition Agent(string reasoningEffort = null)
        {
            return new SubAgentDefinition
            {
                Id = "agent",
                Description = "Agent",
                CustomBaseUrl = "http://localhost:1234",
                Model = "m",
                ReasoningEffort = reasoningEffort
            };
        }

        private static readonly JsonSerializerSettings IndentedIgnoreNulls = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        // -----------------------------------------------------------------
        // ApplyDefaults: root (global) default fills agents without a value
        // -----------------------------------------------------------------

        [Test]
        public void ApplyDefaults_MissingReasoning_FilledFromRoot()
        {
            var cfg = new SubAgentsConfig { ReasoningEffort = "high" };
            cfg.Agents.Add(Agent());

            cfg.ApplyDefaults();

            Assert.That(cfg.Agents[0].ReasoningEffort, Is.EqualTo("high"));
        }

        [Test]
        public void ApplyDefaults_AgentReasoning_OverridesRoot()
        {
            var cfg = new SubAgentsConfig { ReasoningEffort = "high" };
            cfg.Agents.Add(Agent(reasoningEffort: "low"));

            cfg.ApplyDefaults();

            Assert.That(cfg.Agents[0].ReasoningEffort, Is.EqualTo("low"));
        }

        [Test]
        public void ApplyDefaults_EmptyAgentReasoning_ReplacedWithRoot()
        {
            var cfg = new SubAgentsConfig { ReasoningEffort = "medium" };
            cfg.Agents.Add(Agent(reasoningEffort: ""));

            cfg.ApplyDefaults();

            Assert.That(cfg.Agents[0].ReasoningEffort, Is.EqualTo("medium"));
        }

        [Test]
        public void ApplyDefaults_NoReasoningAnywhere_StaysNull()
        {
            var cfg = new SubAgentsConfig();
            cfg.Agents.Add(Agent());

            cfg.ApplyDefaults();

            Assert.That(cfg.Agents[0].ReasoningEffort, Is.Null);
        }

        // -----------------------------------------------------------------
        // Clone
        // -----------------------------------------------------------------

        [Test]
        public void Clone_CopiesReasoningEffort_RootAndAgent()
        {
            var cfg = new SubAgentsConfig { ReasoningEffort = "high" };
            cfg.Agents.Add(Agent(reasoningEffort: "low"));

            var clone = cfg.Clone();

            Assert.That(clone.ReasoningEffort, Is.EqualTo("high"));
            Assert.That(clone.Agents[0].ReasoningEffort, Is.EqualTo("low"));

            // mutation isolation
            clone.ReasoningEffort = "max";
            clone.Agents[0].ReasoningEffort = "none";

            Assert.That(cfg.ReasoningEffort, Is.EqualTo("high"));
            Assert.That(cfg.Agents[0].ReasoningEffort, Is.EqualTo("low"));
        }

        // -----------------------------------------------------------------
        // JSON (subagents.json) round-trip
        // -----------------------------------------------------------------

        [Test]
        public void Deserialize_ParsesRootAndAgentReasoningEffort()
        {
            const string json = @"{
                ""reasoningEffort"": ""high"",
                ""agents"": [
                    { ""id"": ""coder"", ""description"": ""A"", ""customBaseUrl"": ""http://localhost:1234"", ""model"": ""m"", ""reasoningEffort"": ""low"" },
                    { ""id"": ""writer"", ""description"": ""B"", ""customBaseUrl"": ""http://localhost:1234"", ""model"": ""m"" }
                ]
            }";

            var cfg = JsonConvert.DeserializeObject<SubAgentsConfig>(json);

            Assert.That(cfg, Is.Not.Null);
            Assert.That(cfg.ReasoningEffort, Is.EqualTo("high"));
            Assert.That(cfg.Agents[0].ReasoningEffort, Is.EqualTo("low"));
            Assert.That(cfg.Agents[1].ReasoningEffort, Is.Null);
        }

        [Test]
        public void Serialize_AbsentReasoningEffort_IsNotWritten()
        {
            var cfg = new SubAgentsConfig();
            cfg.Agents.Add(Agent());

            var json = JsonConvert.SerializeObject(cfg, IndentedIgnoreNulls);

            Assert.That(json, Does.Not.Contain("reasoningEffort"));
        }

        [Test]
        public void Serialize_SetReasoningEffort_IsWritten()
        {
            var cfg = new SubAgentsConfig { ReasoningEffort = "high" };
            cfg.Agents.Add(Agent(reasoningEffort: "low"));

            var json = JsonConvert.SerializeObject(cfg, IndentedIgnoreNulls);

            Assert.That(json, Does.Contain("\"reasoningEffort\": \"high\""));
            Assert.That(json, Does.Contain("\"reasoningEffort\": \"low\""));
        }
    }
}
