using System;
using System.Globalization;
using System.Net.Http.Headers;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.LlmApi
{
    /// <summary>
    /// Parses error responses from LLM APIs into structured 
    /// </summary>
    internal static class ApiErrorParser
    {
        private const int MaxErrorPreviewLength = 300;
        private static readonly string EmptyResponseMessage = "Empty response";
        private static readonly string UnknownErrorMessage = "Unknown error";

        /// <summary>Text shown when the provider sent no message; the body itself goes to <c>RawBody</c>.</summary>
        private static readonly string GenericErrorMessage = "The provider returned an error without a description.";

        /// <summary>
        /// Parses a raw JSON response body into a structured error.
        /// </summary>
        public static ApiErrorInfo ParseErrorBody(string rawResponse)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rawResponse))
                    return new ApiErrorInfo { Message = EmptyResponseMessage };

                var parsed = JObject.Parse(rawResponse);
                var info = new ApiErrorInfo();

                var errorToken = parsed["error"];
                JObject errorObj = null;

                if (errorToken != null)
                {
                    if (errorToken.Type == JTokenType.Object)
                    {
                        errorObj = (JObject)errorToken;

                        var msg = errorObj["message"]?.ToString();

                        if (string.IsNullOrEmpty(msg) || msg == "Provider returned error")
                        {
                            var metadataToken = errorObj["metadata"];
                            if (metadataToken != null)
                            {
                                var rawMsg = metadataToken["raw"]?.ToString();
                                if (!string.IsNullOrEmpty(rawMsg))
                                    msg = rawMsg;
                            }
                        }

                        info.Message = msg;

                        if (string.IsNullOrEmpty(info.Message))
                        {
                            var nestedError = errorObj["error"];
                            if (nestedError?.Type == JTokenType.Object)
                            {
                                var nestedMsg = nestedError["message"]?.ToString();
                                if (!string.IsNullOrEmpty(nestedMsg))
                                    info.Message = nestedMsg;
                            }
                        }

                        if (errorObj["code"] != null)
                        {
                            var rawCode = errorObj["code"].ToString();
                            if (int.TryParse(rawCode, out int code))
                            {
                                info.Code = code;
                            }
                            else if (!string.IsNullOrWhiteSpace(rawCode) && rawCode != "{}")
                            {
                                info.RawCode = rawCode;
                            }
                        }

                        var errorType = errorObj["type"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(errorType))
                            info.Type = errorType;

                        var metadata = errorObj["metadata"];
                        if (metadata != null)
                        {
                            info.RawMetadata = metadata.ToString();
                            info.Provider = metadata["provider_name"]?.ToString();
                        }

                        ApplyBodyRetryAfter(errorObj, info);
                    }
                    else if (errorToken.Type == JTokenType.String)
                    {
                        info.Message = errorToken.ToString();
                    }
                }

                ReadRootLevelCodeAndType(parsed, info);

                if (string.IsNullOrEmpty(info.Message))
                {
                    var errorsArray = parsed["errors"];
                    if (errorsArray?.Type == JTokenType.Array && errorsArray.HasValues)
                    {
                        var firstError = errorsArray[0];
                        info.Message = firstError["message"]?.ToString()
                                       ?? firstError["detail"]?.ToString()
                                       ?? firstError.ToString();
                    }
                }

                if (string.IsNullOrEmpty(info.Message))
                {
                    var detail = parsed["detail"];
                    if (detail != null)
                        info.Message = detail.ToString();
                }

                if (string.IsNullOrEmpty(info.Message))
                {
                    var msg = parsed["message"] ?? parsed["errorMessage"];
                    if (msg != null)
                        info.Message = msg.ToString();
                }

                if (string.IsNullOrEmpty(info.Message))
                {
                    info.RawBody = Preview(rawResponse);
                    info.Message = GenericErrorMessage;

                    if (errorObj != null)
                        InternalLogger.Debug($"ApiErrorParser: no message field; raw body kept for diagnostics.");
                }

                ReadSizeHints(parsed, errorObj, info);

                return info;
            }
            catch (JsonException ex)
            {
                InternalLogger.Warn($"ApiErrorParser: Invalid JSON. {ex.Message}");
                var fallback = rawResponse?.Trim();
                if (string.IsNullOrEmpty(fallback))
                    return new ApiErrorInfo { Message = UnknownErrorMessage };

                return new ApiErrorInfo
                {
                    Message = GenericErrorMessage,
                    RawBody = Preview(fallback)
                };
            }
        }

        /// <summary>
        /// Reads every supported body hint for the retry delay and keeps the largest valid value, so a malformed or
        /// smaller duplicate field never masks the delay another field reports.
        /// </summary>
        private static void ApplyBodyRetryAfter(JObject errorObj, ApiErrorInfo info)
        {
            if (errorObj == null)
                return;

            var metadata = errorObj["metadata"];
            double? best = null;

            foreach (var candidate in new[]
            {
                metadata?["retry_after_seconds"],
                metadata?["retry_after_seconds_raw"],
                errorObj["retry_after"]
            })
            {
                if (TryParseSeconds(candidate, out double seconds) && (!best.HasValue || seconds > best.Value))
                    best = seconds;
            }

            if (!best.HasValue)
                return;

            info.RetryAfterSeconds = best.Value;
            info.RetryAfterSource = RetryAfterSource.Body;
        }

        /// <summary>
        /// Merges the <c>Retry-After</c> header into the parsed hint, keeping the larger value so a retry never
        /// happens earlier than any server signal asked for.
        /// </summary>
        public static void ApplyRetryAfterHeader(ApiErrorInfo info, HttpResponseHeaders headers)
        {
            if (info == null)
                return;

            double? headerHint = ParseRetryAfterHeader(headers);
            if (!headerHint.HasValue)
                return;

            if (info.RetryAfterSeconds.HasValue && info.RetryAfterSeconds.Value >= headerHint.Value)
                return;

            info.RetryAfterSeconds = headerHint.Value;
            info.RetryAfterSource = RetryAfterSource.Header;
        }

        /// <summary>
        /// Reads the <c>Retry-After</c> header: delta-seconds or HTTP-date. Returns null when absent or unusable.
        /// </summary>
        public static double? ParseRetryAfterHeader(HttpResponseHeaders headers)
        {
            if (headers == null)
                return null;

            RetryConditionHeaderValue retryAfter = headers.RetryAfter;
            if (retryAfter == null)
                return null;

            if (retryAfter.Delta.HasValue)
            {
                double deltaSeconds = retryAfter.Delta.Value.TotalSeconds;
                return deltaSeconds < 0 ? (double?)null : deltaSeconds;
            }

            if (!retryAfter.Date.HasValue)
                return null;

            // Prefer the server's own clock, so a skewed client clock cannot shorten the wait.
            DateTimeOffset reference = headers.Date ?? DateTimeOffset.UtcNow;
            double seconds = (retryAfter.Date.Value - reference).TotalSeconds - ReadAgeSeconds(headers);

            return seconds < 0 ? 0 : seconds;
        }

        /// <summary>
        /// Parses one JSON hint into a non-negative, finite number of seconds.
        /// </summary>
        private static bool TryParseSeconds(JToken token, out double seconds)
        {
            seconds = 0;

            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.None)
                return false;

            try
            {
                if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
                {
                    seconds = token.Value<double>();
                }
                else if (token.Type == JTokenType.String)
                {
                    string raw = token.Value<string>();
                    if (string.IsNullOrWhiteSpace(raw))
                        return false;

                    if (!double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
                        return false;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is ArgumentException)
            {
                seconds = 0;
                return false;
            }

            return !(double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0);
        }

        /// <summary>
        /// Age of the response in a shared cache, used to discount an HTTP-date hint.
        /// </summary>
        private static double ReadAgeSeconds(HttpResponseHeaders headers)
        {
            TimeSpan? age = headers.Age;
            if (!age.HasValue)
                return 0;

            double seconds = age.Value.TotalSeconds;
            return seconds > 0 ? seconds : 0;
        }

        /// <summary>
        /// Truncates a body for logging so a huge payload cannot flood the log.
        /// </summary>
        private static string Preview(string body)
        {
            if (string.IsNullOrEmpty(body))
                return null;

            var trimmed = body.Trim();
            return trimmed.Length > MaxErrorPreviewLength
                ? trimmed.Substring(0, MaxErrorPreviewLength) + "..."
                : trimmed;
        }

        /// <summary>
        /// Reads <c>code</c> and <c>type</c> from the root of the body when the provider does not nest them under <c>error</c>.
        /// </summary>
        private static void ReadRootLevelCodeAndType(JObject parsed, ApiErrorInfo info)
        {
            if (info.Code == null && string.IsNullOrWhiteSpace(info.RawCode))
            {
                var codeToken = parsed["code"];
                if (codeToken != null && codeToken.Type != JTokenType.Null)
                {
                    var rawCode = codeToken.ToString();
                    if (int.TryParse(rawCode, out int numeric))
                        info.Code = numeric;
                    else if (!string.IsNullOrWhiteSpace(rawCode))
                        info.RawCode = rawCode;
                }
            }

            if (string.IsNullOrWhiteSpace(info.Type))
            {
                var type = parsed["type"]?.ToString();
                if (!string.IsNullOrWhiteSpace(type))
                    info.Type = type;
            }
        }

        /// <summary>
        /// Reads the size hints llama.cpp puts in a context-overflow error.
        /// </summary>
        private static void ReadSizeHints(JObject parsed, JObject errorObj, ApiErrorInfo info)
        {
            info.PromptTokens = ReadInt(parsed, errorObj, "n_prompt_tokens");
            info.ContextLength = ReadInt(parsed, errorObj, "n_ctx");
        }

        /// <summary>
        /// Looks for an integer field on the error object first, then on the root of the body.
        /// </summary>
        private static int? ReadInt(JObject root, JObject errorObj, string name)
        {
            if (TryGetInt(errorObj?[name], out int nested))
                return nested;

            if (TryGetInt(root[name], out int top))
                return top;

            return null;
        }

        private static bool TryGetInt(JToken token, out int value)
        {
            value = 0;
            if (token == null || token.Type == JTokenType.Null)
                return false;

            if (token.Type == JTokenType.Integer)
            {
                value = token.Value<int>();
                return true;
            }

            return int.TryParse(token.ToString(), out value);
        }
    }
}
