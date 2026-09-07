using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.SubAgents;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using LMLocal.Tests.Unit.Infrastructure;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.SubAgents
{
    // =========================================================================
    // Manager — v4.2/v4.5: raw persistence of enabled flags.
    // SaveAsync/UpdateEnabledFlagsAsync must write the RAW config (no top-level
    // defaults baked into agents, no null literals) and keep the in-memory
    // snapshot EFFECTIVE (inherited defaults applied).
    // =========================================================================

    [TestFixture]
    public class SubAgentsRawPersistenceTests
    {
        private static (SubAgentsConfigManager Manager, InMemoryFileSystem Fs, string FilePath) CreateManager()
        {
            var fs = new InMemoryFileSystem();
            var settings = new Mock<ISettingsManager>();
            settings.Setup(s => s.LocalAppDataFolder).Returns("LMLocalChatUnit");

            var builtInTools = new Mock<IBuiltInVsToolProvider>();
            builtInTools.Setup(b => b.GetAllToolDefinitionsUnfiltered())
                .Returns(new List<ToolDefinition>());

            var manager = new SubAgentsConfigManager(fs, settings.Object, builtInTools.Object);

            var filePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LMLocalChatUnit",
                "subagents.json");

            return (manager, fs, filePath);
        }

        private static void WriteConfig(InMemoryFileSystem fs, string filePath, string json)
        {
            fs.WriteAllBytesAsync(filePath, System.Text.Encoding.UTF8.GetBytes(json)).GetAwaiter().GetResult();
        }

        private static string ReadFileText(InMemoryFileSystem fs, string filePath)
        {
            return fs.ReadAllTextAsync(filePath).GetAwaiter().GetResult();
        }

        // ---------------------------------------------------------------------
        // UpdateEnabledFlagsAsync writes the file
        // ---------------------------------------------------------------------

        [Test]
        public async Task UpdateEnabledFlags_WritesFile()
        {
            var (manager, fs, path) = CreateManager();
            WriteConfig(fs, path, @"{ ""agents"": [ { ""id"": ""coder"", ""description"": ""A"", ""customBaseUrl"": ""http://localhost:1234"", ""model"": ""m"", ""enabled"": true } ] }");

            var errors = await manager.UpdateEnabledFlagsAsync(
                new List<SubAgentEnabledFlag> { new SubAgentEnabledFlag { Id = "coder", Enabled = false } });

            Assert.That(errors, Is.Empty);

            var saved = ReadFileText(fs, path).FromJson<SubAgentsConfig>();
            Assert.That(saved.Agents[0].Enabled, Is.False);
        }

        [Test]
        public async Task UpdateEnabledFlags_PreservesRawAgentFields()
        {
            var (manager, fs, path) = CreateManager();
            WriteConfig(fs, path, @"{ ""model"": ""root-model"", ""customBaseUrl"": ""http://localhost:1234"", ""agents"": [ { ""id"": ""coder"", ""description"": ""A"", ""enabled"": true } ] }");

            var errors = await manager.UpdateEnabledFlagsAsync(new List<SubAgentEnabledFlag>());
            Assert.That(errors, Is.Empty);

            var fileText = ReadFileText(fs, path);
            var saved = fileText.FromJson<SubAgentsConfig>();

            // Root default stays in the file...
            Assert.That(saved.Model, Is.EqualTo("root-model"));
            // ...but is NOT baked into the agent.
            Assert.That(string.IsNullOrWhiteSpace(saved.Agents[0].Model), Is.True);
        }

        [Test]
        public async Task UpdateEnabledFlags_KeepsRootDefaults()
        {
            var (manager, fs, path) = CreateManager();
            WriteConfig(fs, path, @"{ ""model"": ""root-model"", ""temperature"": 0.1, ""timeoutSeconds"": 480, ""customBaseUrl"": ""http://localhost:1234"", ""agents"": [ { ""id"": ""coder"", ""description"": ""A"", ""enabled"": true } ] }");

            var errors = await manager.UpdateEnabledFlagsAsync(new List<SubAgentEnabledFlag>());
            Assert.That(errors, Is.Empty);

            var saved = ReadFileText(fs, path).FromJson<SubAgentsConfig>();
            Assert.That(saved.Model, Is.EqualTo("root-model"));
            Assert.That(saved.Temperature, Is.EqualTo(0.1));
            Assert.That(saved.TimeoutSeconds, Is.EqualTo(480));
        }

        [Test]
        public async Task UpdateEnabledFlags_SnapshotStaysEffective()
        {
            var (manager, fs, path) = CreateManager();
            WriteConfig(fs, path, @"{ ""model"": ""root-model"", ""customBaseUrl"": ""http://localhost:1234"", ""agents"": [ { ""id"": ""coder"", ""description"": ""A"", ""enabled"": true } ] }");

            var errors = await manager.UpdateEnabledFlagsAsync(new List<SubAgentEnabledFlag>());
            Assert.That(errors, Is.Empty);

            var snapshot = manager.TryGetSnapshot();
            Assert.That(snapshot.Agents[0].Model, Is.EqualTo("root-model"));
        }

        [Test]
        public async Task UpdateEnabledFlags_ExplicitNull_BecomesAbsent()
        {
            var (manager, fs, path) = CreateManager();
            WriteConfig(fs, path, @"{ ""customApiKey"": null, ""customBaseUrl"": ""http://localhost:1234"", ""agents"": [ { ""id"": ""coder"", ""description"": ""A"", ""model"": ""m"", ""enabled"": true } ] }");

            var errors = await manager.UpdateEnabledFlagsAsync(new List<SubAgentEnabledFlag>());
            Assert.That(errors, Is.Empty);

            var fileText = ReadFileText(fs, path);
            Assert.That(fileText, Does.Not.Contain("customApiKey"));
        }

        [Test]
        public async Task UpdateEnabledFlags_InvalidConfig_FileUntouched()
        {
            var (manager, fs, path) = CreateManager();
            var original = @"{ ""agents"": [ { ""id"": ""coder"", ""description"": ""A"", ""customBaseUrl"": ""http://a"", ""model"": ""m"" }, { ""id"": ""coder"", ""description"": ""B"", ""customBaseUrl"": ""http://b"", ""model"": ""m"" } ] }";
            WriteConfig(fs, path, original);

            var errors = await manager.UpdateEnabledFlagsAsync(new List<SubAgentEnabledFlag>());

            Assert.That(errors, Is.Not.Empty);
            Assert.That(errors.Any(e => e.Contains("not unique") || e.Contains("duplicate")), Is.True);
            Assert.That(ReadFileText(fs, path), Is.EqualTo(original));
        }

        // ---------------------------------------------------------------------
        // SaveAsync validates effective and writes raw
        // ---------------------------------------------------------------------

        [Test]
        public async Task SaveAsync_ValidatesEffective_WritesRaw()
        {
            var (manager, fs, path) = CreateManager();
            var rawJson = @"{ ""model"": ""root-model"", ""customBaseUrl"": ""http://localhost:1234"", ""agents"": [ { ""id"": ""coder"", ""description"": ""A"", ""enabled"": true } ] }";
            WriteConfig(fs, path, rawJson);

            var config = rawJson.FromJson<SubAgentsConfig>();

            await manager.SaveAsync(config);

            var saved = ReadFileText(fs, path).FromJson<SubAgentsConfig>();
            // raw on disk: bare agent, root default NOT baked in
            Assert.That(string.IsNullOrWhiteSpace(saved.Agents[0].Model), Is.True);
            Assert.That(saved.Model, Is.EqualTo("root-model"));

            // snapshot: effective
            Assert.That(manager.TryGetSnapshot().Agents[0].Model, Is.EqualTo("root-model"));
        }

        [Test]
        public void SaveAsync_RejectsInvalidRaw_EvenIfRootWouldFixIt()
        {
            var (manager, fs, path) = CreateManager();
            WriteConfig(fs, path, @"{}");

            // Bare agent with no model and no root model -> must still fail.
            var config = new SubAgentsConfig();
            config.Agents.Add(new SubAgentDefinition
            {
                Id = "coder",
                Description = "A",
                CustomBaseUrl = "http://localhost:1234",
                Enabled = true
            });

            Assert.ThrowsAsync<ArgumentException>(() => manager.SaveAsync(config));
        }

        [Test]
        public async Task SaveAsync_NoNullsWritten()
        {
            var (manager, fs, path) = CreateManager();
            var config = new SubAgentsConfig
            {
                ProviderType = "lmstudio",
                CustomBaseUrl = "http://localhost:1234",
                Model = "root-model"
            };
            config.Agents.Add(new SubAgentDefinition
            {
                Id = "coder",
                Description = "A",
                CustomBaseUrl = "http://localhost:1234",
                Model = "m",
                Enabled = true
            });

            await manager.SaveAsync(config);

            var fileText = ReadFileText(fs, path);
            Assert.That(fileText, Does.Not.Contain("customApiKey"));
            Assert.That(fileText, Does.Not.Contain("temperature"));
        }

        // ---------------------------------------------------------------------
        // Use Active Model must not bake a model into agents without one
        // ---------------------------------------------------------------------

        [Test]
        public async Task UseActiveModel_AgentWithoutModel_NotAdded()
        {
            var (manager, fs, path) = CreateManager();
            WriteConfig(fs, path, @"{ ""customBaseUrl"": ""http://localhost:1234"", ""agents"": [ { ""id"": ""coder"", ""description"": ""A"", ""enabled"": true } ] }");

            var errors = await manager.UpdateEnabledFlagsAsync(
                new List<SubAgentEnabledFlag>(),
                cancellationToken: CancellationToken.None,
                defaults: new SubAgentDefaults { Model = "active-model" });

            Assert.That(errors, Is.Empty);

            var saved = ReadFileText(fs, path).FromJson<SubAgentsConfig>();
            // root updated, agent left raw (no baked model)...
            Assert.That(saved.Model, Is.EqualTo("active-model"));
            Assert.That(string.IsNullOrWhiteSpace(saved.Agents[0].Model), Is.True);
            // ...but effective snapshot shows the inherited model.
            Assert.That(manager.TryGetSnapshot().Agents[0].Model, Is.EqualTo("active-model"));
        }

        // ---------------------------------------------------------------------
        // Clone
        // ---------------------------------------------------------------------

        [Test]
        public void Clone_DeepCopy_AgentsIndependent()
        {
            var config = new SubAgentsConfig
            {
                Model = "root",
                Agents =
                {
                    new SubAgentDefinition
                    {
                        Id = "coder",
                        Description = "A",
                        CustomBaseUrl = "http://localhost:1234",
                        Model = "m",
                        AllowedTools = new List<string> { "find_files", "grep" }
                    }
                }
            };

            var clone = config.Clone();

            Assert.That(clone, Is.Not.SameAs(config));
            Assert.That(clone.Agents[0], Is.Not.SameAs(config.Agents[0]));
            Assert.That(clone.Agents[0].AllowedTools, Is.Not.SameAs(config.Agents[0].AllowedTools));

            clone.Model = "changed";
            clone.Agents[0].Id = "changed-id";
            clone.Agents[0].AllowedTools.Add("extra");

            Assert.That(config.Model, Is.EqualTo("root"));
            Assert.That(config.Agents[0].Id, Is.EqualTo("coder"));
            Assert.That(config.Agents[0].AllowedTools, Has.Count.EqualTo(2));
        }

        [Test]
        public void Clone_CoversAllPublicProperties()
        {
            var source = new SubAgentsConfig
            {
                ProviderType = "provider-root",
                CustomBaseUrl = "http://root.example.com",
                CustomApiKey = "root-key",
                Model = "root-model",
                Temperature = 0.4,
                TimeoutSeconds = 42,
                MaxRounds = 7,
                MaxTokens = 2048
            };
            source.Agents.Add(new SubAgentDefinition
            {
                Id = "coder",
                DisplayName = "Coder",
                Description = "Codes",
                ProviderType = "provider-agent",
                CustomBaseUrl = "http://agent.example.com",
                CustomApiKey = "agent-key",
                Model = "agent-model",
                System = "be nice",
                Temperature = 0.8,
                TimeoutSeconds = 99,
                MaxRounds = 3,
                MaxTokens = 1024,
                Enabled = false,
                AllowedTools = new List<string> { "find_files", "grep" }
            });

            var clone = source.Clone();

            Assert.That(clone, Is.Not.SameAs(source));

            // Every writable public property of SubAgentsConfig must survive cloning
            // (Errors is runtime-only/[JsonIgnore] and intentionally not copied).
            foreach (var property in typeof(SubAgentsConfig)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite
                    && p.Name != nameof(SubAgentsConfig.Errors)
                    && p.Name != nameof(SubAgentsConfig.Agents)))
            {
                Assert.That(
                    property.GetValue(clone),
                    Is.EqualTo(property.GetValue(source)),
                    $"SubAgentsConfig.{property.Name} was not cloned");
            }

            Assert.That(clone.Agents, Has.Count.EqualTo(source.Agents.Count));
            Assert.That(clone.Agents[0], Is.Not.SameAs(source.Agents[0]));

            // Same for every public property of SubAgentDefinition.
            foreach (var property in typeof(SubAgentDefinition)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite))
            {
                var expected = property.GetValue(source.Agents[0]);
                var actual = property.GetValue(clone.Agents[0]);

                if (expected is List<string> expectedList)
                {
                    Assert.That((List<string>)actual, Is.Not.SameAs(expectedList), $"SubAgentDefinition.{property.Name} list must be deep-copied");
                    Assert.That((List<string>)actual, Is.EqualTo(expectedList), $"SubAgentDefinition.{property.Name} was not cloned");
                }
                else
                {
                    Assert.That(actual, Is.EqualTo(expected), $"SubAgentDefinition.{property.Name} was not cloned");
                }
            }
        }
    }
}
