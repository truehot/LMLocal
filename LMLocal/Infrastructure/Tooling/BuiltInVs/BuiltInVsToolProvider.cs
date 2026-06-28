using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Tooling.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs
{

    /// <summary>
    /// Interface for built-in VS tool provider.
    /// </summary>
    internal interface IBuiltInVsToolProvider
    {
        /// <summary>
        /// Returns all registered tool definitions for the LLM (filtered by enabled status).
        /// </summary>
        IReadOnlyList<ToolDefinition> GetAllToolDefinitions();

        /// <summary>
        /// Returns all registered tool definitions including disabled ones (for UI configuration).
        /// </summary>
        IReadOnlyList<ToolDefinition> GetAllToolDefinitionsUnfiltered();

        /// <summary>
        /// Returns the access level of a tool by its name. Throws if the tool is not found.
        /// </summary>
        ToolAccessLevel GetToolAccessLevel(string toolName);

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
        Task<object> ExecuteAsync(string toolName, Dictionary<string, object> parameters, CancellationToken cancellationToken);

        /// <summary>
        /// Gets processing message for a tool based on its parameters.
        /// </summary>
        string GetProcessingMessage(string toolName, Dictionary<string, object> parameters);

        /// <summary>
        /// Gets completion message for a tool based on its execution result.
        /// </summary>
        string GetCompletionMessage(string toolName, object result);
    }

    /// <summary>
    /// Provider for Visual Studio built-in tools.
    /// Handles filtering of tools based on configuration.
    /// </summary>
    internal class BuiltInVsToolProvider : IBuiltInVsToolProvider
    {
        private readonly IReadOnlyList<ToolDefinition> _allToolDefinitions;
        private readonly Dictionary<string, IBuiltInTool> _toolsByName;
        private readonly IToolsConfigManager _toolsConfigManager;
        private readonly ISearchResultCache _searchCache;

        public BuiltInVsToolProvider(
            IEnumerable<IBuiltInTool> tools,
            IToolsConfigManager toolsConfigManager,
            ISearchResultCache searchCache)
        {
            var toolsList = tools?.ToList() ?? throw new ArgumentNullException(nameof(tools));
            if (toolsList.Count == 0)
                throw new ArgumentException("At least one tool must be provided.", nameof(tools));

            _toolsConfigManager = toolsConfigManager ?? throw new ArgumentNullException(nameof(toolsConfigManager));
            _searchCache = searchCache ?? throw new ArgumentNullException(nameof(searchCache));
            _toolsByName = new Dictionary<string, IBuiltInTool>(StringComparer.OrdinalIgnoreCase);
            var definitions = new List<ToolDefinition>();

            foreach (var tool in toolsList)
            {
                _toolsByName[tool.ToolName] = tool;
                definitions.Add(tool.GetToolInfo());
            }

            _allToolDefinitions = definitions.AsReadOnly();
        }

        /// <summary>
        /// Checks if a tool is enabled based on cached configuration.
        /// </summary>
        private bool IsToolEnabled(string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
                return false;

            try
            {
                var config = _toolsConfigManager.Current;
                if (config?.Tools == null || config.Tools.Count == 0)
                    return true;

                var toolConfig = config.Tools.FirstOrDefault(t => t.Id == toolName);
                if (toolConfig == null)
                    return true;

                return toolConfig.Enabled;
            }
            catch (InvalidOperationException ex)
            {
                InternalLogger.Warn($"Failed to get tool configuration for '{toolName}'. Defaulting to disabled. Exception: {ex}");
                return false;
            }
        }

        public IReadOnlyList<ToolDefinition> GetAllToolDefinitions()
        {
            return _allToolDefinitions
                .Where(t => IsToolEnabled(t.Name))
                .ToList()
                .AsReadOnly();
        }

        public IReadOnlyList<ToolDefinition> GetAllToolDefinitionsUnfiltered()
        {
            return _allToolDefinitions;
        }

        public bool ToolExists(string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
                return false;

            return _toolsByName.ContainsKey(toolName);
        }

        public ITool GetTool(string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
                throw new ArgumentException("Tool name cannot be empty.", nameof(toolName));

            if (_toolsByName.TryGetValue(toolName, out var tool))
                return tool;

            throw new ArgumentException($"Unknown tool: '{toolName}'", nameof(toolName));
        }

        public ToolAccessLevel GetToolAccessLevel(string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
                throw new ArgumentException("Tool name cannot be empty.", nameof(toolName));

            if (_toolsByName.TryGetValue(toolName, out var tool))
                return tool.AccessLevel;

            throw new ArgumentException($"Unknown tool: '{toolName}'", nameof(toolName));
        }

        public async Task<object> ExecuteAsync(string toolName, Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(toolName))
                throw new ArgumentException("Tool name cannot be empty.", nameof(toolName));

            if (!_toolsByName.TryGetValue(toolName, out var tool))
                throw new ArgumentException($"Unknown tool: '{toolName}'", nameof(toolName));

            var result = await tool.ExecuteAsync(parameters ?? new Dictionary<string, object>(), cancellationToken).ConfigureAwait(false);

            if (tool.AccessLevel == ToolAccessLevel.FullAccess)
                _searchCache.Clear();

            return result;
        }

        public string GetProcessingMessage(string toolName, Dictionary<string, object> parameters)
        {
            if (!string.IsNullOrEmpty(toolName) && _toolsByName.TryGetValue(toolName, out var tool))
            {
                return tool.GetProcessingMessage(parameters ?? new Dictionary<string, object>());
            }

            return "Processing...";
        }

        public string GetCompletionMessage(string toolName, object result)
        {
            if (!string.IsNullOrEmpty(toolName) && _toolsByName.TryGetValue(toolName, out var tool))
            {
                return tool.GetCompletionMessage(result);
            }

            return "Completed";
        }
    }
}