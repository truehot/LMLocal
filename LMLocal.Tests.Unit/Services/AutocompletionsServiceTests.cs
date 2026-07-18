using System.Collections.Generic;

using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Autocompletions;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Autocompletions;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.Providers;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Services
{
    [TestFixture]
    public class AutocompletionsServiceTests
    {
        private Mock<IAutocompletionsConfigManager> _configManagerMock;
        private Mock<IProvidersConfigManager> _providersConfigManagerMock;
        private Mock<IOpenApiAdapter> _openApiAdapterMock;
        private AutocompletionsService _service;

        [SetUp]
        public void SetUp()
        {
            _configManagerMock = new Mock<IAutocompletionsConfigManager>();
            _providersConfigManagerMock = new Mock<IProvidersConfigManager>();
            _openApiAdapterMock = new Mock<IOpenApiAdapter>();
            _service = new AutocompletionsService(
                _configManagerMock.Object,
                _providersConfigManagerMock.Object,
                _openApiAdapterMock.Object);
        }

        [TearDown]
        public void TearDown()
        {
            _service = null;
        }

        // =========================================================================
        // FimTemplate.GetTokens
        // =========================================================================

        [TestCase("qwen-2.5-coder", "<|fim_prefix|>", "<|fim_suffix|>", "<|fim_middle|>")]
        [TestCase("Qwen2.5-Coder-7B", "<|fim_prefix|>", "<|fim_suffix|>", "<|fim_middle|>")]
        [TestCase("codegemma-7b", "<|fim_prefix|>", "<|fim_suffix|>", "<|fim_middle|>")]
        [TestCase("CodeGemma-2b", "<|fim_prefix|>", "<|fim_suffix|>", "<|fim_middle|>")]
        public void GetTokens_QwenAndCodeGemma_ReturnsCorrectFimTokens(
            string modelId, string expectedPrefix, string expectedSuffix, string expectedMiddle)
        {
            var (prefix, suffix, middle) = AutocompletionsService.FimTemplate.GetTokens(modelId);

            Assert.That(prefix, Is.EqualTo(expectedPrefix));
            Assert.That(suffix, Is.EqualTo(expectedSuffix));
            Assert.That(middle, Is.EqualTo(expectedMiddle));
        }

        [TestCase("starcoder2-15b", "<fim_prefix>", "<fim_suffix>", "<fim_middle>")]
        [TestCase("StarCoder2-7B", "<fim_prefix>", "<fim_suffix>", "<fim_middle>")]
        [TestCase("refact-1.6b", "<fim_prefix>", "<fim_suffix>", "<fim_middle>")]
        [TestCase("Refact-3B", "<fim_prefix>", "<fim_suffix>", "<fim_middle>")]
        public void GetTokens_StarCoderAndRefact_ReturnsCorrectFimTokens(
            string modelId, string expectedPrefix, string expectedSuffix, string expectedMiddle)
        {
            var (prefix, suffix, middle) = AutocompletionsService.FimTemplate.GetTokens(modelId);

            Assert.That(prefix, Is.EqualTo(expectedPrefix));
            Assert.That(suffix, Is.EqualTo(expectedSuffix));
            Assert.That(middle, Is.EqualTo(expectedMiddle));
        }

        [TestCase("stable-code-3b", "<fim_prefix>", "<fim_suffix>", "<fim_middle>")]
        [TestCase("stable-code-instruct-3b", "<fim_prefix>", "<fim_suffix>", "<fim_middle>")]
        public void GetTokens_StableCode_ReturnsCorrectFimTokens(
            string modelId, string expectedPrefix, string expectedSuffix, string expectedMiddle)
        {
            var (prefix, suffix, middle) = AutocompletionsService.FimTemplate.GetTokens(modelId);

            Assert.That(prefix, Is.EqualTo(expectedPrefix));
            Assert.That(suffix, Is.EqualTo(expectedSuffix));
            Assert.That(middle, Is.EqualTo(expectedMiddle));
        }

        [TestCase("deepseek-coder-6.7b")]
        [TestCase("DeepSeek-Coder-V2")]
        public void GetTokens_DeepSeek_ReturnsNonEmptyTokens(string modelId)
        {
            var (prefix, suffix, middle) = AutocompletionsService.FimTemplate.GetTokens(modelId);

            Assert.That(prefix, Is.Not.Empty);
            Assert.That(suffix, Is.Not.Empty);
            Assert.That(middle, Is.Not.Empty);
            Assert.That(prefix, Does.StartWith("<").And.EndWith(">"));
            Assert.That(suffix, Does.StartWith("<").And.EndWith(">"));
            Assert.That(middle, Does.StartWith("<").And.EndWith(">"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        [TestCase("unknown-model")]
        [TestCase("llama-3.1-8b")]
        [TestCase("mistral-7b")]
        public void GetTokens_UnknownOrEmptyModelId_ReturnsEmptyTokens(string modelId)
        {
            var (prefix, suffix, middle) = AutocompletionsService.FimTemplate.GetTokens(modelId);

            Assert.That(prefix, Is.Empty);
            Assert.That(suffix, Is.Empty);
            Assert.That(middle, Is.Empty);
        }

        // =========================================================================
        // FimTemplate.TrimFimArtifacts
        // =========================================================================

        [TestCase("qwen-2.5-coder")]
        [TestCase("codegemma-7b")]
        public void TrimFimArtifacts_QwenCodeGemmaWithArtifact_StripsArtifact(string modelId)
        {
            var output = "    return one + two;\n<|file_separator|>extra stuff here";

            var result = AutocompletionsService.FimTemplate.TrimFimArtifacts(output, modelId);

            Assert.That(result, Is.EqualTo("    return one + two;\n"));
            Assert.That(result, Does.Not.Contain("<|file_separator|>"));
        }

        [TestCase("qwen-2.5-coder")]
        [TestCase("codegemma-7b")]
        public void TrimFimArtifacts_QwenCodeGemmaWithoutArtifact_ReturnsUnchanged(string modelId)
        {
            var output = "    return one + two;\n";

            var result = AutocompletionsService.FimTemplate.TrimFimArtifacts(output, modelId);

            Assert.That(result, Is.EqualTo(output));
        }

        [TestCase("starcoder2-15b")]
        [TestCase("deepseek-coder-6.7b")]
        [TestCase("stable-code-3b")]
        [TestCase("llama-3.1-8b")]
        [TestCase("")]
        [TestCase(null)]
        public void TrimFimArtifacts_NonQwenModels_ReturnsUnchanged(string modelId)
        {
            var output = "    return one + two;\n<|file_separator|>extra";

            var result = AutocompletionsService.FimTemplate.TrimFimArtifacts(output, modelId);

            Assert.That(result, Is.EqualTo(output));
        }

        [Test]
        public void TrimFimArtifacts_NullOutput_ReturnsNull()
        {
            var result = AutocompletionsService.FimTemplate.TrimFimArtifacts(null, "qwen-2.5-coder");
            Assert.That(result, Is.Null);
        }

        [Test]
        public void TrimFimArtifacts_EmptyOutput_ReturnsEmpty()
        {
            var result = AutocompletionsService.FimTemplate.TrimFimArtifacts(string.Empty, "qwen-2.5-coder");
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void TrimFimArtifacts_WhitespaceModelId_ReturnsUnchanged()
        {
            var output = "    return one + two;\n<|file_separator|>extra";
            var result = AutocompletionsService.FimTemplate.TrimFimArtifacts(output, "  ");
            Assert.That(result, Is.EqualTo(output));
        }

        // =========================================================================
        // Integration: GetCompletionAsync uses TrimFimArtifacts
        // =========================================================================

        [Test]
        public async Task GetCompletionAsync_WithQwenModel_TrimsFimArtifactsFromResult()
        {
            var config = new AutocompletionsConfig
            {
                Enabled = true,
                ModelId = "qwen-2.5-coder",
                ProviderId = 99,
                ProviderType = "openai"
            };
            _configManagerMock
                .Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(config);

            var providersConfig = new ProvidersConfigFile
            {
                DefaultProviders = new List<CustomProvider>
                {
                    new CustomProvider
                    {
                        Id = 99,
                        ProviderType = "openai",
                        CustomBaseUrl = "http://localhost:1234",
                        CustomApiKey = "test-key"
                    }
                }
            };
            _providersConfigManagerMock
                .Setup(p => p.GetAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(providersConfig);

            _openApiAdapterMock
                .Setup(a => a.SendCompletionAsync(It.IsAny<CompletionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("    return one + two;\n<|file_separator|>extra");

            var parameters = new CompletionParameters
            {
                Prompt = "public int Add(int one, int two)",
                Suffix = "}",
                MaxTokens = 80,
                Temperature = 0.5
            };

            var result = await _service.GetCompletionAsync(parameters, CancellationToken.None);

            Assert.That(result, Is.EqualTo("    return one + two;\n"));
            Assert.That(result, Does.Not.Contain("<|file_separator|>"));
        }

        [Test]
        public async Task GetCompletionAsync_WithStarCoderModel_DoesNotStripAnything()
        {
            var config = new AutocompletionsConfig
            {
                Enabled = true,
                ModelId = "starcoder2-15b",
                ProviderId = 99,
                ProviderType = "openai"
            };
            _configManagerMock
                .Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(config);

            var providersConfig = new ProvidersConfigFile
            {
                DefaultProviders = new List<CustomProvider>
                {
                    new CustomProvider
                    {
                        Id = 99,
                        ProviderType = "openai",
                        CustomBaseUrl = "http://localhost:1234",
                        CustomApiKey = "test-key"
                    }
                }
            };
            _providersConfigManagerMock
                .Setup(p => p.GetAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(providersConfig);

            var rawOutput = "    return one + two;\n";
            _openApiAdapterMock
                .Setup(a => a.SendCompletionAsync(It.IsAny<CompletionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(rawOutput);

            var parameters = new CompletionParameters
            {
                Prompt = "public int Add(int one, int two)",
                Suffix = "}",
                MaxTokens = 80,
                Temperature = 0.5
            };

            var result = await _service.GetCompletionAsync(parameters, CancellationToken.None);

            Assert.That(result, Is.EqualTo(rawOutput));
        }
    }
}
