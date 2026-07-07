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

                        if (errorObj["code"] != null && int.TryParse(errorObj["code"].ToString(), out int code))
                            info.Code = code;

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
                    if (errorObj != null)
                    {
                        info.Message = errorObj.ToString();
                    }
                    else
                    {
                        var preview = rawResponse.Length > MaxErrorPreviewLength
                            ? rawResponse.Substring(0, MaxErrorPreviewLength) + "..."
                            : rawResponse;
                        InternalLogger.Debug($"ApiErrorParser: using fallback preview: {preview}");
                        info.Message = preview;
                    }
                }

                return info;
            }
            catch (JsonException ex)
            {
                InternalLogger.Warn($"ApiErrorParser: Invalid JSON. {ex.Message}");
                var fallback = rawResponse?.Trim();
                if (string.IsNullOrEmpty(fallback))
                    return new ApiErrorInfo { Message = UnknownErrorMessage };

                var preview = fallback.Length > MaxErrorPreviewLength
                    ? fallback.Substring(0, MaxErrorPreviewLength) + "..."
                    : fallback;
                return new ApiErrorInfo { Message = preview };
            }
        }
    }
}
