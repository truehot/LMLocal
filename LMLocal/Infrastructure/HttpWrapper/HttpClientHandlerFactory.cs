using System;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace LMLocal.Infrastructure.HttpWrapper
{
    /// <summary>
    /// Creates HttpClientHandler instances with the shared TLS policy:
    /// both default-trust and custom-certificate clients use the OS policy
    /// (SslProtocols.None) so the OS negotiates the best available protocol.
    /// </summary>
    internal interface IHttpClientHandlerFactory
    {
        HttpClientHandler CreateDefaultHandler();
        HttpClientHandler CreateCustomHandler(Func<X509Certificate2, SslPolicyErrors, bool> certificateValidationCallback);
    }

    internal sealed class HttpClientHandlerFactory : IHttpClientHandlerFactory
    {
        public HttpClientHandler CreateDefaultHandler()
        {
            return new HttpClientHandler
            {
                SslProtocols = SslProtocols.None
            };
        }

        public HttpClientHandler CreateCustomHandler(Func<X509Certificate2, SslPolicyErrors, bool> certificateValidationCallback)
        {
            if (certificateValidationCallback == null)
                throw new ArgumentNullException(nameof(certificateValidationCallback));

            var handler = new HttpClientHandler
            {
                SslProtocols = SslProtocols.None,
                ServerCertificateCustomValidationCallback = (sender, cert, chain, errors) =>
                        certificateValidationCallback(cert, errors)
            };
            return handler;
        }
    }
}
