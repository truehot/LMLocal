using System;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.ModelsList;
using LMLocal.Infrastructure.Security;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.WebView.Controllers;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class SettingsControllerTests
    {
        private Mock<ISettingsManager> _settingsManagerMock;
        private Mock<ITestConnectionService> _testConnectionServiceMock;
        private Mock<ICertificatePathValidator> _certificatePathValidatorMock;
        private SettingsController _controller;

        [SetUp]
        public void SetUp()
        {
            _settingsManagerMock = new Mock<ISettingsManager>();
            _settingsManagerMock
                .Setup(m => m.SetAiToolsModeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _testConnectionServiceMock = new Mock<ITestConnectionService>();
            _certificatePathValidatorMock = new Mock<ICertificatePathValidator>();
            _controller = new SettingsController(
                _settingsManagerMock.Object,
                _testConnectionServiceMock.Object,
                _certificatePathValidatorMock.Object);
        }

        [TearDown]
        public void TearDown()
        {
            _controller = null;
            _settingsManagerMock = null;
            _testConnectionServiceMock = null;
            _certificatePathValidatorMock = null;
        }

        // =========================================================================
        // SetAiToolsAsync
        // =========================================================================

        [Test]
        public async Task SetAiToolsAsync_ReadOnly_ReturnsTrueAndDelegatesMode()
        {
            var json = "{\"mode\":\"readonly\"}";
            var result = await _controller.SetAiToolsAsync(json);

            Assert.That(result, Is.True);
            _settingsManagerMock.Verify(
                m => m.SetAiToolsModeAsync("readonly", It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task SetAiToolsAsync_ReadWrite_ReturnsTrueAndDelegatesMode()
        {
            var json = "{\"mode\":\"readwrite\"}";
            var result = await _controller.SetAiToolsAsync(json);

            Assert.That(result, Is.True);
            _settingsManagerMock.Verify(
                m => m.SetAiToolsModeAsync("readwrite", It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task SetAiToolsAsync_NoTools_ReturnsTrueAndDelegatesMode()
        {
            var json = "{\"mode\":\"none\"}";
            var result = await _controller.SetAiToolsAsync(json);

            Assert.That(result, Is.True);
            _settingsManagerMock.Verify(
                m => m.SetAiToolsModeAsync("none", It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task SetAiToolsAsync_UnrecognizedMode_StillDelegatesToManager()
        {
            var json = "{\"mode\":\"garbage\"}";
            var result = await _controller.SetAiToolsAsync(json);

            Assert.That(result, Is.True);
            _settingsManagerMock.Verify(
                m => m.SetAiToolsModeAsync("garbage", It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task SetAiToolsAsync_WhenManagerThrows_ReturnsFalse()
        {
            _settingsManagerMock
                .Setup(m => m.SetAiToolsModeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("disk failure"));

            var json = "{\"mode\":\"readwrite\"}";
            var result = await _controller.SetAiToolsAsync(json);

            Assert.That(result, Is.False);
        }

        [Test]
        public async Task SetAiToolsAsync_NullJson_ReturnsFalse()
        {
            var result = await _controller.SetAiToolsAsync(null);

            Assert.That(result, Is.False);
            _settingsManagerMock.Verify(
                m => m.SetAiToolsModeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task SetAiToolsAsync_EmptyJson_ReturnsFalse()
        {
            var result = await _controller.SetAiToolsAsync("");

            Assert.That(result, Is.False);
            _settingsManagerMock.Verify(
                m => m.SetAiToolsModeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task SetAiToolsAsync_InvalidJson_ReturnsFalse()
        {
            var result = await _controller.SetAiToolsAsync("{not valid}");

            Assert.That(result, Is.False);
            _settingsManagerMock.Verify(
                m => m.SetAiToolsModeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task SetAiToolsAsync_MissingMode_ReturnsFalse()
        {
            var result = await _controller.SetAiToolsAsync("{}");

            Assert.That(result, Is.False);
            _settingsManagerMock.Verify(
                m => m.SetAiToolsModeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // =========================================================================
        // TestCertificateAsync
        // =========================================================================

        [Test]
        public async Task TestCertificateAsync_ValidPath_ReturnsSuccessWithThumbprint()
        {
            var info = new CertificateInfo(
                "AABBCCDD",
                new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                "CN=test",
                "CN=issuer");

            _certificatePathValidatorMock
                .Setup(v => v.ValidateOrThrow("C:\\certs\\server.cer"))
                .Returns(info);

            var result = await _controller.TestCertificateAsync("{\"path\":\"C:\\\\certs\\\\server.cer\"}");

            Assert.That(result, Does.Contain("\"success\":true"));
            Assert.That(result, Does.Contain("\"thumbprint\":\"AABBCCDD\""));
        }

        [Test]
        public async Task TestCertificateAsync_NullPayload_ReturnsFalse()
        {
            var result = await _controller.TestCertificateAsync(null);

            Assert.That(result, Does.Contain("\"success\":false"));
        }

        [Test]
        public async Task TestCertificateAsync_MissingPath_ReturnsFalse()
        {
            var result = await _controller.TestCertificateAsync("{}");

            Assert.That(result, Does.Contain("\"success\":false"));
            _certificatePathValidatorMock.Verify(v => v.ValidateOrThrow(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task TestCertificateAsync_ValidatorThrows_ReturnsFalseWithMessage()
        {
            _certificatePathValidatorMock
                .Setup(v => v.ValidateOrThrow(It.IsAny<string>()))
                .Throws(new CertificatePathException("certificate expired: 2021-01-01T00:00:00.0000000Z"));

            var result = await _controller.TestCertificateAsync("{\"path\":\"C:\\\\certs\\\\expired.cer\"}");

            Assert.That(result, Does.Contain("\"success\":false"));
            Assert.That(result, Does.Contain("certificate expired"));
        }

        [Test]
        public async Task TestCertificateAsync_MissingPath_ReturnsErrorMessageShape()
        {
            var result = await _controller.TestCertificateAsync("{}");

            Assert.That(result, Does.Contain("\"success\":false"));
            Assert.That(result, Does.Contain("\"message\":\"Certificate path is required\""));
            _certificatePathValidatorMock.Verify(v => v.ValidateOrThrow(It.IsAny<string>()), Times.Never);
        }

        // =========================================================================
        // TestConnectionAsync
        // =========================================================================

        private void SetupTestConnectionOk()
        {
            _settingsManagerMock.Setup(m => m.RequestTimeoutSeconds).Returns(30);
            _testConnectionServiceMock
                .Setup(m => m.TestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(TestConnectionResult.Ok());
        }

        [Test]
        public async Task TestConnectionAsync_WithoutCertificatePath_DelegatesWithNullPath()
        {
            SetupTestConnectionOk();

            var result = await _controller.TestConnectionAsync(
                "{\"provider\":\"openai\",\"url\":\"https://api.openai.com\",\"apiKey\":\"sk\"}");

            Assert.That(result, Does.Contain("\"success\":true"));
            _testConnectionServiceMock.Verify(
                m => m.TestAsync("openai", "https://api.openai.com", "sk", It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task TestConnectionAsync_WithCertificatePath_PassesPathThrough()
        {
            const string certPath = "C:\\certs\\server.cer";
            _settingsManagerMock.Setup(m => m.RequestTimeoutSeconds).Returns(30);
            _testConnectionServiceMock
                .Setup(m => m.TestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(TestConnectionResult.Ok());

            var result = await _controller.TestConnectionAsync(
                "{\"provider\":\"openai\",\"url\":\"https://localhost:8443\",\"certificatePath\":\"C:\\\\certs\\\\server.cer\"}");

            Assert.That(result, Does.Contain("\"success\":true"));
            _testConnectionServiceMock.Verify(
                m => m.TestAsync("openai", "https://localhost:8443", "", certPath, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task TestConnectionAsync_ServiceFailsWithCertError_Propagates()
        {
            const string certPath = "C:\\certs\\missing.cer";
            _settingsManagerMock.Setup(m => m.RequestTimeoutSeconds).Returns(30);
            _testConnectionServiceMock
                .Setup(m => m.TestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(TestConnectionResult.Fail("file not found: " + certPath));

            var result = await _controller.TestConnectionAsync(
                "{\"provider\":\"openai\",\"url\":\"https://localhost:8443\",\"certificatePath\":\"C:\\\\certs\\\\missing.cer\"}");

            Assert.That(result, Does.Contain("\"success\":false"));
            Assert.That(result, Does.Contain("file not found"));
            Assert.That(result, Does.Contain(certPath.Replace("\\", "\\\\")));
        }

        [Test]
        public async Task TestConnectionAsync_ServiceReturnsFailure_PropagatesError()
        {
            _settingsManagerMock.Setup(m => m.RequestTimeoutSeconds).Returns(30);
            _testConnectionServiceMock
                .Setup(m => m.TestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(TestConnectionResult.Fail("TLS handshake failed: boom"));

            var result = await _controller.TestConnectionAsync(
                "{\"provider\":\"openai\",\"url\":\"https://api.openai.com\"}");

            Assert.That(result, Does.Contain("\"success\":false"));
            Assert.That(result, Does.Contain("TLS handshake failed"));
        }

        [Test]
        public async Task TestConnectionAsync_MissingProviderOrUrl_ReturnsError()
        {
            _settingsManagerMock.Setup(m => m.RequestTimeoutSeconds).Returns(30);

            var result = await _controller.TestConnectionAsync("{\"provider\":\"\",\"url\":\"\"}");

            Assert.That(result, Does.Contain("\"success\":false"));
            Assert.That(result, Does.Contain("Provider and URL are required"));
            _testConnectionServiceMock.Verify(
                m => m.TestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task TestConnectionAsync_NullPayload_ReturnsError()
        {
            var result = await _controller.TestConnectionAsync(null);

            Assert.That(result, Does.Contain("\"success\":false"));
            _testConnectionServiceMock.Verify(
                m => m.TestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}