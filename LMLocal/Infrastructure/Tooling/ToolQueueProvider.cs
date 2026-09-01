using System;
using System.Collections.Generic;
using System.Linq;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.SubAgents;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.SubAgents;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using LMLocal.Infrastructure.Tooling.Mcp.Abstractions;

namespace LMLocal.Infrastructure.Tooling
{
    /// <summary>
    /// Single source of truth for tool queue policy.
    /// </summary>
    internal class ToolQueueProvider : IToolQueueProvider
    {
        private readonly ISettingsManager _settingsManager;
        private readonly IBuiltInVsToolProvider _builtInTools;
        private readonly IMcpToolManager _mcpToolManager;
        private readonly ISubAgentsCatalog _subAgentsCatalog;

        private readonly object _lock = new object();
        private SubAgentsConfig _cachedConfig;
        private string _cachedModeKey;
        private ToolQueue _cachedMainQueue;

        private const string TaskParam = "task";

        public ToolQueueProvider(
            ISettingsManager settingsManager,
            IBuiltInVsToolProvider builtInTools,
            IMcpToolManager mcpToolManager,
            ISubAgentsCatalog subAgentsCatalog)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _builtInTools = builtInTools ?? throw new ArgumentNullException(nameof(builtInTools));
            _mcpToolManager = mcpToolManager ?? throw new ArgumentNullException(nameof(mcpToolManager));
            _subAgentsCatalog = subAgentsCatalog ?? throw new ArgumentNullException(nameof(subAgentsCatalog));
        }

        public ToolQueue GetMainQueue()
        {
            var settings = _settingsManager.Current;
            if (settings == null)
                return ToolQueue.Main(new List<ToolDefinition>());

            var enableAiTools = settings.EnableAiTools;
            var enableSubAgents = settings.EnableSubAgents;
            var allowWrite = settings.EnableAiWriteTools;

            var builtIn = enableAiTools ? GetAvailableBuiltInTools(settings) : Array.Empty<ToolDefinition>();
            var mcp = _mcpToolManager.GetMcpToolDefinitions() ?? Array.Empty<ToolDefinition>();

            SubAgentsConfig config = null;
            if (enableSubAgents)
                config = _subAgentsCatalog.TryGetSnapshot();

            var modeKey = string.Join("|",
                enableAiTools ? "T" : "F",
                enableSubAgents ? "T" : "F",
                allowWrite ? "T" : "F",
                BuildNamesKey(builtIn),
                BuildNamesKey(mcp));

            lock (_lock)
            {
                if (_cachedMainQueue != null &&
                    ReferenceEquals(_cachedConfig, config) &&
                    string.Equals(_cachedModeKey, modeKey, StringComparison.Ordinal))
                {
                    return _cachedMainQueue;
                }
            }

            var definitions = new List<ToolDefinition>();
            if (enableSubAgents && config != null && config.Agents != null)
            {
                definitions.AddRange(FilterOwnedBuiltIn(config, builtIn));
                definitions.AddRange(GetAgentTools(config, builtIn));
            }
            else
            {
                definitions.AddRange(builtIn);
            }
            definitions.AddRange(mcp);

            var queue = ToolQueue.Main(definitions);

            lock (_lock)
            {
                _cachedConfig = config;
                _cachedModeKey = modeKey;
                _cachedMainQueue = queue;
            }

            return queue;
        }

