using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Autocompletions;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.Providers;

namespace LMLocal.Application.Autocompletions
{
    /// <summary>
    /// Service for handling autocomplete (FIM) completion requests.
    /// </summary>
    internal interface IAutocompletionsService
    {
        /// <summary>
        /// Sends a FIM completion request using the stored autocompletion config.
        /// </summary>
        Task<string> GetCompletionAsync(CompletionParameters parameters, CancellationToken cancellationToken);

        /// <summary>
        /// Sends a FIM completion request with an explicit context (bypasses saved config).
        /// </summary>
        Task<string> GetCompletionDirectAsync(CompletionContext context, CancellationToken cancellationToken);

        /// <summary>
        /// Tests FIM completion by sending a fixed prompt to the specified provider/model and validating that the response contains expected code keywords.
        /// </summary>
        Task<(bool Success, string Data)> TestCompletionAsync(
            string providerType, string baseUrl, string apiKey, string modelId,
            CancellationToken cancellationToken);
    }

    internal class AutocompletionsService : IAutocompletionsService
    {
        private const string TestPrompt = "/* Csharp code */ using System;\nnamespace TesteCSharp {\n class Program {\n static void Main(string[] args) {\n public int Add(int one, int two) {";
        private const string TestSuffix = "}";
        private const int TestMaxTokens = 80;
        private const double TestTemperature = 0.5;
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);

        private readonly IAutocompletionsConfigManager _configManager;
        private readonly IProvidersConfigManager _providersConfigManager;
        private readonly IOpenApiAdapter _openApiAdapter;

        public AutocompletionsService(
            IAutocompletionsConfigManager configManager,
            IProvidersConfigManager providersConfigManager,
            IOpenApiAdapter openApiAdapter)
        {
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
            _providersConfigManager = providersConfigManager ?? throw new ArgumentNullException(nameof(providersConfigManager));
            _openApiAdapter = openApiAdapter ?? throw new ArgumentNullException(nameof(openApiAdapter));
        }

        public async Task<string> GetCompletionAsync(CompletionParameters parameters, CancellationToken cancellationToken)
        {
            var config = await _configManager.GetAsync(cancellationToken).ConfigureAwait(false);
            if (!config.Enabled)
                return string.Empty;

            if (string.IsNullOrWhiteSpace(config.ModelId))
            {
                InternalLogger.Warn("AutocompletionsService: modelId is empty, autocomplete skipped");
                return string.Empty;
            }

            var providersConfig = await _providersConfigManager.GetAsync(cancellationToken).ConfigureAwait(false);

            var allProviders = (providersConfig.DefaultProviders ?? Enumerable.Empty<CustomProvider>())
                .Concat(providersConfig.Providers ?? Enumerable.Empty<CustomProvider>());

            var provider = allProviders.FirstOrDefault(p =>
                p.Id == config.ProviderId &&
                string.Equals(p.ProviderType, config.ProviderType, StringComparison.OrdinalIgnoreCase));

            var (prefixToken, suffixToken, middleToken) = FimTemplate.GetTokens(config.ModelId);

            var finalPrompt = string.IsNullOrEmpty(prefixToken)
                ? parameters.Prompt
                : $"{prefixToken}{parameters.Prompt}{suffixToken}{parameters.Suffix}{middleToken}";

            var context = new CompletionContext
            {
                ModelId = config.ModelId,
                Prompt = finalPrompt,
                Suffix = string.Empty,
                MaxTokens = parameters.MaxTokens,
                Temperature = parameters.Temperature,
                Stop = parameters.Stop,
                BaseUrl = provider?.CustomBaseUrl,
                ApiKey = provider?.CustomApiKey,
                ProviderType = config.ProviderType
            };

            var result = await GetCompletionDirectAsync(context, cancellationToken).ConfigureAwait(false);
            return FimTemplate.TrimFimArtifacts(result, config.ModelId);
        }

