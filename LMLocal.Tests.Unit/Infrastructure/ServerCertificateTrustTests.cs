using System;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Security;
using LMLocal.Application.Abstractions.Ports;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class ServerCertificateTrustTests
    {
        private static readonly CertificateInfo TestCertificateInfo = new CertificateInfo(
            "AABBCCDD",
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2034, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            "CN=test",
            "CN=issuer");

        private string _tempFilePath;

        [SetUp]
        public void SetUp()
        {
            _tempFilePath = Path.Combine(Path.GetTempPath(), "trust_test_" + Guid.NewGuid().ToString("N") + ".pem");
            File.WriteAllText(_tempFilePath, string.Empty);
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_tempFilePath))
            {
                File.Delete(_tempFilePath);
            }
        }

        private static AppSettings SettingsWithPath(string path)
        {
            return new AppSettings { TrustedServerCertificatePath = path };
        }

        private static Mock<ISettingsManager> CreateSettingsManager(AppSettings current)
        {
            var mock = new Mock<ISettingsManager>();
            mock.Setup(s => s.Current).Returns(current);
            return mock;
        }

        private static IServerCertificateTrust CreateTrust(
            Mock<ISettingsManager> settingsManager,
            IX509CertificateLoader loader,
            IServerCertificateValidator validator)
        {
            return new ServerCertificateTrust(settingsManager.Object, loader, validator);
        }

        [Test]
        public void Constructor_NullSettingsManager_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new ServerCertificateTrust(null, new X509CertificateLoader(), new ServerCertificateValidator()));
        }

        [Test]
        public void Constructor_NullCertificateLoader_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new ServerCertificateTrust(CreateSettingsManager(new AppSettings()).Object, null, new ServerCertificateValidator()));
        }

        [Test]
        public void Constructor_NullCertificateValidator_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new ServerCertificateTrust(CreateSettingsManager(new AppSettings()).Object, new X509CertificateLoader(), null));
        }

        [Test]
        public void RequiresCustomCertificate_EmptyPath_ReturnsFalse()
        {
            var loaderMock = new Mock<IX509CertificateLoader>();
            var trust = CreateTrust(CreateSettingsManager(new AppSettings()), loaderMock.Object, new ServerCertificateValidator());

            bool result = trust.RequiresCustomCertificate();

            Assert.That(result, Is.False);
            loaderMock.Verify(l => l.LoadCertificateInfo(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void RequiresCustomCertificate_MissingFile_FallsBackToDefaultTrust()
        {
            string missingPath = Path.Combine(Path.GetTempPath(), "does_not_exist_" + Guid.NewGuid().ToString("N") + ".pem");
            var loaderMock = new Mock<IX509CertificateLoader>();
            var trust = CreateTrust(CreateSettingsManager(SettingsWithPath(missingPath)), loaderMock.Object, new ServerCertificateValidator());

            bool result = trust.RequiresCustomCertificate();

            Assert.That(result, Is.False);
            loaderMock.Verify(l => l.LoadCertificateInfo(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void RequiresCustomCertificate_InvalidFile_FallsBackToDefaultTrust()
        {
            var loaderMock = new Mock<IX509CertificateLoader>();
            loaderMock.Setup(l => l.LoadCertificateInfo(It.IsAny<string>())).Returns((CertificateInfo)null);
            var trust = CreateTrust(CreateSettingsManager(SettingsWithPath(_tempFilePath)), loaderMock.Object, new ServerCertificateValidator());

            bool result = trust.RequiresCustomCertificate();

            Assert.That(result, Is.False);
        }

        [Test]
        public void RequiresCustomCertificate_ValidCertificate_ReturnsTrue()
        {
            var loaderMock = new Mock<IX509CertificateLoader>();
            loaderMock.Setup(l => l.LoadCertificateInfo(It.IsAny<string>())).Returns(TestCertificateInfo);
            var trust = CreateTrust(CreateSettingsManager(SettingsWithPath(_tempFilePath)), loaderMock.Object, new ServerCertificateValidator());

            bool result = trust.RequiresCustomCertificate();

            Assert.That(result, Is.True);
        }

        [Test]
        public void RequiresCustomCertificate_CachesPerPath()
        {
            var loaderMock = new Mock<IX509CertificateLoader>();
            loaderMock.Setup(l => l.LoadCertificateInfo(It.IsAny<string>())).Returns(TestCertificateInfo);
            var settingsManager = CreateSettingsManager(SettingsWithPath(_tempFilePath));
            var trust = CreateTrust(settingsManager, loaderMock.Object, new ServerCertificateValidator());

            Assert.That(trust.RequiresCustomCertificate(), Is.True);
            Assert.That(trust.RequiresCustomCertificate(), Is.True);

            loaderMock.Verify(l => l.LoadCertificateInfo(It.IsAny<string>()), Times.Once);
        }

        [Test]
        public void SettingsChanged_SamePath_DoesNotInvalidateCache()
        {
            var loaderMock = new Mock<IX509CertificateLoader>();
            loaderMock.Setup(l => l.LoadCertificateInfo(It.IsAny<string>())).Returns(TestCertificateInfo);
            var settingsManager = CreateSettingsManager(SettingsWithPath(_tempFilePath));
            var trust = CreateTrust(settingsManager, loaderMock.Object, new ServerCertificateValidator());

            Assert.That(trust.RequiresCustomCertificate(), Is.True);

            settingsManager.Raise(s => s.SettingsChanged += null, SettingsWithPath(_tempFilePath));
            Assert.That(trust.RequiresCustomCertificate(), Is.True);

            loaderMock.Verify(l => l.LoadCertificateInfo(It.IsAny<string>()), Times.Once);
        }

        [Test]
        public void SettingsChanged_DifferentPath_InvalidatesCache()
        {
            var loaderMock = new Mock<IX509CertificateLoader>();
            loaderMock.Setup(l => l.LoadCertificateInfo(It.IsAny<string>())).Returns(TestCertificateInfo);
            var settingsManager = CreateSettingsManager(SettingsWithPath(_tempFilePath));
            var trust = CreateTrust(settingsManager, loaderMock.Object, new ServerCertificateValidator());

            Assert.That(trust.RequiresCustomCertificate(), Is.True);

            settingsManager.Raise(s => s.SettingsChanged += null, SettingsWithPath(Path.Combine(Path.GetTempPath(), "other_" + Guid.NewGuid().ToString("N") + ".pem")));
            Assert.That(trust.RequiresCustomCertificate(), Is.True);

            loaderMock.Verify(l => l.LoadCertificateInfo(It.IsAny<string>()), Times.Exactly(2));
        }

        [Test]
        public void Validate_DelegatesToCertificateValidator()
        {
            var validatorMock = new Mock<IServerCertificateValidator>();
            validatorMock.Setup(v => v.Validate(It.IsAny<X509Certificate2>(), It.IsAny<SslPolicyErrors>(), It.IsAny<CertificateInfo>()))
                .Returns(true);
            var trust = CreateTrust(CreateSettingsManager(new AppSettings()), new X509CertificateLoader(), validatorMock.Object);

            bool result = trust.Validate(null, SslPolicyErrors.RemoteCertificateChainErrors);

            Assert.That(result, Is.True);
            validatorMock.Verify(v => v.Validate(null, SslPolicyErrors.RemoteCertificateChainErrors, null), Times.Once);
        }

        [Test]
        public void Dispose_RequiresCustomCertificate_ThrowsObjectDisposedException()
        {
            var trust = CreateTrust(CreateSettingsManager(new AppSettings()), new X509CertificateLoader(), new ServerCertificateValidator());
            trust.Dispose();

            Assert.Throws<ObjectDisposedException>(() => trust.RequiresCustomCertificate());
        }
    }
}
