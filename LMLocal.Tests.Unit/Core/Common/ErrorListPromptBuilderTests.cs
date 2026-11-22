using System;
using System.Collections.Generic;
using System.Linq;
using LMLocal.Core.Common;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class ErrorListPromptBuilderTests
    {
        // ────────────────────────────── helpers ──────────────────────────────

        private const string FullPath = @"C:\Solution\LMLocal\Commands\X.cs";
        private const string RelativePath = @"LMLocal\Commands\X.cs";

        private static ErrorListEntry Entry(string code = "CS0103", string severity = "error",
            string text = "The name 'foo' does not exist in the current context",
            string filePath = FullPath, int line = 41, int column = 6, string project = "LMLocal")
        {
            return new ErrorListEntry
            {
                ErrorCode = code,
                Severity = severity,
                Text = text,
                FilePath = filePath,
                Line = line,
                Column = column,
                ProjectName = project
            };
        }

        private static Func<string, string> Relative => _ => RelativePath;

        // ────────────────────────────── empty input ──────────────────────────────

        [Test]
        public void Build_NullEntries_ReturnsNull()
        {
            Assert.That(ErrorListPromptBuilder.Build("instruction", null, Relative), Is.Null);
        }

        [Test]
        public void Build_EmptyList_ReturnsNull()
        {
            Assert.That(
                ErrorListPromptBuilder.Build("instruction", new List<ErrorListEntry>(), Relative),
                Is.Null);
        }

        [Test]
        public void Build_OnlyEmptyEntries_ReturnsNull()
        {
            var entries = new[] { new ErrorListEntry(), null };

            Assert.That(ErrorListPromptBuilder.Build("instruction", entries, Relative), Is.Null);
        }

        // ────────────────────────────── single entry ──────────────────────────────

        [Test]
        public void Build_SingleEntry_FormatsSeverityCodeMessageLocationAndProject()
        {
            string result = ErrorListPromptBuilder.Build(null, new[] { Entry() }, Relative);

            Assert.That(result, Is.EqualTo(
                "1. error CS0103: The name 'foo' does not exist in the current context\n" +
                "   File: LMLocal\\Commands\\X.cs, line 42, column 7\n" +
                "   Project: LMLocal"));
        }

        [Test]
        public void Build_Instruction_IsPrependedWithBlankLine()
        {
            string result = ErrorListPromptBuilder.Build("Explain this.", new[] { Entry() }, Relative);

            Assert.That(result, Does.StartWith("Explain this.\n\n1. error CS0103: "));
        }

        [Test]
        public void Build_ZeroBasedLocation_IsPrintedOneBased()
        {
            string result = ErrorListPromptBuilder.Build(
                null,
                new[] { Entry(line: 0, column: 0) },
                Relative);

            Assert.That(result, Does.Contain("line 1, column 1"));
        }

        // ────────────────────────────── optional fields ──────────────────────────────

        [Test]
        public void Build_EntryWithoutFile_OmitsFileLine()
        {
            string result = ErrorListPromptBuilder.Build(
                null,
                new[] { Entry(filePath: null, line: 0, column: 0) },
                Relative);

            Assert.That(result, Does.Not.Contain("File:"));
            Assert.That(result, Does.Contain("Project: LMLocal"));
        }

        [Test]
        public void Build_EntryWithoutProject_OmitsProjectLine()
        {
            string result = ErrorListPromptBuilder.Build(null, new[] { Entry(project: null) }, Relative);

            Assert.That(result, Does.Not.Contain("Project:"));
            Assert.That(result, Does.Contain("File: LMLocal\\Commands\\X.cs"));
        }

        [Test]
        public void Build_EntryWithoutCode_PrintsSeverityAndMessage()
        {
            string result = ErrorListPromptBuilder.Build(null, new[] { Entry(code: null) }, Relative);

            Assert.That(result, Does.StartWith("1. error: The name 'foo' does not exist"));
        }

        [Test]
        public void Build_EntryWithoutSeverity_PrintsCodeAndMessage()
        {
            string result = ErrorListPromptBuilder.Build(null, new[] { Entry(severity: null) }, Relative);

            Assert.That(result, Does.StartWith("1. CS0103: The name 'foo' does not exist"));
        }

        [Test]
        public void Build_EntryWithoutMessage_DoesNotEmitEmptyLine()
        {
            string result = ErrorListPromptBuilder.Build(null, new[] { Entry(text: null) }, Relative);

            Assert.That(result, Does.StartWith("1. error CS0103\n"));
        }

        // ────────────────────────────── path conversion ──────────────────────────────

        [Test]
        public void Build_RelativePathConverter_IsAppliedToTheFilePath()
        {
            string result = ErrorListPromptBuilder.Build(null, new[] { Entry() }, Relative);

            Assert.That(result, Does.Contain("File: " + RelativePath + ","));
            Assert.That(result, Does.Not.Contain(FullPath));
        }

        [Test]
        public void Build_NullConverter_KeepsFullPath()
        {
            string result = ErrorListPromptBuilder.Build(null, new[] { Entry() }, null);

            Assert.That(result, Does.Contain("File: " + FullPath + ","));
        }

        [Test]
        public void Build_ConverterReturningNull_KeepsFullPath()
        {
            string result = ErrorListPromptBuilder.Build(null, new[] { Entry() }, _ => null);

            Assert.That(result, Does.Contain("File: " + FullPath + ","));
        }

        [Test]
        public void Build_ThrowingConverter_KeepsFullPathAndDoesNotThrow()
        {
            string result = ErrorListPromptBuilder.Build(
                null,
                new[] { Entry() },
                _ => throw new InvalidOperationException("boom"));

            Assert.That(result, Does.Contain("File: " + FullPath + ","));
        }

        // ────────────────────────────── limit ──────────────────────────────

        [Test]
        public void Build_ElevenEntries_KeepsTenAndReportsOneOmitted()
        {
            var entries = Enumerable.Range(0, 11)
                .Select(i => Entry(text: "message " + i))
                .ToArray();

            string result = ErrorListPromptBuilder.Build(null, entries, Relative);

            Assert.That(result, Does.Contain("10. error CS0103: message 9"));
            Assert.That(result, Does.Not.Contain("11. "));
            Assert.That(result, Does.Contain("and 1 more entry not shown"));
            Assert.That(result, Does.Not.Contain("message 10"));
        }

        [Test]
        public void Build_ThirteenEntries_ReportsTheRemainingCount()
        {
            var entries = Enumerable.Range(0, 13)
                .Select(i => Entry(text: "message " + i))
                .ToArray();

            string result = ErrorListPromptBuilder.Build(null, entries, Relative);

            Assert.That(result, Does.Contain("and 3 more entries not shown"));
        }

        [Test]
        public void Build_TenEntries_DoesNotReportOmittedEntries()
        {
            var entries = Enumerable.Range(0, 10)
                .Select(i => Entry(text: "message " + i))
                .ToArray();

            string result = ErrorListPromptBuilder.Build(null, entries, Relative);

            Assert.That(result, Does.Not.Contain("more entry"));
            Assert.That(result, Does.Contain("10. error CS0103: message 9"));
        }

        // ────────────────────────────── ordering ──────────────────────────────

        [Test]
        public void Build_PreservesSelectionOrder()
        {
            string result = ErrorListPromptBuilder.Build(
                null,
                new[] { Entry(text: "FIRST"), Entry(text: "SECOND") },
                Relative);

            Assert.That(result.IndexOf("FIRST", StringComparison.Ordinal),
                Is.LessThan(result.IndexOf("SECOND", StringComparison.Ordinal)));
        }
    }
}
