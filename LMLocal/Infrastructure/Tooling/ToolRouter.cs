using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.SubAgents;
using LMLocal.Infrastructure.SubAgents;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using LMLocal.Infrastructure.Tooling.Mcp.Abstractions;

namespace LMLocal.Infrastructure.Tooling
{
    /// <summary>
    /// Dumb execution router over the tool sources: given a tool name it dispatches to the right source (built-in VS tools, MCP, SubAgents)."/>'s job.
    /// </summary>
    internal class ToolRouter : IToolRouter
    {
        private readonly IBuiltInVsToolProvider _builtInToolProvider;
        private readonly IMcpToolManager _mcpToolManager;
        private readonly ISettingsManager _settingsManager;
        private readonly ISubAgentsToolSource _subAgentsToolSource;

        public ToolRouter(
            IBuiltInVsToolProvider builtInVsToolProvider,
            IMcpToolManager mcpToolManager,
            ISettingsManager settingsManager,
            ISubAgentsToolSource subAgentsToolSource)
        {
            _builtInToolProvider = builtInVsToolProvider ?? throw new ArgumentNullException(nameof(builtInVsToolProvider));
            _mcpToolManager = mcpToolManager ?? throw new ArgumentNullException(nameof(mcpToolManager));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _subAgentsToolSource = subAgentsToolSource ?? throw new ArgumentNullException(nameof(subAgentsToolSource));
        }

        /// <summary>
        /// Checks if built-in tools are enabled (at least read-only).
        /// </summary>
        private bool AreBuiltInToolsEnabled => _settingsManager?.Current?.EnableAiTools ?? true;

        /// <summary>
        /// Checks if write tools are enabled (full access).
        /// </summary>
        private bool AreWriteToolsEnabled => _settingsManager?.Current?.EnableAiWriteTools ?? false;

        private bool IsBuiltInToolAccessAllowed(string toolName)
        {
            var accessLevel = _builtInToolProvider.GetToolAccessLevel(toolName);
            return accessLevel == ToolAccessLevel.ReadOnly || AreWriteToolsEnabled;
        }

        public bool ToolExists(string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
                return false;

            if (AreBuiltInToolsEnabled && _builtInToolProvider.ToolExists(toolName))
                return IsBuiltInToolAccessAllowed(toolName);

            if (_mcpToolManager.ToolExists(toolName))
                return true;

            return _subAgentsToolSource.ToolExists(toolName);
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

            if (_subAgentsToolSource.ToolExists(toolName))
            {
                return await _subAgentsToolSource.ExecuteAsync(toolName, parameters, cancellationToken).ConfigureAwait(false);
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

            if (_subAgentsToolSource.ToolExists(toolName))
                return $"Running {_subAgentsToolSource.GetDisplayName(toolName)} ...";

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

            if (_subAgentsToolSource.ToolExists(toolName))
                return GetSubAgentCompletionMessage(toolName, result);

            return $"Tool '{toolName}' execution completed.";
        }

        public TimeSpan? GetToolTimeout(string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
                return null;

            if (AreBuiltInToolsEnabled && _builtInToolProvider.ToolExists(toolName))
                return null;

            if (_mcpToolManager.ToolExists(toolName))
                return null;

            if (_subAgentsToolSource.ToolExists(toolName))
                return _subAgentsToolSource.GetToolTimeout(toolName);

            return null;
        }

        private string GetSubAgentCompletionMessage(string toolName, object result)
        {
            var displayName = _subAgentsToolSource.GetDisplayName(toolName);

            if (result is SubAgentsRunResponse response)
            {
                if (response.Success)
                {
                    var steps = response.Rounds;
                    var tokens = FormatTokens(response.TotalTokens);
                    var time = FormatDuration(response.DurationMs);
                    return $"Done ({steps} steps, {tokens} tokens, {time})";
                }

                return !string.IsNullOrWhiteSpace(response.Error)
                    ? $"Error: {Truncate(response.Error)}"
                    : $"Failed";
            }

            return $"{displayName} complete";
        }

        private static string FormatTokens(int? tokens)
        {
            if (!tokens.HasValue)
                return "0";

            var value = tokens.Value;
            if (value >= 1000)
                return $"{(value / 1000.0).ToString("0.#", CultureInfo.InvariantCulture)}k";

            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatDuration(long ms)
        {
            if (ms < 0)
                ms = 0;

            var seconds = ms / 1000.0;
            if (seconds < 60)
                return $"{seconds.ToString("0.#", CultureInfo.InvariantCulture)}s";

            var minutes = (int)(seconds / 60);
            var rest = seconds - minutes * 60;
            return $"{minutes}m {rest.ToString("0.#", CultureInfo.InvariantCulture)}s";
        }

        private static string Truncate(string value)
        {
            const int max = 120;
            if (string.IsNullOrEmpty(value) || value.Length <= max)
                return value;
            return value.Substring(0, max) + "...";
        }
    }
}
