namespace LMLocal.Core.Models.Usage
{
    /// <summary>
    /// Who spent the tokens of one usage record.
    /// </summary>
    public enum UsageSource
    {
        /// <summary>The main chat: all rounds of one user request.</summary>
        Main,

        /// <summary>A single subagent run: all rounds of that run.</summary>
        Subagent,

        /// <summary>A single history compaction call (non-streaming, no generation speed).</summary>
        Compaction
    }
}
