using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.Tooling.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using LMLocal.Infrastructure.Tooling.Mcp.Abstractions;

namespace LMLocal.Infrastructure.Tooling
{
    /// <summary>
    /// Composite tool provider that combines built-in VS tools and MCP (Model Context Protocol) tools.
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

    internal class CompositeToolProvider : ICompositeToolFactory
    {
        private readonly IBuiltInVsToolProvider _builtInToolProvider;
        private readonly IMcpToolManager _mcpToolManager;
        private readonly ISettingsManager _settingsManager;

        public CompositeToolProvider(
            IBuiltInVsToolProvider builtInVsToolProvider,
            IMcpToolManager mcpToolManager,
            ISettingsManager settingsManager)
        {
            _builtInToolProvider = builtInVsToolProvider ?? throw new ArgumentNullException(nameof(builtInVsToolProvider));
            _mcpToolManager = mcpToolManager ?? throw new ArgumentNullException(nameof(mcpToolManager));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        }

        /// <summary>
        /// Checks if built-in tools are enabled (at least read-only).
        /// </summary>
        private bool AreBuiltInToolsEnabled =>
            _settingsManager?.Current?.EnableAiTools ?? true;

        /// <summary>
        /// Checks if write tools are enabled (full access).
        /// </summary>
        private bool AreWriteToolsEnabled =>
            _settingsManager?.Current?.EnableAiWriteTools ?? false;

        private bool IsBuiltInToolAccessAllowed(string toolName)
        {
            var accessLevel = _builtInToolProvider.GetToolAccessLevel(toolName);
            return accessLevel == ToolAccessLevel.ReadOnly || AreWriteToolsEnabled;
        }

        public IReadOnlyList<ToolDefinition> GetAllToolDefinitions()
        {
            var allTools = new List<ToolDefinition>();

            if (AreBuiltInToolsEnabled)
            {
                var builtInTools = _builtInToolProvider.GetAllToolDefinitions();
                if (builtInTools != null)
                {
                    foreach (var toolDef in builtInTools)
                    {
                        if (_builtInToolProvider.ToolExists(toolDef.Name) &&
                            IsBuiltInToolAccessAllowed(toolDef.Name))
                        {
                            allTools.Add(toolDef);
                        }
                    }
                }
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

            if (AreBuiltInToolsEnabled && _builtInToolProvider.ToolExists(toolName))
                return IsBuiltInToolAccessAllowed(toolName);

            return _mcpToolManager.ToolExists(toolName);
        }

        public ITool GetTool(string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
                throw new ArgumentException("Tool name cannot be empty.", nameof(toolName));

            if (AreBuiltInToolsEnabled && _builtInToolProvider.ToolExists(toolName))
            {
                if (!IsBuiltInToolAccessAllowed(toolName))
                    throw new ArgumentException($"Tool '{toolName}' requires write access (EnableAiWriteTools).", nameof(toolName));
                return _builtInToolProvider.GetTool(toolName);
            }

            if (_mcpToolManager.ToolExists(toolName))
            {
                var mcpTool = _mcpToolManager.GetTool(toolName);
                return mcpTool;
            }

            throw new ArgumentException($"Unknown tool: '{toolName}'", nameof(toolName));
        }

        public async Task<object> ExecuteAsync(
            string toolName,
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(toolName))
                throw new ArgumentException("Tool name cannot be empty.", nameof(toolName));
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            if (AreBuiltInToolsEnabled && _builtInToolProvider.ToolExists(toolName))
            {
                if (!IsBuiltInToolAccessAllowed(toolName))
                    throw new ArgumentException($"Tool '{toolName}' requires write access (EnableAiWriteTools).", nameof(toolName));
                return await _builtInToolProvider.ExecuteAsync(toolName, parameters, cancellationToken).ConfigureAwait(false);
            }

            if (_mcpToolManager.ToolExists(toolName))
            {
                var mcpTool = _mcpToolManager.GetTool(toolName);
                return await mcpTool.ExecuteAsync(parameters, cancellationToken).ConfigureAwait(false);
            }

            throw new ArgumentException($"Unknown tool: '{toolName}'.", nameof(toolName));
        }

        public string GetProcessingMessage(string toolName, Dictionary<string, object> parameters)
        {
            if (string.IsNullOrEmpty(toolName))
                return "Processing...";

            if (AreBuiltInToolsEnabled && _builtInToolProvider.ToolExists(toolName))
                return _builtInToolProvider.GetProcessingMessage(toolName, parameters);

            if (_mcpToolManager.ToolExists(toolName))
                return $"Executing tool '{toolName}'...";

            return $"Executing tool '{toolName}'...";
        }

        public string GetCompletionMessage(string toolName, object result)
        {
            if (string.IsNullOrEmpty(toolName))
                return "Execution completed.";

            if (AreBuiltInToolsEnabled && _builtInToolProvider.ToolExists(toolName))
                return _builtInToolProvider.GetCompletionMessage(toolName, result);

            if (_mcpToolManager.ToolExists(toolName))
                return $"Tool '{toolName}' execution completed.";

            return $"Tool '{toolName}' execution completed.";
        }
    }
}