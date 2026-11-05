using System.Net.Http;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.HttpWrapper;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.Security;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Tests.Unit.Infrastructure;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Internal
{
    [TestFixture]
    public class LMStudioClientTests
    {
        [TestCase("{\"error\":{\"message\":\"Something went wrong\"}}", "Something went wrong")]
        [TestCase("", "Empty response")]
        public void ParseErrorBody_HandlesVariousInputs(string raw, string expected)
        {
            var result = ApiErrorParser.ParseErrorBody(raw);
            Assert.That(result.Message, Is.EqualTo(expected));
        }

        [TestCase("{\"error\":{}}", "{\"error\":{}}")]
        [TestCase("{}", "{}")]
        [TestCase("invalid json", "invalid json")]
        public void ParseErrorBody_WithoutUsableMessage_FallsBackToRawBody(string raw, string rawBody)
        {
            // A body with no message yields a generic message and keeps the payload in RawBody, so raw JSON never
            // reaches the user or the overflow detector.
            var result = ApiErrorParser.ParseErrorBody(raw);

            Assert.That(result.Message, Is.Not.Empty);
            Assert.That(result.Message, Is.Not.EqualTo(rawBody), "the raw payload must not become the user-facing message");
            Assert.That(result.RawBody, Is.EqualTo(rawBody));
        }

        /// <summary>
        /// Integration test: Verifies LMStudioClient can be instantiated with dependencies.
        /// Validates that HttpClient and ISettingsManager can be properly injected.
        /// </summary>
        [Test]
        public void DependencyInjection_LMStudioClient_CreatesSuccessfullyWithDependencies()
        {
            // Arrange
            var mockHttpClient = new HttpClient();
            var mockHttpClientWrapper = new TestHttpClientWrapper(mockHttpClient);
            var mockSettingsManager = new Mock<ISettingsManager>();
            mockSettingsManager.Setup(s => s.SystemPrompt).Returns("Test prompt");
            mockSettingsManager.Setup(s => s.VirtualHostName).Returns("app.local");
            var mockToolQueueProvider = new Mock<IToolQueueProvider>();

            // Act
            var client = new OpenApiAdapter(mockHttpClientWrapper, mockSettingsManager.Object, new ApiRequestBuilder(mockSettingsManager.Object, mockToolQueueProvider.Object), new Mock<ITemporaryHttpClientFactory>().Object);

            // Assert
            Assert.That(client, Is.Not.Null);
            Assert.That(client, Is.InstanceOf<IOpenApiAdapter>());
        }
    }
}
