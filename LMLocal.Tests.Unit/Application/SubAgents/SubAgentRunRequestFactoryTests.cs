using System.Collections.Generic;
using System.Threading;
using LMLocal.Application.SubAgents;
using LMLocal.Application.Tool;
using LMLocal.Core.Models;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.SubAgents
{
    [TestFixture]
    public class SubAgentRunRequestFactoryTests
    {
        private static SubAgentDefinition FullAgent()
        {
            return new SubAgentDefinition
            {
                Id = "  agent-a  ",
                DisplayName = "Agent A",
                System = "sys",
                ProviderType = "openai",
                CustomBaseUrl = "https://api.openai.com",
                CustomApiKey = "key",
                Model = "gpt-4o",
                Temperature = 0.2,
                ReasoningEffort = "low",
                MaxTokens = 1000,
                TimeoutSeconds = 60,
                MaxRounds = 4,
                AllowedTools = new List<string> { "read_file_lines" }
            };
        }

        private static SubAgentsConfig RootDefaults()
        {
            return new SubAgentsConfig
            {
                ProviderType = "lmstudio",
                CustomBaseUrl = "http://localhost:1234",
                CustomApiKey = "root-key",
                Model = "root-model",
                Temperature = 0.7,
                TimeoutSeconds = 30,
                MaxRounds = 9,
                MaxTokens = 500,
                ReasoningEffort = "high"
            };
        }

        [Test]
        public void Create_CopiesTheAgentValues()
        {
            var request = SubAgentRunRequestFactory.Create(FullAgent(), new SubAgentRunRequestOptions { Prompt = "do it" });

            Assert.That(request.AgentName, Is.EqualTo("agent-a"));
            Assert.That(request.Prompt, Is.EqualTo("do it"));
            Assert.That(request.System, Is.EqualTo("sys"));
            Assert.That(request.ProviderType, Is.EqualTo("openai"));
            Assert.That(request.BaseUrl, Is.EqualTo("https://api.openai.com"));
            Assert.That(request.ApiKey, Is.EqualTo("key"));
            Assert.That(request.Model, Is.EqualTo("gpt-4o"));
            Assert.That(request.Temperature, Is.EqualTo(0.2));
            Assert.That(request.ReasoningEffort, Is.EqualTo("low"));
            Assert.That(request.MaxTokens, Is.EqualTo(1000));
            Assert.That(request.TimeoutSeconds, Is.EqualTo(60));
            Assert.That(request.MaxRounds, Is.EqualTo(4));
            Assert.That(request.AllowedTools, Is.EquivalentTo(new[] { "read_file_lines" }));
        }

        [Test]
        public void Create_WithoutRootDefaults_LeavesEmptyValuesEmpty()
        {
            var agent = new SubAgentDefinition { Id = "a", Description = "d" };

            var request = SubAgentRunRequestFactory.Create(agent, null);

            Assert.That(request.Model, Is.Null);
            Assert.That(request.TimeoutSeconds, Is.Null);
            Assert.That(request.ProviderType, Is.Null);
        }

        [Test]
        public void Create_WithRootDefaults_FillsWhatTheAgentLeavesEmpty()
        {
            var agent = new SubAgentDefinition { Id = "a", Description = "d" };

            var request = SubAgentRunRequestFactory.Create(agent, new SubAgentRunRequestOptions
            {
                Prompt = "p",
                RootDefaults = RootDefaults()
            });

            Assert.That(request.Model, Is.EqualTo("root-model"));
            Assert.That(request.Temperature, Is.EqualTo(0.7));
            Assert.That(request.TimeoutSeconds, Is.EqualTo(30));
            Assert.That(request.MaxRounds, Is.EqualTo(9));
            Assert.That(request.MaxTokens, Is.EqualTo(500));
            Assert.That(request.ReasoningEffort, Is.EqualTo("high"));
            Assert.That(request.ProviderType, Is.EqualTo("lmstudio"));
            Assert.That(request.BaseUrl, Is.EqualTo("http://localhost:1234"));
            Assert.That(request.ApiKey, Is.EqualTo("root-key"));
        }

        [Test]
        public void Create_AgentValueWinsOverTheRootDefault()
        {
            var request = SubAgentRunRequestFactory.Create(FullAgent(), new SubAgentRunRequestOptions
            {
                RootDefaults = RootDefaults()
            });

            Assert.That(request.Model, Is.EqualTo("gpt-4o"));
            Assert.That(request.TimeoutSeconds, Is.EqualTo(60));
            Assert.That(request.ProviderType, Is.EqualTo("openai"));
        }

        [Test]
        public void Create_OverridesWinOverBoth()
        {
            var request = SubAgentRunRequestFactory.Create(FullAgent(), new SubAgentRunRequestOptions
            {
                ModelOverride = "override-model",
                MaxRoundsOverride = 1,
                RootDefaults = RootDefaults()
            });

            Assert.That(request.Model, Is.EqualTo("override-model"));
            Assert.That(request.MaxRounds, Is.EqualTo(1));
        }

        [Test]
        public void Create_CopiesTheRunContext()
        {
            var progress = new ProgressCollector();
            var excluded = new List<string> { "other" };

            var request = SubAgentRunRequestFactory.Create(FullAgent(), new SubAgentRunRequestOptions
            {
                Prompt = "p",
                Progress = progress,
                ParentSessionId = "session-1",
                RequestId = "request-1",
                Origin = "ipc",
                ExcludedAgentNames = excluded
            });

            Assert.That(request.Progress, Is.SameAs(progress));
            Assert.That(request.ParentSessionId, Is.EqualTo("session-1"));
            Assert.That(request.RequestId, Is.EqualTo("request-1"));
            Assert.That(request.Origin, Is.EqualTo("ipc"));
            Assert.That(request.ExcludedAgentNames, Is.SameAs(excluded));
        }

        [Test]
        public void Create_NullAgent_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => SubAgentRunRequestFactory.Create(null, null));
        }

        private sealed class ProgressCollector : System.IProgress<ToolActivityEvent>
        {
            public void Report(ToolActivityEvent value)
            {
            }
        }
    }
}
