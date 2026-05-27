using System;
using System.Threading;
using System.Threading.Tasks;

namespace LMLocal.Infrastructure.Mcp
{
    /// <summary>
    /// MCP client implementation using stdio transport.
    /// 
    /// CURRENTLY NOT IMPLEMENTED - Placeholder for future development.
    /// This transport type requires solving complex async/await deadlock issues
    /// that are specific to Visual Studio threading model.
    /// 
    /// When implementing, ensure proper use of JoinableTaskFactory.RunAsync()
    /// to avoid VSTHRD003 warnings about deadlocks in UI contexts.
    /// </summary>
    public class StdioMcpClient : McpClientBase
    {
        public StdioMcpClient(string command, string[] args = null, string[] env = null)
        {
            throw new NotImplementedException(
                "Stdio transport type is not currently supported. " +
                "Only HTTP and Streamable-HTTP MCP servers are supported. " +
                "TODO: Implement stdio transport with proper async handling using JoinableTaskFactory.");
        }

        public override Task InitializeAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException("Stdio transport not implemented.");
        }

        public override Task CloseAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException("Stdio transport not implemented.");
        }

        protected override Task<string> SendJsonAndWaitResponseAsync(string json, CancellationToken cancellationToken)
        {
            throw new NotImplementedException("Stdio transport not implemented.");
        }

        protected override Task SendJsonAsync(string json, CancellationToken cancellationToken)
        {
            throw new NotImplementedException("Stdio transport not implemented.");
        }
    }
}
