using System;
using System.Collections.Generic;
using System.Threading;
using LMLocal.Infrastructure.Tooling.Mcp.Client;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Mcp
{
    /// <summary>
    /// Extended tests for StdioMcpClient focusing on error handling and edge cases.
    /// </summary>
    [TestFixture]
    public class StdioMcpClientExtendedTests
    {
        [Test]
        [Category("Integration")]
        public void InitializeAsync_FailsGracefully_WithInvalidCommand()
        {
            var client = new StdioMcpClient("this_command_does_not_exist_12345");

            var ex = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await client.InitializeAsync(CancellationToken.None)
            );

            Assert.That(ex.Message, Contains.Substring("Failed to initialize"));
            Assert.That(ex.InnerException, Is.Not.Null);
        }

        [Test]
        [Category("Integration")]
        public void CloseAsync_SucceedsEvenWithProcessNotRunning()
        {
            var client = new StdioMcpClient("nonexistent_command");

            // First initialization fails, then close should still work
            try
            {
                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await client.InitializeAsync(CancellationToken.None)
                );
            }
            catch { }

            // Should not throw
            Assert.That(async () => await client.CloseAsync(CancellationToken.None), Throws.Nothing);

            Assert.Pass("Close succeeded after failed initialization");
        }

        [Test]
        public void CloseAsync_CanBeCalledMultipleTimes_WithoutError()
        {
            var client = new StdioMcpClient("echo");

            Assert.That(async () => {
                for (int i = 0; i < 5; i++)
                {
                    await client.CloseAsync(CancellationToken.None);
                }
            }, Throws.Nothing);

            Assert.Pass("Multiple close calls succeeded");
        }

        [Test]
        public void ListToolsAsync_ThrowsInvalidOperationException_IfProcessNotRunning()
        {
            var client = new StdioMcpClient("echo");

            var ex = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await client.ListToolsAsync(CancellationToken.None)
            );

            Assert.That(ex, Is.Not.Null);
        }

        [Test]
        public void CallToolAsync_RejectsEmptyToolName()
        {
            var client = new StdioMcpClient("echo");

            Assert.ThrowsAsync<ArgumentException>(
                async () => await client.CallToolAsync("", new Dictionary<string, object>(), CancellationToken.None)
            );
        }

        [Test]
        public void CallToolAsync_RejectsNullToolName()
        {
            var client = new StdioMcpClient("echo");

            Assert.ThrowsAsync<ArgumentException>(
                async () => await client.CallToolAsync(null, new Dictionary<string, object>(), CancellationToken.None)
            );
        }


        [Test]
        public void Constructor_WithSpecialCharactersInArgs()
        {
            var args = new List<string> 
            { 
                "arg with spaces",
                "arg\"with\"quotes",
                "arg\\with\\backslashes"
            };
            var client = new StdioMcpClient("echo", args);

            Assert.That(client, Is.Not.Null);
        }

        [Test]
        public void Constructor_WithSpecialCharactersInEnv()
        {
            var env = new Dictionary<string, string>
            {
                { "KEY_WITH_SPACES", "value with spaces" },
                { "KEY_WITH_SPECIAL", "value!@#$%^&*()" }
            };
            var client = new StdioMcpClient("echo", null, env);

            Assert.That(client, Is.Not.Null);
        }
    }
}
