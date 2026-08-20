using System.Net.Http;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.HttpWrapper;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.Security;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Tests.Unit.Infrastructure;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Internal
{
    [TestFixture]
    public class LMStudioClientTests
    {
        [TestCase("{\"error\":{\"message\":\"Something went wrong\"}}", "Something went wrong")]
        [TestCase("{\"error\":{}}", "{}")]
        [TestCase("{}", "{}")]
        [TestCase("", "Empty response")]
        [TestCase("invalid json", "invalid json")]
        public void ParseErrorBody_HandlesVariousInputs(string raw, string expected)
        {
            var result = ApiErrorParser.ParseErrorBody(raw);
            Assert.That(result.Message, Is.EqualTo(expected));
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
            var mockToolFactory = new Mock<ICompositeToolFactory>();

            // Act
            var client = new OpenApiAdapter(mockHttpClientWrapper, mockSettingsManager.Object, new ApiRequestBuilder(mockSettingsManager.Object, mockToolFactory.Object), new Mock<ITemporaryHttpClientFactory>().Object);

            // Assert
            Assert.That(client, Is.Not.Null);
            Assert.That(client, Is.InstanceOf<IOpenApiAdapter>());
        }
    }
}
