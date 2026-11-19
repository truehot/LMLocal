using System;
using System.Collections.Generic;

namespace BenchFixtures.ReviewCode
{
    /// <summary>
    /// Benchmark fixture (frozen, not compiled): the reviewed deliverable of scenario A4.1.
    /// Keeps chat entries; the ToolCalls copying of the final assistant entry lives in the private helper CopyToolCalls.
    /// </summary>
    internal sealed class ToolCallLedger
    {
        private readonly List<LedgerMessage> _messages = new List<LedgerMessage>();

        public IReadOnlyList<LedgerMessage> Messages => _messages;

        public void Add(LedgerMessage message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            _messages.Add(message);
        }

        /// <summary>
        /// Replaces everything after the last user message with one assistant message that keeps the text
        /// and the ToolCalls of the final assistant message.
        /// </summary>
        public void ConsolidateLastExchange()
        {
            int lastUser = _messages.FindLastIndex(m => m.Role == "user");
            if (lastUser < 0 || lastUser == _messages.Count - 1)
                return;

            LedgerMessage finalAssistant = _messages[_messages.Count - 1];
            var consolidated = new LedgerMessage("assistant", finalAssistant.Text)
            {
                ToolCalls = CopyToolCalls(finalAssistant)
            };

            _messages.RemoveRange(lastUser + 1, _messages.Count - lastUser - 1);
            _messages.Add(consolidated);
        }

        /// <summary>
        /// Returns the ToolCalls list of <paramref name="source"/> itself (copied by reference, not cloned).
        /// </summary>
        private static List<LedgerToolCall> CopyToolCalls(LedgerMessage source)
        {
            return source.ToolCalls;
        }
    }

    internal sealed class LedgerMessage
    {
        public LedgerMessage(string role, string text)
        {
            Role = role;
            Text = text;
        }

        public string Role { get; }

        public string Text { get; }

        public List<LedgerToolCall> ToolCalls { get; set; }
    }

    internal sealed class LedgerToolCall
    {
        public string Name { get; set; }

        public string Arguments { get; set; }
    }
}
