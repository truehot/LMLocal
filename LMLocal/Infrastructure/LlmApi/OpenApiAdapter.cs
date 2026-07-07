using LMLocal.Core.Exceptions;
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Api;
using LMLocal.Infrastructure.HttpWrapper;
using LMLocal.Infrastructure.LlmApi.Responses;
using LMLocal.Infrastructure.Settings;
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
        Task<string> ListModelsRawAsync(string endpoint, string baseUrl, string apiKey, CancellationToken cancellationToken);

        /// <summary>
        /// Opens a streaming chat request and returns the response stream.
        /// </summary>
        Task<SendChatResponse> SendChatAsync(MessageContext messageContext, ModelContext modelContext, CancellationToken cancellationToken);

        /// <summary>
        /// Sends chat request and returns the full response content.
        /// </summary>
        Task<StreamingResponse> SendChatStreamingAsync(MessageContext messageContext, ModelContext modelContext, CancellationToken cancellationToken);
    }


    internal class OpenApiAdapter : IOpenApiAdapter
    {
        private readonly IHttpClientWrapper _httpClientWrapper;
        private readonly ISettingsManager _settingsManager;
        private readonly IApiRequestBuilder _requestBuilder;
        private const string DefaultBaseUrl = "http://localhost:1234";

        public OpenApiAdapter(
            IHttpClientWrapper httpClientWrapper,
            ISettingsManager settingsManager,
            IApiRequestBuilder requestBuilder)
        {
            _httpClientWrapper = httpClientWrapper ?? throw new ArgumentNullException(nameof(httpClientWrapper));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _requestBuilder = requestBuilder ?? throw new ArgumentNullException(nameof(requestBuilder));
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
                    if (!string.IsNullOrEmpty(_settingsManager.UserAgent))
                        request.Headers.UserAgent.ParseAdd(_settingsManager.UserAgent);
                    if (!string.IsNullOrEmpty(apiKey))
                        request.Headers.Add("Authorization", $"Bearer {apiKey}");


                    using (var response = await _httpClientWrapper.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
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

            var content = new StringContent(openAiRequest.ToJson(), Encoding.UTF8, "application/json");
            HttpRequestMessage request = null;
            HttpResponseMessage response = null;
            bool success = false;

            try
            {
                request = new HttpRequestMessage(HttpMethod.Post, GetBaseUrl() + GetChatCompletionsEndpoint()) { Content = content };

                if (!string.IsNullOrEmpty(_settingsManager.UserAgent))
                    request.Headers.UserAgent.ParseAdd(_settingsManager.UserAgent);
                if (!string.IsNullOrEmpty(_settingsManager.Current.ApiKey))
                    request.Headers.Add("Authorization", $"Bearer {_settingsManager.Current.ApiKey}");

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

        public async Task<SendChatResponse> SendChatAsync(
            MessageContext messageContext,
            ModelContext modelContext,
            CancellationToken cancellationToken)
        {
            var openAiRequest = _requestBuilder.BuildRequest(messageContext, modelContext, stream: false, useTools: false);

            using (var content = new StringContent(openAiRequest.ToJson(), Encoding.UTF8, "application/json"))
            using (var request = new HttpRequestMessage(HttpMethod.Post, GetBaseUrl() + GetChatCompletionsEndpoint()) { Content = content })
            {
                if (!string.IsNullOrEmpty(_settingsManager.UserAgent))
                    request.Headers.UserAgent.ParseAdd(_settingsManager.UserAgent);
                if (!string.IsNullOrEmpty(_settingsManager.Current.ApiKey))
                    request.Headers.Add("Authorization", $"Bearer {_settingsManager.Current.ApiKey}");


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
    }
}
