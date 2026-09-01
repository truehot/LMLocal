using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Api;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.LlmApi.Converter;
using LMLocal.Infrastructure.LlmApi.Provider;
using LMLocal.Infrastructure.LlmApi.Responses;
using LMLocal.Infrastructure.ModelsConfig;

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
        private readonly IModelsConfigManager _modelsConfigManager;

        public ModelsListService(
            IOpenApiAdapter openApiAdapter,
            ISettingsManager settingsManager,
            IModelsConfigManager modelsConfigManager)
        {
            _openApiAdapter = openApiAdapter ?? throw new ArgumentNullException(nameof(openApiAdapter));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _modelsConfigManager = modelsConfigManager ?? throw new ArgumentNullException(nameof(modelsConfigManager));
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

                await ApplyModelOverridesAsync(response, providerName, _settingsManager.Current?.ProviderId, cancellationToken).ConfigureAwait(false);
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
        /// <summary>
        /// Applies user-defined model profiles (models.config.json) on top of the models reported
        /// by the provider: overrides context length and display name, and appends custom models
        /// that the provider does not serve. 
        /// </summary>
        private async Task ApplyModelOverridesAsync(
            UnifiedListModelsResponse response,
            string providerType,
            int? providerId,
            CancellationToken cancellationToken)
        {
            if (response == null || response.Models == null)
                return;

            ModelsConfigFile config;
            try
            {
                config = await _modelsConfigManager.GetAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InternalLogger.Error("ApplyModelOverridesAsync failed to load models config: " + ex.Message, ex);
                return;
            }

            if (config == null || config.Models == null || config.Models.Count == 0)
                return;

            var profiles = config.Models
                .Where(m => m != null
                            && m.Enabled
                            && !string.IsNullOrWhiteSpace(m.ModelId)
                            && string.Equals(m.ProviderType, providerType, StringComparison.OrdinalIgnoreCase)
                            && Nullable.Equals(m.ProviderId, providerId))
                .ToList();

            if (profiles.Count == 0)
                return;

            foreach (var profile in profiles)
            {
                var model = response.Models.FirstOrDefault(
                    m => m != null && string.Equals(m.Id, profile.ModelId, StringComparison.Ordinal));

                if (model == null)
                {
                    if (!profile.IsCustom)
                        continue;

                    response.Models.Add(new UnifiedModelInfo
                    {
                        Id = profile.ModelId,
                        Name = profile.DisplayName,
                        MaxTokens = profile.ContextLength,
                        SupportsMaxTokens = profile.ContextLength.HasValue,
                        IsLoaded = false
                    });
                    continue;
                }

                if (profile.ContextLength.HasValue && profile.ContextLength.Value > 0)
                {
                    model.MaxTokens = profile.ContextLength;
                    model.SupportsMaxTokens = true;
                }

                if (!string.IsNullOrWhiteSpace(profile.DisplayName))
                {
                    model.Name = profile.DisplayName;
                }
            }
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
