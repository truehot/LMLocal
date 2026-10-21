using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.HttpWrapper;
using LMLocal.Infrastructure.Tooling.Mcp.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Tooling.Mcp.Client
{
    /// <summary>
    /// MCP client implementation using HTTP transport with Streamable HTTP support.
    /// </summary>
    public class HttpMcpClient : McpClientBase
    {
        private readonly string _baseUrl;
        private readonly Dictionary<string, string> _headers;
        private readonly string _authToken;
        private readonly IHttpClientWrapper _httpClientWrapper;
        private readonly TimeSpan _requestTimeout;
        private readonly SemaphoreSlim _recoveryLock = new SemaphoreSlim(1, 1);
        private string _sessionId;
        private volatile bool _closed = false;
        private readonly object _sessionLock = new object();

        public HttpMcpClient(
            string baseUrl,
            IHttpClientWrapper httpClientWrapper,
            Dictionary<string, string> headers = null,
            string authToken = null,
            TimeSpan? requestTimeout = null)
        {
            if (string.IsNullOrEmpty(baseUrl))
                throw new ArgumentException("Base URL cannot be empty", nameof(baseUrl));

            _baseUrl = baseUrl.TrimEnd('/');
            _httpClientWrapper = httpClientWrapper ?? throw new ArgumentNullException(nameof(httpClientWrapper));
            _headers = headers ?? new Dictionary<string, string>();
            _authToken = authToken;
            _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        }

        public override async Task InitializeAsync(CancellationToken cancellationToken)
        {
            try
            {
                await InitializeSessionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to initialize HTTP MCP client: {ex.Message}", ex);
            }
        }

        public override async Task CloseAsync(CancellationToken cancellationToken)
        {
            if (_closed)
                return;

            _closed = true;

            string sessionIdToDelete;
            lock (_sessionLock)
            {
                sessionIdToDelete = _sessionId;
                _sessionId = null;
            }

            if (!string.IsNullOrEmpty(sessionIdToDelete))
            {
                try
                {
                    await DeleteSessionAsync(sessionIdToDelete, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"Error deleting session: {ex.Message}");
                }
            }
        }

        protected override async Task<string> SendJsonAndWaitResponseAsync(string json, CancellationToken cancellationToken)
        {
            ThrowIfClosed();

            var result = await PostAsync(json, includeSessionId: true, readResponseBody: true, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess)
                return result.Body;

            if (IsSessionExpired(result))
            {
                InternalLogger.Warn("MCP session expired (404), re-initializing");
                await RecoverSessionAsync(result.SessionIdUsed, cancellationToken).ConfigureAwait(false);

                var retryResult = await PostAsync(json, includeSessionId: true, readResponseBody: true, cancellationToken: cancellationToken).ConfigureAwait(false);

                if (retryResult.IsSuccess)
                    return retryResult.Body;

                throw CreateHttpError(retryResult);
            }

            throw CreateHttpError(result);
        }

        protected override async Task SendJsonAsync(string json, CancellationToken cancellationToken)
        {
            ThrowIfClosed();

            var result = await PostAsync(json, includeSessionId: true, readResponseBody: false, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!result.IsSuccess)
                throw CreateHttpError(result);
        }

        /// <summary>
        /// Performs the MCP initialization handshake: <c>initialize</c> followed by <c>notifications/initialized</c>.
        /// </summary>
        private async Task InitializeSessionAsync(CancellationToken cancellationToken)
        {
            var initRequest = new JsonRpcRequest
            {
                Id = GetNextRequestId(),
                Method = "initialize",
                Params = new InitializeRequest()
            };

            var result = await PostAsync(initRequest.ToJson(), includeSessionId: false, readResponseBody: true, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!result.IsSuccess)
                throw CreateHttpError(result);

            if (string.IsNullOrEmpty(result.Body))
                throw new InvalidOperationException("Empty response from MCP server");

            var response = result.Body.FromJson<JsonRpcResponse>() ?? throw new InvalidOperationException("Failed to initialize MCP server");
            if (!response.IsSuccess)
                throw new InvalidOperationException($"MCP error: {response.Error?.Message ?? "Unknown error"}");

            await SendInitializedNotificationAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends the <c>notifications/initialized</c> notification required by the MCP lifecycle.
        /// </summary>
        private async Task SendInitializedNotificationAsync(CancellationToken cancellationToken)
        {
            try
            {
                var notification = new JsonRpcNotification { Method = "notifications/initialized" };

                var result = await PostAsync(notification.ToJson(), includeSessionId: true, readResponseBody: false, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (!result.IsSuccess)
                {
                    InternalLogger.Warn(
                        $"notifications/initialized returned {result.StatusCode} {result.StatusDescription}: {result.ErrorDetails}");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"Failed to send notifications/initialized: {ex.Message}");
            }
        }

        /// <summary>
        /// Restores an expired MCP session by starting a new one.
        /// </summary>
        private async Task RecoverSessionAsync(string expiredSessionId, CancellationToken cancellationToken)
        {
            await _recoveryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfClosed();

                var currentSessionId = GetCurrentSessionId();
                if (!string.IsNullOrEmpty(currentSessionId)
                    && !string.Equals(currentSessionId, expiredSessionId, StringComparison.Ordinal))
                {
                    InternalLogger.Debug("MCP session already re-initialized by another request");
                    return;
                }

                lock (_sessionLock)
                {
                    _sessionId = null;
                }

                await InitializeSessionAsync(cancellationToken).ConfigureAwait(false);
                InternalLogger.Info("MCP session re-initialized after 404");
            }
            finally
            {
                _recoveryLock.Release();
            }
        }

        private static bool IsSessionExpired(McpHttpResult result)
        {
            return result.StatusCode == (int)HttpStatusCode.NotFound
                && !string.IsNullOrEmpty(result.SessionIdUsed);
        }

        private static InvalidOperationException CreateHttpError(McpHttpResult result)
        {
            return new InvalidOperationException(
                $"HTTP request failed ({result.StatusCode} {result.StatusDescription}): {result.ErrorDetails}");
        }

        private async Task<McpHttpResult> PostAsync(
            string json,
            bool includeSessionId,
            bool readResponseBody,
            CancellationToken cancellationToken)
        {
            if (_httpClientWrapper == null)
                throw new InvalidOperationException("HTTP client wrapper not initialized");

            using (var request = CreateRequest(json, includeSessionId, out string sessionIdUsed))
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cts.CancelAfter(_requestTimeout);

                var completionOption = readResponseBody
                    ? HttpCompletionOption.ResponseHeadersRead
                    : HttpCompletionOption.ResponseContentRead;

                using (var response = await _httpClientWrapper.SendAsync(request, completionOption, cts.Token).ConfigureAwait(false))
                {
                    ExtractSessionIdFromResponse(response);

                    var result = new McpHttpResult
                    {
                        StatusCode = (int)response.StatusCode,
                        StatusDescription = response.StatusCode.ToString(),
                        IsSuccess = response.IsSuccessStatusCode,
                        SessionIdUsed = sessionIdUsed
                    };

                    if (!result.IsSuccess)
                    {
                        result.ErrorDetails = await ExtractErrorDetailsAsync(response).ConfigureAwait(false);
                        return result;
                    }

                    if (!readResponseBody)
                        return result;

                    var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                    if (contentType.Equals("text/event-stream", StringComparison.OrdinalIgnoreCase))
                        result.Body = await ReadSseResponseAsync(response, cts.Token).ConfigureAwait(false);
                    else
                        result.Body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    return result;
                }
            }
        }

        private HttpRequestMessage CreateRequest(string json, bool includeSessionId, out string sessionIdUsed)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");

            sessionIdUsed = includeSessionId ? GetCurrentSessionId() : null;
            if (!string.IsNullOrEmpty(sessionIdUsed))
                request.Headers.Add("Mcp-Session-Id", sessionIdUsed);

            if (!string.IsNullOrEmpty(_authToken))
                request.Headers.Add("Authorization", $"Bearer {_authToken}");

            foreach (var header in _headers)
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);

            return request;
        }

        private string GetCurrentSessionId()
        {
            lock (_sessionLock) { return _sessionId; }
        }

        private void ThrowIfClosed()
        {
            if (_closed)
                throw new InvalidOperationException("Client is closed");
        }

        private async Task<string> ExtractErrorDetailsAsync(HttpResponseMessage response)
        {
            try
            {
                var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(errorBody))
                    return "No error details provided";

                try
                {
                    var errorJson = JObject.Parse(errorBody);
                    var error = errorJson["error"]?.ToString()
                        ?? errorJson["message"]?.ToString()
                        ?? errorJson["detail"]?.ToString()
                        ?? errorBody;
                    return error;
                }
                catch (JsonException)
                {
                    return errorBody.Length > 200 ? errorBody.Substring(0, 200) + "..." : errorBody;
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Debug($"Failed to extract error details: {ex.Message}");
                return "Unable to extract error details";
            }
        }

        private void ExtractSessionIdFromResponse(HttpResponseMessage response)
        {
            if (response?.Headers == null) return;

            if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessionIdValues))
            {
                var sessionId = sessionIdValues?.FirstOrDefault();
                if (!string.IsNullOrEmpty(sessionId))
                {
                    lock (_sessionLock)
                    {
                        if (_closed)
                        {
                            InternalLogger.Debug("MCP session ID ignored: client is closed");
                            return;
                        }

                        if (sessionId != _sessionId)
                        {
                            _sessionId = sessionId;
                            InternalLogger.Debug($"MCP session ID received: {_sessionId}");
                        }
                    }
                }
            }
        }

        private async Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            if (_httpClientWrapper == null) return;

            var request = new HttpRequestMessage(HttpMethod.Delete, _baseUrl);
            request.Headers.Add("Mcp-Session-Id", sessionId);

            if (!string.IsNullOrEmpty(_authToken))
                request.Headers.Add("Authorization", $"Bearer {_authToken}");

            foreach (var header in _headers)
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cts.CancelAfter(_requestTimeout);
                try
                {
                    using (var response = await _httpClientWrapper.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false))
                    {
                        if (response.IsSuccessStatusCode)
                            InternalLogger.Debug("MCP session deleted successfully");
                        else
                        {
                            var errorDetails = await ExtractErrorDetailsAsync(response).ConfigureAwait(false);
                            InternalLogger.Warn($"Delete session returned {(int)response.StatusCode}: {errorDetails}");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    InternalLogger.Warn("Delete session request timed out");
                }
            }
        }

        private async Task<string> ReadSseResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var accumulatedDataLines = new List<string>();
                var currentEventType = "";
                string lastValidResponse = null;
                bool isTerminalEventSeen = false;

                while (!cancellationToken.IsCancellationRequested && !isTerminalEventSeen)
                {
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line == null) break;

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        if (accumulatedDataLines.Count > 0)
                        {
                            var combined = string.Join("\n", accumulatedDataLines);
                            if (IsValidJson(combined))
                            {
                                lastValidResponse = combined;
                            }
                            accumulatedDataLines.Clear();
                            currentEventType = "";
                        }
                        continue;
                    }

                    var sseMessage = SseStreamParser.TryParseSseLine(line);
                    if (sseMessage == null) continue;

                    switch (sseMessage.Type)
                    {
                        case SseMessageType.Event:
                            currentEventType = sseMessage.EventType;
                            InternalLogger.Debug($"SSE event: {currentEventType}");
                            if (currentEventType.Equals("done", StringComparison.OrdinalIgnoreCase))
                                isTerminalEventSeen = true;
                            break;
                        case SseMessageType.Data:
                            if (!string.IsNullOrEmpty(sseMessage.RawData))
                                accumulatedDataLines.Add(sseMessage.RawData);
                            break;
                        case SseMessageType.Done:
                            isTerminalEventSeen = true;
                            if (accumulatedDataLines.Count > 0)
                            {
                                var combined = string.Join("\n", accumulatedDataLines);
                                if (IsValidJson(combined))
                                    return combined;
                            }
                            break;
                    }
                }

                if (lastValidResponse != null)
                    return lastValidResponse;

                throw new InvalidOperationException("No valid JSON-RPC response found in SSE stream");
            }
        }

        private bool IsValidJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            try { JObject.Parse(json); return true; }
            catch (JsonException) { return false; }
        }

        /// <summary>
        /// Outcome of a single HTTP POST to the MCP endpoint.
        /// </summary>
        private sealed class McpHttpResult
        {
            public int StatusCode { get; set; }

            public string StatusDescription { get; set; }

            public bool IsSuccess { get; set; }

            public string Body { get; set; }

            public string ErrorDetails { get; set; }

            /// <summary>
            /// Session id that was actually sent with the request (null when none was sent).
            /// </summary>
            public string SessionIdUsed { get; set; }
        }
    }
}
