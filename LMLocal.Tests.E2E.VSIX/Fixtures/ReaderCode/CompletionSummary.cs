using System.Collections.Generic;

namespace BenchFixtures.ReaderCode
{
    /// <summary>
    /// Benchmark fixture (frozen, not compiled): the outcome of one streamed completion.
    /// Scenario A2.3 depends on its content.
    /// </summary>
    internal sealed class CompletionSummary
    {
        /// <summary>Text returned by the model.</summary>
        public string ContentText { get; set; }

        /// <summary>Tool call frames in arrival order.</summary>
        public IReadOnlyList<FrameRecord> ToolFrames { get; set; }

        /// <summary>Why the model stopped: 0 = stop, 1 = length, 2 = tool calls, 3 = error.</summary>
        public int FinishReasonCode { get; set; }

        /// <summary>HTTP status of a failed request; null when the request succeeded.</summary>
        public int? HttpStatus { get; set; }

        /// <summary>True when the model stopped to call tools.</summary>
        public bool HasToolFrames => ToolFrames != null && ToolFrames.Count > 0;
    }
}
