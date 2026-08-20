using System;
using System.Net.Http;
using System.Threading;
using LMLocal.Infrastructure.Security;

namespace LMLocal.Infrastructure.HttpWrapper
{
    /// <summary>
    /// Builds a short-lived HttpClient that trusts a single server certificate for a Test Connection probe with an unsaved certificate path. 
    /// </summary>
    internal interface ITemporaryHttpClientFactory
    {
        HttpClient Create(string certificatePath);
    }

    internal sealed class TemporaryHttpClientFactory : ITemporaryHttpClientFactory
    {
        private readonly ICertificatePathValidator _certificatePathValidator;
        private readonly IHttpClientHandlerFactory _handlerFactory;
        private readonly IServerCertificateValidator _certificateValidator;

        public TemporaryHttpClientFactory(
            ICertificatePathValidator certificatePathValidator,
            IHttpClientHandlerFactory handlerFactory,
            IServerCertificateValidator certificateValidator)
        {
            _certificatePathValidator = certificatePathValidator ?? throw new ArgumentNullException(nameof(certificatePathValidator));
            _handlerFactory = handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory));
            _certificateValidator = certificateValidator ?? throw new ArgumentNullException(nameof(certificateValidator));
        }

        public HttpClient Create(string certificatePath)
        {
            if (string.IsNullOrWhiteSpace(certificatePath))
                throw new ArgumentException("Certificate path is required.", nameof(certificatePath));

            CertificateInfo info = _certificatePathValidator.ValidateOrThrow(certificatePath);
            var handler = _handlerFactory.CreateCustomHandler((cert, errors) =>
                _certificateValidator.Validate(cert, errors, info));

            return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        }
    }
}
