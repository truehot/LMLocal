using System;
using System.Net.Http;
using LMLocal.Core.Models;

namespace LMLocal.Core.Exceptions
{
    /// <summary>
    /// Exception thrown when an LLM API returns an error.
    /// </summary>
    internal class ApiException : HttpRequestException
    {
        /// <summary>
        /// Structured error information parsed from the API response.
        /// </summary>
        public ApiErrorInfo ErrorInfo { get; }

        /// <summary>
        /// HTTP status code, if available.
        /// </summary>
        public int? HttpStatusCode { get; }

        public ApiException(ApiErrorInfo errorInfo, int? httpStatusCode = null)
            : base(errorInfo?.Message ?? "Unknown API error")
        {
            ErrorInfo = errorInfo ?? new ApiErrorInfo { Message = "Unknown API error" };
            HttpStatusCode = httpStatusCode;
        }

        public ApiException(string message, Exception inner)
            : base(message, inner)
        {
            ErrorInfo = new ApiErrorInfo { Message = message };
        }
    }
}
