using LMLocal.Infrastructure.Tooling;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling
{
    /// <summary>
    /// The "## Tool Results" section carries the reads of one step as one block per file: union of the lines,
    /// later reads winning, sorted by line number.
    /// </summary>
    [TestFixture]
    public class ToolResultMarkdownFormatterMergeTests
    {
        private static string Format(params (string FunctionName, string Json)[] results)
        {
            return new ToolResultMarkdownFormatter()
                .FormatToolResults(results)
                .Replace("\r\n", "\n");
        }

        [Test]
        public void OverlappingReads_OneBlock_SortedByLineNumber()
        {
            var markdown = Format(
                ("read_file_lines", "{\"file_path\":\"a.cs\",\"content\":[[\"10\",\"ten\"],[\"2\",\"two\"]],\"success\":true}"),
                ("read_file_lines", "{\"file_path\":\"a.cs\",\"content\":[[\"3\",\"three\"]],\"success\":true}"));

            Assert.That(markdown, Does.Contain("2: two\n3: three\n10: ten"), "line numbers, not read order");
        }

        [Test]
        public void LaterReadWins_ForTheSameLineNumber()
        {
            var markdown = Format(
                ("read_file_lines", "{\"file_path\":\"a.cs\",\"content\":[[\"7\",\"old\"]],\"success\":true}"),
                ("read_file_lines", "{\"file_path\":\"a.cs\",\"content\":[[\"7\",\"new\"]],\"success\":true}"));

            Assert.That(markdown, Does.Contain("7: new"));
            Assert.That(markdown, Does.Not.Contain("old"));
        }

        [Test]
        public void HasMoreResults_ComesFromTheLastReadThatContributedLines()
        {
            var truncatedSecond = Format(
                ("read_file_lines", "{\"file_path\":\"a.cs\",\"content\":[[\"1\",\"one\"]],\"has_more_results\":false,\"success\":true}"),
                ("read_file_lines", "{\"file_path\":\"a.cs\",\"content\":[[\"2\",\"two\"]],\"has_more_results\":true,\"success\":true}"));

            Assert.That(truncatedSecond, Does.Contain("truncated, more lines available"));

            var completeSecond = Format(
                ("read_file_lines", "{\"file_path\":\"a.cs\",\"content\":[[\"1\",\"one\"]],\"has_more_results\":true,\"success\":true}"),
                ("read_file_lines", "{\"file_path\":\"a.cs\",\"content\":[[\"2\",\"two\"]],\"has_more_results\":false,\"success\":true}"));

            Assert.That(completeSecond, Does.Not.Contain("truncated"));
        }

        [Test]
        public void PlainStringContent_CannotBeMerged_LatestBlockOnly()
        {
            var markdown = Format(
                ("read_file_lines", "{\"file_path\":\"a.cs\",\"content\":\"version one\",\"success\":true}"),
                ("read_file_lines", "{\"file_path\":\"a.cs\",\"content\":\"version two\",\"success\":true}"));

            Assert.That(markdown, Does.Contain("version two"));
            Assert.That(markdown, Does.Not.Contain("version one"));
        }

        [Test]
        public void FailedReadBlock_IsSkipped()
        {
            var markdown = Format(
                ("read_file_lines", "{\"files\":["
                                    + "{\"file_path\":\"a.cs\",\"content\":[[\"1\",\"one\"]],\"success\":true},"
                                    + "{\"file_path\":\"b.cs\",\"error_message\":\"File not found\",\"success\":false}],\"success\":true}"));

            Assert.That(markdown, Does.Contain("1: one"));
            Assert.That(markdown, Does.Not.Contain("b.cs"));
        }

        [Test]
        public void WriteResults_RenderNothingButTheChangedLine()
        {
            var markdown = Format(
                ("search_file_content", "{\"results\":[],\"success\":true}"),
                ("replace_file_content", "{\"success\":true,\"file_path\":\"a.cs\"}"));

            Assert.That(markdown, Is.EqualTo("## Tool Results\n\nChanged in this step (re-read before use): a.cs"),
                "a write and an unknown tool produce no block of their own");
        }

        [Test]
        public void NothingToCarry_ReturnsEmptyString()
        {
            var markdown = Format(
                ("replace_file_lines", "{\"success\":false,\"file_path\":\"a.cs\"}"),
                ("get_active_document", "{\"success\":false,\"error_message\":\"No active document found.\"}"),
                ("read_file_lines", "{\"success\":false,\"error_message\":\"File not found\"}"));

            Assert.That(markdown, Is.Empty, "no header without content");
        }

        [Test]
        public void ChangedLine_IsAddedAfterTheFileBlocks()
        {
            var markdown = Format(
                ("read_file_lines", "{\"file_path\":\"a.cs\",\"content\":[[\"1\",\"one\"]],\"success\":true}"),
                ("delete_file", "{\"success\":true,\"file_path\":\"b.cs\"}"));

            Assert.That(markdown, Does.Contain("Changed in this step (re-read before use): b.cs (deleted)"));
            Assert.That(markdown.IndexOf("1: one", System.StringComparison.Ordinal),
                Is.LessThan(markdown.IndexOf("Changed in this step", System.StringComparison.Ordinal)));
        }

        [Test]
        public void WriteResultWithoutFilePath_WritesNothing()
        {
            var markdown = Format(
                ("read_file_lines", "{\"file_path\":\"a.cs\",\"content\":[[\"1\",\"one\"]],\"success\":true}"),
                ("replace_file_lines", "{\"success\":true}"));

            Assert.That(markdown, Does.Contain("1: one"), "a result that names no file cannot make a read stale");
            Assert.That(markdown, Does.Not.Contain("Changed in this step"));
        }

        [Test]
        public void DeletedThenCreatedAgain_IsListedOnceWithoutTheDeletedMarker()
        {
            var markdown = Format(
                ("delete_file", "{\"success\":true,\"file_path\":\"a.cs\"}"),
                ("create_file", "{\"success\":true,\"file_path\":\"a.cs\"}"));

            Assert.That(markdown, Does.Contain("Changed in this step (re-read before use): a.cs"));
            Assert.That(markdown, Does.Not.Contain("(deleted)"), "the last write to the file created it");
        }
    }
}
