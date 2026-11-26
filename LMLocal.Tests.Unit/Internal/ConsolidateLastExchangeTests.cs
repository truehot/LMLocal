using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Chat;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi.Provider;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    /// <summary>
    /// "Consolidate last completed exchange" carries the files read in the step, one block per file, without the
    /// reads a later write made stale, and names the files the step changed.
    /// </summary>
    [TestFixture]
    public class ConsolidateLastExchangeTests
    {
        private const string Answer = "done";

        [Test]
        public async Task SameFileReadTwice_OneBlock_LaterReadWinsForTheSameLine()
        {
            var user = await ConsolidateAsync(
                ("read_file_lines", "{\"file_path\":\"a.cs\",\"content\":[[\"1\",\"one\"],[\"2\",\"two\"]],\"success\":true}"),
                ("read_file_lines", "{\"file_path\":\"a.cs\",\"content\":[[\"2\",\"two-new\"],[\"3\",\"three\"]],\"success\":true}"));

            Assert.That(Count(user, "**`a.cs`**"), Is.EqualTo(1), "one copy per file");
            Assert.That(user, Does.Contain("1: one\n2: two-new\n3: three"), "the union of both reads, the later one winning");
        }

        [Test]
        public async Task TwoFiles_TwoBlocks_InOrderOfFirstRead()
        {
            var user = await ConsolidateAsync(
                ("read_file_lines", "{\"files\":[{\"file_path\":\"a.cs\",\"content\":[[\"1\",\"a1\"]],\"success\":true},"
                                    + "{\"file_path\":\"b.cs\",\"content\":[[\"2\",\"b2\"]],\"success\":true}],\"success\":true}"));

            Assert.That(user, Does.Contain("1: a1").And.Contain("2: b2"));
            Assert.That(user.IndexOf("**`a.cs`**", StringComparison.Ordinal),
                Is.LessThan(user.IndexOf("**`b.cs`**", StringComparison.Ordinal)), "order of first read");
        }

        [Test]
        public async Task ReadThenSuccessfulWrite_BlockDropped_AndFileListedAsChanged()
        {
            var user = await ConsolidateAsync(
                ("read_file_lines", "{\"file_path\":\"a.cs\",\"content\":[[\"1\",\"one\"]],\"success\":true}"),
                ("replace_file_lines", "{\"success\":true,\"file_path\":\"a.cs\",\"lines_replaced\":1}"));

            Assert.That(user, Does.Not.Contain("1: one"), "a read the step made stale shows the wrong content and line numbers");
            Assert.That(user, Does.Contain("Changed in this step (re-read before use): a.cs"));
        }

        [Test]
        public async Task ReadWriteReadAgain_OnlyTheReadAfterTheWriteIsKept()
        {
            var user = await ConsolidateAsync(
                ("read_file_lines", "{\"file_path\":\"a.cs\",\"content\":[[\"1\",\"one\"]],\"success\":true}"),
                ("replace_file_content", "{\"success\":true,\"file_path\":\"a.cs\"}"),
                ("read_file_lines", "{\"file_path\":\"a.cs\",\"content\":[[\"5\",\"five\"]],\"success\":true}"));

            Assert.That(user, Does.Contain("5: five"));
            Assert.That(user, Does.Not.Contain("1: one"));
            Assert.That(Count(user, "**`a.cs`**"), Is.EqualTo(1));
        }

        [Test]
        public async Task FailedWrite_ReadKept_NoChangedLine()
        {
            var user = await ConsolidateAsync(
                ("read_file_lines", "{\"file_path\":\"a.cs\",\"content\":[[\"1\",\"one\"]],\"success\":true}"),
                ("replace_file_lines", "{\"success\":false,\"file_path\":\"a.cs\",\"error_message\":\"old_lines not found\"}"));

            Assert.That(user, Does.Contain("1: one"), "a failed write does not change the file");
            Assert.That(user, Does.Not.Contain("Changed in this step"));
        }

        [Test]
        public async Task FormatDocumentBatch_SkippedFileKeepsItsRead_FormattedFileDoesNot()
        {
            var user = await ConsolidateAsync(
                ("read_file_lines", "{\"files\":[{\"file_path\":\"a.cs\",\"content\":[[\"1\",\"a1\"]],\"success\":true},"
                                    + "{\"file_path\":\"b.md\",\"content\":[[\"1\",\"b1\"]],\"success\":true}],\"success\":true}"),
                ("format_document", "{\"success\":true,\"total_files\":2,\"succeeded\":2,\"files\":["
                                    + "{\"file_path\":\"a.cs\",\"success\":true},"
                                    + "{\"file_path\":\"b.md\",\"success\":true,\"skipped\":true}]}"));

            Assert.That(user, Does.Not.Contain("a1"), "the formatted file changed after it was read");
            Assert.That(user, Does.Contain("b1"), "a skipped file was left unchanged");
            Assert.That(user, Does.EndWith("Changed in this step (re-read before use): a.cs"),
                "only the formatted file is listed as changed, not the skipped one");
        }

        [Test]
        public async Task DeleteFile_DropsTheReadAndMarksTheFileDeleted()
        {
            var user = await ConsolidateAsync(
                ("read_file_lines", "{\"file_path\":\"a.cs\",\"content\":[[\"1\",\"one\"]],\"success\":true}"),
                ("delete_file", "{\"success\":true,\"file_path\":\"a.cs\",\"deleted_successfully\":true}"));

            Assert.That(user, Does.Not.Contain("1: one"));
            Assert.That(user, Does.Contain("Changed in this step (re-read before use): a.cs (deleted)"));
        }

        [Test]
        public async Task PathsDifferingInCaseAndSlashDirection_AreOneFile()
        {
            var user = await ConsolidateAsync(
                ("read_file_lines", "{\"file_path\":\"src/Program.cs\",\"content\":[[\"1\",\"one\"]],\"success\":true}"),
                ("replace_file_content", "{\"success\":true,\"file_path\":\"src\\\\program.cs\"}"));

            Assert.That(user, Does.Not.Contain("1: one"));
            Assert.That(user, Does.Contain("Changed in this step (re-read before use): src\\program.cs"));
        }

        [Test]
        public async Task PlainStringContentReadTwice_OnlyTheLatestBlock()
        {
            var user = await ConsolidateAsync(
                ("read_file_lines", "{\"file_path\":\"a.cs\",\"content\":\"version one\",\"success\":true}"),
                ("read_file_lines", "{\"file_path\":\"a.cs\",\"content\":\"version two\",\"success\":true}"));

            Assert.That(user, Does.Contain("version two"));
            Assert.That(user, Does.Not.Contain("version one"));
            Assert.That(Count(user, "**`a.cs`**"), Is.EqualTo(1));
        }

        [Test]
        public async Task ActiveDocumentWrittenAfterwards_IsDropped()
        {
            var user = await ConsolidateAsync(
                ("get_active_document", "{\"file_path\":\"a.cs\",\"content\":\"class A { }\",\"success\":true}"),
                ("insert_file_lines", "{\"success\":true,\"file_path\":\"a.cs\",\"lines_inserted\":1}"));

            Assert.That(user, Does.Not.Contain("Active Document"), "the active document text was replaced by the write");
            Assert.That(user, Does.Contain("Changed in this step (re-read before use): a.cs"));
        }

        [Test]
        public async Task ActiveDocumentWithoutWrite_IsKept()
        {
            var user = await ConsolidateAsync(
                ("get_active_document", "{\"file_path\":\"a.cs\",\"content\":\"class A { }\",\"success\":true}"));

            Assert.That(user, Does.Contain("Active Document").And.Contain("class A { }"));
            Assert.That(user, Does.Not.Contain("Changed in this step"));
        }

        [Test]
        public async Task ChangedLine_ListsEachFileOnce_InOrderOfFirstWrite()
        {
            var user = await ConsolidateAsync(
                ("create_file", "{\"success\":true,\"file_path\":\"c.cs\"}"),
                ("replace_file_content", "{\"success\":true,\"file_path\":\"b.cs\"}"),
                ("replace_file_lines", "{\"success\":true,\"file_path\":\"c.cs\",\"lines_replaced\":2}"),
                ("delete_file", "{\"success\":true,\"file_path\":\"d.cs\"}"));

            Assert.That(user, Does.Contain("Changed in this step (re-read before use): c.cs, b.cs, d.cs (deleted)"));
        }

        [Test]
        public async Task ReplaceFileLinesBatchOfNoOps_IsNotAWrite()
        {
            var user = await ConsolidateAsync(
                ("read_file_lines", "{\"file_path\":\"a.cs\",\"content\":[[\"1\",\"one\"]],\"success\":true}"),
                ("replace_file_lines", "{\"success\":true,\"file_path\":\"a.cs\",\"edits\":[{\"start_line\":1,\"no_op\":true}]}"));

            Assert.That(user, Does.Contain("1: one"), "nothing was written, the read is still current");
            Assert.That(user, Does.Not.Contain("Changed in this step"));
        }

        [Test]
        public async Task NoReadsAndNoWrites_NoToolResultsSection()
        {
            var user = await ConsolidateAsync(("build_subagent", "Build succeeded."));

            Assert.That(user, Is.EqualTo("do the work"));
        }

        [Test]
        public async Task OnlyFailedOrNoOpWrites_NoToolResultsSection()
        {
            var user = await ConsolidateAsync(
                ("replace_file_lines", "{\"success\":false,\"file_path\":\"a.cs\",\"error_message\":\"old_lines not found\"}"),
                ("replace_file_lines", "{\"success\":true,\"file_path\":\"b.cs\",\"edits\":[{\"start_line\":1,\"no_op\":true}]}"),
                ("format_document", "{\"success\":true,\"file_path\":\"c.md\",\"skipped\":true}"));

            Assert.That(user, Is.EqualTo("do the work"), "nothing was written or read, so nothing is appended");
        }

        [Test]
        public async Task FailedActiveDocument_NotCarried()
        {
            var user = await ConsolidateAsync(
                ("get_active_document", "{\"file_path\":null,\"content\":\"\",\"success\":false,\"error_message\":\"No active document found.\"}"));

            Assert.That(user, Is.EqualTo("do the work"));
        }

        [Test]
        public async Task FailedSolutionOverview_NotCarried()
        {
            var user = await ConsolidateAsync(
                ("get_solution_overview", "{\"success\":false,\"error_message\":\"No solution is open.\"}"),
                ("read_file_lines", "{\"file_path\":\"a.cs\",\"content\":[[\"1\",\"one\"]],\"success\":true}"));

            Assert.That(user, Does.Not.Contain("**Solution:**"));
            Assert.That(user, Does.Contain("1: one"));
        }

        [Test]
        public async Task ResultsOfOtherTools_AreIgnored()
        {
            var user = await ConsolidateAsync(
                ("code_explorer_subagent", "File: src/Program.cs, lines 1-2\nusing System;"),
                ("search_file_content", "{\"results\":[{\"file_path\":\"a.cs\"}],\"success\":true}"),
                ("read_file_lines", "{\"file_path\":\"a.cs\",\"content\":[[\"1\",\"one\"]],\"success\":true}"));

            Assert.That(user, Does.Not.Contain("using System;").And.Not.Contain("code_explorer_subagent"));
            Assert.That(user, Does.Contain("1: one"));
        }

        // ---------- helpers ----------

        /// <summary>
        /// Consolidates one step made of the given tool results (in call order) and returns the user content of
        /// the consolidated history.
        /// </summary>
        private static async Task<string> ConsolidateAsync(params (string FunctionName, string Json)[] results)
        {
            var settings = new Mock<ISettingsManager>();
            settings.Setup(s => s.SystemPrompt).Returns("sys");

            var manager = new ChatHistoryManager(
                settings.Object,
                new Mock<IChatPersistenceService>().Object,
                new ProviderPolicy(),
                new ToolResultMarkdownFormatter());

            manager.AddUserMessage("do the work");

            if (results.Length > 0)
            {
                var calls = new List<ToolCallRecord>();
                var messages = new List<ChatMessage>();

                for (int i = 0; i < results.Length; i++)
                {
                    string callId = "c" + (i + 1);
                    calls.Add(new ToolCallRecord
                    {
                        Index = i,
                        CallId = callId,
                        FunctionName = results[i].FunctionName,
                        ArgumentsJson = "{}"
                    });
                    messages.Add(new ChatMessage("tool", results[i].Json, callId));
                }

                manager.AddAssistantMessage(null, calls);
                manager.AddToolExecutionResultMessages(messages);
            }

            manager.AddAssistantMessage(Answer);

            await manager.ConsolidateLastExchangeAsync();

            var history = manager.GetHistoryCopy();
            Assert.That(history.Count, Is.EqualTo(2));
            Assert.That(history[1].Content, Is.EqualTo(Answer), "the final answer is kept as is");

            // The formatter builds its lines with StringBuilder.AppendLine (Environment.NewLine).
            return ((string)history[0].Content).Replace("\r\n", "\n");
        }

        private static int Count(string text, string value)
        {
            int count = 0;
            int index = text.IndexOf(value, StringComparison.Ordinal);

            while (index >= 0)
            {
                count++;
                index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
            }

            return count;
        }
    }
}
