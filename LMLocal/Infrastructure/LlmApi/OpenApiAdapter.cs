using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Api.Requests;
using LMLocal.Infrastructure.Api.Responses;
using LMLocal.Infrastructure.HttpWrapper;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.Tooling;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Api
{
    /// <summary>
    /// Client for communicating with the LM backend API.
    /// Supports multiple providers: LM Studio, OpenAI-compatible, Ollama, etc.
    /// </summary>
    internal interface IOpenApiAdapter
    {
        Task<string> ListModelsRawAsync(string endpoint, string baseUrl, string apiKey, CancellationToken cancellationToken);
        Task<SendChatResponse> SendChatAsync(MessageContext messageContext, ModelContext modelContext, CancellationToken cancellationToken);
        Task<StreamingResponse> SendChatStreamingAsync(MessageContext messageContext, ModelContext modelContext, CancellationToken cancellationToken);
    }


    internal class OpenApiAdapter : IOpenApiAdapter
    {
        private readonly IHttpClientWrapper _httpClientWrapper;
        private readonly ISettingsManager _settingsManager;
        private readonly ICompositeToolFactory _toolFactory;
        private const string DefaultBaseUrl = "http://localhost:1234";

        public OpenApiAdapter(
            IHttpClientWrapper httpClientWrapper,
            ISettingsManager settingsManager,
            ICompositeToolFactory toolFactory)
        {
            _httpClientWrapper = httpClientWrapper ?? throw new ArgumentNullException(nameof(httpClientWrapper));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _toolFactory = toolFactory ?? throw new ArgumentNullException(nameof(toolFactory));
        }

        private string GetBaseUrl()
        {
            string url = _settingsManager.Current?.LmStudioBaseUrl;
            if (!string.IsNullOrEmpty(url))
                return url.TrimEnd('/');
            return DefaultBaseUrl;
        }

        private string GetChatCompletionsEndpoint()
        {
            var provider = ProviderResolver.ResolveProvider(_settingsManager.Current?.Provider);
            return ProviderResolver.GetChatCompletionsEndpoint(provider);
        }

        /// <summary>
        /// Retrieves raw JSON response for models list from a specific backend with explicit credentials.
        /// </summary>
        public async Task<string> ListModelsRawAsync(string endpoint, string baseUrl, string apiKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = GetBaseUrl();
            }
            else
            {
                baseUrl = baseUrl.TrimEnd('/');
            }

            if (string.IsNullOrEmpty(apiKey))
                apiKey = _settingsManager.Current?.ApiKey;

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + endpoint))
                {
                    if (!string.IsNullOrEmpty(apiKey))
                    {
                        request.Headers.Add("Authorization", $"Bearer {apiKey}");
                    }

                    using (var response = await _httpClientWrapper.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        if (!response.IsSuccessStatusCode)
                        {
                            var userMessage = TryExtractErrorMessage(json) ?? $"Request failed with status {response.StatusCode}";
                            InternalLogger.Warn($"ListModelsRawAsync: backend returned error: {userMessage}");
                            return string.Empty;
                        }

                        return json;
                    }
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"ListModelsRawAsync failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Opens a streaming chat request and returns the response stream.
        /// The caller must dispose the returned <see cref="StreamingResponse"/>.
        /// </summary>
        public async Task<StreamingResponse> SendChatStreamingAsync(
            MessageContext messageContext,
            ModelContext modelContext,
            CancellationToken cancellationToken)
        {
            var openAiRequest = BuildRequest(messageContext, modelContext, stream: true, store: false);

            var content = new StringContent(openAiRequest.ToJson(), Encoding.UTF8, "application/json");
            HttpRequestMessage request = null;
            HttpResponseMessage response = null;
            bool success = false;

            try
            {
                request = new HttpRequestMessage(HttpMethod.Post, GetBaseUrl() + GetChatCompletionsEndpoint()) { Content = content };
                if (!string.IsNullOrEmpty(_settingsManager.Current.ApiKey))
                {
                    request.Headers.Add("Authorization", $"Bearer {_settingsManager.Current.ApiKey}");
                }

                response = await _httpClientWrapper.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var rawError = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var userMessage = TryExtractErrorMessage(rawError) ?? $"Failed to send chat request: {response.StatusCode}";
                    throw new HttpRequestException(userMessage);
                }

                var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                var streamingResponse = new StreamingResponse(stream, response, request, content);

                success = true;
                return streamingResponse;
            }
            finally
            {
                if (!success)
                {
                    response?.Dispose();
                    request?.Dispose();
                    content?.Dispose();
                }
            }
        }

        /// <summary>
        /// Sends chat request and returns the full response content.
        /// </summary>
        public async Task<SendChatResponse> SendChatAsync(
            MessageContext messageContext,
            ModelContext modelContext,
            CancellationToken cancellationToken)
        {
            var openAiRequest = BuildRequest(messageContext, modelContext, stream: false, store: false, useTools: false);

            using (var content = new StringContent(openAiRequest.ToJson(), Encoding.UTF8, "application/json"))
            using (var request = new HttpRequestMessage(HttpMethod.Post, GetBaseUrl() + GetChatCompletionsEndpoint()) { Content = content })
            {
                if (!string.IsNullOrEmpty(_settingsManager.Current.ApiKey))
                {
                    request.Headers.Add("Authorization", $"Bearer {_settingsManager.Current.ApiKey}");
                }

                using (var response = await _httpClientWrapper.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        var userMessage = TryExtractErrorMessage(json) ?? $"Generation failed: {response.StatusCode}";
                        throw new HttpRequestException(userMessage);
                    }

                    try
                    {
                        return json.FromJson<SendChatResponse>();
                    }
                    catch (JsonException ex)
                    {
                        InternalLogger.Error("SendChatAsync: failed to parse response JSON", ex);
                    }
                    return null;
                }
            }
        }

        private SendChatRequest BuildRequest(
            MessageContext messageContext,
            ModelContext modelContext,
            bool stream, bool store, bool useTools = true)
        {
            var messages = new List<Message>();

            foreach (var msg in messageContext.Input)
            {
                var apiMessage = new Message
                {
                    Role = msg.Role,
                    Content = msg.Content,
                    ToolCallId = msg.ToolCallId,
                    ToolCalls = msg.ToolCalls as List<Requests.ToolCall>
                };

                messages.Add(apiMessage);
            }
            // Google Gemini and Mistral do not support the 'store' parameter.
            var request = new SendChatRequest
            {
                Model = modelContext.ModelId,
                Messages = messages,
                Stream = stream,
                Temperature = modelContext.Temperature,
                TopP = modelContext.TopP,
                MaxCompletionTokens = modelContext.MaxOutputTokens,
                PresencePenalty = modelContext.PresencePenalty,
                FrequencyPenalty = modelContext.FrequencyPenalty,
                ReasoningEffort = modelContext.Reasoning,
                StreamOptions = stream ? new StreamOptions { IncludeUsage = stream } : null // Usage depends if stream is enabled.
            };

            if (_settingsManager.Current.EnableAiTools && useTools)
            {
                try
                {
                    var vsTools = _toolFactory.GetAllToolDefinitions();
                    if (vsTools.Count > 0)
                    {
                        var openAiTools = ToolDefinitionConverter.ConvertToOpenAiFormat(vsTools);
                        request.Tools = openAiTools;
                        request.ToolChoice = "auto";
                        request.ParallelToolCalls = false;
                    }
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"BuildRequest: failed to add tools to request: {ex.Message}");
                }
            }


            return request;
        }

        internal static string TryExtractErrorMessage(string rawResponse)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rawResponse)) return null;

                var parsed = JObject.Parse(rawResponse);
                var parsedError = parsed["error"];
                if (parsedError == null) return null;

                if (parsedError.Type == JTokenType.Object)
                {
                    var msgToken = parsedError["message"];
                    if (msgToken != null && msgToken.Type == JTokenType.String)
                    {
                        var text = msgToken.ToString();
                        return string.IsNullOrWhiteSpace(text) ? null : text;
                    }

                    return null;
                }

                if (parsedError.Type == JTokenType.String)
                {
                    var text = parsedError.ToString();
                    return string.IsNullOrWhiteSpace(text) ? null : text;
                }

                return null;
            }
            catch (JsonException ex)
            {
                InternalLogger.Warn($"TryExtractErrorMessage: invalid JSON response: {rawResponse} {ex.Message}");
                return string.IsNullOrWhiteSpace(rawResponse) ? null : rawResponse.Trim();
            }
        }
    }
}
