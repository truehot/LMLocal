using System;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using LMLocal.Infrastructure.HttpWrapper;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class HttpClientHandlerFactoryTests
    {
        private readonly HttpClientHandlerFactory _factory = new HttpClientHandlerFactory();

        [Test]
        public void CreateDefaultHandler_UsesOsPolicy()
        {
            using (var handler = _factory.CreateDefaultHandler())
            {
                Assert.That(handler, Is.Not.Null);
                Assert.That(handler.SslProtocols, Is.EqualTo(SslProtocols.None));
                Assert.That(handler.ServerCertificateCustomValidationCallback, Is.Null);
            }
        }

        [Test]
        public void CreateCustomHandler_UsesOsPolicyWithCallback()
        {
            using (var handler = _factory.CreateCustomHandler((cert, errors) => true))
            {
                Assert.That(handler.SslProtocols, Is.EqualTo(SslProtocols.None));
                Assert.That(handler.ServerCertificateCustomValidationCallback, Is.Not.Null);
            }
        }

        [Test]
        public void CreateCustomHandler_NullCallback_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _factory.CreateCustomHandler(null));
        }

        [Test]
        public void CreateCustomHandler_CallbackDelegatesCertificateAndErrors()
        {
            X509Certificate2 capturedCert = null;
            SslPolicyErrors capturedErrors = SslPolicyErrors.None;
            bool invoked = false;

            using (var handler = _factory.CreateCustomHandler((cert, errors) =>
            {
                invoked = true;
                capturedCert = cert;
                capturedErrors = errors;
                return true;
            }))
            {
                bool result = handler.ServerCertificateCustomValidationCallback(null, null, null, SslPolicyErrors.RemoteCertificateChainErrors);

                Assert.That(result, Is.True);
                Assert.That(invoked, Is.True);
                Assert.That(capturedCert, Is.Null);
                Assert.That(capturedErrors, Is.EqualTo(SslPolicyErrors.RemoteCertificateChainErrors));
            }
        }

        [Test]
        public void CreateCustomHandler_CallbackReturnValue_IsPropagated()
        {
            using (var handler = _factory.CreateCustomHandler((cert, errors) => false))
            {
                bool result = handler.ServerCertificateCustomValidationCallback(null, null, null, SslPolicyErrors.RemoteCertificateChainErrors);

                Assert.That(result, Is.False);
            }
        }
    }
}