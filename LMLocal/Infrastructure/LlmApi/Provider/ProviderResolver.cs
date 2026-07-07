using System;
using System.Collections.Generic;
using System.Reflection;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi.Provider;

namespace LMLocal.Infrastructure.Api
{
    /// <summary>
    /// Resolves the model provider and corresponding API endpoint.
    /// </summary>
    internal class ProviderResolver
    {
        /// <summary>
        /// Determines the model provider based on provider name string.
        /// </summary>
        public static ModelProvider ResolveProvider(string providerName)
        {
            if (string.IsNullOrEmpty(providerName))
                return ModelProvider.LmStudio;

            switch (providerName.ToLowerInvariant())
            {
                case "lmstudio":
                    return ModelProvider.LmStudio;
                case "ollama":
                    return ModelProvider.Ollama;
                case "openai":
                    return ModelProvider.OpenAi;
                case "jan":
                    return ModelProvider.Jan;
                case "togetherai":
                    return ModelProvider.TogetherAi;
                case "deepseek":
                    return ModelProvider.DeepSeek;
                case "gemini":
                    return ModelProvider.Gemini;
                case "githubmodelsazure":
                    return ModelProvider.GithubModelsAzure;
                case "llamacpp":
                    return ModelProvider.LlamaCpp;
                default:
                    return ModelProvider.LmStudio;
            }
        }

        /// <summary>
        /// Returns all provider types defined in the ModelProvider enum with their keys and human-readable display names.
        /// </summary>
        public static List<ProviderTypeInfo> GetProviderTypes()
        {
            var values = (ModelProvider[])Enum.GetValues(typeof(ModelProvider));
            var result = new List<ProviderTypeInfo>(values.Length);
            foreach (var value in values)
            {
                result.Add(new ProviderTypeInfo
                {
                    Key = value.ToString().ToLowerInvariant(),
                    DisplayName = GetDisplayName(value)
                });
            }
            return result;
        }
        /// <summary>
        /// Returns human-readable display name for a ModelProvider value from its ProviderDisplayAttribute. Falls back to enum name.
        /// </summary>
        internal static string GetDisplayName(ModelProvider value)
        {
            var member = typeof(ModelProvider).GetMember(value.ToString());
            if (member == null || member.Length == 0)
                return value.ToString();
            var attr = member[0].GetCustomAttribute<ProviderDisplayAttribute>();
            return attr?.DisplayName ?? value.ToString();
        }

        /// <summary>
        /// Gets the API endpoint for listing models based on the provider type.
        /// </summary>
        public static string GetListModelsEndpoint(ModelProvider provider)
        {
            if (provider == ModelProvider.LmStudio)
                return ApiEndpoints.LmStudioListModels;
            else if (provider == ModelProvider.Ollama)
                return ApiEndpoints.OllamaRunningModels;
            else if (provider == ModelProvider.Jan)
                return ApiEndpoints.ListModels;
            else if (provider == ModelProvider.TogetherAi)
                return ApiEndpoints.ListModels;
            else if (provider == ModelProvider.DeepSeek)
                return ApiEndpoints.DeepSeekListModels;
            else if (provider == ModelProvider.Gemini)
                return ApiEndpoints.GeminiListModels;
            else if (provider == ModelProvider.GithubModelsAzure)
                return ApiEndpoints.GithubModelsAzureListModels;
            else
                return ApiEndpoints.ListModels;
        }

        /// <summary>
        /// Gets the API endpoint for chat completions based on the provider type.
        /// </summary>
        public static string GetChatCompletionsEndpoint(ModelProvider provider)
        {
            if (provider == ModelProvider.TogetherAi)
                return ApiEndpoints.ChatCompletions;
            else if (provider == ModelProvider.DeepSeek)
                return ApiEndpoints.DeepSeekCompletions;
            else if (provider == ModelProvider.Gemini)
                return ApiEndpoints.GeminiCompletions;
            else if (provider == ModelProvider.GithubModelsAzure)
                return ApiEndpoints.GithubModelsAzureCompletions;
            else
                return ApiEndpoints.ChatCompletions;
        }
    }
}
