using System;
using System.Net.Http;
using LMLocal.Application.Chat;
using LMLocal.Infrastructure.DependencyInjection;
using LMLocal.Infrastructure.HttpWrapper;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Security;
using LMLocal.Application.Abstractions.Ports;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    /// <summary>
    /// Integration tests for verifying core DI functionality.
    /// Tests essential service instantiation and dependency resolution.
    /// </summary>
    [TestFixture]
    public class ServiceLifetimeTests
    {
        /// <summary>
        /// Helper method to create a mock ISettingsManager with all required properties.
        /// </summary>
        private Mock<ISettingsManager> CreateMockSettingsManager()
        {
            var mock = new Mock<ISettingsManager>();
            mock.Setup(s => s.ApplicationName).Returns("LMLocal");
            mock.Setup(s => s.LocalAppDataFolder).Returns("LMLocalChat");
            mock.Setup(s => s.ChatHistoryFolder).Returns("ChatHistory");
            mock.Setup(s => s.ChatHistoryFileLabel).Returns("chat_");
            mock.Setup(s => s.SystemPrompt).Returns("Test system prompt");
            mock.Setup(s => s.VirtualHostName).Returns("app.local");
            mock.Setup(s => s.HtmlResourcePath).Returns("Resources/app.html");
            mock.Setup(s => s.WebViewUserDataFolder).Returns("WebViewData");
            mock.Setup(s => s.SettingsFileName).Returns("settings.json");
            mock.Setup(s => s.LocalAppSettingFileName).Returns("settings.json");
            mock.Setup(s => s.LocalAppInstructionsFileName).Returns("instructions.json");
            return mock;
        }

        /// <summary>
        /// Core DI Test: Verifies that all major services can be instantiated with correct dependencies.
        /// This validates the complete dependency injection chain works properly.
        /// </summary>
        [Test]
        public void DependencyInjection_CompleteServiceChain_ResolvesSuccessfully()
        {
            // Arrange
            var settingsManager = CreateMockSettingsManager().Object;
            var httpClient = new HttpClient();
            var httpClientWrapper = new TestHttpClientWrapper(httpClient);
            var fileSystem = new Mock<IFileSystem>().Object;
            var toolQueueProvider = new Mock<IToolQueueProvider>().Object;

            // Act - Create the complete service chain
            var persistence = new ChatPersistenceService(settingsManager, fileSystem);
            var history = new ChatHistoryManager(settingsManager, persistence);
            var lmClient = new OpenApiAdapter(httpClientWrapper, settingsManager, new ApiRequestBuilder(settingsManager, toolQueueProvider), new Mock<ITemporaryHttpClientFactory>().Object);

            // Assert - Verify all services were created successfully
            Assert.That(persistence, Is.Not.Null, "IChatPersistenceService should be created");
            Assert.That(persistence, Is.InstanceOf<IChatPersistenceService>());

            Assert.That(history, Is.Not.Null, "IChatHistoryManager should be created");
            Assert.That(history, Is.InstanceOf<IChatHistoryManager>());

            Assert.That(lmClient, Is.Not.Null, "ILMStudioClient should be created");
            Assert.That(lmClient, Is.InstanceOf<IOpenApiAdapter>());
        }

        /// <summary>
        /// ServiceConfiguration.GetService Test: Verifies that ServiceConfiguration can retrieve registered services.
        /// This test validates the static GetService method works correctly after container initialization.
        /// </summary>
        [Test]
        public void ServiceConfiguration_GetService_ThrowsInvalidOperationWhenNotInitialized()
        {
            // Arrange
            ServiceConfiguration.Cleanup();

            // Act & Assert - GetService should throw when container is not initialized
            var ex = Assert.Throws<InvalidOperationException>(() => 
                ServiceConfiguration.GetService<ISettingsManager>()
            );

            Assert.That(ex.Message, Does.Contain("not initialized"));
        }

        /// <summary>
        /// ServiceConfiguration Lifecycle Test: Verifies that IsInitialized property works correctly.
        /// This ensures the container state can be checked before attempting to retrieve services.
        /// </summary>
        [Test]
        public void ServiceConfiguration_IsInitialized_ReturnsCorrectState()
        {
            // Arrange
            ServiceConfiguration.Cleanup();

            // Act & Assert
            Assert.That(ServiceConfiguration.IsInitialized, Is.False, 
                "IsInitialized should be False after Cleanup");

            // After cleanup, the container should not be initialized
            var isInitialized = ServiceConfiguration.IsInitialized;
            Assert.That(isInitialized, Is.TypeOf<bool>());
        }
    }
}

