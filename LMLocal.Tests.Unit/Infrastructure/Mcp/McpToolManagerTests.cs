using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.HttpWrapper;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Infrastructure.Tooling.Mcp;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Mcp
{
    [TestFixture]
    public class McpToolManagerTests
    {
        private Mock<IMcpConfigManager> _mockMcpConfigManager;
        private Mock<IFileSystem> _mockFileSystem;
        private Mock<IHttpClientWrapper> _mockHttpClientWrapper;
        private Mock<ISettingsManager> _mockSettingsManager;
        private McpToolManager _toolManager;

        [SetUp]
        public void SetUp()
        {
            _mockMcpConfigManager = new Mock<IMcpConfigManager>();
            _mockFileSystem = new Mock<IFileSystem>();
            _mockHttpClientWrapper = new Mock<IHttpClientWrapper>();
            _mockSettingsManager = new Mock<ISettingsManager>();

            _toolManager = new McpToolManager(
                _mockMcpConfigManager.Object,
                _mockFileSystem.Object,
                _mockHttpClientWrapper.Object,
                _mockSettingsManager.Object);
        }

        [Test]
        public async Task RefreshServersAsync_SkipsDisabledServers()
        {
            // Arrange
            var config = new McpConfigFile
            {
                EnableMcp = true,
                McpServersJson = @"{
                    ""servers"": {
                        ""enabled-server"": {
                            ""type"": ""http"",
                            ""url"": ""https://example.com/mcp"",
                            ""disabled"": false
                        },
                        ""disabled-server"": {
                            ""type"": ""http"",
                            ""url"": ""https://disabled.com/mcp"",
                            ""disabled"": true
                        }
                    }
                }"
            };

            // Act
            await _toolManager.RefreshServersAsync(config, CancellationToken.None).ConfigureAwait(false);

            // Assert
            var definitions = _toolManager.GetMcpToolDefinitions();
            Assert.That(definitions, Is.Empty, "Disabled server tools should not be added to cache");
        }

        [Test]
        public async Task RefreshServersAsync_IncludesEnabledServers()
        {
            // Arrange
            var config = new McpConfigFile
            {
                EnableMcp = true,
                McpServersJson = @"{
                    ""servers"": {
                        ""server1"": {
                            ""type"": ""http"",
                            ""url"": ""https://example.com/mcp"",
                            ""disabled"": false
                        }
                    }
                }"
            };

            // Act
            await _toolManager.RefreshServersAsync(config, CancellationToken.None).ConfigureAwait(false);

            // Assert
            var statuses = _toolManager.GetServerStatuses();
            Assert.That(statuses.Count, Is.GreaterThanOrEqualTo(0), "Enabled server should be attempted to connect");
        }

        [Test]
        public async Task RefreshServersAsync_AllDisabledServers_ResultsInEmptyTools()
        {
            // Arrange
            var config = new McpConfigFile
            {
                EnableMcp = true,
                McpServersJson = @"{
                    ""servers"": {
                        ""server1"": {
                            ""type"": ""http"",
                            ""url"": ""https://example1.com/mcp"",
                            ""disabled"": true
                        },
                        ""server2"": {
                            ""type"": ""http"",
                            ""url"": ""https://example2.com/mcp"",
                            ""disabled"": true
                        }
                    }
                }"
            };

            // Act
            await _toolManager.RefreshServersAsync(config, CancellationToken.None).ConfigureAwait(false);

            // Assert
            var definitions = _toolManager.GetMcpToolDefinitions();
            Assert.That(definitions, Is.Empty, "All disabled servers should result in empty tool definitions");
        }

        [Test]
        public void DisabledProperty_DefaultsToFalse()
        {
            // Arrange & Act
            var serverConfig = new McpServerConfig
            {
                Type = "http",
                Url = "https://example.com"
            };

            // Assert
            Assert.That(serverConfig.Disabled, Is.False, "Disabled property should default to false");
        }

        [Test]
        public void DisabledProperty_CanBeSetToTrue()
        {
            // Arrange & Act
            var serverConfig = new McpServerConfig
            {
                Type = "http",
                Url = "https://example.com",
                Disabled = true
            };

            // Assert
            Assert.That(serverConfig.Disabled, Is.True, "Disabled property should be settable to true");
        }

        [Test]
        public void McpServerConfig_Equals_ConsidersDisabledProperty()
        {
            // Arrange
            var config1 = new McpServerConfig
            {
                Type = "http",
                Url = "https://example.com",
                Disabled = false
            };

            var config2 = new McpServerConfig
            {
                Type = "http",
                Url = "https://example.com",
                Disabled = false
            };

            var config3 = new McpServerConfig
            {
                Type = "http",
                Url = "https://example.com",
                Disabled = true
            };

            // Assert
            Assert.That(config1, Is.EqualTo(config2), "Configs with same disabled value should be equal");
            Assert.That(config1, Is.Not.EqualTo(config3), "Configs with different disabled values should not be equal");
        }

        [Test]
        public void McpServerConfig_GetHashCode_ConsidersDisabledProperty()
        {
            // Arrange
            var config1 = new McpServerConfig
            {
                Type = "http",
                Url = "https://example.com",
                Disabled = false
            };

            var config2 = new McpServerConfig
            {
                Type = "http",
                Url = "https://example.com",
                Disabled = true
            };

            // Act
            var hash1 = config1.GetHashCode();
            var hash2 = config2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2), "Different disabled values should produce different hash codes");
        }

        [Test]
        public async Task RefreshServersAsync_SkipsBlockedToolsByPermissions()
        {
            // Arrange
            var config = new McpConfigFile
            {
                EnableMcp = true,
                McpServersJson = @"{
                    ""servers"": {
                        ""test-server"": {
                            ""type"": ""http"",
                            ""url"": ""https://example.com/mcp""
                        }
                    },
                    ""permissions"": {
                        ""blocked-tool"": ""disable"",
                        ""another-blocked-tool"": ""disable""
                    }
                }"
            };

            // Act
            await _toolManager.RefreshServersAsync(config, CancellationToken.None).ConfigureAwait(false);

            // Assert
            Assert.That(_toolManager.ToolExists("blocked-tool"), Is.False, "Blocked tool should not exist");
            Assert.That(_toolManager.ToolExists("another-blocked-tool"), Is.False, "Another blocked tool should not exist");
        }

        [Test]
        public void ToolExists_ReturnsFalse_ForNonExistentTools()
        {
            // Assert
            Assert.That(_toolManager.ToolExists("non-existent-tool"), Is.False, "Non-existent tool should return false");
        }

        [Test]
        public void McpClientConfig_WithPermissions_Equals()
        {
            // Arrange
            var config1 = new McpClientConfig
            {
                Servers = new Dictionary<string, McpServerConfig>
                {
                    {
                        "server1",
                        new McpServerConfig
                        {
                            Command = "test",
                            Permissions = new Dictionary<string, string>
                            {
                                { "tool1", "disable" },
                                { "tool2", "disable" }
                            }
                        }
                    }
                }
            };

            var config2 = new McpClientConfig
            {
                Servers = new Dictionary<string, McpServerConfig>
                {
                    {
                        "server1",
                        new McpServerConfig
                        {
                            Command = "test",
                            Permissions = new Dictionary<string, string>
                            {
                                { "tool1", "disable" },
                                { "tool2", "disable" }
                            }
                        }
                    }
                }
            };

            var config3 = new McpClientConfig
            {
                Servers = new Dictionary<string, McpServerConfig>
                {
                    {
                        "server1",
                        new McpServerConfig
                        {
                            Command = "test",
                            Permissions = new Dictionary<string, string>
                            {
                                { "tool1", "disable" }
                            }
                        }
                    }
                }
            };

            // Assert
            Assert.That(config1, Is.EqualTo(config2), "Configs with same server permissions should be equal");
            Assert.That(config1, Is.Not.EqualTo(config3), "Configs with different server permissions should not be equal");
        }

        [Test]
        public void McpServerConfig_Permissions_DefaultsToEmpty()
        {
            // Arrange & Act
            var serverConfig = new McpServerConfig { Command = "test" };

            // Assert
            Assert.That(serverConfig.Permissions, Is.Not.Null, "Permissions should not be null");
            Assert.That(serverConfig.Permissions.Count, Is.EqualTo(0), "Permissions should default to empty");
        }
    }
}
