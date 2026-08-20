using System;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using LMLocal.Infrastructure.HttpWrapper;
using LMLocal.Infrastructure.Security;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class TemporaryHttpClientFactoryTests
    {
        private static readonly CertificateInfo TestInfo = new CertificateInfo(
            "AABBCCDD",
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2034, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            "CN=test",
            "CN=issuer");

        [Test]
        public void Constructor_NullCertificatePathValidator_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new TemporaryHttpClientFactory(null, new Mock<IHttpClientHandlerFactory>().Object, new ServerCertificateValidator()));
        }

        [Test]
        public void Constructor_NullHandlerFactory_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new TemporaryHttpClientFactory(new Mock<ICertificatePathValidator>().Object, null, new ServerCertificateValidator()));
        }

        [Test]
        public void Constructor_NullCertificateValidator_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new TemporaryHttpClientFactory(new Mock<ICertificatePathValidator>().Object, new Mock<IHttpClientHandlerFactory>().Object, null));
        }

        [Test]
        public void Create_EmptyPath_ThrowsArgumentException()
        {
            var validator = new Mock<ICertificatePathValidator>();
            var factory = new TemporaryHttpClientFactory(
                validator.Object,
                new Mock<IHttpClientHandlerFactory>().Object,
                new ServerCertificateValidator());

            Assert.Throws<ArgumentException>(() => factory.Create(""));
            validator.Verify(v => v.ValidateOrThrow(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void Create_ValidatorThrowsCertificatePathException_Propagates()
        {
            var validator = new Mock<ICertificatePathValidator>();
            validator
                .Setup(v => v.ValidateOrThrow("C:\\certs\\server.cer"))
                .Throws(new CertificatePathException("file not found: C:\\certs\\server.cer"));
            var handlerFactory = new Mock<IHttpClientHandlerFactory>();
            var factory = new TemporaryHttpClientFactory(validator.Object, handlerFactory.Object, new ServerCertificateValidator());

            var ex = Assert.Throws<CertificatePathException>(() => factory.Create("C:\\certs\\server.cer"));

            Assert.That(ex.Message, Does.Contain("file not found"));
            handlerFactory.Verify(h => h.CreateCustomHandler(It.IsAny<Func<X509Certificate2, SslPolicyErrors, bool>>()), Times.Never);
        }

        [Test]
        public void Create_ValidPath_ReturnsClientBuiltFromHandlerFactory()
        {
            var validator = new Mock<ICertificatePathValidator>();
            validator.Setup(v => v.ValidateOrThrow("C:\\certs\\server.cer")).Returns(TestInfo);
            var handlerFactory = new Mock<IHttpClientHandlerFactory>();
            handlerFactory
                .Setup(h => h.CreateCustomHandler(It.IsAny<Func<X509Certificate2, SslPolicyErrors, bool>>()))
                .Returns<Func<X509Certificate2, SslPolicyErrors, bool>>(callback => new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (s, cert, chain, errors) => callback(cert, errors)
                });
            var factory = new TemporaryHttpClientFactory(validator.Object, handlerFactory.Object, new ServerCertificateValidator());

            using (var client = factory.Create("C:\\certs\\server.cer"))
            {
                Assert.That(client, Is.Not.Null);
                validator.Verify(v => v.ValidateOrThrow("C:\\certs\\server.cer"), Times.Once);
                handlerFactory.Verify(h => h.CreateCustomHandler(It.IsAny<Func<X509Certificate2, SslPolicyErrors, bool>>()), Times.Once);
            }
        }
    }
}
