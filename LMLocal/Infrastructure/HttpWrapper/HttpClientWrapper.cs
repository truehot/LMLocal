using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LMLocal.Infrastructure.HttpWrapper
{
    /// <summary>
    /// Abstraction over HttpClient to enable easier testing and mock implementations.
    /// </summary>
    public interface IHttpClientWrapper : IDisposable
    {
        /// <summary>
        /// Sends a POST request and returns the response.
        /// </summary>
        Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead, CancellationToken cancellationToken = default);
    }

    internal sealed class HttpClientWrapper : IHttpClientWrapper
    {
        // Shared HttpClient instance to avoid socket exhaustion on .NET Framework 4.7.2
        private static readonly HttpClient SharedHttpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        private bool _disposed;

        public HttpClientWrapper()
        {
        }

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return await SharedHttpClient.SendAsync(request, completionOption, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Do NOT dispose the shared HttpClient. Disposing HttpClient can lead to
            // socket exhaustion issues on .NET Framework. The wrapper is disposable
            // to allow callers to signal disposal, but the underlying client is shared.
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HttpClientWrapper));
        }
    }
}
