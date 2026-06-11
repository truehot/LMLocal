using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Tooling.Mcp.Models;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.Mcp.Client
{
    /// <summary>
    /// MCP client implementation that communicates with a server process via standard input/output (stdio).
    /// </summary>
    public class StdioMcpClient : McpClientBase
    {
        private readonly string _command;
        private readonly List<string> _args;
        private readonly Dictionary<string, string> _env;

        private Process _process;
        private StreamWriter _stdinWriter;
        private StreamReader _stdoutReader;
        private StreamReader _stderrReader;

        private readonly Dictionary<long, TaskCompletionSource<JsonRpcResponse>> _pendingRequests =
            new Dictionary<long, TaskCompletionSource<JsonRpcResponse>>();

        private readonly object _pendingRequestsLock = new object();
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

        private Task _readingTask;
        private Task _stderrTask;
        private CancellationTokenSource _internalCancellationTokenSource;
        private bool _closed = false;
        private const int RequestTimeoutMilliseconds = 30000;

        public StdioMcpClient(string command, List<string> args = null, Dictionary<string, string> env = null)
        {
            if (string.IsNullOrWhiteSpace(command))
                throw new ArgumentException("Command cannot be empty", nameof(command));

            _command = command;
            _args = args ?? new List<string>();
            _env = env ?? new Dictionary<string, string>();
        }

        public override async Task InitializeAsync(CancellationToken cancellationToken)
        {
            if (_process != null)
                throw new InvalidOperationException("Process already started");

            try
            {
                await StartProcessAsync(cancellationToken).ConfigureAwait(false);

                var initRequest = new JsonRpcRequest
                {
                    Id = GetNextRequestId(),
                    Method = "initialize",
                    Params = new InitializeRequest()
                };

                var response = await SendRequestAndWaitResponseAsync(initRequest, cancellationToken)
                    .ConfigureAwait(false);

                if (response?.IsSuccess != true)
                    throw new InvalidOperationException("Failed to initialize MCP server");

                InternalLogger.Debug($"[StdioMcpClient] Initialized successfully with process {_process?.Id}");
            }
            catch (Exception ex)
            {
                StopProcess();
                throw new InvalidOperationException($"Failed to initialize stdio MCP client: {ex.Message}", ex);
            }
        }

        public override async Task CloseAsync(CancellationToken cancellationToken)
        {
            if (_closed)
                return;

            _closed = true;

            try
            {
                _internalCancellationTokenSource?.Cancel();

                if (_readingTask != null || _stderrTask != null)
                {
                    try
                    {
                        var loopsTask = Task.WhenAll(_readingTask ?? Task.FromResult(true), _stderrTask ?? Task.FromResult(true));
                        await Task.WhenAny(loopsTask, Task.Delay(2000, CancellationToken.None)).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Debug($"[StdioMcpClient] Error waiting for background tasks: {ex.Message}");
                    }
                }

                StopProcess();
                InternalLogger.Debug($"[StdioMcpClient] Closed successfully");
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"[StdioMcpClient] Error during close: {ex.Message}", ex);
            }
        }

        protected override async Task<string> SendJsonAndWaitResponseAsync(string json, CancellationToken cancellationToken)
        {
            if (_process == null)
                throw new InvalidOperationException("Process is not running");

            if (_closed)
                throw new InvalidOperationException("Client is closed");

            if (_readingTask != null && _readingTask.IsFaulted)
                throw new InvalidOperationException("Internal reader loop crashed", _readingTask.Exception);

            long requestId = ExtractRequestId(json);

            var tcs = new TaskCompletionSource<JsonRpcResponse>(TaskContinuationOptions.RunContinuationsAsynchronously);

            lock (_pendingRequestsLock)
            {
                _pendingRequests[requestId] = tcs;
            }

            try
            {
                await SendJsonAsync(json, cancellationToken).ConfigureAwait(false);

                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(RequestTimeoutMilliseconds);

                    using (cts.Token.Register(() => tcs.TrySetCanceled(cts.Token)))
                    {
                        var response = await tcs.Task.ConfigureAwait(false);
                        return response?.ToJson() ?? throw new InvalidOperationException("Empty response from server");
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"MCP request {requestId} timed out after {RequestTimeoutMilliseconds}ms.");
            }
            finally
            {
                lock (_pendingRequestsLock)
                {
                    _pendingRequests.Remove(requestId);
                }
            }
        }

        protected override async Task SendJsonAsync(string json, CancellationToken cancellationToken)
        {
            if (_stdinWriter == null || _process == null)
                throw new InvalidOperationException("Process is not running");

            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _stdinWriter.WriteLineAsync(json).ConfigureAwait(false);
                await _stdinWriter.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private Task StartProcessAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _internalCancellationTokenSource = new CancellationTokenSource();

            var psi = new ProcessStartInfo
            {
                FileName = _command,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var argsBuilder = new StringBuilder();
            foreach (var arg in _args)
            {
                if (argsBuilder.Length > 0) argsBuilder.Append(" ");
                argsBuilder.Append(QuoteArgument(arg));
            }
            psi.Arguments = argsBuilder.ToString();

            foreach (System.Collections.DictionaryEntry envVar in Environment.GetEnvironmentVariables())
            {
                string key = envVar.Key.ToString();
                if (!psi.EnvironmentVariables.ContainsKey(key))
                {
                    psi.EnvironmentVariables[key] = envVar.Value?.ToString();
                }
            }

            foreach (var envVar in _env)
            {
                psi.EnvironmentVariables[envVar.Key] = envVar.Value;
            }

            _process = new Process { StartInfo = psi };

            try
            {
                if (!_process.Start())
                    throw new InvalidOperationException("Failed to start process");

                _stdinWriter = _process.StandardInput;
                _stdoutReader = _process.StandardOutput;
                _stderrReader = _process.StandardError;

                _readingTask = ReadJsonRpcMessagesAsync(_internalCancellationTokenSource.Token);
                _stderrTask = ReadStderrAsync(_internalCancellationTokenSource.Token);

                InternalLogger.Debug($"[StdioMcpClient] Process started with PID {_process.Id}");
            }
            catch (Exception ex)
            {
                StopProcess();
                throw new InvalidOperationException($"Failed to start process '{_command}': {ex.Message}", ex);
            }

            return Task.FromResult(true);
        }

        private async Task ReadJsonRpcMessagesAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var reader = _stdoutReader;
                    if (reader == null)
                    {
                        InternalLogger.Debug("[StdioMcpClient] stdout reader became null, exiting");
                        break;
                    }

                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line == null)
                    {
                        InternalLogger.Debug("[StdioMcpClient] stdout closed by server");
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    try
                    {
                        var response = JsonConvert.DeserializeObject<JsonRpcResponse>(line);
                        if (response?.Id != null)
                        {
                            HandleJsonRpcResponse(response);
                        }
                    }
                    catch (JsonException ex)
                    {
                        InternalLogger.Warn($"[StdioMcpClient] Failed to parse JSON-RPC response: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
            {
                InternalLogger.Error($"[StdioMcpClient] ReadJsonRpcMessagesAsync failed unexpectedly: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"[StdioMcpClient] ReadJsonRpcMessagesAsync failed unexpectedly: {ex.Message}", ex);
            }
            finally
            {
                CancelAllPendingRequests("Process reader loop stopped.");
            }
        }

        private async Task ReadStderrAsync(CancellationToken cancellationToken)
        {
            var reader = _stderrReader;
            if (reader == null)
            {
                InternalLogger.Debug("[StdioMcpClient] stderr reader is null");
                return;
            }

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line == null) break;

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        InternalLogger.Warn($"[StdioMcpServer STDERR] {line}");
                    }
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
            {
                InternalLogger.Info($"[StdioMcpClient] Shutdown");
            }
            catch (Exception ex)
            {
                InternalLogger.Debug($"[StdioMcpClient] Error reading from stderr: {ex.Message}");
            }
        }

        private void HandleJsonRpcResponse(JsonRpcResponse response)
        {
            if (response?.Id == null)
                return;

            long requestId = response.Id is long id ? id : Convert.ToInt64(response.Id);

            lock (_pendingRequestsLock)
            {
                if (_pendingRequests.TryGetValue(requestId, out var tcs))
                {
                    if (response.IsSuccess)
                    {
                        tcs.TrySetResult(response);
                    }
                    else
                    {
                        var errorMsg = response.Error?.Message ?? "Unknown error";
                        tcs.TrySetException(new InvalidOperationException($"MCP error: {errorMsg}"));
                    }
                }
            }
        }

        private void CancelAllPendingRequests(string message)
        {
            lock (_pendingRequestsLock)
            {
                foreach (var tcs in _pendingRequests.Values)
                {
                    tcs.TrySetException(new InvalidOperationException(message));
                }
                _pendingRequests.Clear();
            }
        }

        private static long ExtractRequestId(string json)
        {
            try
            {
                var obj = JsonConvert.DeserializeObject<JsonRpcRequest>(json);
                return Convert.ToInt64(obj.Id);
            }
            catch
            {
                return 0;
            }
        }

        private void StopProcess()
        {
            try
            {
                _stdinWriter?.Dispose();
                _stdoutReader?.Dispose();
                _stderrReader?.Dispose();

                if (_process != null)
                {
                    try
                    {
                        bool hasExited = false;
                        try { hasExited = _process.HasExited; } catch { hasExited = true; }

                        if (!hasExited)
                        {
                            int pid = _process.Id;

                            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = "taskkill",
                                    Arguments = $"/T /F /PID {pid}",
                                    CreateNoWindow = true,
                                    UseShellExecute = false
                                })?.WaitForExit(3000);
                            }
                            else
                            {
                                _process.Kill();
                            }

                            _process.WaitForExit(2000);
                        }
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Debug($"[StdioMcpClient] Error killing process tree: {ex.Message}");
                    }

                    _process.Dispose();
                    _process = null;
                }

                _internalCancellationTokenSource?.Dispose();
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"[StdioMcpClient] Error in StopProcess: {ex.Message}", ex);
            }
        }

        private static string QuoteArgument(string arg)
        {
            if (string.IsNullOrEmpty(arg))
                return "\"\"";
            if (arg.Contains(" ") || arg.Contains("\"") || arg.Contains("\\"))
            {
                return "\"" + arg.Replace("\"", "\\\"") + "\"";
            }
            return arg;
        }
    }
}
