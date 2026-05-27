using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

/**
 * Implements a simple IPC server using named pipes to allow external processes to send commands to the VS instance.
 * It uses a cancellation token to allow graceful shutdown when the package is unloaded.
 */
internal static class VsIpcServer
{
    private static Task serverTask;
    private static CancellationTokenSource internalCts;
    private static readonly object lockObj = new object();

    public static void Start(AsyncPackage package, CancellationToken externalToken)
    {
        lock (lockObj)
        {
            if (serverTask != null)
                return;

            internalCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            var token = internalCts.Token;

            serverTask = Task.Run(async () =>
            {
                Debug.WriteLine("IPC Server loop started");
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        var currentUserSid = WindowsIdentity.GetCurrent().User;
                        var security = new PipeSecurity();
                        security.AddAccessRule(new PipeAccessRule(
                            currentUserSid,
                            PipeAccessRights.ReadWrite,
                            AccessControlType.Allow));


                        using (var server = new NamedPipeServerStream(
                            "LMLocal.Ipc",
                            PipeDirection.InOut,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous,
                            1024,
                            1024,
                            security))
                        {
                            try
                            {
                                await server.WaitForConnectionAsync(token);
                            }
                            catch (OperationCanceledException)
                            {
                                Debug.WriteLine("IPC: WaitForConnection cancelled, exiting loop");
                                break;
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"IPC: WaitForConnection error: {ex.Message}");
                                continue;
                            }

                            using (var reader = new StreamReader(server, Encoding.UTF8, false, 1024, true))
                            using (var writer = new StreamWriter(server, Encoding.UTF8, 1024, true))
                            {
                                writer.AutoFlush = true;

                                while (!token.IsCancellationRequested && server.IsConnected)
                                {
                                    string command = null;
                                    try
                                    {
                                        command = await ReadLineWithCancelAsync(reader, server, token);
                                    }
                                    catch (OperationCanceledException)
                                    {
                                        Debug.WriteLine("IPC: Operation cancelled during ReadLine");
                                        break;
                                    }

                                    if (command == null)
                                    {
                                        Debug.WriteLine("IPC: Client disconnected (null read)");
                                        break;
                                    }

                                    Debug.WriteLine($"IPC: Received command: {command}");

                                    try
                                    {
                                        await IpcCommandHandler.HandleCommandAsync(package, command, writer, token);
                                    }
                                    catch (OperationCanceledException)
                                    {
                                        Debug.WriteLine("IPC: Operation cancelled during command handling");
                                        break;
                                    }
                                    catch (Exception ex)
                                    {
                                        try { await writer.WriteLineAsync("ERROR " + ex.Message.ToString()); } catch { }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("IPC Server loop cancelled");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"IPC Server loop fatal error: {ex.Message}");
                }
                finally
                {
                    Debug.WriteLine("IPC Server loop stopped");
                }
            }, token);
        }
    }


    private static async Task<string> ReadLineWithCancelAsync(StreamReader reader, NamedPipeServerStream server, CancellationToken token)
    {

        using (token.Register(() =>
        {
            try { server.Dispose(); } catch { }
        }))
        {
            try
            {
                string line = await reader.ReadLineAsync();
                return line;
            }
            catch (ObjectDisposedException)
            {

                throw new OperationCanceledException(token);
            }
        }
    }

    public static async Task StopAsync()
    {
        Task taskToWait = null;
        lock (lockObj)
        {
            internalCts?.Cancel();
            taskToWait = serverTask;
            internalCts = null;
            serverTask = null;
        }
        if (taskToWait != null)
        {
            try
            {
                var completed = await Task.WhenAny(taskToWait, Task.Delay(25000));
                if (completed != taskToWait)
                {
                    Debug.WriteLine("IPC Server stop timeout");
                }
                else
                {
                    await taskToWait;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"IPC Server stop error: {ex.Message}");
            }
        }
    }
}
