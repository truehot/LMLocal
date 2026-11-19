using System.Collections.Generic;
using NUnit.Framework;

namespace BenchFixtures.ReviewCode
{
    /// <summary>
    /// Benchmark fixture (frozen, not compiled): the unit test of the reviewed deliverable of scenario A4.1.
    /// </summary>
    [TestFixture]
    public class ToolCallLedgerTests
    {
        [Test]
        public void ConsolidateLastExchange_KeepsToolCallsOfFinalAssistantByReference()
        {
            var toolCalls = new List<LedgerToolCall> { new LedgerToolCall { Name = "read_file_lines", Arguments = "{}" } };
            var ledger = new ToolCallLedger();
            ledger.Add(new LedgerMessage("user", "question"));
            ledger.Add(new LedgerMessage("assistant", "step"));
            ledger.Add(new LedgerMessage("assistant", "answer") { ToolCalls = toolCalls });

            ledger.ConsolidateLastExchange();

            Assert.That(ledger.Messages.Count, Is.EqualTo(2));
            Assert.That(ledger.Messages[1].Text, Is.EqualTo("answer"));
            Assert.That(ledger.Messages[1].ToolCalls, Is.SameAs(toolCalls));
        }
    }
}
