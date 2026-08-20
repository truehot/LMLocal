using System;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Security;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class TestConnectionErrorClassifierTests
    {
        private readonly TestConnectionErrorClassifier _classifier = new TestConnectionErrorClassifier();

        [Test]
        public void Classify_Null_ReturnsUnknownError()
        {
            Assert.That(_classifier.Classify(null), Is.EqualTo("Unknown error"));
        }

        [Test]
        public void Classify_GenericException_ReturnsMessage()
        {
            Assert.That(_classifier.Classify(new InvalidOperationException("connection refused")), Is.EqualTo("connection refused"));
        }

        [Test]
        public void Classify_Cancellation_ReturnsTimeout()
        {
            Assert.That(_classifier.Classify(new TaskCanceledException()), Is.EqualTo("Request timed out"));
        }

        [Test]
        public void Classify_CertificatePathException_ReturnsItsMessage()
        {
            var ex = new CertificatePathException("file not found: C:\\certs\\x.cer");
            Assert.That(_classifier.Classify(ex), Is.EqualTo("file not found: C:\\certs\\x.cer"));
        }

        [Test]
        public void Classify_TlsAuthentication_ReturnsTlsHandshake()
        {
            var auth = new AuthenticationException("The remote certificate is invalid according to the validation procedure.");
            var ex = new HttpRequestException("SSL connection could not be established.", auth);

            string result = _classifier.Classify(ex);

            Assert.That(result, Does.StartWith("TLS handshake failed:"));
            Assert.That(result, Does.Contain("remote certificate is invalid"));
        }

        [Test]
        public void Classify_DeeplyNestedAuthentication_IsFound()
        {
            var auth = new AuthenticationException("bad cert");
            var inner = new InvalidOperationException("inner", auth);
            var ex = new HttpRequestException("outer", inner);

            Assert.That(_classifier.Classify(ex), Does.StartWith("TLS handshake failed:"));
        }
    }
}