        public ToolQueue GetSubAgentQueue(SubAgentRunRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.AgentName))
                throw new ArgumentException("AgentName is required.", nameof(request));

            var settings = _settingsManager.Current;
            var allowWrite = settings?.EnableAiWriteTools ?? false;
            var allowed = request.AllowedTools;
            var definitions = new List<ToolDefinition>();

            if (allowed != null && allowed.Count > 0)
            {
                var allowedNames = new HashSet<string>(
                    allowed.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()),
                    StringComparer.OrdinalIgnoreCase);

                var all = _builtInTools.GetAllToolDefinitions() ?? Array.Empty<ToolDefinition>();
                foreach (var def in all)
                {
                    if (def == null || string.IsNullOrWhiteSpace(def.Name))
                        continue;

                    if (!allowWrite && _builtInTools.GetToolAccessLevel(def.Name) != ToolAccessLevel.ReadOnly)
                        continue;

                    if (allowedNames.Contains(def.Name))
                        definitions.Add(def);
                }
            }

            return ToolQueue.ForSubAgent(request.AgentName, definitions);
        }

        /// <summary>
        /// Enabled built-in tools available in the current mode (read-only vs write).
        /// </summary>
        private IReadOnlyList<ToolDefinition> GetAvailableBuiltInTools(AppSettings settings)
        {
            var all = _builtInTools.GetAllToolDefinitions();
            if (all == null)
                return Array.Empty<ToolDefinition>();

            var allowWrite = settings?.EnableAiWriteTools ?? false;
            var result = new List<ToolDefinition>();
            foreach (var def in all)
            {
                if (def == null || string.IsNullOrWhiteSpace(def.Name))
                    continue;

                if (!allowWrite && _builtInTools.GetToolAccessLevel(def.Name) != ToolAccessLevel.ReadOnly)
                    continue;

                result.Add(def);
            }

            return result;
        }

        /// <summary>
        /// Built-in tools minus the ones owned by visible agents.
        /// </summary>
        private IReadOnlyList<ToolDefinition> FilterOwnedBuiltIn(
            SubAgentsConfig config,
            IReadOnlyList<ToolDefinition> builtIn)
        {
            var owned = GetOwnedToolNames(config, builtIn);
            return builtIn
                .Where(t => t != null && !owned.Contains(t.Name))
                .ToList();
        }

        /// <summary>
        /// Tool definitions of visible agents (reasoning-only or with at least one available allowed tool).
        /// </summary>
        private IReadOnlyList<ToolDefinition> GetAgentTools(
            SubAgentsConfig config,
            IReadOnlyList<ToolDefinition> builtIn)
        {
            var available = new HashSet<string>(
                builtIn.Where(t => t != null && !string.IsNullOrWhiteSpace(t.Name)).Select(t => t.Name),
                StringComparer.OrdinalIgnoreCase);

            var agents = new List<ToolDefinition>();
            foreach (var agent in config.Agents)
            {
                if (agent == null || !agent.Enabled || string.IsNullOrWhiteSpace(agent.Id))
                    continue;

                var allowed = agent.AllowedTools;
                if (allowed == null || allowed.Count == 0)
                {
                    // Reasoning-only agent: always visible.
                    agents.Add(GetToolDefinition(agent.Id, agent.Description));
                    continue;
                }

                var anyAvailable = allowed.Any(n => !string.IsNullOrWhiteSpace(n) && available.Contains(n.Trim()));
                if (anyAvailable)
                    agents.Add(GetToolDefinition(agent.Id, agent.Description));
            }

            return agents;
        }

        /// <summary>
        /// Union of allowed tool names of all visible agents.
        /// </summary>
        private IReadOnlyCollection<string> GetOwnedToolNames(
            SubAgentsConfig config,
            IReadOnlyList<ToolDefinition> builtIn)
        {
            var available = new HashSet<string>(
                builtIn.Where(t => t != null && !string.IsNullOrWhiteSpace(t.Name)).Select(t => t.Name),
                StringComparer.OrdinalIgnoreCase);

            var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var agent in config.Agents)
            {
                if (agent == null || !agent.Enabled || string.IsNullOrWhiteSpace(agent.Id))
                    continue;

                var allowed = agent.AllowedTools;
                if (allowed == null || allowed.Count == 0)
                    continue;

                var anyAvailable = allowed.Any(n => !string.IsNullOrWhiteSpace(n) && available.Contains(n.Trim()));
                if (!anyAvailable)
                    continue;

                foreach (var name in allowed)
                {
                    if (!string.IsNullOrWhiteSpace(name))
                        owned.Add(name.Trim());
                }
            }

            return owned;
        }

        private static ToolDefinition GetToolDefinition(string name, string description)
        {
            return new ToolDefinition
            {
                Name = name,
                Description = description,
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { TaskParam, new ToolDetails { Type = "string", Description = description } }
                    },
                    Required = new List<string> { TaskParam }
                }
            };
        }

        private static string BuildNamesKey(IReadOnlyList<ToolDefinition> tools)
        {
            if (tools == null || tools.Count == 0)
                return string.Empty;

            return string.Join("|", tools.Select(t => t?.Name ?? string.Empty));
        }
    }
}
