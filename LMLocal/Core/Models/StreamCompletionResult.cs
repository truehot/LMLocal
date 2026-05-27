using System.Collections.Generic;

namespace LMLocal.Core.Models
{
    /// <summary>
    /// Complete result of a stream processing operation.
    /// Contains all metadata accumulated during streaming (not the content itself, which was already streamed via callbacks).
    /// </summary>
    internal class StreamCompletionResult
    {
        /// <summary>
        /// The complete generated response text (content only, excluding reasoning).
        /// </summary>
        public string ContentResponse { get; set; }

        /// <summary>
        /// Reason the model stopped generating.
        /// Values: "stop" (natural completion), "length" (max tokens), "tool_calls" (tools invoked), "content_filter" (safety).
        /// Null if stream ended without explicit completion reason.
        /// </summary>
        public string FinishReason { get; set; }

        /// <summary>
        /// Token usage statistics for this generation request.
        /// Contains prompt tokens, completion tokens, total tokens, and reasoning tokens if applicable.
        /// </summary>
        public TokenUsageMetadata TokenUsage { get; set; }

        /// <summary>
        /// If FinishReason is "content_filter" or model refused to respond, contains the refusal reason.
        /// Null if no refusal occurred.
        /// </summary>
        public string RefusalReason { get; set; }

        /// <summary>
        /// Server-side fingerprint of the model configuration.
        /// Useful for detecting backend changes that might affect determinism during testing.
        /// Null if not provided by the server.
        /// </summary>
        public string SystemFingerprint { get; set; }

        /// <summary>
        /// Tool calls detected and collected during generation.
        /// Contains metadata (function name, call ID, index) and accumulated arguments for each tool call.
        /// Empty list if no tool calls were made.
        /// </summary>
        public IReadOnlyList<ToolCallRecord> ToolCalls { get; set; }

        /// <summary>
        /// Indicates whether stream was cancelled by user (via StopExecution).
        /// If true, generation was interrupted and result may be incomplete.
        /// </summary>
        public bool WasCancelled { get; set; }

        /// <summary>
        /// Error message if stream processing encountered an error.
        /// Null if processing completed successfully or was cancelled without error.
        /// </summary>
        public string ErrorMessage { get; set; }
    }
    /// <summary>
    /// Token usage breakdown for a single generation request.
    /// </summary>
    internal class TokenUsageMetadata
    {
        /// <summary>
        /// Total tokens used: prompt tokens + completion tokens.
        /// Null if not provided by the server.
        /// </summary>
        public int? TotalTokens { get; set; }

        /// <summary>
        /// Number of tokens in the input prompt.
        /// Null if not provided by the server.
        /// </summary>
        public int? PromptTokens { get; set; }

        /// <summary>
        /// Number of tokens generated in the completion.
        /// Null if not provided by the server.
        /// </summary>
        public int? CompletionTokens { get; set; }

        /// <summary>
        /// Number of tokens spent on model reasoning.
        /// Only populated for reasoning-capable models (e.g., o1-like models).
        /// Null if model doesn't support extended reasoning or no reasoning was used.
        /// </summary>
        public int? ReasoningTokens { get; set; }
    }

    /// <summary>
    /// Represents a single tool call invocation extracted from the stream.
    /// Tool call metadata arrives at the start of tool invocation, arguments stream as JSON fragments.
    /// </summary>
    internal class ToolCallRecord
    {
        /// <summary>
        /// Zero-based index for parallel tool calls.
        /// When multiple tools are invoked in one generation, each gets a unique index.
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Unique identifier for this tool call invocation (e.g., "call_abc123").
        /// Used to correlate tool results back to this specific invocation.
        /// </summary>
        public string CallId { get; set; }

        /// <summary>
        /// Function name to invoke (e.g., "search_in_files", "get_code_context").
        /// Determines which tool handler processes this invocation.
        /// </summary>
        public string FunctionName { get; set; }

        /// <summary>
        /// Complete JSON-formatted arguments for this tool call.
        /// Accumulated from streaming JSON fragments during generation.
        /// Valid JSON that can be deserialized to the tool's expected parameter type.
        /// </summary>
        public string ArgumentsJson { get; set; }
    }
}
