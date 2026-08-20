using System;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.ModelsList;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.Security;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class TestConnectionServiceTests
    {
        private static TestConnectionService CreateService(Mock<IOpenApiAdapter> adapter)
        {
            return new TestConnectionService(adapter.Object, new TestConnectionErrorClassifier());
        }

        private static Mock<IOpenApiAdapter> CreateAdapterOk()
        {
            var adapter = new Mock<IOpenApiAdapter>();
            adapter
                .Setup(a => a.ListModelsRawAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
                .ReturnsAsync("{}");
            return adapter;
        }

        [Test]
        public void Constructor_NullAdapter_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new TestConnectionService(null, new TestConnectionErrorClassifier()));
        }

        [Test]
        public void Constructor_NullClassifier_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new TestConnectionService(Mock.Of<IOpenApiAdapter>(), null));
        }

        [Test]
        public async Task TestAsync_EmptyBaseUrl_ReturnsFailureWithoutAdapterCall()
        {
            var adapter = new Mock<IOpenApiAdapter>();
            var service = CreateService(adapter);

            var result = await service.TestAsync("openai", "", "", null, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("Base URL"));
            adapter.Verify(a => a.ListModelsRawAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>()),
                Times.Never);
        }

        [Test]
        public async Task TestAsync_WithoutCertificatePath_UsesStandardAdapterPath()
        {
            var adapter = CreateAdapterOk();
            var service = CreateService(adapter);

            var result = await service.TestAsync("openai", "http://localhost:1234", "key", null, CancellationToken.None);

            Assert.That(result.Success, Is.True);
            adapter.Verify(a => a.ListModelsRawAsync(
                "/v1/models", "http://localhost:1234", "key", It.IsAny<CancellationToken>(), It.IsAny<string>()),
                Times.Once);
        }

        [Test]
        public async Task TestAsync_WithCertificatePath_PassesPathToAdapter()
        {
            const string certPath = "C:\\certs\\server.cer";
            var adapter = CreateAdapterOk();
            var service = CreateService(adapter);

            var result = await service.TestAsync("openai", "https://localhost:8443", "", certPath, CancellationToken.None);

            Assert.That(result.Success, Is.True);
            adapter.Verify(a => a.ListModelsRawAsync(
                "/v1/models", "https://localhost:8443", "", It.IsAny<CancellationToken>(), certPath),
                Times.Once);
        }

        [Test]
        public async Task TestAsync_TrimsTrailingSlashFromBaseUrl()
        {
            var adapter = CreateAdapterOk();
            var service = CreateService(adapter);

            await service.TestAsync("openai", "https://api.openai.com/", "sk", null, CancellationToken.None);

            adapter.Verify(a => a.ListModelsRawAsync(
                "/v1/models", "https://api.openai.com", "sk", It.IsAny<CancellationToken>(), It.IsAny<string>()),
                Times.Once);
        }

        [Test]
        public async Task TestAsync_LmStudio_UsesLmStudioEndpoint()
        {
            var adapter = CreateAdapterOk();
            var service = CreateService(adapter);

            await service.TestAsync("lmstudio", "http://localhost:1234", "", null, CancellationToken.None);

            adapter.Verify(a => a.ListModelsRawAsync(
                "/api/v1/models", "http://localhost:1234", "", It.IsAny<CancellationToken>(), It.IsAny<string>()),
                Times.Once);
        }

        [Test]
        public async Task TestAsync_DeepSeek_UsesDeepSeekEndpoint()
        {
            var adapter = CreateAdapterOk();
            var service = CreateService(adapter);

            await service.TestAsync("deepseek", "https://api.deepseek.com", "", null, CancellationToken.None);

            adapter.Verify(a => a.ListModelsRawAsync(
                "/models", "https://api.deepseek.com", "", It.IsAny<CancellationToken>(), It.IsAny<string>()),
                Times.Once);
        }

        [Test]
        public async Task TestAsync_AdapterThrows_ReturnsErrorWithReason()
        {
            var adapter = new Mock<IOpenApiAdapter>();
            adapter
                .Setup(a => a.ListModelsRawAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
                .ThrowsAsync(new HttpRequestException("connection refused"));
            var service = CreateService(adapter);

            var result = await service.TestAsync("openai", "http://localhost:1234", "", null, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("connection refused"));
        }

        [Test]
        public async Task TestAsync_TlsAuthenticationFailure_ClassifiedAsTlsHandshake()
        {
            var auth = new AuthenticationException("The remote certificate is invalid according to the validation procedure.");
            var adapter = new Mock<IOpenApiAdapter>();
            adapter
                .Setup(a => a.ListModelsRawAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
                .ThrowsAsync(new HttpRequestException("SSL connection could not be established.", auth));
            var service = CreateService(adapter);

            var result = await service.TestAsync("openai", "https://localhost:8443", "", null, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("TLS handshake failed"));
            Assert.That(result.Error, Does.Contain("remote certificate is invalid"));
        }

        [Test]
        public async Task TestAsync_Cancellation_ClassifiedAsTimeout()
        {
            var adapter = new Mock<IOpenApiAdapter>();
            adapter
                .Setup(a => a.ListModelsRawAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
                .ThrowsAsync(new TaskCanceledException());
            var service = CreateService(adapter);

            var result = await service.TestAsync("openai", "http://localhost:1234", "", null, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo("Request timed out"));
        }
    }
}
