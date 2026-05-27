using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Common;
using LMLocal.Infrastructure.Http;
using LMLocal.Infrastructure.Tooling.Mcp.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Mcp
{
    /// <summary>
    /// MCP client implementation using HTTP transport with Streamable HTTP support.
    /// Caller must explicitly call CloseAsync() to properly clean up server-side resources.
    /// </summary>
    public class HttpMcpClient : McpClientBase
    {
        private readonly string _baseUrl;
        private readonly Dictionary<string, string> _headers;
        private readonly string _authToken;
        private readonly IHttpClientWrapper _httpClientWrapper;
        private readonly TimeSpan _requestTimeout;
        private string _sessionId;
        private bool _closed = false;

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
                var initRequest = new JsonRpcRequest
                {
                    Id = GetNextRequestId(),
                    Method = "initialize",
                    Params = new InitializeRequest()
                };

                var response = await SendRequestAndWaitResponseAsync(initRequest, cancellationToken)
                    .ConfigureAwait(false);

                if (response?.IsSuccess != true)
                    throw new InvalidOperationException("Failed to initialize MCP server");
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

            try
            {
                var shutdownRequest = new JsonRpcRequest
                {
                    Id = GetNextRequestId(),
                    Method = "shutdown"
                };

                try
                {
                    await SendJsonAsync(shutdownRequest.ToJson(), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"Error sending shutdown request: {ex.Message}");
                }

                if (!string.IsNullOrEmpty(_sessionId))
                {
                    try
                    {
                        await DeleteSessionAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Warn($"Error deleting session: {ex.Message}");
                    }
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(_sessionId))
                {
                    InternalLogger.Debug($"Closing MCP session: {_sessionId}");
                    _sessionId = null;
                }
                _closed = true;
            }
        }

        protected override async Task<string> SendJsonAndWaitResponseAsync(string json, CancellationToken cancellationToken)
        {
            if (_httpClientWrapper == null)
                throw new InvalidOperationException("HTTP client wrapper not initialized");

            var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            request.Headers.Add("Accept", "application/json, text/event-stream");

            if (!string.IsNullOrEmpty(_sessionId))
            {
                request.Headers.Add("Mcp-Session-Id", _sessionId);
            }

            if (!string.IsNullOrEmpty(_authToken))
            {
                request.Headers.Add("Authorization", $"Bearer {_authToken}");
            }

            foreach (var header in _headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cts.CancelAfter(_requestTimeout);

                using (var response = await _httpClientWrapper.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                    .ConfigureAwait(false))
                {
                    ExtractSessionIdFromResponse(response);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorMessage = await ExtractErrorDetailsAsync(response).ConfigureAwait(false);
                        throw new InvalidOperationException(
                            $"HTTP request failed ({(int)response.StatusCode} {response.StatusCode}): {errorMessage}");
                    }

                    var contentType = response.Content.Headers.ContentType?.MediaType ?? "";

                    if (contentType.Equals("text/event-stream", StringComparison.OrdinalIgnoreCase))
                    {
                        return await ReadSseResponseAsync(response, cts.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return responseJson;
                    }
                }
            }
        }

        /// <summary>
        /// Extracts error details from HTTP error response.
        /// </summary>
        private async Task<string> ExtractErrorDetailsAsync(HttpResponseMessage response)
        {
            try
            {
                var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(errorBody))
                {
                    return "No error details provided";
                }

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
                    return errorBody.Length > 200
                        ? errorBody.Substring(0, 200) + "..."
                        : errorBody;
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Debug($"Failed to extract error details: {ex.Message}");
                return "Unable to extract error details";
            }
        }

        /// <summary>
        /// Extracts Mcp-Session-Id from response headers if present and stores it for future requests.
        /// </summary>
        private void ExtractSessionIdFromResponse(HttpResponseMessage response)
        {
            if (response?.Headers == null)
                return;

            if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessionIdValues))
            {
                var sessionId = sessionIdValues?.FirstOrDefault();
                if (!string.IsNullOrEmpty(sessionId) && sessionId != _sessionId)
                {
                    _sessionId = sessionId;
                    InternalLogger.Debug($"MCP session ID received: {_sessionId}");
                }
            }
        }

        /// <summary>
        /// Deletes the session by sending HTTP DELETE request with session ID.
        /// </summary>
        private async Task DeleteSessionAsync(CancellationToken cancellationToken)
        {
            if (_httpClientWrapper == null)
                return;

            var request = new HttpRequestMessage(HttpMethod.Delete, _baseUrl) { };

            request.Headers.Add("Mcp-Session-Id", _sessionId);

            if (!string.IsNullOrEmpty(_authToken))
            {
                request.Headers.Add("Authorization", $"Bearer {_authToken}");
            }

            foreach (var header in _headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cts.CancelAfter(_requestTimeout);

                try
                {
                    using (var response = await _httpClientWrapper.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token)
                        .ConfigureAwait(false))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            InternalLogger.Debug("MCP session deleted successfully");
                        }
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
                    throw;
                }
            }
        }

        /// <summary>
        /// Reads a Server-Sent Events (SSE) response and extracts JSON-RPC response(s).
        /// </summary>
        private async Task<string> ReadSseResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var accumulatedDataLines = new List<string>();
                var currentEventType = "";
                var lastValidResponse = null as string;  // Keep track of last valid response
                var isTerminalEventSeen = false;

                while (!cancellationToken.IsCancellationRequested && !isTerminalEventSeen)
                {
                    try
                    {
                        var line = await reader.ReadLineAsync().ConfigureAwait(false);

                        if (line == null)
                        {
                            if (accumulatedDataLines.Count > 0)
                            {
                                var combined = string.Join("\n", accumulatedDataLines);
                                if (TryValidateJson(combined))
                                {
                                    lastValidResponse = combined;
                                }
                            }
                            break;
                        }

                        if (string.IsNullOrWhiteSpace(line))
                        {
                            if (accumulatedDataLines.Count > 0)
                            {
                                var combined = string.Join("\n", accumulatedDataLines);
                                if (TryValidateJson(combined))
                                {
                                    lastValidResponse = combined;
                                    InternalLogger.Debug($"SSE message received (event: {(string.IsNullOrEmpty(currentEventType) ? "message" : currentEventType)})");

                                    if (string.IsNullOrEmpty(currentEventType) ||
                                        currentEventType.Equals("message", StringComparison.OrdinalIgnoreCase))
                                    {
                                        return lastValidResponse;
                                    }
                                }

                                accumulatedDataLines.Clear();
                                currentEventType = "";
                            }
                            continue;
                        }

                        var sseMessage = SseStreamParser.TryParseSseLine(line);

                        if (sseMessage == null)
                        {
                            continue;
                        }

                        switch (sseMessage.Type)
                        {
                            case SseMessageType.Comment:
                                break;

                            case SseMessageType.Event:
                                currentEventType = sseMessage.EventType;
                                InternalLogger.Debug($"SSE event: {currentEventType}");

                                if (currentEventType.Equals("done", StringComparison.OrdinalIgnoreCase))
                                {
                                    isTerminalEventSeen = true;
                                }
                                break;

                            case SseMessageType.Data:
                                if (!string.IsNullOrEmpty(sseMessage.RawData))
                                {
                                    accumulatedDataLines.Add(sseMessage.RawData);
                                }
                                break;

                            case SseMessageType.Done:
                                isTerminalEventSeen = true;
                                if (accumulatedDataLines.Count > 0)
                                {
                                    var combined = string.Join("\n", accumulatedDataLines);
                                    if (TryValidateJson(combined))
                                    {
                                        return combined;
                                    }
                                }
                                if (lastValidResponse != null)
                                {
                                    return lastValidResponse;
                                }
                                break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Error($"Error reading SSE response: {ex.Message}", ex);
                        throw new InvalidOperationException($"Failed to read SSE response: {ex.Message}", ex);
                    }
                }

                if (lastValidResponse != null)
                {
                    return lastValidResponse;
                }

                throw new InvalidOperationException("No valid JSON-RPC response found in SSE stream");
            }
        }

        /// <summary>
        /// Validates if a string is valid JSON.
        /// </summary>
        private bool TryValidateJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                JObject.Parse(json);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        protected override async Task SendJsonAsync(string json, CancellationToken cancellationToken)
        {
            if (_httpClientWrapper == null)
                throw new InvalidOperationException("HTTP client wrapper not initialized");

            var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            request.Headers.Add("Accept", "application/json, text/event-stream");

            if (!string.IsNullOrEmpty(_sessionId))
            {
                request.Headers.Add("Mcp-Session-Id", _sessionId);
            }

            if (!string.IsNullOrEmpty(_authToken))
            {
                request.Headers.Add("Authorization", $"Bearer {_authToken}");
            }

            foreach (var header in _headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cts.CancelAfter(_requestTimeout);

                using (var response = await _httpClientWrapper.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false))
                {
                    ExtractSessionIdFromResponse(response);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorMessage = await ExtractErrorDetailsAsync(response).ConfigureAwait(false);
                        throw new InvalidOperationException(
                            $"HTTP request failed ({(int)response.StatusCode} {response.StatusCode}): {errorMessage}");
                    }
                }
            }
        }
    }
}
