using System;
using LMLocal.Core.Exceptions;
using LMLocal.Core.Models;

namespace LMLocal.Application.ChatSessionStream
{
    /// <summary>
    /// Decides whether a finished generation round failed because the request did not fit the model context window.
    /// </summary>
    internal static class ContextOverflowDetector
    {
        /// <summary>Auth, timeout and rate-limit failures are never a context overflow.</summary>
        private static readonly int[] RejectStatuses = { 401, 403, 408, 429 };

        /// <summary>Server failures: treated as overflow only when a structured context marker is present.</summary>
        private static readonly int[] ServerErrorStatuses = { 500, 502, 503, 504 };

        private const string ContextLengthExceededCode = "context_length_exceeded";
        private const string ContextSizeExceededType = "exceed_context_size_error";

        /// <summary>How close to a limit counts as "reached it", to absorb provider rounding.</summary>
        private const double LengthTolerance = 0.95;

        /// <summary>
        /// Fallback keywords for providers (or proxies) that send no structured code and only a message.
        /// </summary>
        private static readonly string[] TextMarkers =
        {
            "context length",
            "maximum context length",
            "exceed_context_size",
            "available context size",
            "n_keep",
            "prompt is too long"
        };

        /// <summary>
        /// True when the round result is a context overflow that may be recovered by compacting the history.
        /// </summary>
        public static bool IsContextOverflow(StreamCompletionResult result, int? maxOutputTokens = null, int? contextLength = null)
        {
            if (result == null)
                return false;

            int? status = result.HttpStatusCode;

            if (status.HasValue && Contains(RejectStatuses, status.Value))
                return false;

            if (status == 413)
                return true;

            if (IsContentFilter(result.FinishReason))
                return false;

            if (IsLengthStop(result.FinishReason))
                return IsLengthCausedByWindow(result, maxOutputTokens, contextLength);

            if (MatchesStructural(result.ErrorCode, ContextLengthExceededCode) ||
                MatchesStructural(result.ErrorType, ContextSizeExceededType))
                return true;

            if (status.HasValue && Contains(ServerErrorStatuses, status.Value))
                return false;

            return MatchesContextOverflowText(result.ErrorMessage);
        }

        /// <summary>
        /// Tells an answer cut off by the output limit from an answer cut off by the window.
        /// </summary>
        private static bool IsLengthCausedByWindow(StreamCompletionResult result, int? maxOutputTokens, int? contextLength)
        {
            var usage = result.TokenUsage;
            if (usage == null)
                return false;

            int? completion = usage.CompletionTokens;
            int? prompt = usage.PromptTokens;

            bool completionNearCap = completion.HasValue && maxOutputTokens.HasValue && maxOutputTokens.Value > 0 &&
                                     completion.Value >= maxOutputTokens.Value * LengthTolerance;

            bool windowNearFull = prompt.HasValue && completion.HasValue && contextLength.HasValue &&
                                  contextLength.Value > 0 &&
                                  prompt.Value + completion.Value >= contextLength.Value * LengthTolerance;


            if (windowNearFull)
                return true;

            if (completionNearCap)
                return false;

            if (contextLength.HasValue && contextLength.Value > 0 && prompt.HasValue && completion.HasValue)
                return false;

            return false;
        }

        /// <summary>
        /// True when a failed request threw because it did not fit the context window.
        /// </summary>
        public static bool IsContextOverflowError(ApiException exception)
        {
            if (exception == null)
                return false;

            var info = exception.ErrorInfo;
            int? status = exception.HttpStatusCode ?? info?.Code;

            if (status == 413)
                return true;

            if (status.HasValue && Contains(RejectStatuses, status.Value))
                return false;

            if (MatchesStructural(info?.RawCode, ContextLengthExceededCode) ||
                MatchesStructural(info?.Type, ContextSizeExceededType))
                return true;

            if (status.HasValue && Contains(ServerErrorStatuses, status.Value))
                return false;

            return MatchesContextOverflowText(info?.Message);
        }

        private static bool MatchesStructural(string value, string expected)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   string.Equals(value.Trim(), expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesContextOverflowText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            for (int i = 0; i < TextMarkers.Length; i++)
            {
                if (text.IndexOf(TextMarkers[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static bool IsLengthStop(string finishReason)
        {
            return !string.IsNullOrEmpty(finishReason) &&
                   string.Equals(finishReason.Trim(), "length", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsContentFilter(string finishReason)
        {
            return !string.IsNullOrEmpty(finishReason) &&
                   string.Equals(finishReason.Trim(), "content_filter", StringComparison.OrdinalIgnoreCase);
        }

        private static bool Contains(int[] values, int value)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == value)
                    return true;
            }

            return false;
        }
    }
}
