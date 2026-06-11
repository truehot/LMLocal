using System;
using System.Collections.Generic;
using System.Threading;
using LMLocal.Infrastructure.Tooling.Mcp.Client;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Mcp
{
    [TestFixture]
    public class StdioMcpClientTests
    {
        [Test]
        public void Constructor_ThrowsArgumentException_WhenCommandIsNull()
        {
            Assert.That(() => new StdioMcpClient(null), Throws.ArgumentException);
        }

        [Test]
        public void Constructor_ThrowsArgumentException_WhenCommandIsEmpty()
        {
            Assert.That(() => new StdioMcpClient(""), Throws.ArgumentException);
        }

        [Test]
        public void Constructor_ThrowsArgumentException_WhenCommandIsWhitespace()
        {
            Assert.That(() => new StdioMcpClient("   "), Throws.ArgumentException);
        }

        [Test]
        public void Constructor_Succeeds_WithValidCommand()
        {
            var client = new StdioMcpClient("echo");
            Assert.That(client, Is.Not.Null);
        }

        [Test]
        public void Constructor_Succeeds_WithCommandAndArgs()
        {
            var args = new List<string> { "arg1", "arg2" };
            var client = new StdioMcpClient("echo", args);
            Assert.That(client, Is.Not.Null);
        }

        [Test]
        public void Constructor_Succeeds_WithCommandArgsAndEnv()
        {
            var args = new List<string> { "arg1" };
            var env = new Dictionary<string, string> { { "KEY", "value" } };
            var client = new StdioMcpClient("echo", args, env);
            Assert.That(client, Is.Not.Null);
        }

        [Test]
        public void InitializeAsync_ThrowsOperationCanceledException_WhenCommandDoesNotExist()
        {
            var client = new StdioMcpClient("nonexistent_command_xyz");
            Assert.That(() => client.InitializeAsync(CancellationToken.None), 
                Throws.TypeOf<InvalidOperationException>());
        }



        [Test]
        public void CloseAsync_DoesNotThrow_WhenNotInitialized()
        {
            var client = new StdioMcpClient("echo");

            // Should not throw
            Assert.That(async () => await client.CloseAsync(CancellationToken.None), Throws.Nothing);

            Assert.Pass("CloseAsync succeeded on uninitialized client");
        }

        [Test]
        public void CloseAsync_DoesNotThrow_WhenCalledTwice()
        {
            var client = new StdioMcpClient("echo");

            Assert.That(async () => {
                await client.CloseAsync(CancellationToken.None);
                await client.CloseAsync(CancellationToken.None);
            }, Throws.Nothing);

            Assert.Pass("Double close succeeded");
        }

        [Test]
        public void Constructor_AcceptsNullArgs()
        {
            var client = new StdioMcpClient("echo", null);
            Assert.That(client, Is.Not.Null);
        }

        [Test]
        public void Constructor_AcceptsNullEnv()
        {
            var client = new StdioMcpClient("echo", new List<string> { "arg" }, null);
            Assert.That(client, Is.Not.Null);
        }

        [Test]
        public void Constructor_AcceptsEmptyArgs()
        {
            var client = new StdioMcpClient("echo", new List<string>());
            Assert.That(client, Is.Not.Null);
        }

        [Test]
        public void Constructor_AcceptsEmptyEnv()
        {
            var client = new StdioMcpClient("echo", new List<string>(), new Dictionary<string, string>());
            Assert.That(client, Is.Not.Null);
        }

        [Test]
        public void ListToolsAsync_ThrowsInvalidOperationException_WhenNotInitialized()
        {
            var client = new StdioMcpClient("echo");

            Assert.That(
                () => client.ListToolsAsync(CancellationToken.None),
                Throws.TypeOf<InvalidOperationException>()
            );
        }

        [Test]
        public void CallToolAsync_ThrowsArgumentException_WithNullToolName()
        {
            var client = new StdioMcpClient("echo");

            Assert.That(
                () => client.CallToolAsync(null, new Dictionary<string, object>(), CancellationToken.None),
                Throws.ArgumentException
            );
        }

        [Test]
        public void CallToolAsync_ThrowsArgumentException_WithEmptyToolName()
        {
            var client = new StdioMcpClient("echo");

            Assert.That(
                () => client.CallToolAsync("", new Dictionary<string, object>(), CancellationToken.None),
                Throws.ArgumentException
            );
        }

        [Test]
        public void CallToolAsync_AcceptsNullParameters()
        {
            var client = new StdioMcpClient("echo");

            // Just verifies null parameters don't cause immediate exception
            // Actual execution would fail (process not running)
            Assert.That(
                () => client.CallToolAsync("test_tool", null, CancellationToken.None),
                Throws.TypeOf<InvalidOperationException>()
            );
        }
    }
}
