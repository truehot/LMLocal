using System.Net.Http;
using LMLocal.Infrastructure.LlmApi;
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
        [TestCase("{\"error\":{}}", null)]
        [TestCase("{}", null)]
        [TestCase("", null)]
        [TestCase("invalid json", "invalid json")]
        public void TryExtractErrorMessage_HandlesVariousInputs(string raw, string expected)
        {
            string result = OpenApiAdapter.TryExtractErrorMessage(raw);
            Assert.That(result, Is.EqualTo(expected));
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
            var client = new OpenApiAdapter(mockHttpClientWrapper, mockSettingsManager.Object, mockToolFactory.Object);

            // Assert
            Assert.That(client, Is.Not.Null);
            Assert.That(client, Is.InstanceOf<IOpenApiAdapter>());
        }
    }
}
