namespace LMLocal.Application.SubAgents
{
    /// <summary>
    /// Why a SubAgent run failed.
    /// </summary>
    public enum SubAgentFailureKind
    {
        /// <summary>The run succeeded.</summary>
        None = 0,

        /// <summary>Transient provider failure: network, 408, 5xx, a broken stream or an unexpected stream cancellation.</summary>
        Provider,

        /// <summary>Provider error a repeated run cannot fix, or a rate limit the round has already handled.</summary>
        ProviderNotRetryable,

        /// <summary>The agent watchdog timeout expired.</summary>
        Timeout,

        /// <summary>The model stopped on the length limit or on a content filter.</summary>
        Incomplete,

        /// <summary>The user stopped the run.</summary>
        Cancelled,

        /// <summary>Logical agent failure: round limit, tools in the final round, refused recovery, empty answer.</summary>
        Agent,

        /// <summary>The request itself is incomplete: no prompt or no model.</summary>
        InvalidRequest,

        /// <summary>Anything else.</summary>
        Unexpected
    }
}
