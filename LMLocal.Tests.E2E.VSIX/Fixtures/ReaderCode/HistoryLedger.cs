using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BenchFixtures.ReaderCode
{
    /// <summary>
    /// Benchmark fixture (frozen, not compiled): keeps chat entries and merges the last
    /// user/assistant exchange into one entry. Scenario A2.3 depends on its content.
    /// </summary>
    internal sealed class HistoryLedger
    {
        private readonly List<LedgerEntry> _entries = new List<LedgerEntry>();
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        public IReadOnlyList<LedgerEntry> Entries => _entries;

        public void Add(string role, string text)
        {
            _entries.Add(new LedgerEntry(role, text));
        }

        /// <summary>
        /// Replaces everything after the last user entry with a single assistant entry that keeps
        /// the text of the final assistant entry.
        /// </summary>
        public async Task MergeLastExchangeAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                int lastUser = _entries.FindLastIndex(e => e.Role == "user");
                if (lastUser < 0 || lastUser == _entries.Count - 1)
                    return;

                LedgerEntry finalAssistant = _entries.Last(e => e.Role == "assistant");
                _entries.RemoveRange(lastUser + 1, _entries.Count - lastUser - 1);
                _entries.Add(finalAssistant);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    /// <summary>One chat entry.</summary>
    internal sealed class LedgerEntry
    {
        public LedgerEntry(string role, string text)
        {
            Role = role ?? throw new ArgumentNullException(nameof(role));
            Text = text ?? string.Empty;
        }

        public string Role { get; }

        public string Text { get; }
    }
}
