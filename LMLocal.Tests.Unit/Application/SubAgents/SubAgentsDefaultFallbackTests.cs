using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.SubAgents;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using LMLocal.Tests.Unit.Infrastructure;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.SubAgents
{
    /// <summary>
    /// Verifies the runtime fallback to the built-in default SubAgents shipped with the extension
    /// (see docs\SubAgent_model_default.md, Часть 6 / v5): when no custom subagents.json exists the
    /// runtime snapshot (<see cref="SubAgentsConfigManager.RefreshAsync"/>) is populated from the default
    /// resource, while <see cref="SubAgentsConfigManager.GetAsync"/> (dialog / IPC) stays custom-only.
    /// </summary>
    [TestFixture]
    public class SubAgentsDefaultFallbackTests
    {
        private const string DefaultConfigPath = @"C:\app\Resources\json\subagents.json";

        private const string DefaultFixtureJson = @"{
            ""agents"": [
                {
                    ""id"": ""default_researcher"",
                    ""description"": ""Built-in default agent"",
                    ""customBaseUrl"": ""http://localhost:1234/v1"",
                    ""model"": ""default-model"",
                    ""enabled"": true
                }
            ]
        }";

        private static string CustomConfigPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LMLocalChat",
                "subagents.json");
        }

        private static (SubAgentsConfigManager manager, InMemoryFileSystem fs, string customPath) CreateManager()
        {
            var fs = new InMemoryFileSystem();

            var settings = new Mock<ISettingsManager>();
            settings.Setup(s => s.LocalAppDataFolder).Returns("LMLocalChat");

            var builtInTools = new Mock<IBuiltInVsToolProvider>();
            builtInTools.Setup(b => b.GetAllToolDefinitionsUnfiltered())
                .Returns(new List<ToolDefinition>());

            var manager = new SubAgentsConfigManager(
                fs,
                settings.Object,
                builtInTools.Object,
                DefaultConfigPath);

            return (manager, fs, CustomConfigPath());
        }

        private static void SeedDefault(InMemoryFileSystem fs)
        {
            fs.WriteAllBytesAsync(DefaultConfigPath, System.Text.Encoding.UTF8.GetBytes(DefaultFixtureJson))
                .GetAwaiter().GetResult();
        }

        private static void SeedCustom(InMemoryFileSystem fs, string path, string json)
        {
            fs.WriteAllBytesAsync(path, System.Text.Encoding.UTF8.GetBytes(json))
                .GetAwaiter().GetResult();
        }

        [Test]
        public async Task RefreshAsync_MissingCustomFile_SnapshotHasDefaults()
        {
            var (manager, fs, _) = CreateManager();
            SeedDefault(fs);

            await manager.RefreshAsync();

            var snapshot = manager.TryGetSnapshot();
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.Agents, Is.Not.Empty);
            Assert.That(snapshot.Agents.Any(a => a.Id == "default_researcher"), Is.True);

            var enabled = manager.GetEnabledAgents();
            Assert.That(enabled.Any(a => a.Id == "default_researcher"), Is.True);
        }

        [Test]
        public async Task RefreshAsync_MissingCustomFile_DoesNotCreateCustomFile()
        {
            var (manager, fs, customPath) = CreateManager();
            SeedDefault(fs);

            await manager.RefreshAsync();

            Assert.That(fs.FileExists(customPath), Is.False);
        }

        [Test]
        public async Task RefreshAsync_CustomFileExists_CustomWins()
        {
            var (manager, fs, customPath) = CreateManager();
            SeedDefault(fs);
            SeedCustom(fs, customPath,
                "{ \"agents\": [ { \"id\": \"my_custom\", \"description\": \"Custom agent\", \"customBaseUrl\": \"http://localhost:1234/v1\", \"model\": \"custom-model\", \"enabled\": true } ] }");

            await manager.RefreshAsync();

            var snapshot = manager.TryGetSnapshot();
            Assert.That(snapshot.Agents.Any(a => a.Id == "my_custom"), Is.True);
            Assert.That(snapshot.Agents.Any(a => a.Id == "default_researcher"), Is.False);
        }

        [Test]
        public async Task GetAsync_MissingCustomFile_StillEmpty()
        {
            var (manager, fs, _) = CreateManager();
            SeedDefault(fs);

            var cfg = await manager.GetAsync();

            Assert.That(cfg, Is.Not.Null);
            Assert.That(cfg.Agents, Is.Empty);
        }

        [Test]
        public async Task RefreshAsync_MissingDefaultResource_EmptyNoThrow()
        {
            var (manager, _, _) = CreateManager();

            await manager.RefreshAsync();

            var snapshot = manager.TryGetSnapshot();
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.Agents, Is.Empty);
        }
    }
}
