using System;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Autocompletions;
using LMLocal.Application.ModelsList;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Autocompletions;
using LMLocal.Models;

namespace LMLocal.Infrastructure.WebView.Controllers
{
    public interface IAutocompletionsController
    {
        Task<string> GetConfigAsync();
        Task<bool> UpdateConfigAsync(string configJson);
        Task<string> GetCompletionAsync(string json);
        Task<string> ListModelsForProviderAsync(string json);
        Task<string> TestCompletionAsync(string json);
    }

    [System.Runtime.InteropServices.ComVisible(true)]
    public class AutocompletionsController : IAutocompletionsController
    {
        private readonly IAutocompletionsConfigManager _configManager;
        private readonly IAutocompletionsService _autocompletionsService;
        private readonly IModelsListService _modelsListService;

        internal AutocompletionsController(
            IAutocompletionsConfigManager configManager,
            IAutocompletionsService autocompletionsService,
            IModelsListService modelsListService)
        {
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
            _autocompletionsService = autocompletionsService ?? throw new ArgumentNullException(nameof(autocompletionsService));
            _modelsListService = modelsListService ?? throw new ArgumentNullException(nameof(modelsListService));
        }

        /// <summary>
        /// Returns the current autocomplete configuration as JSON.
        /// </summary>
        public async Task<string> GetConfigAsync()
        {
            try
            {
                var config = await _configManager.GetAsync().ConfigureAwait(false);
                return config.ToJson();
            }
            catch (Exception ex)
            {
                InternalLogger.Error("GetConfigAsync failed", ex);
                return "{}";
            }
        }

        /// <summary>
        /// Updates the autocomplete configuration from JSON.
        /// </summary>
        public async Task<bool> UpdateConfigAsync(string configJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(configJson))
                {
                    return false;
                }

                var config = configJson.FromJson<AutocompletionsConfig>();
                if (config == null)
                {
                    return false;
                }

                await _configManager.UpdateAsync(config).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("UpdateConfigAsync failed", ex);
                return false;
            }
        }

        /// <summary>
        /// Sends a FIM (Fill-In-the-Middle) completion request to the LLM backend and returns the generated ghost text.
        /// </summary>
        public async Task<string> GetCompletionAsync(string json)
        {
            try
            {
                var parameters = json.FromJson<CompletionParameters>();
                if (parameters == null)
                {
                    InternalLogger.Warn("GetCompletionAsync: invalid JSON parameters");
                    return string.Empty;
                }

                var result = await _autocompletionsService.GetCompletionAsync(parameters, CancellationToken.None).ConfigureAwait(false);
                return result ?? string.Empty;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("GetCompletionAsync failed", ex);
                return string.Empty;
            }
        }

        /// <summary>
        /// Lists models for a given provider.
        /// </summary>
        public async Task<string> ListModelsForProviderAsync(string json)
        {
            try
            {
                var parameters = json.FromJson<ListModelsParameters>();
                if (parameters == null)
                {
                    return new { Error = "Invalid JSON parameters" }.ToJson();
                }

                var response = await _modelsListService.ListModelsForProviderAsync(
                    parameters.ProviderType,
                    parameters.BaseUrl ?? string.Empty,
                    parameters.ApiKey ?? string.Empty,
                    CancellationToken.None
                ).ConfigureAwait(false);

                return response == null ? "{}" : response.ToJson();
            }
            catch (Exception ex)
            {
                InternalLogger.Error("ListModelsForProviderAsync failed", ex);
                return new { Error = "Failed to list models: " + ex.Message }.ToJson();
            }
        }

        /// <summary>
        /// Tests the FIM completion by sending a fixed prompt to the specified provider and model.
        /// Returns { success, data } where data is the generated text.
        /// </summary>
        public async Task<string> TestCompletionAsync(string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json))
                    return new { success = false, error = "Invalid parameters" }.ToJson();

                var request = json.FromJson<TestCompletionRequest>();
                if (request == null || string.IsNullOrWhiteSpace(request.ProviderType))
                    return new { success = false, error = "Provider type is required" }.ToJson();

                var (success, data) = await _autocompletionsService.TestCompletionAsync(
                    request.ProviderType,
                    request.BaseUrl ?? string.Empty,
                    request.ApiKey ?? string.Empty,
                    request.ModelId ?? string.Empty,
                    CancellationToken.None
                ).ConfigureAwait(false);

                return new { success, data }.ToJson();
            }
            catch (Exception ex)
            {
                InternalLogger.Error("TestCompletionAsync failed", ex);
                return new { success = false, error = ex.Message }.ToJson();
            }
        }
    }
}
