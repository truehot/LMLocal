using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Api;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.LlmApi.Converter;
using LMLocal.Infrastructure.LlmApi.Provider;
using LMLocal.Infrastructure.LlmApi.Responses;
using LMLocal.Infrastructure.Settings;

namespace LMLocal.Application.ModelsList
{
    /// <summary>
    /// Service for retrieving and managing models list from backend providers.
    /// </summary>
    internal interface IModelsListService
    {
        Task<UnifiedListModelsResponse> ListModelsAsync(string currentActiveModelId, CancellationToken cancellationToken);
        Task<UnifiedListModelsResponse> ListModelsForProviderAsync(string providerType, string baseUrl, string apiKey, CancellationToken cancellationToken);
    }

    internal class ModelsListService : IModelsListService
    {
        private readonly IOpenApiAdapter _openApiAdapter;
        private readonly ISettingsManager _settingsManager;

        public ModelsListService(
            IOpenApiAdapter openApiAdapter,
            ISettingsManager settingsManager)
        {
            _openApiAdapter = openApiAdapter ?? throw new ArgumentNullException(nameof(openApiAdapter));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        }

        public async Task<UnifiedListModelsResponse> ListModelsAsync(string currentActiveModelId, CancellationToken cancellationToken)
        {
            try
            {
                string baseUrl = GetBaseUrl();
                string providerName = _settingsManager.Current?.Provider ?? "lmstudio";
                ModelProvider provider = ProviderResolver.ResolveProvider(providerName);
                string endpoint = ProviderResolver.GetListModelsEndpoint(provider);

                UnifiedListModelsResponse response;
                if (provider == ModelProvider.Ollama)
                {
                    response = await GetOllamaModelsAsync(baseUrl, null, cancellationToken);
                }
                else if (provider == ModelProvider.LlamaCpp)
                {
                    response = await GetLlamaCppModelsAsync(baseUrl, null, cancellationToken);
                }
                else
                {
                    var json = await _openApiAdapter.ListModelsRawAsync(endpoint, baseUrl, null, cancellationToken);
                    response = ModelResponseConverter.ConvertToUnified(json, provider);
                }

                ApplyCurrentActiveModel(response, currentActiveModelId);
                return response;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("ListModelsAsync failed", ex);
                return new UnifiedListModelsResponse { Error = ex.Message };
            }
        }

        private async Task<UnifiedListModelsResponse> GetOllamaModelsAsync(string baseUrl, string apiKey, CancellationToken cancellationToken)
        {
            var activeJson = await _openApiAdapter.ListModelsRawAsync(ApiEndpoints.OllamaRunningModels, baseUrl, apiKey, cancellationToken);
            var allJson = await _openApiAdapter.ListModelsRawAsync(ApiEndpoints.ListModels, baseUrl, apiKey, cancellationToken);

            var activeModelsResponse = ModelResponseConverter.ConvertToUnified(activeJson, ModelProvider.Ollama);
            var allModelsResponse = ModelResponseConverter.ConvertToUnified(allJson, ModelProvider.OpenAi);

            return ModelResponseConverter.MergeOllamaModels(activeModelsResponse, allModelsResponse);
        }

        private async Task<UnifiedListModelsResponse> GetLlamaCppModelsAsync(string baseUrl, string apiKey, CancellationToken cancellationToken)
        {
            var modelsJson = await _openApiAdapter.ListModelsRawAsync(ApiEndpoints.ListModels, baseUrl, apiKey, cancellationToken);
            return ModelResponseConverter.ConvertLlamaCppResponseToUnified(modelsJson);
        }

        private string GetBaseUrl()
        {
            string url = _settingsManager.Current?.LmStudioBaseUrl;
            if (!string.IsNullOrEmpty(url))
                return url.TrimEnd('/');
            return "http://localhost:1234";
        }

        private void ApplyCurrentActiveModel(UnifiedListModelsResponse response, string currentActiveModelId)
        {
            if (response?.Models == null || response.Models.Count == 0)
                return;

            if (string.IsNullOrEmpty(currentActiveModelId))
                return;

            var currentModel = response.Models.FirstOrDefault(m => m.Id == currentActiveModelId);
            if (currentModel != null)
            {
                response.ActiveModel = currentModel;
                response.HasActiveModel = true;
            }
        }

        public async Task<UnifiedListModelsResponse> ListModelsForProviderAsync(string providerType, string baseUrl, string apiKey, CancellationToken cancellationToken)
        {
            try
            {
                ModelProvider provider = ProviderResolver.ResolveProvider(providerType);

                UnifiedListModelsResponse response;
                if (provider == ModelProvider.Ollama)
                {
                    response = await GetOllamaModelsAsync(baseUrl, apiKey, cancellationToken);
                }
                else if (provider == ModelProvider.LlamaCpp)
                {
                    response = await GetLlamaCppModelsAsync(baseUrl, apiKey, cancellationToken);
                }
                else
                {
                    string endpoint = ProviderResolver.GetListModelsEndpoint(provider);
                    var json = await _openApiAdapter.ListModelsRawAsync(endpoint, baseUrl, apiKey, cancellationToken);
                    response = ModelResponseConverter.ConvertToUnified(json, provider);
                }

                return response;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("ListModelsForProviderAsync failed", ex);
                return new UnifiedListModelsResponse { Error = ex.Message };
            }
        }
    }
}
