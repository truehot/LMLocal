namespace LMLocal.Infrastructure.WebView
{
    /// <summary>
    /// Type of message sent from backend to WebView2 frontend.
    /// Supports streaming, tool execution, session lifecycle, and completion metadata.
    /// </summary>
    internal enum WebView2MessageType
    {
        /// <summary>
        /// Extended thinking tokens (reasoning_content field).
        /// Streamed incrementally during model thinking phase.
        /// </summary>
        StreamThought,

        /// <summary>
        /// Response text tokens (content field).
        /// Streamed incrementally during generation phase.
        /// </summary>
        StreamContent,

        /// <summary>
        /// Generation completed normally.
        /// Sent before ChatSessionComplete to signal end of streaming.
        /// </summary>
        StreamEnd,

        /// <summary>
        /// History compaction(summarization)  started.
        /// </summary>
        CompactionStart,

        /// <summary>
        /// History compaction(summarization)  completed.
        /// </summary>
        CompactionEnd,

        /// <summary>
        /// Tool invocation detected during generation.
        /// Contains tool function name, call ID, and arguments.
        /// </summary>
        StreamToolCall,

        /// <summary>
        /// Tool execution completed.
        /// Signals end of tool execution phase, resuming generation.
        /// </summary>
        StreamToolEnd,

        /// <summary>
        /// Session begins (first message of a conversation generation cycle).
        /// Signals UI to initialize session state and clear previous status.
        /// </summary>
        ChatSessionStart,

        /// <summary>
        /// Session completed with metadata (finish reason, token counts).
        /// Last message before session ends.
        /// </summary>
        ChatSessionComplete,

        /// <summary>
        /// Session ended with error state.
        /// Contains error description.
        /// </summary>
        ChatSessionError,

        /// <summary>
        /// Session cancelled by user or system.
        /// Contains cancellation reason.
        /// </summary>
        ChatSessionCancelled,

        /// <summary>
        /// Tool iteration loop started (after tool execution, before next generation).
        /// Signals UI that a new iteration of the tool execution cycle is beginning.
        /// </summary>
        ChatSessionIterating,

        /// <summary>
        /// Snapshot files list has changed (files were backed up, rolled back, or committed).
        /// Sent after tool execution or when snapshot operations complete.
        /// </summary>
        SnapshotFilesChanged
    }
}
