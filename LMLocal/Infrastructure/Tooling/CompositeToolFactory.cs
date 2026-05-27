using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Mcp;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.Tooling.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs;

namespace LMLocal.Infrastructure.Tooling
{
    /// <summary>
    /// Composite tool factory that combines built-in VS tools and MCP (Model Context Protocol) tools.
    /// Serves as the primary IVsToolFactory entry point, delegating to BuiltInVsToolFactory for VS tools
    /// and IMcpToolManager for external MCP tools.
    /// </summary>
    public interface ICompositeToolFactory
    {
        /// <summary>
        /// Returns all registered tool definitions for the LLM.
        /// </summary>
        IReadOnlyList<ToolDefinition> GetAllToolDefinitions();

        /// <summary>
        /// Checks whether a tool with the specified name is registered.
        /// </summary>
        bool ToolExists(string toolName);

        /// <summary>
        /// Resolves a tool by its name.
        /// </summary>
        ITool GetTool(string toolName);

        /// <summary>
        /// Executes a tool with the given parameters from LLM response.
        /// </summary>
        Task<object> ExecuteAsync(
            string toolName,
            IServiceProvider sp,
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken);

        /// <summary>
        /// Gets processing message for a tool based on its parameters.
        /// </summary>
        string GetProcessingMessage(string toolName, Dictionary<string, object> parameters);

        /// <summary>
        /// Gets completion message for a tool based on its execution result.
        /// </summary>
        string GetCompletionMessage(string toolName, object result);
    }

    internal class CompositeToolFactory : ICompositeToolFactory
    {
        private readonly IBuiltInVsToolProvider _builtInFactory;
        private readonly IMcpToolManager _mcpToolManager;
        private readonly ISettingsManager _settingsManager;

        public CompositeToolFactory(
            IBuiltInVsToolProvider builtInFactory,
            IMcpToolManager mcpToolManager,
            ISettingsManager settingsManager)
        {
            _builtInFactory = builtInFactory ?? throw new ArgumentNullException(nameof(builtInFactory));
            _mcpToolManager = mcpToolManager ?? throw new ArgumentNullException(nameof(mcpToolManager));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        }

        /// <summary>
        /// Checks if built-in tools are enabled.
        /// </summary>
        private bool AreBuiltInToolsEnabled =>
            _settingsManager?.Current?.EnableAiTools ?? true;

        public IReadOnlyList<ToolDefinition> GetAllToolDefinitions()
        {
            var allTools = new List<ToolDefinition>();

            if (AreBuiltInToolsEnabled)
            {
                var builtInTools = _builtInFactory.GetAllToolDefinitions();
                if (builtInTools != null)
                    allTools.AddRange(builtInTools);
            }

            var mcpTools = _mcpToolManager.GetMcpToolDefinitions();
            if (mcpTools != null)
                allTools.AddRange(mcpTools);

            return allTools.AsReadOnly();
        }

        public bool ToolExists(string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
                return false;

            if (AreBuiltInToolsEnabled && _builtInFactory.ToolExists(toolName))
                return true;

            return _mcpToolManager.ToolExists(toolName);
        }

        public ITool GetTool(string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
                throw new ArgumentException("Tool name cannot be empty.", nameof(toolName));

            if (AreBuiltInToolsEnabled && _builtInFactory.ToolExists(toolName))
                return _builtInFactory.GetTool(toolName);

            if (_mcpToolManager.ToolExists(toolName))
            {
                var mcpTool = _mcpToolManager.GetTool(toolName);
                return mcpTool;
            }

            throw new ArgumentException($"Unknown tool: '{toolName}'", nameof(toolName));
        }

        public async Task<object> ExecuteAsync(
            string toolName,
            IServiceProvider sp,
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(toolName))
                throw new ArgumentException("Tool name cannot be empty.", nameof(toolName));
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            if (AreBuiltInToolsEnabled && _builtInFactory.ToolExists(toolName))
                return await _builtInFactory.ExecuteAsync(toolName, sp, parameters, cancellationToken);

            if (_mcpToolManager.ToolExists(toolName))
            {
                var mcpTool = _mcpToolManager.GetTool(toolName);
                return await mcpTool.ExecuteAsync(parameters, cancellationToken);
            }

            throw new ArgumentException($"Unknown tool: '{toolName}'", nameof(toolName));
        }

        public string GetProcessingMessage(string toolName, Dictionary<string, object> parameters)
        {
            if (string.IsNullOrEmpty(toolName))
                return "Processing...";

            if (AreBuiltInToolsEnabled && _builtInFactory.ToolExists(toolName))
                return _builtInFactory.GetProcessingMessage(toolName, parameters);

            if (_mcpToolManager.ToolExists(toolName))
                return $"Executing tool '{toolName}'...";

            return $"Executing tool '{toolName}'...";
        }

        public string GetCompletionMessage(string toolName, object result)
        {
            if (string.IsNullOrEmpty(toolName))
                return "Execution completed.";

            if (AreBuiltInToolsEnabled && _builtInFactory.ToolExists(toolName))
                return _builtInFactory.GetCompletionMessage(toolName, result);

            if (_mcpToolManager.ToolExists(toolName))
                return $"Tool '{toolName}' execution completed.";

            return $"Tool '{toolName}' execution completed.";
        }
    }
}
