namespace LMLocal.Application.Tool
{
    /// <summary>
    /// Structured result contract that tool sources.
    /// </summary>
    public interface IToolExecutionOutcome
    {
        /// <summary>
        /// True when the tool run completed successfully.
        /// </summary>
        bool Success { get; }

        /// <summary>
        /// Error text when <see cref="Success"/> is false; otherwise null/empty.
        /// </summary>
        string Error { get; }

        /// <summary>
        /// The meaningful payload of a successful run.
        /// </summary>
        object Result { get; }
    }
}
