using System;
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

                            var retry = metadata["retry_after_seconds"] ?? metadata["retry_after_seconds_raw"];
                            if (retry != null && double.TryParse(retry.ToString(), out double secs))
                                info.RetryAfterSeconds = secs;
                        }

                        if (!info.RetryAfterSeconds.HasValue && errorObj["retry_after"] != null)
                        {
                            if (double.TryParse(errorObj["retry_after"].ToString(), out double secs))
                                info.RetryAfterSeconds = secs;
                        }
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
