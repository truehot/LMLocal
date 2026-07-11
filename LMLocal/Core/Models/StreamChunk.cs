namespace LMLocal.Core.Models
{
    /// <summary>
    /// Represents the type of content in a streamed chunk from LLM.
    /// Supports multiple LLM providers: Nemotron, Qwen, Gemma, OpenAI.
    /// </summary>
    internal enum ChunkKind
    {
        /// <summary>
        /// Regular text content (content field)
        /// </summary>
        Content,

        /// <summary>
        /// Reasoning or internal monologue (reasoning_content field).
        /// </summary>
        Reasoning,

        /// <summary>
        /// Tool call arguments in JSON format (OpenAI-compatible format for Qwen/Gemma/OpenAI).
        /// </summary>
        ToolCallArguments,

        /// <summary>
        /// Raw tool call block (complete &lt;tool_call&gt;...&lt;/tool_call&gt; from Nemotron/DeepSeek).
        /// </summary>
        ToolCallRaw,

        /// <summary>
        /// Completion metadata: finish_reason, token usage, refusal.
        /// </summary>
        Completion,

        /// <summary>
        /// Server-side error from SSE stream (OpenAI-compatible format).
        /// </summary>
        Error
    }

    /// <summary>
    /// Base class for all SSE stream chunks from LLM.
    /// </summary>
    internal abstract class StreamChunk
    {
        /// <summary>
        /// The type of content in this chunk.
        /// </summary>
        public ChunkKind Kind { get; }

        protected StreamChunk(ChunkKind kind)
        {
            Kind = kind;
        }

        public abstract bool IsEmpty { get; }
    }

    /// <summary>
    /// Text content chunk (content or reasoning).
    /// </summary>
    internal class TextStreamChunk : StreamChunk
    {
        /// <summary>
        /// The actual text content (may be partial for streaming).
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Tool call index for parallel function invocations.
        /// </summary>
        public int? ToolCallIndex { get; }

        public TextStreamChunk(string text, ChunkKind kind) : base(kind)
        {
            Text = text;
            ToolCallIndex = null;
        }

        /// <summary>
        /// Creates a tool call arguments chunk with index for parallel tool calls.
        /// </summary>
        public TextStreamChunk(string text, ChunkKind kind, int? toolCallIndex) : base(kind)
        {
            Text = text;
            ToolCallIndex = toolCallIndex;
        }

        public override bool IsEmpty => string.IsNullOrEmpty(Text);

        /// <summary>
        /// Helper property to detect XML-formatted tool calls from Nemotron models.
        /// </summary>
        public bool IsXmlToolCall => Kind == ChunkKind.Reasoning && Text?.Contains("<tool_call>") == true;
    }

    /// <summary>
    /// Tool call metadata chunk (index, id, function name).
    /// </summary>
    internal class ToolCallMetadataChunk : StreamChunk
    {
        /// <summary>
        /// Zero-based index of the tool call (for parallel function invocations).
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Unique identifier for this tool call (e.g., "call_abc123").
        /// </summary>
        public string CallId { get; }

        /// <summary>
        /// Function name being called (e.g., "search_in_files").
        /// </summary>
        public string FunctionName { get; }

        /// <summary>
        /// Initial fragment of tool call arguments if they arrive in the same chunk as metadata.
        /// </summary>
        public string InitialArguments { get; }

        public ToolCallMetadataChunk(int index, string callId, string functionName, string initialArguments = null)
            : base(ChunkKind.ToolCallArguments)
        {
            Index = index;
            CallId = callId;
            FunctionName = functionName;
            InitialArguments = initialArguments;
        }

        public override bool IsEmpty => string.IsNullOrEmpty(CallId) && string.IsNullOrEmpty(FunctionName);
    }

    /// <summary>
    /// Completion chunk containing final data that arrives once at the end of the stream.
    /// </summary>
    internal class CompletionStreamChunk : StreamChunk
    {
        /// <summary>
        /// Reason why the model stopped generating: "stop", "length", "tool_calls", "content_filter".
        /// </summary>
        public string FinishReason { get; }

        /// <summary>
        /// Total tokens consumed in this request (prompt + completion).
        /// </summary>
        public int? TotalTokens { get; }

        /// <summary>
        /// Number of tokens in the input prompt.
        /// </summary>
        public int? PromptTokens { get; }

        /// <summary>
        /// Number of tokens generated in the completion.
        /// </summary>
        public int? CompletionTokens { get; }

        /// <summary>
        /// Number of tokens spent on model reasoning (for reasoning-capable models).
        /// </summary>
        public int? ReasoningTokens { get; }

        /// <summary>
        /// Text containing model's refusal reason if the model declined to respond.
        /// </summary>
        public string Refusal { get; }

        /// <summary>
        /// Server-side fingerprint of the model configuration.
        /// </summary>
        public string SystemFingerprint { get; }

        public CompletionStreamChunk(
            string finishReason = null,
            int? totalTokens = null,
            int? promptTokens = null,
            int? completionTokens = null,
            int? reasoningTokens = null,
            string refusal = null,
            string systemFingerprint = null)
            : base(ChunkKind.Completion)
        {
            FinishReason = finishReason;
            TotalTokens = totalTokens;
            PromptTokens = promptTokens;
            CompletionTokens = completionTokens;
            ReasoningTokens = reasoningTokens;
            Refusal = refusal;
            SystemFingerprint = systemFingerprint;
        }

        public override bool IsEmpty =>
            string.IsNullOrEmpty(FinishReason) &&
            !TotalTokens.HasValue &&
            string.IsNullOrEmpty(Refusal) &&
            string.IsNullOrEmpty(SystemFingerprint);
    }

    /// <summary>
    /// Error event received from SSE stream (OpenAI-compatible format).
    /// </summary>
    internal class ErrorStreamChunk : StreamChunk
    {
        /// <summary>
        /// Human-readable error description (e.g. "Cannot find model ...").
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Error type from the server (e.g. "invalid_request_error", "server_error").
        /// Null if not provided.
        /// </summary>
        public string ErrorType { get; }

        /// <summary>
        /// Error code from the server (e.g. "model_not_found", "rate_limit_exceeded").
        /// </summary>
        public string ErrorCode { get; }

        public ErrorStreamChunk(string message, string errorType, string errorCode)
            : base(ChunkKind.Error)
        {
            Message = message;
            ErrorType = errorType;
            ErrorCode = errorCode;
        }

        public override bool IsEmpty => string.IsNullOrEmpty(Message);
    }
}
