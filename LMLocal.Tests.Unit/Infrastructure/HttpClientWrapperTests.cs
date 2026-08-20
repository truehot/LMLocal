using System;
using System.Net.Http;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.HttpWrapper;
using LMLocal.Infrastructure.Security;
using LMLocal.Infrastructure.Settings;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class HttpClientWrapperTests
    {
        private static Mock<ISettingsManager> CreateMockSettingsManager(AppSettings current)
        {
            var mock = new Mock<ISettingsManager>();
            mock.Setup(s => s.Current).Returns(current);
            return mock;
        }

        private static HttpClientWrapper CreateWrapper(ISettingsManager settingsManager = null)
        {
            var settings = settingsManager ?? CreateMockSettingsManager(new AppSettings()).Object;
            var trust = new ServerCertificateTrust(settings, new X509CertificateLoader(), new ServerCertificateValidator());
            var handlerFactory = new HttpClientHandlerFactory();
            return new HttpClientWrapper(trust, handlerFactory);
        }

        [Test]
        public void Constructor_NullTrust_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new HttpClientWrapper(null, new HttpClientHandlerFactory()));
        }

        [Test]
        public void Constructor_NullHandlerFactory_ThrowsArgumentNullException()
        {
            var trust = new ServerCertificateTrust(
                CreateMockSettingsManager(new AppSettings()).Object,
                new X509CertificateLoader(),
                new ServerCertificateValidator());
            Assert.Throws<ArgumentNullException>(() => new HttpClientWrapper(trust, null));
        }

        [Test]
        public void SendAsync_AfterDispose_ThrowsObjectDisposedException()
        {
            var wrapper = CreateWrapper();
            wrapper.Dispose();

            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await wrapper.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost/")));
        }
    }
}
