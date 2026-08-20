using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Security;

namespace LMLocal.Infrastructure.HttpWrapper
{
    /// <summary>
    /// Abstraction over HttpClient to enable easier testing and mock implementations.
    /// </summary>
    public interface IHttpClientWrapper : IDisposable
    {
        Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Owns two fixed HttpClients and picks one based on the current server trust target.
    /// </summary>
    internal sealed class HttpClientWrapper : IHttpClientWrapper
    {
        private readonly IServerCertificateTrust _serverCertificateTrust;
        private readonly HttpClient _sharedHttpClient;
        private readonly HttpClient _customHttpClient;
        private bool _disposed;

        public HttpClientWrapper(IServerCertificateTrust serverCertificateTrust, IHttpClientHandlerFactory handlerFactory)
        {
            _serverCertificateTrust = serverCertificateTrust ?? throw new ArgumentNullException(nameof(serverCertificateTrust));
            if (handlerFactory == null) throw new ArgumentNullException(nameof(handlerFactory));

            _sharedHttpClient = new HttpClient(handlerFactory.CreateDefaultHandler()) { Timeout = Timeout.InfiniteTimeSpan };
            _customHttpClient = new HttpClient(
                handlerFactory.CreateCustomHandler((cert, errors) => _serverCertificateTrust.Validate(cert, errors)))
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
        }

        public async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            HttpClient client = _serverCertificateTrust.RequiresCustomCertificate()
                ? _customHttpClient
                : _sharedHttpClient;

            return await client.SendAsync(request, completionOption, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _sharedHttpClient.Dispose();
            _customHttpClient.Dispose();
            _serverCertificateTrust.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HttpClientWrapper));
            }
        }
    }
}
