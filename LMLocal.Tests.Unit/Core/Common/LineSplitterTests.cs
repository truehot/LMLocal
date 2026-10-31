using LMLocal.Core.Common;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Core.Common
{
    /// <summary>
    /// Contract of the single line splitter every file tool shares (docs plan, check 39).
    /// <para>
    /// The contract has to match <c>StreamReader.ReadLine</c>, because that is what numbers the lines the model
    /// sees in <c>read_file_lines</c>: three separators, and a trailing line break does not start a new line.
    /// The exhaustive cases of <c>UnifiedDiffBuilderTests</c> keep guarding the same code after the move into
    /// <see cref="LineSplitter"/>; the cases here pin the edges that the file tools depend on.
    /// </para>
    /// </summary>
    [TestFixture]
    public class LineSplitterTests
    {
        [Test]
        public void Split_NullAndEmpty_ProduceNoLines()
        {
            // An empty file is zero lines for read_file_lines: the ReadLine loop never runs. A made-up empty
            // line would put every tool one line off.
            Assert.That(LineSplitter.Split(null), Is.Empty);
            Assert.That(LineSplitter.Split(string.Empty), Is.Empty);
        }

        [Test]
        public void Split_SingleBreak_IsOneEmptyLine()
        {
            // "\n" is the only form in which the model can express "replace the block with one empty line":
            // new_lines = "" already means "delete the range", and insert_file_lines rejects it.
            Assert.That(LineSplitter.Split("\n"), Is.EqualTo(new[] { "" }));
            Assert.That(LineSplitter.Split("\r\n"), Is.EqualTo(new[] { "" }));
            Assert.That(LineSplitter.Split("\r"), Is.EqualTo(new[] { "" }));
        }

        [Test]
        public void Split_TwoBreaks_AreTwoEmptyLines()
        {
            Assert.That(LineSplitter.Split("\n\n"), Is.EqualTo(new[] { "", "" }));
            Assert.That(LineSplitter.Split("\r\n\r\n"), Is.EqualTo(new[] { "", "" }));
        }

        [Test]
        public void Split_TrailingBreak_DoesNotCreateAPhantomLine()
        {
            // No phantom last element: that element used to leak into every line count (realCount was too high).
            Assert.That(LineSplitter.Split("a\n"), Is.EqualTo(new[] { "a" }));
            Assert.That(LineSplitter.Split("a\r\n"), Is.EqualTo(new[] { "a" }));
            Assert.That(LineSplitter.Split("a\r"), Is.EqualTo(new[] { "a" }));
            Assert.That(LineSplitter.Split("a\nb\n"), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(LineSplitter.Split("a\nb"), Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void Split_ThreeSeparators_AreAllLineBreaks()
        {
            // A file with mixed EOLs must be numbered exactly like read_file_lines, which uses ReadLine.
            Assert.That(LineSplitter.Split("a\r\nb\nc\rd"), Is.EqualTo(new[] { "a", "b", "c", "d" }));
            Assert.That(LineSplitter.Split("a\r\nb\nc\r\n"), Is.EqualTo(new[] { "a", "b", "c" }));
            Assert.That(LineSplitter.Split("a\r\n\r\nb"), Is.EqualTo(new[] { "a", "", "b" }));
        }

        [Test]
        public void Split_CrOnlyText_IsSplitOnCr()
        {
            // E6: old_lines glued by the model with "\n" has to match a file that uses lone CR.
            Assert.That(LineSplitter.Split("a\rb\rc"), Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void Split_DoesNotSplitInsideASurrogatePair()
        {
            // Only break characters split; astral characters stay whole.
            const string line = "a\U0001F41Bb";
            Assert.That(LineSplitter.Split(line + "\r\n" + line), Is.EqualTo(new[] { line, line }));
        }

        [Test]
        public void HasTrailingEol_ReportsTheCharacter_NotTheFileSeparator()
        {
            Assert.That(LineSplitter.HasTrailingEol("a\n"), Is.True);
            Assert.That(LineSplitter.HasTrailingEol("a\r\n"), Is.True);
            // CR-only text: EndsWith(separator) with separator = "\n" would miss this — the reason the flag
            // is computed from the last character.
            Assert.That(LineSplitter.HasTrailingEol("a\r"), Is.True);
            Assert.That(LineSplitter.HasTrailingEol("a"), Is.False);
            Assert.That(LineSplitter.HasTrailingEol(""), Is.False);
            Assert.That(LineSplitter.HasTrailingEol(null), Is.False);
        }

        [Test]
        public void Split_AndHasTrailingEol_AgreeOnTheLineCount()
        {
            // The two members are always used together: lines + flag must reproduce the input exactly.
            foreach (string content in new[] { "", "a", "a\n", "a\r\n", "a\r", "a\n\n", "\n", "\r\n", "a\r\nb\nc\r\n" })
            {
                string separator = content.Contains("\r\n") ? "\r\n" : "\n";
                string rejoined = string.Join(separator, LineSplitter.Split(content));
                if (LineSplitter.HasTrailingEol(content))
                    rejoined += separator;

                string normalized = content.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", separator);

                Assert.That(rejoined, Is.EqualTo(normalized),
                    "Join(Split(x)) + trailing flag must round-trip: " + content.Replace("\r\n", "<CRLF>").Replace("\r", "<CR>").Replace("\n", "<LF>"));
            }
        }

        [Test]
        public void Join_BlankLastLine_KeepsItByAddingOneBreak()
        {
            // A blank last line needs one more break behind it: Split drops the empty element a trailing break
            // produces, so ["X", ""] written as "X\n" reads back as the single line "X" — the blank line vanished
            // from the file while the tool response still counted it.
            Assert.That(LineSplitter.Join("\n", new[] { "X", "" }, false), Is.EqualTo("X\n\n"));
            Assert.That(LineSplitter.Join("\n", new[] { "" }, false), Is.EqualTo("\n"));
            Assert.That(LineSplitter.Join("\n", new[] { "", "" }, false), Is.EqualTo("\n\n"));
            Assert.That(LineSplitter.Join("\r\n", new[] { "a", "" }, false), Is.EqualTo("a\r\n\r\n"));
            // The flag and the blank line are covered by the same element: the break is never doubled.
            Assert.That(LineSplitter.Join("\n", new[] { "a", "" }, true), Is.EqualTo("a\n\n"));
        }

        [Test]
        public void Join_NonBlankLastLine_LeavesTheShapeToTheCaller()
        {
            // Without the flag a file that had no trailing break keeps none; the append policy is the caller's.
            Assert.That(LineSplitter.Join("\n", new[] { "a", "b" }, false), Is.EqualTo("a\nb"));
            Assert.That(LineSplitter.Join("\n", new[] { "a", "b" }, true), Is.EqualTo("a\nb\n"));
        }

        [Test]
        public void Join_EmptyLineList_StaysEmpty()
        {
            // Deleting every line of a file produces an empty file, not a file with one blank line.
            Assert.That(LineSplitter.Join("\n", new string[0], true), Is.EqualTo(string.Empty));
            Assert.That(LineSplitter.Join("\n", null, true), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Join_AndSplit_RoundTripEveryShape()
        {
            // The invariant the file tools write files with: what is joined must read back as the same lines.
            var cases = new[]
            {
                new[] { "a" },
                new[] { "a", "b" },
                new[] { "" },
                new[] { "", "" },
                new[] { "a", "" },
                new[] { "X", "" },
                new[] { "a", "", "b" }
            };

            foreach (string[] lines in cases)
            {
                foreach (bool endsWithLineBreak in new[] { false, true })
                {
                    string text = LineSplitter.Join("\n", lines, endsWithLineBreak);

                    Assert.That(LineSplitter.Split(text), Is.EqualTo(lines),
                        "Split(Join(x)) must return x for [" + string.Join(" | ", lines) + "], endsWithLineBreak=" + endsWithLineBreak);
                }
            }
        }
    }
}
