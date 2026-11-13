using System;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Persistence;

namespace LMLocal.Application.SubAgents.ReadRefs
{
    /// <summary>
    /// What one SubAgent run has read and found: the read and search ledgers filled from successful tool results,
    /// used to expand the ref markers of the final answer.
    /// </summary>
    internal sealed class SubAgentRunEvidence
    {
        private readonly ReadLedger _readLedger = new ReadLedger();
        private readonly SearchLedger _searchLedger = new SearchLedger();

        /// <summary>True when no tool result has been recorded yet.</summary>
        public bool IsEmpty => _readLedger.Count == 0 && _searchLedger.Count == 0;

        /// <summary>
        /// Records a successful tool result: read_file_lines goes to the read ledger, search_file_content and the
        /// symbol tools go to the search ledger; other tools are ignored.
        /// </summary>
        public void Record(string toolName, object result)
        {
            if (string.Equals(toolName, ReadResultRecorder.ReadFileLinesToolName, StringComparison.OrdinalIgnoreCase))
            {
                ReadResultRecorder.TryRecord(_readLedger, result);
            }
            else if (string.Equals(toolName, SearchResultRecorder.SearchFileContentToolName, StringComparison.OrdinalIgnoreCase))
            {
                SearchResultRecorder.TryRecord(_searchLedger, result);
            }
            else if (SymbolReferenceRecorder.IsSymbolTool(toolName))
            {
                SymbolReferenceRecorder.TryRecord(_searchLedger, result);
            }
        }

        /// <summary>
        /// Expands the ref markers of a final answer; returns null for empty content. Runs without evidence too, so a
        /// marker written before any tool result was recorded is reported as unresolved.
        /// </summary>
        public Task<ReadExpandResult> ExpandAsync(string content, IFileSystem fileSystem, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(content))
                return Task.FromResult<ReadExpandResult>(null);

            var expander = new ReadRefExpander(_readLedger, fileSystem, _searchLedger);
            return expander.ExpandAsync(content, cancellationToken);
        }
    }
}
