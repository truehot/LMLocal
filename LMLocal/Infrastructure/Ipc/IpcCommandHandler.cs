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
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class IpcCommandHandler
{
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

                if (string.Equals(cmd, "GetActiveDocument", StringComparison.OrdinalIgnoreCase))
                {
                    var parameters = new Dictionary<string, object>();
                    var res = await builtInVsToolProvider.ExecuteAsync("get_active_document", parameters, token);
                    await writer.WriteLineAsync(JsonConvert.SerializeObject(res));
                }
                else if (string.Equals(cmd, "SearchInFiles", StringComparison.OrdinalIgnoreCase))
                {
                    if (parts.Length < 3)
                    {
                        await writer.WriteLineAsync("MissingQuery");
                        return;
                    }

                    var text = parts[2];
                    var extension = parts.Length >= 4 ? parts[3] : ".cs";
                    var parameters = new Dictionary<string, object>
                    {
                        { "text", text },
                        { "extension_filter", extension }
                    };

                    var res = await builtInVsToolProvider.ExecuteAsync("search_file_content", parameters, token);
                    await writer.WriteLineAsync(JsonConvert.SerializeObject(res));
                }
                else if (string.Equals(cmd, "ReadFileLines", StringComparison.OrdinalIgnoreCase))
                {
                    if (parts.Length < 5)
                    {
                        await writer.WriteLineAsync("MissingParameters");
                        return;
                    }

                    var filePath = parts[2];
                    if (!int.TryParse(parts[3], out int startLine) || !int.TryParse(parts[4], out int endLine))
                    {
                        await writer.WriteLineAsync("InvalidLineNumbers");
                        return;
                    }

                    var parameters = new Dictionary<string, object>
                    {
                        { "file_path", filePath },
                        { "start_line", startLine },
                        { "end_line", endLine }
                    };

                    var res = await builtInVsToolProvider.ExecuteAsync("read_file_lines", parameters, token);
                    await writer.WriteLineAsync(JsonConvert.SerializeObject(res));
                }
                else if (string.Equals(cmd, "GetSolutionOverview", StringComparison.OrdinalIgnoreCase))
                {
                    var parameters = new Dictionary<string, object>();
                    var res = await builtInVsToolProvider.ExecuteAsync("get_solution_overview", parameters, token);
                    await writer.WriteLineAsync(JsonConvert.SerializeObject(res));
                }
                else if (string.Equals(cmd, "FindFilesByName", StringComparison.OrdinalIgnoreCase))
                {
                    if (parts.Length < 3)
                    {
                        await writer.WriteLineAsync("MissingFileName");
                        return;
                    }

                    var fileName = parts[2];
                    var extension = parts.Length >= 4 ? parts[3] : null;
                    var parameters = new Dictionary<string, object>
                    {
                        { "file_name", fileName }
                    };

                    if (!string.IsNullOrEmpty(extension))
                    {
                        parameters["extension_filter"] = extension;
                    }

                    var res = await builtInVsToolProvider.ExecuteAsync("find_files", parameters, token);
                    await writer.WriteLineAsync(JsonConvert.SerializeObject(res));
                }
                else if (string.Equals(cmd, "find_symbol_references", StringComparison.OrdinalIgnoreCase))
                {
                    if (parts.Length < 3)
                    {
                        await writer.WriteLineAsync("MissingSymbolName");
                        return;
                    }

                    var symbolName = parts[2];
                    var parameters = new Dictionary<string, object>
                    {
                        { "symbol_name", symbolName }
                    };

                    var res = await builtInVsToolProvider.ExecuteAsync("get_symbol_info", parameters, token);
                    var json = JsonConvert.SerializeObject(res);
                    var obj = JObject.Parse(json);

                    // Transform to match test expectations: 'references' → 'results', 'text' → 'matches'
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
                    await writer.WriteLineAsync(transformed.ToString(Formatting.None));
                }
                else if (string.Equals(cmd, "ListDirectoryContents", StringComparison.OrdinalIgnoreCase))
                {
                    var directoryPath = parts.Length >= 3 ? parts[2] : ".";
                    var parameters = new Dictionary<string, object>
                    {
                        { "directory_path", directoryPath }
                    };

                    var res = await builtInVsToolProvider.ExecuteAsync("list_directory", parameters, token);
                    await writer.WriteLineAsync(JsonConvert.SerializeObject(res));
                }
                else
                {
                    await writer.WriteLineAsync("UnknownToolCommand");
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
                        string.Equals(a.Id != null ? a.Id.Trim() : null, agentId, StringComparison.OrdinalIgnoreCase));

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

                var response = await subAgentService.ExecutePromptAsync(request, token);

                var json = JsonConvert.SerializeObject(response, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    Formatting = Formatting.None,
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                });

                await writer.WriteLineAsync(json);
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
}
