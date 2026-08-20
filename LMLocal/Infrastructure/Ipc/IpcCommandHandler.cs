using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.DependencyInjection;
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

        else if (command.StartsWith("OpenSolution|"))
        {
            var path = command.Substring("OpenSolution|".Length);
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(token);
            if (await package.GetServiceAsync(typeof(SVsSolution)) is IVsSolution shell)
            {
                shell.OpenSolutionFile(0, path);
                await writer.WriteLineAsync("OK");
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
                        parameters["file_extension"] = extension;
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

        await writer.WriteLineAsync("UnknownCommand");
    }
}
