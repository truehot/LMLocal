using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.HttpWrapper;

namespace LMLocal.Tests.Unit.Infrastructure
{
    /// <summary>
    /// Test helper: Simple implementation of IHttpClientWrapper that wraps an HttpClient for testing.
    /// Used to adapt existing test HttpClient instances to the IHttpClientWrapper interface.
    /// </summary>
    internal class TestHttpClientWrapper : IHttpClientWrapper
    {
        private readonly HttpClient _httpClient;
        private bool _disposed;

        public TestHttpClientWrapper(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<HttpResponseMessage> GetAsync(string requestUri, HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return await _httpClient.GetAsync(requestUri, completionOption, cancellationToken).ConfigureAwait(false);
        }

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return await _httpClient.SendAsync(request, completionOption, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _httpClient?.Dispose();
            GC.SuppressFinalize(this);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TestHttpClientWrapper));
        }
    }
}
