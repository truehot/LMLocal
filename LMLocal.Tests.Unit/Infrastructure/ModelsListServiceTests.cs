using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.ModelsList;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.LlmApi.Responses;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Infrastructure.ModelsConfig;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    /// <summary>
    /// Tests for the real ModelsListService implementation.
    /// These verify that ListModelsForProviderAsync picks the correct
    /// provider-specific code path (Ollama merge, llama.cpp converter,
    /// or the standard OpenAI-compatible endpoint) instead of always
    /// using a single generic path.
    /// </summary>
    [TestFixture]
    public class ModelsListServiceTests
    {
        private const string OllamaBaseUrl = "http://localhost:11434";

        private const string OllamaPsJson = @"{
            ""models"": [
                {
                    ""name"": ""codellama:latest"",
                    ""model"": ""codellama:latest"",
                    ""size"": 3825471775,
                    ""details"": { ""families"": [ ""llama"" ] }
                }
            ]
        }";

        private const string OllamaV1ModelsJson = @"{
            ""object"": ""list"",
            ""data"": [
                { ""id"": ""codellama:latest"", ""object"": ""model"", ""owned_by"": ""ollama"" },
                { ""id"": ""llama3:8b"", ""object"": ""model"", ""owned_by"": ""ollama"" }
            ]
        }";

        private const string LlamaCppJson = @"{
            ""object"": ""list"",
            ""data"": [
                {
                    ""id"": ""/models/llama-2-7b.gguf"",
                    ""object"": ""model"",
                    ""meta"": { ""n_ctx"": 4096, ""size"": 3825471775 }
                }
            ]
        }";

        private const string OpenAiJson = @"{
            ""object"": ""list"",
            ""data"": [
                { ""id"": ""gpt-4o"", ""object"": ""model"", ""owned_by"": ""openai"" }
            ]
        }";

        private const string LmStudioJson = @"{
            ""models"": [
                {
                    ""type"": ""llm"",
                    ""key"": ""model-key"",
                    ""display_name"": ""Model Name"",
                    ""loaded_instances"": [ { ""id"": ""inst1"" } ]
                }
            ]
        }";

        private static ModelsListService CreateService(
            Mock<IOpenApiAdapter> adapter,
            string settingsBaseUrl = "http://localhost:1234",
            string provider = null,
            int? providerId = null,
            Mock<IModelsConfigManager> modelsConfigManager = null)
        {
            var settings = new Mock<ISettingsManager>();
            settings.Setup(s => s.Current).Returns(new AppSettings
            {
                LmStudioBaseUrl = settingsBaseUrl,
                Provider = provider,
                ProviderId = providerId
            });

            if (modelsConfigManager == null)
            {
                modelsConfigManager = new Mock<IModelsConfigManager>();
                modelsConfigManager
                    .Setup(m => m.GetAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new ModelsConfigFile { Models = new System.Collections.Generic.List<ModelDefinition>() });
            }

            return new ModelsListService(adapter.Object, settings.Object, modelsConfigManager.Object);
        }

        private static Mock<IOpenApiAdapter> CreateAdapter(Func<string, string> responder)
        {
            var adapter = new Mock<IOpenApiAdapter>();
            adapter
                .Setup(a => a.ListModelsRawAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
                .Returns<string, string, string, CancellationToken, string>((endpoint, baseUrl, apiKey, ct, certificatePath) =>
                    Task.FromResult(responder(endpoint)));
            return adapter;
        }

        // =====================================================================
        // ListModelsForProviderAsync — provider-specific branching
        // =====================================================================

        [Test]
        public async Task ListModelsForProviderAsync_Ollama_MergesRunningAndAvailableModels()
        {
            var adapter = CreateAdapter(endpoint =>
                endpoint == "/api/ps" ? OllamaPsJson : OllamaV1ModelsJson);
            var service = CreateService(adapter);

            var result = await service.ListModelsForProviderAsync("ollama", OllamaBaseUrl, "", CancellationToken.None);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Error, Is.Null);
            Assert.That(result.SupportsIsLoaded, Is.True);
            Assert.That(result.Models, Is.Not.Null);
            Assert.That(result.Models.Count, Is.EqualTo(2));

            var loaded = result.Models.First(m => m.Id == "codellama:latest");
            Assert.That(loaded.IsLoaded, Is.True);

            var unloaded = result.Models.First(m => m.Id == "llama3:8b");
            Assert.That(unloaded.IsLoaded, Is.False);

            // Both Ollama endpoints must be hit — proves the provider-specific branch,
            // not the old single "/api/ps" path.
            adapter.Verify(a => a.ListModelsRawAsync("/api/ps", OllamaBaseUrl, "", It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Once);
            adapter.Verify(a => a.ListModelsRawAsync("/v1/models", OllamaBaseUrl, "", It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Once);
        }

        [Test]
        public async Task ListModelsForProviderAsync_Ollama_WithApiKey_PassesApiKeyToBothCalls()
        {
            const string apiKey = "ollama-key";
            var adapter = CreateAdapter(endpoint =>
                endpoint == "/api/ps" ? OllamaPsJson : OllamaV1ModelsJson);
            var service = CreateService(adapter);

            await service.ListModelsForProviderAsync("ollama", OllamaBaseUrl, apiKey, CancellationToken.None);

            adapter.Verify(a => a.ListModelsRawAsync("/api/ps", OllamaBaseUrl, apiKey, It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Once);
            adapter.Verify(a => a.ListModelsRawAsync("/v1/models", OllamaBaseUrl, apiKey, It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Once);
        }

        [Test]
        public async Task ListModelsForProviderAsync_LlamaCpp_UsesLlamaCppConverter()
        {
            var adapter = CreateAdapter(_ => LlamaCppJson);
            var service = CreateService(adapter);

            var result = await service.ListModelsForProviderAsync("llamacpp", "http://localhost:8080", "", CancellationToken.None);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Models, Has.Count.EqualTo(1));
            Assert.That(result.Models[0].Id, Is.EqualTo("/models/llama-2-7b.gguf"));
            Assert.That(result.Models[0].Name, Is.EqualTo("llama-2-7b.gguf"));
            Assert.That(result.Models[0].IsLoaded, Is.True);
            Assert.That(result.Models[0].MaxTokens, Is.EqualTo(4096));
            Assert.That(result.SupportsIsLoaded, Is.True);

            adapter.Verify(a => a.ListModelsRawAsync("/v1/models", "http://localhost:8080", "", It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Once);
        }

        [Test]
        public async Task ListModelsForProviderAsync_OpenAi_UsesStandardListEndpoint()
        {
            const string baseUrl = "https://api.openai.com";
            const string apiKey = "sk-test";
            var adapter = CreateAdapter(_ => OpenAiJson);
            var service = CreateService(adapter);

            var result = await service.ListModelsForProviderAsync("openai", baseUrl, apiKey, CancellationToken.None);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Models, Has.Count.EqualTo(1));
            Assert.That(result.Models[0].Id, Is.EqualTo("gpt-4o"));
            Assert.That(result.SupportsIsLoaded, Is.False);

            adapter.Verify(a => a.ListModelsRawAsync("/v1/models", baseUrl, apiKey, It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Once);
        }

        [Test]
        public async Task ListModelsForProviderAsync_LmStudio_UsesLmStudioEndpoint()
        {
            var adapter = CreateAdapter(_ => LmStudioJson);
            var service = CreateService(adapter);

            var result = await service.ListModelsForProviderAsync("lmstudio", "http://localhost:1234", "", CancellationToken.None);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Models, Has.Count.EqualTo(1));
            Assert.That(result.Models[0].Id, Is.EqualTo("inst1"));
            Assert.That(result.Models[0].IsLoaded, Is.True);
            Assert.That(result.SupportsIsLoaded, Is.True);

            adapter.Verify(a => a.ListModelsRawAsync("/api/v1/models", "http://localhost:1234", "", It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Once);
        }

        [Test]
        public async Task ListModelsForProviderAsync_AdapterThrows_ReturnsErrorResponse()
        {
            var adapter = new Mock<IOpenApiAdapter>();
            adapter
                .Setup(a => a.ListModelsRawAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("network error"));
            var service = CreateService(adapter);

            var result = await service.ListModelsForProviderAsync("openai", "https://api.openai.com", "sk-test", CancellationToken.None);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Error, Does.Contain("network error"));
            Assert.That(result.Models, Is.Empty);
        }

        // =====================================================================
        // Regression: ListModelsAsync must keep its own provider branching
        // =====================================================================

        [Test]
        public async Task ListModelsAsync_Ollama_MergesUsingSettingsBaseUrlAndSetsActiveModel()
        {
            const string settingsBaseUrl = "http://localhost:1234";
            var adapter = CreateAdapter(endpoint =>
                endpoint == "/api/ps" ? OllamaPsJson : OllamaV1ModelsJson);
            var service = CreateService(adapter, settingsBaseUrl, "ollama");

            var result = await service.ListModelsAsync("codellama:latest", CancellationToken.None);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Error, Is.Null);
            Assert.That(result.HasActiveModel, Is.True);
            Assert.That(result.ActiveModel?.Id, Is.EqualTo("codellama:latest"));
            Assert.That(result.Models.Count, Is.EqualTo(2));

            // ListModelsAsync falls back to the saved settings base URL and null api key
            adapter.Verify(a => a.ListModelsRawAsync("/api/ps", settingsBaseUrl, null, It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Once);
            adapter.Verify(a => a.ListModelsRawAsync("/v1/models", settingsBaseUrl, null, It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Once);
        }

        // =====================================================================
        // Enrichment (models.config.json overrides) — ListModelsAsync
        // =====================================================================

        private static Mock<IModelsConfigManager> ConfigManagerWith(params ModelDefinition[] profiles)
        {
            var manager = new Mock<IModelsConfigManager>();
            manager
                .Setup(m => m.GetAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ModelsConfigFile { Models = profiles.ToList() });
            return manager;
        }

        [Test]
        public async Task ListModelsAsync_OverrideContextLength_WinsOverKnownModelContexts()
        {
            var adapter = CreateAdapter(_ => OpenAiJson); // gpt-4o
            var manager = ConfigManagerWith(new ModelDefinition
            {
                Id = 1,
                ModelId = "gpt-4o",
                ProviderType = "openai",
                ContextLength = 123456
            });
            var service = CreateService(adapter, "https://api.openai.com", "openai", null, manager);

            var result = await service.ListModelsAsync(null, CancellationToken.None);

            Assert.That(result.Error, Is.Null);
            var model = result.Models.First(m => m.Id == "gpt-4o");
            Assert.That(model.MaxTokens, Is.EqualTo(123456));
            Assert.That(model.SupportsMaxTokens, Is.True);
        }

        [Test]
        public async Task ListModelsAsync_OverrideDisplayName_ChangesName()
        {
            var adapter = CreateAdapter(_ => OpenAiJson); // gpt-4o
            var manager = ConfigManagerWith(new ModelDefinition
            {
                Id = 1,
                ModelId = "gpt-4o",
                ProviderType = "openai",
                DisplayName = "My GPT-4o"
            });
            var service = CreateService(adapter, "https://api.openai.com", "openai", null, manager);

            var result = await service.ListModelsAsync(null, CancellationToken.None);

            var model = result.Models.First(m => m.Id == "gpt-4o");
            Assert.That(model.Name, Is.EqualTo("My GPT-4o"));
        }

        [Test]
        public async Task ListModelsAsync_CustomModel_IsAppendedWhenMissing()
        {
            var adapter = CreateAdapter(_ => OpenAiJson); // only gpt-4o
            var manager = ConfigManagerWith(new ModelDefinition
            {
                Id = 2,
                ModelId = "my-manual-model",
                ProviderType = "openai",
                DisplayName = "Manual Model",
                ContextLength = 4096,
                IsCustom = true
            });
            var service = CreateService(adapter, "https://api.openai.com", "openai", null, manager);

            var result = await service.ListModelsAsync(null, CancellationToken.None);

            Assert.That(result.Models.Any(m => m.Id == "my-manual-model"), Is.True);
            var custom = result.Models.First(m => m.Id == "my-manual-model");
            Assert.That(custom.Name, Is.EqualTo("Manual Model"));
            Assert.That(custom.MaxTokens, Is.EqualTo(4096));
            Assert.That(custom.SupportsMaxTokens, Is.True);
        }

        [Test]
        public async Task ListModelsAsync_NonCustomProfile_DoesNotAppendUnknownModel()
        {
            var adapter = CreateAdapter(_ => OpenAiJson); // only gpt-4o
            var manager = ConfigManagerWith(new ModelDefinition
            {
                Id = 3,
                ModelId = "unknown-model",
                ProviderType = "openai",
                IsCustom = false
            });
            var service = CreateService(adapter, "https://api.openai.com", "openai", null, manager);

            var result = await service.ListModelsAsync(null, CancellationToken.None);

            Assert.That(result.Models.Any(m => m.Id == "unknown-model"), Is.False);
            Assert.That(result.Models.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task ListModelsAsync_ProfileOfAnotherProviderType_IsIgnored()
        {
            var adapter = CreateAdapter(_ => OpenAiJson); // gpt-4o, provider=openai
            var manager = ConfigManagerWith(new ModelDefinition
            {
                Id = 4,
                ModelId = "gpt-4o",
                ProviderType = "ollama", // different provider
                ContextLength = 999999
            });
            var service = CreateService(adapter, "https://api.openai.com", "openai", null, manager);

            var result = await service.ListModelsAsync(null, CancellationToken.None);

            var model = result.Models.First(m => m.Id == "gpt-4o");
            Assert.That(model.MaxTokens, Is.Not.EqualTo(999999));
        }

        [Test]
        public async Task ListModelsAsync_ProfileOfAnotherProviderId_IsIgnored()
        {
            var adapter = CreateAdapter(_ => OpenAiJson); // gpt-4o, provider=openai
            var manager = ConfigManagerWith(new ModelDefinition
            {
                Id = 5,
                ModelId = "gpt-4o",
                ProviderType = "openai",
                ProviderId = 42, // current ProviderId is null
                ContextLength = 888888
            });
            var service = CreateService(adapter, "https://api.openai.com", "openai", null, manager);

            var result = await service.ListModelsAsync(null, CancellationToken.None);

            var model = result.Models.First(m => m.Id == "gpt-4o");
            Assert.That(model.MaxTokens, Is.Not.EqualTo(888888));
        }

        [Test]
        public async Task ListModelsAsync_DisabledProfile_IsIgnored()
        {
            var adapter = CreateAdapter(_ => OpenAiJson); // gpt-4o
            var manager = ConfigManagerWith(new ModelDefinition
            {
                Id = 6,
                ModelId = "gpt-4o",
                ProviderType = "openai",
                ContextLength = 777777,
                Enabled = false
            });
            var service = CreateService(adapter, "https://api.openai.com", "openai", null, manager);

            var result = await service.ListModelsAsync(null, CancellationToken.None);

            var model = result.Models.First(m => m.Id == "gpt-4o");
            Assert.That(model.MaxTokens, Is.Not.EqualTo(777777));
        }

        [Test]
        public async Task ListModelsAsync_EmptyConfig_DoesNotChangeAnything()
        {
            var adapter = CreateAdapter(_ => OpenAiJson);
            var manager = ConfigManagerWith();
            var service = CreateService(adapter, "https://api.openai.com", "openai", null, manager);

            var result = await service.ListModelsAsync(null, CancellationToken.None);

            Assert.That(result.Models, Has.Count.EqualTo(1));
            Assert.That(result.Models[0].Id, Is.EqualTo("gpt-4o"));
        }

        [Test]
        public async Task ListModelsAsync_ConfigManagerThrows_StillReturnsProviderModels()
        {
            var adapter = CreateAdapter(_ => OpenAiJson);
            var manager = new Mock<IModelsConfigManager>();
            manager
                .Setup(m => m.GetAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("config error"));
            var service = CreateService(adapter, "https://api.openai.com", "openai", null, manager);

            var result = await service.ListModelsAsync(null, CancellationToken.None);

            Assert.That(result.Models, Has.Count.EqualTo(1));
            Assert.That(result.Models[0].Id, Is.EqualTo("gpt-4o"));
        }
    }
}







