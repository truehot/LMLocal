using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Syntax;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Proves the plain cycle the tools promise: read a file, replace a line, read it again and get
    /// exactly what is expected — byte for byte, character for character.
    /// <para>
    /// Fixtures are never modified: the repository fixture
    /// <c>LMLocal.Tests.Unit\Fixtures\RoundTripContent.txt</c> is copied into a temp "solution"
    /// directory in <see cref="SetUp"/>, and <see cref="Fixture_IsNeverModifiedByTheSuite"/> asserts that
    /// the original bytes are untouched. The same temp directory hosts generated files for characters
    /// that must not depend on a hand edit (combining marks, U+2028, U+0085, lone CR) — those are written
    /// with explicit escapes in code.
    /// </para>
    /// <para>
    /// The tools under test are the real <see cref="ReadFileLines"/> and <see cref="ReplaceFileLines"/>
    /// over the real <see cref="DefaultFileSystem"/> and <see cref="PathResolver"/>; only the VS
    /// dependencies, the snapshot manager and the syntax checker are mocked.
    /// </para>
    /// <para>
    /// Several cases characterise known defects instead of asserting ideal behaviour (their names say
    /// "_Documents..." / carry a NOTE).
    /// </para>
    /// </summary>
    [TestFixture]
    public class ReadReplaceRoundTripFixtureTests
    {
        private const string WorkingFileName = "content.txt";

        private static readonly string FixtureRelativePath =
            Path.Combine("LMLocal.Tests.Unit", "Fixtures", "RoundTripContent.txt");

        private static byte[] _fixtureBytesAtStart;
        private static string _fixturePath;

        private string _tempDir;
        private string _workingPath;
        private ReadFileLines _reader;
        private ReplaceFileLines _writer;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _fixturePath = ResolveFixturePath();
            if (_fixturePath != null)
                _fixtureBytesAtStart = File.ReadAllBytes(_fixturePath);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_fixturePath == null || _fixtureBytesAtStart == null)
                return;

            Assert.That(File.ReadAllBytes(_fixturePath), Is.EqualTo(_fixtureBytesAtStart),
                "The repository fixture must never be modified by the test suite; only its temp copy is.");
        }

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "LMLocalRoundTripFixture", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _workingPath = Path.Combine(_tempDir, WorkingFileName);

            if (_fixturePath == null)
            {
                Assert.Ignore(
                    "Fixture not found. Create it at the repository root: " + FixtureRelativePath +
                    " (the folder must sit next to LMLocal.sln).");
            }

            // Always work on a copy: the original stays byte-identical.
            File.WriteAllBytes(_workingPath, File.ReadAllBytes(_fixturePath));
            EnsureInvisibleCharacterLine();

            var vsMock = new Mock<IVsDependencies>();
            vsMock.Setup(v => v.IsSolutionOpen).Returns(true);
            vsMock.Setup(v => v.GetSolutionDirectory()).Returns(_tempDir);

            var fileSystem = new DefaultFileSystem();
            var pathResolver = new PathResolver();

            var snapshotMock = new Mock<ISnapshotManager>();
            var syntaxFactoryMock = new Mock<ISyntaxCheckerFactory>();
            syntaxFactoryMock.Setup(f => f.GetChecker(It.IsAny<string>())).Returns((ISyntaxChecker)null);

            _reader = new ReadFileLines(vsMock.Object, pathResolver, fileSystem);
            _writer = new ReplaceFileLines(
                vsMock.Object, pathResolver, snapshotMock.Object, fileSystem, syntaxFactoryMock.Object);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        // ── 1. What is read is exactly what is on disk ────────────────────────

        [Test]
        public async Task Read_ReturnsTheFileVerbatim()
        {
            var before = File.ReadAllBytes(_workingPath);
            var raw = FileBody(before);
            var lines = await ReadAllTextsAsync();

            Assert.That(lines.Count, Is.GreaterThan(0), "The fixture is empty.");

            // A leading U+FEFF is the encoding BOM and must be consumed by the decoder; a U+FEFF in the
            // middle of a line is real content and must survive.
            Assert.That(lines[0].Length == 0 || lines[0][0] != '\uFEFF',
                "The BOM must not leak into the first returned line.");

            foreach (var line in lines)
            {
                Assert.That(line, Does.Not.Contain('\uFFFD'),
                    "No replacement characters: the fixture must be decoded cleanly (UTF-8, no BOM-less legacy encoding).");
                Assert.That(IsWellFormedUtf16(line), Is.True,
                    "No returned line may contain a lone surrogate: " + CodePointsOf(line));
            }

            string eol = raw.Contains("\r\n") ? "\r\n" : "\n";
            string joined = string.Join(eol, lines);
            if (raw.EndsWith(eol, StringComparison.Ordinal))
                joined += eol;

            Assert.That(joined, Is.EqualTo(raw),
                "Joining the returned lines with the file's EOL must reproduce the file body exactly " +
                "(BOM excluded, since the BOM is not part of a line).");
        }

        // ── 2. No-op replace: the file must not change at all ─────────────────

        [Test]
        public async Task NoOpReplace_KeepsEveryByte_AndTheSecondReadIsIdentical()
        {
            var before = File.ReadAllBytes(_workingPath);
            var raw = FileBody(before);
            var read1 = await ReadAllTextsAsync();

            string eol = raw.Contains("\r\n") ? "\r\n" : "\n";
            string body = string.Join(eol, read1);
            string oldLines = raw.EndsWith(eol, StringComparison.Ordinal) ? body + eol : body;

            var response = await ReplaceAsync(1, oldLines, oldLines);

            Assert.That(response.Success, Is.True, response.ErrorMessage);
            Assert.That(response.AutoCorrected, Is.Null, "Verbatim content must not need auto-correction.");
            Assert.That(response.MatchedIgnoringFirstLineIndent, Is.Null,
                "Verbatim content must match with exact indentation.");

            Assert.That(File.ReadAllBytes(_workingPath), Is.EqualTo(before),
                "A no-op replace must not change a single byte: encoding, BOM, EOL and every character are preserved.");

            var read2 = await ReadAllTextsAsync();
            Assert.That(read2, Is.EqualTo(read1), "read → replace → read must return identical text.");
            Assert.That(CodePointsOf(FileBody(File.ReadAllBytes(_workingPath))),
                Is.EqualTo(CodePointsOf(raw)),
                "The multiset of code points must be untouched by a no-op replace.");
        }

        // ── 3. A real edit touches only the target line ───────────────────────

        [Test]
        public async Task ReplaceOneLine_ChangesOnlyThatLine()
        {
            var read1 = await ReadAllTextsAsync();
            Assert.That(read1.Count, Is.GreaterThanOrEqualTo(3), "The fixture needs at least 3 lines.");

            int lineNumber = read1.Count / 2 + 1;
            string originalLine = read1[lineNumber - 1];
            string newLine = originalLine + " EDITED";

            var response = await ReplaceAsync(lineNumber, originalLine, newLine);
            Assert.That(response.Success, Is.True, response.ErrorMessage);

            var read2 = await ReadAllTextsAsync();
            Assert.That(read2.Count, Is.EqualTo(read1.Count), "The line count must not change.");
            Assert.That(read2[lineNumber - 1], Is.EqualTo(newLine), "The edited line must be the new one.");

            for (int i = 0; i < read1.Count; i++)
            {
                if (i == lineNumber - 1)
                    continue;

                Assert.That(read2[i], Is.EqualTo(read1[i]),
                    $"Line {i + 1} changed although only line {lineNumber} was edited.");
            }

            var beforeCodes = CodePointsOf(string.Join("\n", read1.Where((_, i) => i != lineNumber - 1)));
            var afterCodes = CodePointsOf(string.Join("\n", read2.Where((_, i) => i != lineNumber - 1)));
            Assert.That(afterCodes, Is.EqualTo(beforeCodes),
                "Editing one line must not alter any character of the other lines.");
        }

        // ── 4. Invisible characters: same input works, dropped input fails ────

        [Test]
        public async Task ReplaceLine_WithInvisibleCharacter_CopiedVerbatim_Succeeds()
        {
            var read1 = await ReadAllTextsAsync();
            int index = FindLineWithInvisibleCharacter(read1);
            Assert.That(index, Is.GreaterThanOrEqualTo(0),
                "The working copy must contain a line with an invisible character (injected in SetUp when absent).");

            string line = read1[index];
            var response = await ReplaceAsync(index + 1, line, line + " X");

            Assert.That(response.Success, Is.True,
                "A line containing an invisible character must be replaceable when old_lines carries that character.");
            Assert.That(response.MatchedIgnoringFirstLineIndent, Is.Null,
                "A verbatim copy must match exactly, without the relaxed first-line rule.");

            var read2 = await ReadAllTextsAsync();
            Assert.That(read2[index], Is.EqualTo(line + " X"));
            Assert.That(read2.Count, Is.EqualTo(read1.Count));
        }

        [Test]
        public async Task ReplaceLine_WithDroppedInvisibleCharacter_FailsAndLeavesTheFileUntouched()
        {
            var read1 = await ReadAllTextsAsync();
            int index = FindLineWithInvisibleCharacter(read1);
            Assert.That(index, Is.GreaterThanOrEqualTo(0),
                "The working copy must contain a line with an invisible character (injected in SetUp when absent).");

            string line = read1[index];
            string withoutInvisible = StripInvisibleCharacters(line);
            Assert.That(withoutInvisible, Is.Not.EqualTo(line), "The invisible character must actually be removed.");

            var before = File.ReadAllBytes(_workingPath);
            var response = await ReplaceAsync(index + 1, withoutInvisible, "should not be applied");

            Assert.That(response.Success, Is.False,
                "If a character present in the file is missing from old_lines, nothing may match.");
            Assert.That(response.ErrorMessage, Is.Not.Null.And.Not.Empty);
            Assert.That(File.ReadAllBytes(_workingPath), Is.EqualTo(before),
                "A failed replace must not touch the file.");
        }

        // ── 5. Surrogate pairs (emoji) ────────────────────────────────────────

        [Test]
        public async Task SurrogatePairs_AreReadAndWrittenWithoutCuttingAPair()
        {
            // Generated with explicit escapes so the check does not depend on how a hand edit survived.
            const string emoji = "\U0001F41B";   // U+1F41B BUG
            const string rocket = "\U0001F680";  // U+1F680 ROCKET

            // The leading ASCII line is required by the replace path: its encoding probe reads only the
            // first four bytes, so a multibyte character at that offset makes it fall back to ANSI.
            // See FourByteEncodingProbe_MisclassifiesUtf8AndBreaksReplace for the defect itself.
            string content = "abcd\r\n" + "a" + emoji + "b" + rocket + "c\r\n";

            var bytes = Encoding.UTF8.GetBytes(content);
            File.WriteAllBytes(_workingPath, bytes);

            var read1 = await ReadAllTextsAsync();
            Assert.That(read1.Count, Is.EqualTo(2));
            Assert.That(read1[1], Is.EqualTo("a" + emoji + "b" + rocket + "c"),
                "Both surrogate pairs must come back intact: " + CodePointsOf(read1[1]));
            Assert.That(IsWellFormedUtf16(read1[1]), Is.True,
                "A cut pair would leave a lone surrogate: " + CodePointsOf(read1[1]));
            Assert.That(read1[1].Length, Is.EqualTo("abc".Length + 4),
                "Two astral characters occupy exactly four UTF-16 code units.");

            // A char-index-unaware writer would slice the pair; prove the line survives an edit verbatim.
            var response = await ReplaceAsync(2, read1[1], read1[1]);
            Assert.That(response.Success, Is.True, response.ErrorMessage);
            Assert.That(File.ReadAllBytes(_workingPath), Is.EqualTo(bytes),
                "Surrogate pairs must be written back byte-identically (no char-index slicing).");
        }

        // ── 6. Normalization must not happen ──────────────────────────────────

        [Test]
        public async Task CombiningAndPrecomposedAccents_AreNotNormalized()
        {
            const string precomposed = "caf\u00E9";   // single U+00E9
            const string decomposed = "cafe\u0301";   // U+0065 + U+0301 combining acute

            // The leading ASCII line keeps the first four bytes ASCII, see the four-byte probe defect.
            string content = "abcd\r\n" + precomposed + "\r\n" + decomposed + "\r\n";

            Assert.That(precomposed, Is.Not.EqualTo(decomposed), "The two forms must be distinct strings.");

            var bytes = Encoding.UTF8.GetBytes(content);
            File.WriteAllBytes(_workingPath, bytes);

            var read1 = await ReadAllTextsAsync();
            Assert.That(read1.Count, Is.EqualTo(3));
            Assert.That(read1[1], Is.EqualTo(precomposed),
                "The precomposed form must not be decomposed: " + CodePointsOf(read1[1]));
            Assert.That(read1[2], Is.EqualTo(decomposed),
                "The decomposed form must not be composed: " + CodePointsOf(read1[2]));
            Assert.That(read1[1], Does.Contain('\u00E9').And.Not.Contain('\u0301'));
            Assert.That(read1[2], Does.Contain('\u0301').And.Not.Contain('\u00E9'));

            string oldLines = read1[1] + "\r\n" + read1[2];
            var response = await ReplaceAsync(2, oldLines, oldLines);
            Assert.That(response.Success, Is.True, response.ErrorMessage);
            Assert.That(File.ReadAllBytes(_workingPath), Is.EqualTo(bytes),
                "No Unicode normalization may be applied on the write path.");
        }

        // ── 6b. The 8 KB encoding probe ───────────────────────────────────────

        [Test]
        public async Task EightKilobyteProbe_ClassifiesUtf8FileWithEarlyMultibyteChar()
        {
            // E2: the old probe validated only the first four bytes, so a
            // multibyte sequence starting inside that window made a valid UTF-8 file look like ANSI. The probe
            // buffer is now 8 KB and a file that fits in it is validated whole, so this content is UTF-8 and
            // verbatim old_lines matches.
            const string emoji = "\U0001F41B";

            // An emoji starting at byte 1 used to put a continuation byte at position 4.
            string content = "a" + emoji + "b\r\nx\r\n";
            var bytes = Encoding.UTF8.GetBytes(content);
            File.WriteAllBytes(_workingPath, bytes);

            var read1 = await ReadAllTextsAsync();
            Assert.That(read1[0], Is.EqualTo("a" + emoji + "b"),
                "The read path returns the correct text: " + CodePointsOf(read1[0]));

            var response = await ReplaceAsync(1, read1[0], read1[0]);
            Assert.That(response.Success, Is.True,
                "The replace path now detects the same encoding as the read path, so verbatim content matches: " +
                response.ErrorMessage);
            Assert.That(File.ReadAllBytes(_workingPath), Is.EqualTo(bytes),
                "A no-op replace must not touch the file.");
        }

        // ── 7. U+2028 / U+0085: line separator vs the tools' splitter ─────────

        [Test]
        public async Task LineSeparatorAndNel_AreNotLineBreaksForTheTools_AlthoughRoslynCountsThem()
        {
            const char lineSeparator = '\u2028';
            const char nextLine = '\u0085';

            // The leading ASCII line is kept so the fixture text stays exactly as it was when this test
            // was written against the old four-byte probe; the current 8 KB probe does not need it.
            string content = "abcd\r\n" + "L1" + lineSeparator + "L2\r\n" + "M1" + nextLine + "M2\r\n";
            var bytes = Encoding.UTF8.GetBytes(content);
            File.WriteAllBytes(_workingPath, bytes);

            var lines = await ReadAllTextsAsync();

            Assert.That(lines.Count, Is.EqualTo(3),
                "Neither U+2028 nor U+0085 may split a line for the tools: " + DescribeLines(lines));
            Assert.That(lines[1], Is.EqualTo("L1" + lineSeparator + "L2"));
            Assert.That(lines[2], Is.EqualTo("M1" + nextLine + "M2"));

            // Roslyn, by contrast, treats both as line terminators — verified here so the divergence
            // cannot silently change: the same text yields line 2 for Roslyn and line 1 for the tools.
            // Consequence: a syntax error reported by the checker may point at a different line number
            // than the line numbers returned by read_file_lines / expected by replace_file_lines.
            var checker = new CSharpSyntaxChecker();
            Assert.That(checker.IsSyntaxValid("class A { }" + lineSeparator + "###", out var lsErrors), Is.False);
            Assert.That(lsErrors[0].StartLine, Is.EqualTo(2),
                "Roslyn counts U+2028 as a line break, while the tools' splitter does not.");
            Assert.That(checker.IsSyntaxValid("class A { }" + nextLine + "###", out var nelErrors), Is.False);
            Assert.That(nelErrors[0].StartLine, Is.EqualTo(2),
                "Roslyn counts U+0085 as a line break, while the tools' splitter does not.");

            // And the tools must not lose these characters on the write path.
            string oldLines = lines[1] + "\r\n" + lines[2];
            var response = await ReplaceAsync(2, oldLines, oldLines);
            Assert.That(response.Success, Is.True, response.ErrorMessage);
            Assert.That(File.ReadAllBytes(_workingPath), Is.EqualTo(bytes),
                "U+2028 / U+0085 must survive a no-op replace unchanged.");
        }

        // ── 8. Each mid-line invisible character, echoed individually ─────────

        [TestCase('\u200B', "zero width space", TestName = "Echo_ZeroWidthSpace")]
        [TestCase('\u200C', "zero width non-joiner", TestName = "Echo_ZeroWidthNonJoiner")]
        [TestCase('\u200D', "zero width joiner", TestName = "Echo_ZeroWidthJoiner")]
        [TestCase('\uFEFF', "zero width no-break space used as content", TestName = "Echo_BomAsContent")]
        [TestCase('\u2009', "thin space", TestName = "Echo_ThinSpace")]
        [TestCase('\u200A', "hair space", TestName = "Echo_HairSpace")]
        [TestCase('\u2003', "em space", TestName = "Echo_EmSpace")]
        [TestCase('\u3000', "ideographic space", TestName = "Echo_IdeographicSpace")]
        [TestCase('\u00A0', "no-break space", TestName = "Echo_NoBreakSpace")]
        public async Task MidLineInvisibleCharacter_IsEchoedVerbatim(char special, string description)
        {
            string content = "before" + special + "after\r\nplain\r\n";
            var bytes = Encoding.UTF8.GetBytes(content);
            File.WriteAllBytes(_workingPath, bytes);

            var read1 = await ReadAllTextsAsync();
            Assert.That(read1[0], Is.EqualTo("before" + special + "after"),
                description + " must be echoed verbatim: " + CodePointsOf(read1[0]));

            var response = await ReplaceAsync(1, read1[0], read1[0]);
            Assert.That(response.Success, Is.True, response.ErrorMessage);
            Assert.That(File.ReadAllBytes(_workingPath), Is.EqualTo(bytes),
                description + " must survive a no-op replace byte-identically.");
        }

        // ── 9. EOL matrix ─────────────────────────────────────────────────────

        [TestCase("a\r\nb\r\nc", 3, TestName = "Eol_Crlf")]
        [TestCase("a\r\nb\r\n", 2, TestName = "Eol_CrlfWithTrailing")]
        [TestCase("a\nb\nc", 3, TestName = "Eol_Lf")]
        [TestCase("a\rb\rc", 3, TestName = "Eol_CrOnly")]
        [TestCase("a\r\nb\nc\r\n", 3, TestName = "Eol_Mixed")]
        public async Task EndOfLineVariants_NoOpIsByteStable(string content, int expectedLineCount)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            File.WriteAllBytes(_workingPath, bytes);

            var read1 = await ReadAllTextsAsync();
            Assert.That(read1.Count, Is.EqualTo(expectedLineCount),
                "Line count for " + EscapedEol(content) + ": " + DescribeLines(read1));
            Assert.That(read1[0], Is.EqualTo("a"));

            string eol = content.Contains("\r\n") ? "\r\n" : "\n";
            string oldLines = string.Join(eol, read1);
            if (content.EndsWith(eol, StringComparison.Ordinal))
                oldLines += eol;

            var response = await ReplaceAsync(1, oldLines, oldLines);
            Assert.That(response.Success, Is.True, response.ErrorMessage);
            Assert.That(response.IsNoOp, Is.True,
                "The requested text is exactly what the file holds on every EOL variant - including CR-only and " +
                "mixed, where the EOL normalization alone makes the written text differ from the file.");

            // The no-op is decided on lines, not on the bytes that a write would produce: nothing is written, so
            // the untouched lines keep their own EOL even where the file is not uniform. E5 (unification on a real
            // edit) stays a separate boundary and is measured by RealEdit_MixedEol_UnifiesUntouchedLines_DocumentsE5.
            Assert.That(File.ReadAllBytes(_workingPath), Is.EqualTo(bytes),
                "A no-op replace must leave " + EscapedEol(content) + " byte-identical.");

            var read2 = await ReadAllTextsAsync();
            Assert.That(read2, Is.EqualTo(read1), "read → replace → read must return identical text.");
        }

        [Test]
        public async Task RealEdit_MixedEol_UnifiesUntouchedLines_DocumentsE5()
        {
            // E5 is a separate boundary: a real edit rebuilds the whole file with the one separator chosen for the
            // file, so untouched lines lose their own EOL. This is the only place where that is still measured —
            // the no-op cases above no longer write at all, so without this test the defect would silently drop
            // out of the suite.
            var bytes = Encoding.UTF8.GetBytes("a\r\nb\nc\r\n");
            File.WriteAllBytes(_workingPath, bytes);

            var read1 = await ReadAllTextsAsync();
            Assert.That(read1, Is.EqualTo(new[] { "a", "b", "c" }));

            var response = await ReplaceAsync(2, "b", "B");
            Assert.That(response.Success, Is.True, response.ErrorMessage);
            Assert.That(response.IsNoOp, Is.False);

            var after = File.ReadAllBytes(_workingPath);
            Assert.That(FileBody(after), Is.EqualTo("a\r\nB\r\nc\r\n"),
                "Boundary E5: line 3 was touched by the edit even though its text did not change, because the " +
                "separator is chosen once for the whole file.");
            Assert.That(after, Is.Not.EqualTo(bytes), "A real edit must change the bytes.");

            var read2 = await ReadAllTextsAsync();
            Assert.That(read2, Is.EqualTo(new[] { "a", "B", "c" }),
                "The visible text changes only on the edited line.");
        }

        [TestCase("a\r\nb\r\nc", TestName = "LastLine_Crlf")]
        [TestCase("a\r\nb\r\n", TestName = "LastLine_CrlfWithTrailing")]
        [TestCase("a\nb\nc", TestName = "LastLine_Lf")]
        [TestCase("a\rb\rc", TestName = "LastLine_CrOnly")]
        [TestCase("a\r\nb\nc\r\n", TestName = "LastLine_Mixed")]
        public async Task LastLineEdit_KeepsLineCountOnEveryEolVariant(string content)
        {
            // Check 47: both tools and read_file_lines must agree on the line model, and the phantom trailing
            // element used to hide exactly here — editing the LAST line lost the file's trailing break.
            File.WriteAllBytes(_workingPath, Encoding.UTF8.GetBytes(content));

            var read1 = await ReadAllTextsAsync();
            Assert.That(read1.Count, Is.GreaterThan(0));

            int lastLine = read1.Count;
            var response = await ReplaceAsync(lastLine, read1[lastLine - 1], "C");
            Assert.That(response.Success, Is.True, response.ErrorMessage);

            var expected = new List<string>(read1)
            {
                [lastLine - 1] = "C"
            };

            var read2 = await ReadAllTextsAsync();
            Assert.That(read2, Is.EqualTo(expected),
                "The line count must match read_file_lines after editing the last line: " + DescribeLines(read2));

            string body = FileBody(File.ReadAllBytes(_workingPath));
            bool hadTrailing = content.EndsWith("\n", StringComparison.Ordinal) || content.EndsWith("\r", StringComparison.Ordinal);
            bool hasTrailing = body.EndsWith("\n", StringComparison.Ordinal) || body.EndsWith("\r", StringComparison.Ordinal);
            Assert.That(hasTrailing, Is.EqualTo(hadTrailing),
                "The trailing line break must survive a real edit of the last line.");
        }

        // ── 10. The repository fixture itself is never written ────────────────

        [Test]
        public void Fixture_IsNeverModifiedByTheSuite()
        {
            Assert.That(_fixturePath, Is.Not.Null, "Fixture not found.");
            Assert.That(File.ReadAllBytes(_fixturePath), Is.EqualTo(_fixtureBytesAtStart),
                "Every test works on a temp copy; the repository fixture must stay byte-identical.");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static readonly char[] InvisibleCharacters = { '\u200B', '\u00AD', '\uFEFF' };

        /// <summary>
        /// The two invisible-character checks need a line that really contains such a character.
        /// Invisible characters cannot be typed reliably into a plain content file, so when the fixture
        /// has none they are appended to the WORKING COPY in code. The repository fixture is untouched
        /// (guarded by <see cref="Fixture_IsNeverModifiedByTheSuite"/>) and the checks never skip.
        /// </summary>
        private void EnsureInvisibleCharacterLine()
        {
            var bytes = File.ReadAllBytes(_workingPath);
            var body = FileBody(bytes);
            if (body.IndexOfAny(InvisibleCharacters) >= 0)
                return;

            string eol = body.Contains("\r\n") ? "\r\n" : "\n";
            string injected = "L26: injected zero width \u200Bspace and soft\u00ADhyphen";

            if (body.Length > 0 && !body.EndsWith(eol, StringComparison.Ordinal))
                body += eol;

            body += injected + eol;

            File.WriteAllBytes(_workingPath, Encoding.UTF8.GetBytes(body));
        }

        private static int FindLineWithInvisibleCharacter(IList<string> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (!string.IsNullOrEmpty(lines[i]) && lines[i].IndexOfAny(InvisibleCharacters) >= 0)
                    return i;
            }
            return -1;
        }

        private static string StripInvisibleCharacters(string value)
        {
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (Array.IndexOf(InvisibleCharacters, c) < 0)
                    sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// True when every high surrogate is followed by a low surrogate and no lone low surrogate exists.
        /// </summary>
        private static bool IsWellFormedUtf16(string value)
        {
            if (value == null)
                return true;

            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsHighSurrogate(value[i]))
                {
                    if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                        return false;
                    i++;
                }
                else if (char.IsLowSurrogate(value[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static string DescribeLines(IList<string> lines)
        {
            return "[" + string.Join(" | ", lines.Select(CodePointsOf)) + "]";
        }

        private static string EscapedEol(string value)
        {
            return value.Replace("\r\n", "<CRLF>").Replace("\r", "<CR>").Replace("\n", "<LF>");
        }

        /// <summary>
        /// Returns the file text without the BOM: the BOM belongs to the encoding, not to a line,
        /// and <c>read_file_lines</c> does not return it.
        /// </summary>
        private static string FileBody(byte[] bytes)
        {
            bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            return Encoding.UTF8.GetString(hasBom ? bytes.Skip(3).ToArray() : bytes);
        }

        private static string CodePointsOf(string value)
        {
            if (value == null)
                return "<null>";

            return string.Join(" ", value.Select(c => ((int)c).ToString("X4", CultureInfo.InvariantCulture)));
        }

        private static string ResolveFixturePath()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "LMLocal.sln")))
                {
                    var candidate = Path.Combine(dir.FullName, FixtureRelativePath);
                    return File.Exists(candidate) ? candidate : null;
                }
                dir = dir.Parent;
            }

            return null;
        }

        /// <summary>
        /// Reads the working file through read_file_lines, following has_more_results, and returns
        /// the raw line texts (no line numbers) — exactly what the model receives.
        /// </summary>
        private async Task<List<string>> ReadAllTextsAsync()
        {
            var texts = new List<string>();
            int startLine = 1;

            while (true)
            {
                var result = await _reader.ExecuteAsync(new Dictionary<string, object>
                {
                    ["file_path"] = WorkingFileName,
                    ["start_line"] = startLine,
                    ["end_line"] = int.MaxValue
                }, CancellationToken.None).ConfigureAwait(false);

                var response = result as ReadFileLines.FileLinesResponse;
                Assert.That(response, Is.Not.Null);
                Assert.That(response.Success, Is.True, response.ErrorMessage);

                foreach (var entry in response.Content)
                    texts.Add(entry.Text);

                if (!response.HasMoreResults || response.Content.Count == 0)
                    return texts;

                startLine = response.Content[response.Content.Count - 1].LineNumber + 1;
            }
        }

        private async Task<ReplaceLinesResponse> ReplaceAsync(int startLine, string oldLines, string newLines)
        {
            var result = await _writer.ExecuteAsync(new Dictionary<string, object>
            {
                ["file_path"] = WorkingFileName,
                ["start_line"] = startLine,
                ["old_lines"] = oldLines,
                ["new_lines"] = newLines
            }, CancellationToken.None).ConfigureAwait(false);

            var response = result as ReplaceLinesResponse;
            Assert.That(response, Is.Not.Null);
            return response;
        }
    }
}
