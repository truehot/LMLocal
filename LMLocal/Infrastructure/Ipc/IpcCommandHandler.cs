using LMLocal.Core.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.SubAgents;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.DependencyInjection;
using LMLocal.Infrastructure.SubAgents;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class IpcCommandHandler
{
    /// <summary>
    /// Maps legacy PascalCase IPC command names to the canonical lowercase tool names
    /// registered in the built-in tool provider.
    /// </summary>
    private static readonly Dictionary<string, string> LegacyToolAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "GetActiveDocument", "get_active_document" },
        { "SearchInFiles", "search_file_content" },
        { "ReadFileLines", "read_file_lines" },
        { "GetSolutionOverview", "get_solution_overview" },
        { "FindFilesByName", "find_files" },
        { "find_symbol_references", "get_symbol_info" },
        { "ListDirectoryContents", "list_directory" }
    };

    private static string GetLegacyAlias(string cmd)
    {
        return LegacyToolAliases.TryGetValue(cmd, out var alias) ? alias : null;
    }

    public static async Task HandleCommandAsync(AsyncPackage package, string command, StreamWriter writer, CancellationToken token)
    {
        if (string.Equals(command, "Ping", StringComparison.OrdinalIgnoreCase))
        {
            await writer.WriteLineAsync("Pong");
            return;
        }

        else if (command.StartsWith("OpenSolution|", StringComparison.OrdinalIgnoreCase))
        {
            var path = command.Substring("OpenSolution|".Length).Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                await writer.WriteLineAsync("{\"error\":\"OpenSolution requires a non-empty path.\"}");
                return;
            }

            await package.JoinableTaskFactory.SwitchToMainThreadAsync(token);

            // DTE.Solution.Open loads the solution synchronously
            try
            {
                if (await package.GetServiceAsync(typeof(SDTE)) is EnvDTE.DTE dte)
                {
                    dte.Solution.Open(path);
                    await writer.WriteLineAsync("OK");
                    return;
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"IPC: DTE.Solution.Open failed for '{path}': {ex.Message}");
                Debug.WriteLine($"IPC: DTE.Solution.Open failed for '{path}': {ex.Message}");
            }

            // Fallback for environments where the DTE automation object is unavailable.
            if (await package.GetServiceAsync(typeof(SVsSolution)) is IVsSolution shell)
            {
                int openResult = shell.OpenSolutionFile(0, path);
                if (openResult < 0)
                {
                    await writer.WriteLineAsync("{\"error\":\"OpenSolutionFile failed: 0x" + openResult.ToString("X8") + "\"}");
                }
                else
                {
                    await writer.WriteLineAsync("OK");
                }
            }
            else
            {
                await writer.WriteLineAsync("{\"error\":\"SVsSolution service is unavailable.\"}");
            }

            return;
        }

        else if (command.StartsWith("RunTool"))
        {
            var builtInVsToolProvider = ServiceConfiguration.GetService<IBuiltInVsToolProvider>();
            if (builtInVsToolProvider == null)
            {
                await writer.WriteLineAsync("NoFactory");
                return;
            }

            var parts = command.Split('|');
            if (parts.Length < 2)
            {
                await writer.WriteLineAsync("InvalidRunToolCommand");
                return;
            }

            var cmd = parts[1];
            try
            {
                await package.JoinableTaskFactory.SwitchToMainThreadAsync(token);

                for (int i = 2; i < parts.Length; i++)
                {
                    parts[i] = Uri.UnescapeDataString(parts[i]);
                }

                var canonical = cmd;
                string legacyAlias = GetLegacyAlias(canonical);
                if (legacyAlias != null)
                    canonical = legacyAlias;

                // Resolve the tool up-front so unknown names fail fast and reliably.
                if (!builtInVsToolProvider.ToolExists(canonical))
                {
                    await writer.WriteLineAsync("{\"error\":\"Unknown tool: " + canonical + "\"}");
                    return;
                }

                // Write tools (FullAccess) record file snapshots for the Changes panel /
                // rollback, and SnapshotManager requires an active snapshot batch around
                // every execution round — exactly as ChatSessionOrchestrator does.
                // Open the batch here so write tools work through raw IPC (E2E harness)
                // instead of failing with "Cannot perform snapshot while a batch is not active."
                var accessLevel = builtInVsToolProvider.GetToolAccessLevel(canonical);
                bool isFullAccess = accessLevel == ToolAccessLevel.FullAccess;

                ISnapshotManager snapshotManager = null;
                if (isFullAccess)
                {
                    snapshotManager = ServiceConfiguration.GetService<ISnapshotManager>();
                    if (snapshotManager == null)
                    {
                        await writer.WriteLineAsync("{\"error\":\"Snapshot service is not registered.\"}");
                        return;
                    }
                    await snapshotManager.BeginBatchAsync(token).ConfigureAwait(false);
                }

                bool toolSucceeded = false;
                try
                {
                    var res = await ExecuteToolAsync(builtInVsToolProvider, canonical, parts, token);
                    toolSucceeded = true;

                    if (string.Equals(canonical, "get_symbol_info", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync(TransformSymbolInfo(res));
                    }
                    else
                    {
                        await writer.WriteLineAsync(JsonConvert.SerializeObject(res));
                    }
                }
                finally
                {
                    if (isFullAccess && snapshotManager != null)
                    {
                        if (toolSucceeded)
                            await snapshotManager.EndBatchAsync(token).ConfigureAwait(false);
                        else
                            await snapshotManager.CancelBatchAsync(token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"IPC: RunTool error: {ex.Message}", ex);
                Debug.WriteLine($"IPC: RunTool error: {ex.Message}");
                try { await writer.WriteLineAsync($"ERROR {ex.Message}"); } catch { }
            }

            return;
        }

        else if (command.StartsWith("RunSubAgent|", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                // parts[0] = "RunSubAgent"
                // parts[1] = agent id (code_explorer_subagent, code_reader_subagent, ...)
                // parts[2] = Uri.EscapeDataString(prompt)
                // parts[3] = model override (optional)
                // parts[4] = maxRounds override (optional)

                var parts = command.Split('|');
                if (parts.Length < 3)
                {
                    await writer.WriteLineAsync("{\"error\":\"Usage: RunSubAgent|agentId|prompt_escaped|model?|maxRounds?\"}");
                    return;
                }

                var agentId = parts[1].Trim();
                string prompt;
                try
                {
                    prompt = Uri.UnescapeDataString(parts[2]);
                }
                catch (UriFormatException)
                {
                    await writer.WriteLineAsync("{\"error\":\"Invalid prompt encoding. Use Uri.EscapeDataString.\"}");
                    return;
                }

                if (string.IsNullOrWhiteSpace(agentId) || string.IsNullOrWhiteSpace(prompt))
                {
                    await writer.WriteLineAsync("{\"error\":\"RunSubAgent requires a non-empty agentId and prompt.\"}");
                    return;
                }

                var modelOverride = parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]) ? parts[3].Trim() : null;
                var maxRoundsOverride = parts.Length > 4 && int.TryParse(parts[4], out int mr) ? (int?)mr : null;

                var subAgentService = ServiceConfiguration.GetService<ISubAgentsService>();
                if (subAgentService == null)
                {
                    await writer.WriteLineAsync("{\"error\":\"ISubAgentsService is not registered in DI.\"}");
                    return;
                }

                var configManager = ServiceConfiguration.GetService<ISubAgentsConfigManager>();
                if (configManager == null)
                {
                    await writer.WriteLineAsync("{\"error\":\"ISubAgentsConfigManager is not registered in DI.\"}");
                    return;
                }

                var settingsManager = ServiceConfiguration.GetService<ISettingsManager>();
                if (settingsManager == null)
                {
                    await writer.WriteLineAsync("{\"error\":\"ISettingsManager is not registered in DI.\"}");
                    return;
                }

                // The normal startup path loads settings from WebViewInitializer
                await settingsManager.LoadAsync(token);

                var toolsConfigManager = ServiceConfiguration.GetService<IToolsConfigManager>();
                if (toolsConfigManager == null)
                {
                    await writer.WriteLineAsync("{\"error\":\"IToolsConfigManager is not registered in DI.\"}");
                    return;
                }
                await toolsConfigManager.LoadAsync(token);

                var config = await configManager.GetAsync(token);
                var agent = (config == null || config.Agents == null)
                    ? null
                    : config.Agents.FirstOrDefault(a => a != null &&
                        string.Equals(a.Id?.Trim(), agentId, StringComparison.OrdinalIgnoreCase));

                if (agent == null)
                {
                    var notFound = JsonConvert.SerializeObject(new { error = "SubAgent '" + agentId + "' not found in subagents.json." });
                    await writer.WriteLineAsync(notFound);
                    return;
                }

                var request = new SubAgentRunRequest
                {
                    AgentName = agent.Id.Trim(),
                    Prompt = prompt,
                    System = agent.System,
                    AllowedTools = agent.AllowedTools,
                    Model = modelOverride ?? agent.Model ?? config.Model,
                    Temperature = agent.Temperature ?? config.Temperature,
                    ReasoningEffort = !string.IsNullOrWhiteSpace(agent.ReasoningEffort) ? agent.ReasoningEffort : config.ReasoningEffort,
                    MaxTokens = agent.MaxTokens ?? config.MaxTokens,
                    MaxRounds = maxRoundsOverride ?? agent.MaxRounds ?? config.MaxRounds,
                    TimeoutSeconds = agent.TimeoutSeconds ?? config.TimeoutSeconds,
                    ProviderType = !string.IsNullOrWhiteSpace(agent.ProviderType) ? agent.ProviderType : config.ProviderType,
                    BaseUrl = !string.IsNullOrWhiteSpace(agent.CustomBaseUrl) ? agent.CustomBaseUrl : config.CustomBaseUrl,
                    ApiKey = !string.IsNullOrWhiteSpace(agent.CustomApiKey) ? agent.CustomApiKey : config.CustomApiKey
                };

                // A subagent that owns write tools records file snapshots too, and SnapshotManager
                // requires an active batch around every execution round. Nothing else opens one on
                // this path: ChatSessionOrchestrator opens the batch for chat rounds, and the RunTool
                // branch above opens it for raw IPC tool calls. Open it here as well, and never nest
                // it - BeginBatchAsync clears the paths recorded by an already open batch.
                ISnapshotManager snapshotManager = null;
                bool snapshotBatchBegan = false;
                if (NeedsSnapshotBatch(agent.AllowedTools))
                {
                    snapshotManager = ServiceConfiguration.GetService<ISnapshotManager>();
                    if (snapshotManager != null && !snapshotManager.IsBatchActive)
                    {
                        await package.JoinableTaskFactory.SwitchToMainThreadAsync(token);
                        await snapshotManager.BeginBatchAsync(token).ConfigureAwait(false);
                        snapshotBatchBegan = true;
                    }
                }

                var runSucceeded = false;
                try
                {
                    var response = await subAgentService.ExecutePromptAsync(request, token);
                    runSucceeded = response != null && response.Success;

                    var json = JsonConvert.SerializeObject(response, new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore,
                        Formatting = Formatting.None,
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                    });

                    await writer.WriteLineAsync(json);
                }
                finally
                {
                    if (snapshotBatchBegan && snapshotManager != null)
                    {
                        // Cleanup must not be skipped because of a cancelled token and must not mask
                        // the original exception. End keeps the recorded snapshots for the Changes
                        // panel and rollback; Cancel drops only the batch bookkeeping.
                        try
                        {
                            await package.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken.None);

                            if (runSucceeded)
                                await snapshotManager.EndBatchAsync(CancellationToken.None).ConfigureAwait(false);
                            else
                                await snapshotManager.CancelBatchAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception batchEx)
                        {
                            InternalLogger.Error($"IPC: RunSubAgent snapshot batch cleanup failed: {batchEx.Message}", batchEx);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"IPC: RunSubAgent error: {ex.Message}", ex);
                Debug.WriteLine($"IPC: RunSubAgent error: {ex.Message}");
                try
                {
                    await writer.WriteLineAsync(JsonConvert.SerializeObject(new { error = ex.Message }));
                }
                catch
                {
                }
            }

            return;
        }

        await writer.WriteLineAsync("UnknownCommand");
    }

    /// <summary>
    /// True when at least one allowed tool of the agent records file snapshots (FullAccess), so the
    /// subagent run needs an open snapshot batch around its tool calls.
    /// </summary>
    private static bool NeedsSnapshotBatch(IEnumerable<string> allowedTools)
    {
        if (allowedTools == null)
            return false;

        var provider = ServiceConfiguration.GetService<IBuiltInVsToolProvider>();
        if (provider == null)
            return false;

        foreach (var name in allowedTools)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var trimmed = name.Trim();
            if (!provider.ToolExists(trimmed))
                continue;

            if (provider.GetToolAccessLevel(trimmed) == ToolAccessLevel.FullAccess)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Executes any registered tool using positional string arguments (index 2.. of the
    /// pipe-delimited command).
    /// </summary>
    private static async Task<object> ExecuteToolAsync(
        IBuiltInVsToolProvider provider,
        string toolName,
        string[] parts,
        CancellationToken token)
    {
        var tool = provider.GetTool(toolName);
        var definition = tool.GetToolInfo();
        var props = definition.Parameters?.Properties;
        if (props == null || props.Count == 0 || parts.Length <= 2)
        {
            return await provider.ExecuteAsync(toolName, new Dictionary<string, object>(), token);
        }

        var required = definition.Parameters?.Required ?? new List<string>();

        // Order parameters as: required first (in declared Required order), then the rest
        // in dictionary order. Positional values map to this list one-to-one.
        var orderedNames = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in required)
        {
            if (!seen.Contains(r) && props.ContainsKey(r))
            {
                orderedNames.Add(r);
                seen.Add(r);
            }
        }
        foreach (var kv in props)
        {
            if (!seen.Contains(kv.Key))
            {
                orderedNames.Add(kv.Key);
                seen.Add(kv.Key);
            }
        }

        var parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        int idx = 0;
        for (int i = 2; i < parts.Length && idx < orderedNames.Count; i++, idx++)
        {
            parameters[orderedNames[idx]] = parts[i];
        }

        return await provider.ExecuteAsync(toolName, parameters, token);
    }

    /// <summary>
    /// Transforms get_symbol_info output to the legacy shape expected by existing tests:
    /// 'references' → 'results', per-reference 'text' → 'matches'.
    /// </summary>
    private static string TransformSymbolInfo(object result)
    {
        var json = JsonConvert.SerializeObject(result);
        var obj = JObject.Parse(json);

        var transformed = new JObject
        {
            ["symbol_name"] = obj["symbol_name"],
            ["total_references"] = obj["total_references"],
            ["success"] = obj["success"],
            ["error_message"] = obj["error_message"]
        };

        var results = new JArray();
        if (obj["references"] is JArray references)
        {
            foreach (var r in references)
            {
                var match = new JObject
                {
                    ["line"] = r["line"],
                    ["text"] = r["text"]
                };
                var resultItem = new JObject
                {
                    ["file_path"] = r["file_path"],
                    ["matches"] = new JArray(match)
                };
                results.Add(resultItem);
            }
        }
        transformed["results"] = results;
        return transformed.ToString(Formatting.None);
    }
}
