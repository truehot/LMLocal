using System;
using System.Collections.Generic;
using System.Linq;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.LlmApi.Provider;
using LMLocal.Infrastructure.LlmApi.Responses;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.LlmApi.Converter
{
    /// <summary>
    /// Converts provider-specific API responses to unified model information format.
    /// Handles LM Studio, OpenAI-compatible, and Ollama backends.
    /// </summary>
    internal static class ModelResponseConverter
    {
        /// <summary>
        /// Converts raw JSON response to unified format based on provider type.
        /// </summary>
        public static UnifiedListModelsResponse ConvertToUnified(string json, ModelProvider provider)
        {
            if (string.IsNullOrEmpty(json))
            {
                return new UnifiedListModelsResponse { Models = new List<UnifiedModelInfo>() };
            }

            try
            {
                if (provider == ModelProvider.LmStudio)
                    return ConvertLmStudioResponseToUnified(json);
                else if (provider == ModelProvider.Ollama)
                    return ConvertOllamaResponseToUnified(json);
                else if (provider == ModelProvider.Jan)
                    return ConvertJanResponseToUnified(json);
                else if (provider == ModelProvider.GithubModelsAzure)
                    return ConvertAzureResponseToUnified(json);
                else
                    return ConvertOpenAiResponseToUnified(json);
            }
            catch (JsonException ex)
            {
                InternalLogger.Warn($"ConvertToUnified failed for provider {provider}: {ex.Message}");
                return new UnifiedListModelsResponse { Models = new List<UnifiedModelInfo>() };
            }
        }

        public static UnifiedListModelsResponse ConvertLmStudioResponseToUnified(string json)
        {
            try
            {
                var lmStudioResponse = json.FromJson<LmStudioModelsResponse>();
                if (lmStudioResponse?.Models == null || lmStudioResponse.Models.Count == 0)
                {
                    return new UnifiedListModelsResponse { Error = "No models returned from LM Studio" };
                }

                var models = new List<UnifiedModelInfo>();

                foreach (var model in lmStudioResponse.Models)
                {
                    if (model.Type != "llm")
                        continue;

                    var isLoaded = model.LoadedInstances?.Count > 0;
                    var instance = isLoaded ? model.LoadedInstances[0] : null;
                    var maxTokens = instance?.Config?.ContextLength ?? model.MaxContextLength;

                    models.Add(new UnifiedModelInfo
                    {
                        Id = instance?.Id ?? model.Key,
                        Name = model.DisplayName ?? model.Key,
                        MaxTokens = maxTokens > 0 ? maxTokens : (int?)null,
                        SupportsMaxTokens = maxTokens > 0,
                        IsLoaded = isLoaded,
                        SupportsToolUse = model.Capabilities?.TrainedForToolUse,
                        SizeInBytes = model.SizeBytes > 0 ? model.SizeBytes : (long?)null
                    });
                }

                return new UnifiedListModelsResponse { Models = models };
            }
            catch (JsonException ex)
            {
                InternalLogger.Warn($"ConvertLmStudioResponseToUnified: failed to parse LM Studio response: {ex.Message}");
                return new UnifiedListModelsResponse { Error = "Failed to parse LM Studio response" };
            }
        }

        /// <summary>
        /// Converts Ollama /api/ps response to unified format.
        /// </summary>
        public static UnifiedListModelsResponse ConvertOllamaResponseToUnified(string json)
        {
            try
            {
                var ollamaResponse = json.FromJson<OllamaPsResponse>();
                if (ollamaResponse?.Models == null || ollamaResponse.Models.Count == 0)
                {
                    return new UnifiedListModelsResponse { Error = "No models loaded in Ollama" };
                }

                var result = new UnifiedListModelsResponse { Models = new List<UnifiedModelInfo>() };

                foreach (var ollamaModel in ollamaResponse.Models)
                {
                    int? maxTokens = ollamaModel.ContextLength > 0 ? ollamaModel.ContextLength : (int?)null;

                    var unifiedModel = new UnifiedModelInfo
                    {
                        Id = ollamaModel.Name ?? ollamaModel.Model ?? "unknown",
                        Name = ollamaModel.Name,
                        MaxTokens = maxTokens,
                        SupportsMaxTokens = maxTokens.HasValue && maxTokens > 0,
                        IsLoaded = true,
                        SizeInBytes = ollamaModel.Size > 0 ? ollamaModel.Size : (long?)null
                    };

                    result.Models.Add(unifiedModel);
                }

                return result;
            }
            catch (JsonException ex)
            {
                InternalLogger.Warn($"ConvertOllamaResponseToUnified: failed to parse Ollama response: {ex.Message}");
                return new UnifiedListModelsResponse { Error = "Failed to parse Ollama response" };
            }
        }

        /// <summary>
        /// Converts OpenAI-compatible /v1/models response to unified format.
        /// </summary>
        public static UnifiedListModelsResponse ConvertOpenAiResponseToUnified(string json)
        {
            try
            {
                var openAiResponse = json.FromJson<ListModelsResponse>();
                if (openAiResponse?.Data == null)
                {
                    return new UnifiedListModelsResponse { Error = "No models returned from OpenAI-compatible backend" };
                }

                var result = new UnifiedListModelsResponse { Models = new List<UnifiedModelInfo>() };

                foreach (var model in openAiResponse.Data)
                {
                    var unifiedModel = new UnifiedModelInfo
                    {
                        Id = model.Id,
                        Name = null,
                        MaxTokens = null,
                        SupportsMaxTokens = false,
                        IsLoaded = false
                    };

                    result.Models.Add(unifiedModel);
                }

                return result;
            }
            catch (JsonException ex)
            {
                InternalLogger.Warn($"ConvertOpenAiResponseToUnified: failed to parse OpenAI response: {ex.Message}");
                return new UnifiedListModelsResponse { Error = "Failed to parse OpenAI-compatible response" };
            }
        }

        /// <summary>
        /// Merges Ollama models from two sources: active models (from /api/ps) and all available models (from /v1/models).
        /// </summary>
        public static UnifiedListModelsResponse MergeOllamaModels(
            UnifiedListModelsResponse activeModelsResponse,
            UnifiedListModelsResponse allModelsResponse)
        {
            var activeModels = activeModelsResponse?.Models ?? new List<UnifiedModelInfo>();
            var allModels = allModelsResponse?.Models ?? new List<UnifiedModelInfo>();

            if (activeModels.Count == 0 && allModels.Count == 0)
            {
                return new UnifiedListModelsResponse { Error = "No models available in Ollama" };
            }

            var activeIds = new HashSet<string>(activeModels.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
            var mergedModels = new List<UnifiedModelInfo>(activeModels);

            foreach (var model in allModels)
            {
                if (!activeIds.Contains(model.Id))
                {
                    mergedModels.Add(new UnifiedModelInfo
                    {
                        Id = model.Id,
                        Name = model.Name,
                        MaxTokens = model.MaxTokens,
                        SupportsMaxTokens = model.SupportsMaxTokens,
                        IsLoaded = false,
                        SupportsToolUse = model.SupportsToolUse
                    });
                }
            }

            return new UnifiedListModelsResponse
            {
                Models = mergedModels,
                HasActiveModel = activeModels.Count > 0,
                ActiveModel = activeModels.Count > 0 ? activeModels[0] : null
            };
        }

        /// <summary>
        /// Converts Jan /v1/models response to unified format.
        /// </summary>
        public static UnifiedListModelsResponse ConvertJanResponseToUnified(string json)
        {
            try
            {
                var janResponse = json.FromJson<JanModelsResponse>();
                if (janResponse?.Data == null || janResponse.Data.Count == 0)
                {
                    return new UnifiedListModelsResponse { Error = "No models returned from Jan" };
                }

                var result = new UnifiedListModelsResponse { Models = new List<UnifiedModelInfo>() };

                foreach (var model in janResponse.Data)
                {
                    int? maxTokens = null;

                    // Prefer settings.ContextLength if available, then fall back to parameters.MaxTokens
                    if (model.Settings?.ContextLength > 0)
                    {
                        maxTokens = model.Settings.ContextLength;
                    }
                    else if (model.Parameters?.MaxTokens > 0)
                    {
                        maxTokens = model.Parameters.MaxTokens;
                    }

                    var unifiedModel = new UnifiedModelInfo
                    {
                        Id = model.Id,
                        Name = model.Name,
                        MaxTokens = maxTokens,
                        SupportsMaxTokens = maxTokens.HasValue && maxTokens > 0,
                        IsLoaded = true,
                        SizeInBytes = model.Size > 0 ? model.Size : (long?)null
                    };

                    result.Models.Add(unifiedModel);
                }

                return result;
            }
            catch (JsonException ex)
            {
                InternalLogger.Warn($"ConvertJanModels: failed to parse Jan response: {ex.Message}");
                return new UnifiedListModelsResponse { Error = "Failed to parse Jan response" };
            }
        }

        /// <summary>
        /// Converts Azure/Github Models API response to unified format.
        /// Filters models by task type (only "chat-completion" models are included).
        /// </summary>
        public static UnifiedListModelsResponse ConvertAzureResponseToUnified(string json)
        {
            try
            {
                var azureModels = json.FromJson<List<AzureModelInfo>>();
                if (azureModels == null || azureModels.Count == 0)
                {
                    return new UnifiedListModelsResponse { Error = "No models returned from Azure" };
                }

                var result = new UnifiedListModelsResponse { Models = new List<UnifiedModelInfo>() };

                var chatModels = azureModels
                    .Where(m => m.Task == "chat-completion")
                    .ToList();

                foreach (var model in chatModels)
                {
                    var unifiedModel = new UnifiedModelInfo
                    {
                        Id = model.Name,
                        Name = !string.IsNullOrEmpty(model.FriendlyName) ? model.FriendlyName : model.Name,
                        MaxTokens = null,
                        SupportsMaxTokens = false,
                        IsLoaded = false
                    };

                    result.Models.Add(unifiedModel);
                }

                if (result.Models.Count == 0)
                {
                    return new UnifiedListModelsResponse { Error = "No chat-completion models available in Azure" };
                }

                return result;
            }
            catch (JsonException ex)
            {
                InternalLogger.Warn($"ConvertAzureResponseToUnified: failed to parse Azure response: {ex.Message}");
                return new UnifiedListModelsResponse { Error = "Failed to parse Azure response" };
            }
        }
    }
}
