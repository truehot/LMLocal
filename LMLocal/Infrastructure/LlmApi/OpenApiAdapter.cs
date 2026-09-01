using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Autocompletions;
using LMLocal.Core.Common;
using LMLocal.Core.Exceptions;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Api;
using LMLocal.Infrastructure.HttpWrapper;
using LMLocal.Infrastructure.LlmApi.Provider;
using LMLocal.Infrastructure.LlmApi.Requests;
using LMLocal.Infrastructure.LlmApi.Responses;
using LMLocal.Infrastructure.Tooling;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.LlmApi
{
    /// <summary>
    /// Client for communicating with the LLM backend API.
    /// </summary>
    internal interface IOpenApiAdapter
    {
        /// <summary>
        /// Retrieves raw JSON response for models list from a specific backend with explicit credentials.
        /// </summary>
        Task<string> ListModelsRawAsync(string endpoint, string baseUrl, string apiKey, CancellationToken cancellationToken, string certificatePath = null);

        /// <summary>
        /// Sends a chat request and returns the full response content.
        /// </summary>
        Task<SendChatResponse> SendChatAsync(MessageContext messageContext, ModelContext modelContext, CancellationToken cancellationToken);

        /// <summary>
        /// Sends a chat request with an explicit provider connection and restricted tools (used by SubAgent).
        /// </summary>
        Task<SendChatResponse> SendChatAsync(MessageContext messageContext, ModelContext modelContext, ProviderContext provider, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken);

        /// <summary>
        /// Sends chat request and returns the full response content.
        /// </summary>
        Task<StreamingResponse> SendChatStreamingAsync(MessageContext messageContext, ModelContext modelContext, CancellationToken cancellationToken);

        /// <summary>
        /// Sends a streaming chat request with an explicit provider connection and restricted tools (used by SubAgent).
        /// </summary>
        Task<StreamingResponse> SendChatStreamingAsync(MessageContext messageContext, ModelContext modelContext, ProviderContext provider, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken);

        /// <summary>
        /// Sends a text completion request (FIM - Fill In the Middle) and returns the generated text.
        /// </summary>
        Task<string> SendCompletionAsync(CompletionContext context, CancellationToken cancellationToken);

    }

    internal class OpenApiAdapter : IOpenApiAdapter
    {
        private readonly IHttpClientWrapper _httpClientWrapper;
        private readonly ISettingsManager _settingsManager;
        private readonly IApiRequestBuilder _requestBuilder;
        private readonly ITemporaryHttpClientFactory _temporaryHttpClientFactory;
        private const string DefaultBaseUrl = "http://localhost:1234";

        public OpenApiAdapter(
            IHttpClientWrapper httpClientWrapper,
            ISettingsManager settingsManager,
            IApiRequestBuilder requestBuilder,
            ITemporaryHttpClientFactory temporaryHttpClientFactory)
        {
            _httpClientWrapper = httpClientWrapper ?? throw new ArgumentNullException(nameof(httpClientWrapper));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _requestBuilder = requestBuilder ?? throw new ArgumentNullException(nameof(requestBuilder));
            _temporaryHttpClientFactory = temporaryHttpClientFactory ?? throw new ArgumentNullException(nameof(temporaryHttpClientFactory));
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

        public async Task<string> ListModelsRawAsync(string endpoint, string baseUrl, string apiKey, CancellationToken cancellationToken, string certificatePath = null)
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

            string url = baseUrl + endpoint;

            if (string.IsNullOrWhiteSpace(certificatePath))
            {
                return await ListModelsRawCoreAsync(_httpClientWrapper.SendAsync, url, apiKey, cancellationToken).ConfigureAwait(false);
            }

            HttpClient temporaryClient = _temporaryHttpClientFactory.Create(certificatePath);
            try
            {
                return await ListModelsRawCoreAsync(temporaryClient.SendAsync, url, apiKey, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                temporaryClient.Dispose();
            }
        }

        private async Task<string> ListModelsRawCoreAsync(
            Func<HttpRequestMessage, HttpCompletionOption, CancellationToken, Task<HttpResponseMessage>> sendAsync,
            string url,
            string apiKey,
            CancellationToken cancellationToken)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrEmpty(_settingsManager.UserAgent))
                        request.Headers.UserAgent.ParseAdd(_settingsManager.UserAgent);
                    if (!string.IsNullOrEmpty(apiKey))
                        request.Headers.Add("Authorization", $"Bearer {apiKey}");


                    using (var response = await sendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        if (!response.IsSuccessStatusCode)
                        {
                            var errorInfo = ApiErrorParser.ParseErrorBody(json);
                            errorInfo.Code = (int)response.StatusCode;
                            InternalLogger.Warn($"ListModelsRawAsync: backend returned error: {errorInfo}");
                            throw new ApiException(errorInfo, (int)response.StatusCode);
                        }

                        return json;
                    }
                }
            }
            catch (Exception ex) when (!(ex is ApiException) && !(ex is OperationCanceledException))
            {
                InternalLogger.Warn($"ListModelsRawAsync failed: {ex.Message}");
                throw;
            }
        }

        public async Task<StreamingResponse> SendChatStreamingAsync(
            MessageContext messageContext,
            ModelContext modelContext,
            CancellationToken cancellationToken)
        {
            var openAiRequest = _requestBuilder.BuildRequest(messageContext, modelContext, stream: true);

            return await SendChatStreamingCoreAsync(
                openAiRequest,
                GetBaseUrl(),
                GetChatCompletionsEndpoint(),
                _settingsManager.Current?.ApiKey,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a streaming chat request with an explicit provider connection and restricted tools.
        /// </summary>
        public async Task<StreamingResponse> SendChatStreamingAsync(
            MessageContext messageContext,
            ModelContext modelContext,
            ProviderContext provider,
            IReadOnlyList<ToolDefinition> tools,
            CancellationToken cancellationToken)
        {
            var openAiRequest = _requestBuilder.BuildRequest(messageContext, modelContext, stream: true, tools);

            var baseUrl = ResolveBaseUrl(provider?.BaseUrl);
            var apiKey = ResolveApiKey(provider?.ApiKey);
            var providerType = ResolveProviderType(provider?.ProviderType);
            var endpoint = ProviderResolver.GetChatCompletionsEndpoint(providerType);

            return await SendChatStreamingCoreAsync(openAiRequest, baseUrl, endpoint, apiKey, cancellationToken).ConfigureAwait(false);
        }

        private async Task<StreamingResponse> SendChatStreamingCoreAsync(
            SendChatRequest openAiRequest,
            string baseUrl,
            string endpoint,
            string apiKey,
            CancellationToken cancellationToken)
        {
            var content = new StringContent(openAiRequest.ToJson(), Encoding.UTF8, "application/json");
            HttpRequestMessage request = null;
            HttpResponseMessage response = null;
            bool success = false;

            try
            {
                request = new HttpRequestMessage(HttpMethod.Post, baseUrl + endpoint) { Content = content };

                if (!string.IsNullOrEmpty(_settingsManager.UserAgent))
                    request.Headers.UserAgent.ParseAdd(_settingsManager.UserAgent);
                if (!string.IsNullOrEmpty(apiKey))
                    request.Headers.Add("Authorization", $"Bearer {apiKey}");

                response = await _httpClientWrapper.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var rawError = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var errorInfo = ApiErrorParser.ParseErrorBody(rawError);
                    errorInfo.Code = (int)response.StatusCode;
                    InternalLogger.Warn($"SendChatStreamingAsync: API error: {errorInfo}");
                    throw new ApiException(errorInfo, (int)response.StatusCode);
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

        public Task<SendChatResponse> SendChatAsync(
            MessageContext messageContext,
            ModelContext modelContext,
            CancellationToken cancellationToken)
        {
            var openAiRequest = _requestBuilder.BuildRequest(messageContext, modelContext, stream: false, useTools: false);
            return SendChatCoreAsync(
                openAiRequest,
                GetBaseUrl(),
                GetChatCompletionsEndpoint(),
                _settingsManager.Current?.ApiKey,
                cancellationToken);
        }

        /// <summary>
        /// Sends a chat request with an explicit provider connection and a restricted tool set.
        /// </summary>
        public Task<SendChatResponse> SendChatAsync(
            MessageContext messageContext,
            ModelContext modelContext,
            ProviderContext provider,
            IReadOnlyList<ToolDefinition> tools,
            CancellationToken cancellationToken)
        {
            var openAiRequest = _requestBuilder.BuildRequest(messageContext, modelContext, stream: false, tools);

            var baseUrl = ResolveBaseUrl(provider?.BaseUrl);
            var apiKey = ResolveApiKey(provider?.ApiKey);
            var providerType = ResolveProviderType(provider?.ProviderType);
            var endpoint = ProviderResolver.GetChatCompletionsEndpoint(providerType);

            return SendChatCoreAsync(openAiRequest, baseUrl, endpoint, apiKey, cancellationToken);
        }

        private async Task<SendChatResponse> SendChatCoreAsync(
            SendChatRequest openAiRequest,
            string baseUrl,
            string endpoint,
            string apiKey,
            CancellationToken cancellationToken)
        {
            using (var content = new StringContent(openAiRequest.ToJson(), Encoding.UTF8, "application/json"))
            using (var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + endpoint) { Content = content })
            {
                if (!string.IsNullOrEmpty(_settingsManager.UserAgent))
                    request.Headers.UserAgent.ParseAdd(_settingsManager.UserAgent);
                if (!string.IsNullOrEmpty(apiKey))
                    request.Headers.Add("Authorization", $"Bearer {apiKey}");

                using (var response = await _httpClientWrapper.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        var errorInfo = ApiErrorParser.ParseErrorBody(json);
                        errorInfo.Code = (int)response.StatusCode;
                        InternalLogger.Warn($"SendChatAsync: API error: {errorInfo}");
                        throw new ApiException(errorInfo, (int)response.StatusCode);
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

        private string ResolveBaseUrl(string explicitUrl)
        {
            if (!string.IsNullOrWhiteSpace(explicitUrl))
            {
                return explicitUrl.TrimEnd('/');
            }

            return GetBaseUrl();
        }

        private string ResolveApiKey(string explicitKey)
        {
            if (!string.IsNullOrEmpty(explicitKey))
            {
                return explicitKey;
            }

            return _settingsManager.Current?.ApiKey;
        }

        private ModelProvider ResolveProviderType(string explicitProviderType)
        {
            if (!string.IsNullOrWhiteSpace(explicitProviderType))
            {
                return ProviderResolver.ResolveProvider(explicitProviderType);
            }

            return ProviderResolver.ResolveProvider(_settingsManager.Current?.Provider);
        }

        /// <summary>
        /// Sends a text completion request (FIM) to the LLM backend and returns the generated text.
        /// </summary>
        public async Task<string> SendCompletionAsync(CompletionContext context, CancellationToken cancellationToken)
        {
            var body = new
            {
                model = context.ModelId,
                prompt = context.Prompt,
                suffix = context.Suffix,
                max_tokens = context.MaxTokens,
                temperature = context.Temperature,
                stop = context.Stop
            };

            var jsonBody = body.ToJson();
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var provider = ProviderResolver.ResolveProvider(context.ProviderType);
            var endpoint = ProviderResolver.GetCompletionsEndpoint(provider);

            var url = string.IsNullOrWhiteSpace(context.BaseUrl) ? DefaultBaseUrl : context.BaseUrl.TrimEnd('/');

            using (var request = new HttpRequestMessage(HttpMethod.Post, url + endpoint) { Content = content })
            {
                if (!string.IsNullOrEmpty(_settingsManager.UserAgent))
                    request.Headers.UserAgent.ParseAdd(_settingsManager.UserAgent);
                if (!string.IsNullOrEmpty(context.ApiKey))
                    request.Headers.Add("Authorization", $"Bearer {context.ApiKey}");

                using (var response = await _httpClientWrapper.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false))
                {
                    var rawJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorInfo = ApiErrorParser.ParseErrorBody(rawJson);
                        errorInfo.Code = (int)response.StatusCode;
                        InternalLogger.Warn($"SendCompletionAsync: API error: {errorInfo}");
                        throw new ApiException(errorInfo, (int)response.StatusCode);
                    }

                    try
                    {
                        var result = rawJson.FromJson<CompletionResponse>();
                        if (result?.Choices != null && result.Choices.Count > 0)
                        {
                            return result.Choices[0].Text;
                        }
                    }
                    catch (JsonException ex)
                    {
                        InternalLogger.Error("SendCompletionAsync: failed to parse response JSON", ex);
                    }

                    return string.Empty;
                }
            }
        }
    }
}
