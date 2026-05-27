namespace LMLocal.Application.ChatSession
{
    /// <summary>
    /// Defines possible states in the chat session lifecycle.
    /// </summary>
    internal enum ChatSessionState
    {
        Initial,
        Generating,
        ProcessingResult,
        ExecutingTools,
        Completing,
        CompactingHistory,
        Error,
        Cancelled,
        Terminated
    }
}
