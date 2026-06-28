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
        Task<bool> TestConnectionAsync(string baseUrl, string providerName, string apiKey, CancellationToken cancellationToken);
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
                    response = await GetOllamaModelsAsync(cancellationToken);
                }
                else if (provider == ModelProvider.LlamaCpp)
                {
                    response = await GetLlamaCppModelsAsync(baseUrl, cancellationToken);
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

        private async Task<UnifiedListModelsResponse> GetOllamaModelsAsync(CancellationToken cancellationToken)
        {
            var activeJson = await _openApiAdapter.ListModelsRawAsync(ApiEndpoints.OllamaRunningModels, null, null, cancellationToken);
            var allJson = await _openApiAdapter.ListModelsRawAsync(ApiEndpoints.ListModels, null, null, cancellationToken);

            var activeModelsResponse = ModelResponseConverter.ConvertToUnified(activeJson, ModelProvider.Ollama);
            var allModelsResponse = ModelResponseConverter.ConvertToUnified(allJson, ModelProvider.OpenAi);

            return ModelResponseConverter.MergeOllamaModels(activeModelsResponse, allModelsResponse);
        }

        private async Task<UnifiedListModelsResponse> GetLlamaCppModelsAsync(string baseUrl, CancellationToken cancellationToken)
        {
            var modelsJson = await _openApiAdapter.ListModelsRawAsync(ApiEndpoints.ListModels, baseUrl, null, cancellationToken);
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

        public async Task<bool> TestConnectionAsync(string baseUrl, string providerName, string apiKey, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(baseUrl))
                    return false;

                baseUrl = baseUrl.TrimEnd('/');

                ModelProvider provider = ProviderResolver.ResolveProvider(providerName);

                await _openApiAdapter.ListModelsRawAsync(ApiEndpoints.ListModels, baseUrl, apiKey, cancellationToken);

                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"TestConnectionAsync failed for {providerName} at {baseUrl}", ex);
                return false;
            }
        }
    }
}