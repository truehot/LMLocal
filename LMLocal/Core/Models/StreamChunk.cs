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
        /// For Nemotron, may contain XML-formatted tool calls: &lt;tool_call&gt;...&lt;/tool_call&gt;
        /// </summary>
        Reasoning,

        /// <summary>
        /// Tool call arguments in JSON format (OpenAI-compatible format for Qwen/Gemma/OpenAI).
        /// Accumulated from delta.tool_calls[i].function.arguments across multiple chunks.
        /// </summary>
        ToolCallArguments,

        /// <summary>
        /// Completion metadata: finish_reason, token usage, refusal.
        /// Arrives once at the end of the stream.
        /// </summary>
        Completion
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
        /// Only populated when Kind is ToolCallArguments.
        /// Indicates which tool call this arguments chunk belongs to.
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
        /// Returns true if this is reasoning content containing &lt;tool_call&gt; tags.
        /// </summary>
        public bool IsXmlToolCall => Kind == ChunkKind.Reasoning && Text?.Contains("<tool_call>") == true;
    }

    /// <summary>
    /// Tool call metadata chunk (index, id, function name).
    /// Arrives once at the beginning of a tool_calls sequence.
    /// May include initial arguments fragment if they arrive in the same chunk.
    /// </summary>
    internal class ToolCallMetadataChunk : StreamChunk
    {
        /// <summary>
        /// Zero-based index of the tool call (for parallel function invocations).
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Unique identifier for this tool call (e.g., "call_abc123").
        /// Used to correlate the tool result back to this specific invocation.
        /// </summary>
        public string CallId { get; }

        /// <summary>
        /// Function name being called (e.g., "search_in_files").
        /// </summary>
        public string FunctionName { get; }

        /// <summary>
        /// Initial fragment of tool call arguments if they arrive in the same chunk as metadata.
        /// May be null if arguments arrive in subsequent chunks or are not present in this chunk.
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
        /// Used for billing and quota tracking.
        /// Only populated when usage data is present.
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
        /// Useful for detecting backend changes that might affect determinism during testing.
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
}