        public async Task<string> GetCompletionDirectAsync(CompletionContext context, CancellationToken cancellationToken)
        {
            var result = await _openApiAdapter.SendCompletionAsync(context, cancellationToken).ConfigureAwait(false);
            return result ?? string.Empty;
        }

        public async Task<(bool Success, string Data)> TestCompletionAsync(
            string providerType, string baseUrl, string apiKey, string modelId,
            CancellationToken cancellationToken)
        {
            var (prefixToken, suffixToken, middleToken) = FimTemplate.GetTokens(modelId ?? string.Empty);

            var finalPrompt = string.IsNullOrEmpty(prefixToken)
                ? TestPrompt
                : $"{prefixToken}{TestPrompt}{suffixToken}{TestSuffix}{middleToken}";

            var context = new CompletionContext
            {
                ModelId = modelId ?? string.Empty,
                Prompt = finalPrompt,
                Suffix = string.Empty,
                MaxTokens = TestMaxTokens,
                Temperature = TestTemperature,
                Stop = null,
                BaseUrl = baseUrl,
                ApiKey = apiKey,
                ProviderType = providerType
            };

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cts.CancelAfter(TestTimeout);

                try
                {
                    var result = await GetCompletionDirectAsync(context, cts.Token).ConfigureAwait(false);
                    var data = FimTemplate.TrimFimArtifacts(result ?? string.Empty, modelId);

                    var success = !string.IsNullOrEmpty(data)
                                  && data.IndexOf("return", StringComparison.OrdinalIgnoreCase) >= 0
                                  && data.IndexOf("one", StringComparison.OrdinalIgnoreCase) >= 0
                                  && data.IndexOf("two", StringComparison.OrdinalIgnoreCase) >= 0
                                  && data.IndexOf("+", StringComparison.OrdinalIgnoreCase) >= 0;

                    return (success, data);
                }
                catch (OperationCanceledException)
                {
                    InternalLogger.Warn("AutocompletionsService: TestCompletionAsync timed out");
                    return (false, string.Empty);
                }
            }
        }

        public static class FimTemplate
        {
            /// <summary>
            /// Returns the appropriate FIM tokens (prefix, suffix, middle) based on the model ID.
            /// CodeLlama (<PRE>,<SUF>,<MID>) and Codestral ([PREFIX],[SUFFIX],[MIDDLE]) FIM tokens didn't work in my LM Studio properly
            /// </summary>
            public static (string Prefix, string Suffix, string Middle) GetTokens(string modelId)
            {
                if (string.IsNullOrWhiteSpace(modelId))
                    return GetDefaultTokens();

                var modelLower = modelId.ToLowerInvariant();

                if (modelLower.Contains("qwen") || modelLower.Contains("codegemma")) return ("<|fim_prefix|>", "<|fim_suffix|>", "<|fim_middle|>");

                if (modelLower.Contains("starcoder") || modelLower.Contains("refact")) return ("<fim_prefix>", "<fim_suffix>", "<fim_middle>");

                if (modelLower.Contains("stable-code")) return ("<fim_prefix>", "<fim_suffix>", "<fim_middle>");

                if (modelLower.Contains("deepseek")) return ("<｜fim▁begin｜>", "<｜fim▁hole｜>", "<｜fim▁end｜>");

                return GetDefaultTokens();
            }

            /// <summary>
            /// Strips known FIM artifact tokens from the end of the generated output.
            /// </summary>
            public static string TrimFimArtifacts(string output, string modelId)
            {
                if (string.IsNullOrEmpty(output) || string.IsNullOrWhiteSpace(modelId))
                    return output;

                var modelLower = modelId.ToLowerInvariant();

                if (modelLower.Contains("qwen") || modelLower.Contains("codegemma"))
                {
                    var idx = output.IndexOf("<|file_separator|>", StringComparison.Ordinal);
                    if (idx >= 0)
                        return output.Substring(0, idx);
                }

                return output;
            }

            private static (string Prefix, string Suffix, string Middle) GetDefaultTokens() => (string.Empty, string.Empty, string.Empty);
        }
    }
}




