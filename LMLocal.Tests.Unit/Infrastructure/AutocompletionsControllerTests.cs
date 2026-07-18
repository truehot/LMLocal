using System;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Autocompletions;
using LMLocal.Application.ModelsList;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Autocompletions;
using LMLocal.Infrastructure.LlmApi.Responses;
using LMLocal.Infrastructure.WebView.Controllers;
using LMLocal.Models;
using Newtonsoft.Json.Linq;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class AutocompletionsControllerTests
    {
        private Mock<IAutocompletionsConfigManager> _configManagerMock;
        private Mock<IAutocompletionsService> _serviceMock;
        private Mock<IModelsListService> _modelsListServiceMock;
        private AutocompletionsController _controller;

        [SetUp]
        public void SetUp()
        {
            _configManagerMock = new Mock<IAutocompletionsConfigManager>();
            _serviceMock = new Mock<IAutocompletionsService>();
            _modelsListServiceMock = new Mock<IModelsListService>();
            _controller = new AutocompletionsController(
                _configManagerMock.Object,
                _serviceMock.Object,
                _modelsListServiceMock.Object);
        }

        [TearDown]
        public void TearDown()
        {
            _controller = null;
            _configManagerMock = null;
            _serviceMock = null;
            _modelsListServiceMock = null;
        }

        // =========================================================================
        // GetConfigAsync
        // =========================================================================

        [Test]
        public async Task GetConfigAsync_ReturnsConfigJson()
        {
            var config = new AutocompletionsConfig
            {
                Enabled = true,
                ProviderId = 1,
                ProviderType = "ollama",
                ModelId = "codellama"
            };
            _configManagerMock
                .Setup(m => m.GetAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(config);

            var result = await _controller.GetConfigAsync();

            Assert.That(result, Is.Not.Null.Or.Empty);
            var deserialized = result.FromJson<AutocompletionsConfig>();
            Assert.That(deserialized, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(deserialized.Enabled, Is.True);
                Assert.That(deserialized.ProviderId, Is.EqualTo(1));
                Assert.That(deserialized.ProviderType, Is.EqualTo("ollama"));
                Assert.That(deserialized.ModelId, Is.EqualTo("codellama"));
            });
        }

        [Test]
        public async Task GetConfigAsync_WhenThrows_ReturnsEmptyJson()
        {
            _configManagerMock
                .Setup(m => m.GetAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("storage failure"));

            var result = await _controller.GetConfigAsync();

            Assert.That(result, Is.EqualTo("{}"));
        }

        // =========================================================================
        // UpdateConfigAsync
        // =========================================================================

        [Test]
        public async Task UpdateConfigAsync_ValidJson_ReturnsTrue()
        {
            var json = new AutocompletionsConfig
            {
                Enabled = false,
                ProviderId = 2,
                ProviderType = "openai",
                ModelId = "gpt-4"
            }.ToJson();

            _configManagerMock
                .Setup(m => m.UpdateAsync(It.IsAny<AutocompletionsConfig>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var result = await _controller.UpdateConfigAsync(json);

            Assert.That(result, Is.True);
            _configManagerMock.Verify(
                m => m.UpdateAsync(It.Is<AutocompletionsConfig>(c =>
                    c.Enabled == false &&
                    c.ProviderId == 2 &&
                    c.ProviderType == "openai" &&
                    c.ModelId == "gpt-4"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public async Task UpdateConfigAsync_NullOrEmpty_ReturnsFalse(string json)
        {
            var result = await _controller.UpdateConfigAsync(json);

            Assert.That(result, Is.False);
            _configManagerMock.Verify(
                m => m.UpdateAsync(It.IsAny<AutocompletionsConfig>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task UpdateConfigAsync_InvalidJson_ReturnsFalse()
        {
            var result = await _controller.UpdateConfigAsync("{not valid json}");

            Assert.That(result, Is.False);
            _configManagerMock.Verify(
                m => m.UpdateAsync(It.IsAny<AutocompletionsConfig>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task UpdateConfigAsync_WhenThrows_ReturnsFalse()
        {
            var json = new AutocompletionsConfig { Enabled = true }.ToJson();
            _configManagerMock
                .Setup(m => m.UpdateAsync(It.IsAny<AutocompletionsConfig>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("update failed"));

            var result = await _controller.UpdateConfigAsync(json);

            Assert.That(result, Is.False);
        }

        // =========================================================================
        // GetCompletionAsync
        // =========================================================================

        [Test]
        public async Task GetCompletionAsync_ValidParams_ReturnsCompletionText()
        {
            var parameters = new CompletionParameters
            {
                Prompt = "function add(a, b) {",
                Suffix = "return a + b; }",
                MaxTokens = 50,
                Temperature = 0.1
            };
            var json = parameters.ToJson();
            var expectedCompletion = "\n  return a + b;\n}";

            _serviceMock
                .Setup(m => m.GetCompletionAsync(
                    It.Is<CompletionParameters>(p =>
                        p.Prompt == "function add(a, b) {" &&
                        p.Suffix == "return a + b; }" &&
                        p.MaxTokens == 50 &&
                        Math.Abs(p.Temperature - 0.1) < 0.001),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedCompletion);

            var result = await _controller.GetCompletionAsync(json);

            Assert.That(result, Is.EqualTo(expectedCompletion));
        }

        [Test]
        public async Task GetCompletionAsync_CompletionContainsReturn_IndicatesCorrectFimOutput()
        {
            var parameters = new CompletionParameters
            {
                Prompt = "function add(a, b) {",
                Suffix = "return a + b; }",
                MaxTokens = 50,
                Temperature = 0.1
            };
            _serviceMock
                .Setup(m => m.GetCompletionAsync(It.IsAny<CompletionParameters>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("\n  return a + b;\n}");

            var result = await _controller.GetCompletionAsync(parameters.ToJson());

            Assert.That(result, Does.Contain("return"));
            Assert.That(result, Does.StartWith("\n"));
            Assert.That(result, Does.EndWith("\n}"));
        }

        [Test]
        public async Task GetCompletionAsync_NullParams_ReturnsEmptyString()
        {
            var result = await _controller.GetCompletionAsync(null);

            Assert.That(result, Is.Empty);
            _serviceMock.Verify(
                m => m.GetCompletionAsync(It.IsAny<CompletionParameters>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task GetCompletionAsync_InvalidJson_ReturnsEmptyString()
        {
            var result = await _controller.GetCompletionAsync("{invalid}");

            Assert.That(result, Is.Empty);
            _serviceMock.Verify(
                m => m.GetCompletionAsync(It.IsAny<CompletionParameters>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task GetCompletionAsync_WhenThrows_ReturnsEmptyString()
        {
            var parameters = new CompletionParameters
            {
                Prompt = "test",
                Suffix = "",
                MaxTokens = 10,
                Temperature = 0.0
            };
            _serviceMock
                .Setup(m => m.GetCompletionAsync(It.IsAny<CompletionParameters>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("LLM error"));

            var result = await _controller.GetCompletionAsync(parameters.ToJson());

            Assert.That(result, Is.Empty);
        }

        // =========================================================================
        // ListModelsForProviderAsync
        // =========================================================================

        [Test]
        public async Task ListModelsForProviderAsync_ValidParams_ReturnsModelListJson()
        {
            var parameters = new ListModelsParameters
            {
                ProviderType = "ollama",
                BaseUrl = "http://localhost:11434",
                ApiKey = ""
            };
            var modelsResponse = new UnifiedListModelsResponse
            {
                Models = new System.Collections.Generic.List<UnifiedModelInfo>
                {
                    new UnifiedModelInfo { Id = "codellama", Name = "Code Llama", IsLoaded = true }
                }
            };

            _modelsListServiceMock
                .Setup(m => m.ListModelsForProviderAsync(
                    "ollama", "http://localhost:11434", "",
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(modelsResponse);

            var result = await _controller.ListModelsForProviderAsync(parameters.ToJson());

            Assert.That(result, Is.Not.Null.Or.Empty);
            var deserialized = result.FromJson<UnifiedListModelsResponse>();
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized.Models, Has.Count.EqualTo(1));
            Assert.That(deserialized.Models[0].Id, Is.EqualTo("codellama"));
            Assert.That(deserialized.Models[0].Name, Is.EqualTo("Code Llama"));
            Assert.That(deserialized.Models[0].IsLoaded, Is.True);
        }

        [Test]
        public async Task ListModelsForProviderAsync_InvalidJson_ReturnsErrorJson()
        {
            var result = await _controller.ListModelsForProviderAsync("{broken");

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Contain("Error"));
        }

        [Test]
        public async Task ListModelsForProviderAsync_WhenThrows_ReturnsErrorJson()
        {
            var parameters = new ListModelsParameters
            {
                ProviderType = "openai",
                BaseUrl = "https://api.openai.com",
                ApiKey = "sk-test"
            };

            _modelsListServiceMock
                .Setup(m => m.ListModelsForProviderAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("network error"));

            var result = await _controller.ListModelsForProviderAsync(parameters.ToJson());

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Contain("Error"));
        }

        [Test]
        public async Task ListModelsForProviderAsync_WhenResponseIsNull_ReturnsEmptyJson()
        {
            var parameters = new ListModelsParameters
            {
                ProviderType = "lmstudio",
                BaseUrl = "http://localhost:1234",
                ApiKey = ""
            };

            _modelsListServiceMock
                .Setup(m => m.ListModelsForProviderAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((UnifiedListModelsResponse)null);

            var result = await _controller.ListModelsForProviderAsync(parameters.ToJson());

            Assert.That(result, Is.EqualTo("{}"));
        }

        // =========================================================================
        // TestCompletionAsync
        // =========================================================================

        [Test]
        public async Task TestCompletionAsync_ValidRequest_ReturnsSuccessWithData()
        {
            var request = new TestCompletionRequest
            {
                ProviderType = "lmstudio",
                BaseUrl = "http://localhost:1234",
                ApiKey = "",
                ModelId = "ibm/granite-4-micro"
            };

            _serviceMock
                .Setup(m => m.TestCompletionAsync(
                    "lmstudio", "http://localhost:1234", "", "ibm/granite-4-micro",
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((true, "  return a + b;\n"));

            var result = await _controller.TestCompletionAsync(request.ToJson());

            Assert.That(result, Is.Not.Null.Or.Empty);
            var parsed = JObject.Parse(result);
            Assert.That((bool)parsed["success"], Is.True);
            Assert.That((string)parsed["data"], Is.EqualTo("  return a + b;\n"));
        }

        [Test]
        public async Task TestCompletionAsync_EmptyResult_ReturnsSuccessFalse()
        {
            var request = new TestCompletionRequest
            {
                ProviderType = "lmstudio",
                BaseUrl = "http://localhost:1234",
                ModelId = "some-model"
            };

            _serviceMock
                .Setup(m => m.TestCompletionAsync(
                    "lmstudio", "http://localhost:1234", "", "some-model",
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((false, string.Empty));

            var result = await _controller.TestCompletionAsync(request.ToJson());

            var parsed = JObject.Parse(result);
            Assert.That((bool)parsed["success"], Is.False);
            Assert.That((string)parsed["data"], Is.Empty);
        }

        [Test]
        public async Task TestCompletionAsync_PartialResponseWithoutKeywords_ReturnsSuccessFalse()
        {
            var request = new TestCompletionRequest
            {
                ProviderType = "lmstudio",
                BaseUrl = "http://localhost:1234",
                ModelId = "some-model"
            };

            _serviceMock
                .Setup(m => m.TestCompletionAsync(
                    "lmstudio", "http://localhost:1234", "", "some-model",
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((false, "  console.log('hello');\n"));

            var result = await _controller.TestCompletionAsync(request.ToJson());

            var parsed = JObject.Parse(result);
            Assert.That((bool)parsed["success"], Is.False);
            Assert.That((string)parsed["data"], Is.EqualTo("  console.log('hello');\n"));
        }

        [Test]
        public async Task TestCompletionAsync_NullJson_ReturnsError()
        {
            var result = await _controller.TestCompletionAsync(null);

            var parsed = JObject.Parse(result);
            Assert.That((bool)parsed["success"], Is.False);
            Assert.That((string)parsed["error"], Is.Not.Empty);
        }

        [Test]
        public async Task TestCompletionAsync_MissingProviderType_ReturnsError()
        {
            var request = new TestCompletionRequest
            {
                BaseUrl = "http://localhost:1234",
                ModelId = "test-model"
            };

            var result = await _controller.TestCompletionAsync(request.ToJson());

            var parsed = JObject.Parse(result);
            Assert.That((bool)parsed["success"], Is.False);
            Assert.That((string)parsed["error"], Is.EqualTo("Provider type is required"));
        }

        [Test]
        public async Task TestCompletionAsync_WhenThrows_ReturnsError()
        {
            var request = new TestCompletionRequest
            {
                ProviderType = "openai",
                BaseUrl = "https://api.openai.com",
                ApiKey = "sk-test",
                ModelId = "gpt-4"
            };

            _serviceMock
                .Setup(m => m.TestCompletionAsync(
                    "openai", "https://api.openai.com", "sk-test", "gpt-4",
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("connection refused"));

            var result = await _controller.TestCompletionAsync(request.ToJson());

            var parsed = JObject.Parse(result);
            Assert.That((bool)parsed["success"], Is.False);
            Assert.That((string)parsed["error"], Does.Contain("connection refused"));
        }

        // =========================================================================
        // Constructor guard clauses
        // =========================================================================

        [Test]
        public void Constructor_NullConfigManager_ThrowsArgumentNullException()
        {
            Assert.That(() => new AutocompletionsController(null, _serviceMock.Object, _modelsListServiceMock.Object),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configManager"));
        }

        [Test]
        public void Constructor_NullService_ThrowsArgumentNullException()
        {
            Assert.That(() => new AutocompletionsController(_configManagerMock.Object, null, _modelsListServiceMock.Object),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("autocompletionsService"));
        }

        [Test]
        public void Constructor_NullModelsListService_ThrowsArgumentNullException()
        {
            Assert.That(() => new AutocompletionsController(_configManagerMock.Object, _serviceMock.Object, null),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("modelsListService"));
        }
    }
}
