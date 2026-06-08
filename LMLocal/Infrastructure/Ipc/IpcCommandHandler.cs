using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Common;
using LMLocal.Infrastructure.DependencyInjection;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Newtonsoft.Json;

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
                    var res = await builtInVsToolProvider.ExecuteAsync("Get_Active_Document_Content", parameters, token);
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

                    var res = await builtInVsToolProvider.ExecuteAsync("Search_Local_Solution_Files", parameters, token);
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

                    var res = await builtInVsToolProvider.ExecuteAsync("Read_Solution_File_Lines", parameters, token);
                    await writer.WriteLineAsync(JsonConvert.SerializeObject(res));
                }
                else if (string.Equals(cmd, "GetSolutionOverview", StringComparison.OrdinalIgnoreCase))
                {
                    var parameters = new Dictionary<string, object>();
                    var res = await builtInVsToolProvider.ExecuteAsync("Get_Solution_Overview", parameters, token);
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

                    var res = await builtInVsToolProvider.ExecuteAsync("Find_Files_By_Name", parameters, token);
                    await writer.WriteLineAsync(JsonConvert.SerializeObject(res));
                }
                else if (string.Equals(cmd, "Find_Symbol_References", StringComparison.OrdinalIgnoreCase))
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

                    var res = await builtInVsToolProvider.ExecuteAsync("Find_Symbol_References", parameters, token);
                    await writer.WriteLineAsync(JsonConvert.SerializeObject(res));
                }
                else if (string.Equals(cmd, "ListDirectoryContents", StringComparison.OrdinalIgnoreCase))
                {
                    var directoryPath = parts.Length >= 3 ? parts[2] : ".";
                    var parameters = new Dictionary<string, object>
                    {
                        { "directory_path", directoryPath }
                    };

                    var res = await builtInVsToolProvider.ExecuteAsync("List_Directory_Contents", parameters, token);
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
