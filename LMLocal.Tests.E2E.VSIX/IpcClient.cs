using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal sealed class IpcClient : IDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
    private bool _disposed;
    private bool _isBroken;

    private IpcClient(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
        _reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
        _writer = new StreamWriter(pipe, Encoding.UTF8, 1024, true) { AutoFlush = true };
    }

    public static async Task<IpcClient> ConnectAsync(string pipeName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        Exception lastError = null;

        while (DateTime.UtcNow - startTime < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(2));
                    await client.ConnectAsync(cts.Token);
                }
                return new IpcClient(client);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                client.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                client.Dispose();
                await Task.Delay(500, cancellationToken);
            }
        }

        throw new TimeoutException($"Failed to connect to pipe '{pipeName}' within {timeout}.", lastError);
    }

    public Task<string> SendCommandAsync(string command, CancellationToken cancellationToken)
    {
        return SendCommandAsync(command, TimeSpan.FromSeconds(20), cancellationToken);
    }

    public async Task<string> SendCommandAsync(string command, TimeSpan readTimeout, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(IpcClient));
            if (_isBroken) throw new InvalidOperationException("Client is broken due to previous timeout/cancellation. Create a new client.");

            try
            {
                await _writer.WriteLineAsync(command);
            }
            catch (ObjectDisposedException)
            {
                _isBroken = true;
                throw new InvalidOperationException("Pipe was closed while writing command.");
            }

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                // Allow long-running operations on the server (e.g. a SubAgent run).
                if (readTimeout > TimeSpan.Zero)
                {
                    cts.CancelAfter(readTimeout);
                }

                string line = await ReadLineWithCancellationAsync(cts.Token);
                if (line == null)
                {
                    _isBroken = true;
                    throw new InvalidOperationException("Server closed the connection.");
                }
                return line;
            }
        }
        catch (OperationCanceledException)
        {
            _isBroken = true;
            throw new TimeoutException("The read operation timed out or was cancelled. The client can no longer be used.");
        }
        finally
        {
            if (!_disposed)
                _lock.Release();
        }
    }

    private async Task<string> ReadLineWithCancellationAsync(CancellationToken token)
    {
        using (token.Register(() =>
        {
            try
            {
                // Close the stream to interrupt ReadLineAsync without disposing it
                _reader?.Close();
            }
            catch
            {
                // Ignore any exceptions during close
            }
        }, useSynchronizationContext: false))
        {
            try
            {
                return await _reader.ReadLineAsync();
            }
            catch (ObjectDisposedException)
            {
                throw new OperationCanceledException(token);
            }
            catch (IOException)
            {
                // Stream was closed, treat as cancellation
                throw new OperationCanceledException(token);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _lock.Wait();
        try
        {
            try
            {
                _writer?.Close();
            }
            catch
            {
                // Ignore close errors
            }

            try
            {
                _reader?.Close();
            }
            catch
            {
                // Ignore close errors
            }

            try
            {
                _pipe?.Dispose();
            }
            catch
            {
                // Ignore dispose errors
            }
        }
        finally
        {
            _lock.Release();
            _lock.Dispose();
        }
    }
}
