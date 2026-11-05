using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Syntax;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Integration coverage for the read → replace → read cycle of the file tools.
    /// Uses the real <see cref="DefaultFileSystem"/> (so encodings, BOM handling and EOL
    /// splitting are the production ones) and the real <see cref="PathResolver"/>; only the
    /// Visual Studio dependencies, the snapshot manager and the syntax checker are mocked.
    /// Characterisation cases (names end with "_Documents...") pin currently observed defects
    /// so a change in behaviour is noticed.
    /// Scope: raw file I/O symmetry only. Editor-side effects (VS document buffer, format_document,
    /// get_changes_diff, snapshot diffing) are intentionally not covered here — see the E2E counterpart
    /// <c>RunTool_ReadReplaceRead_RoundTripsOneToOneAsync</c> and the known limitations.
    /// </summary>
    [TestFixture]
    public class FileToolsReadReplaceRoundTripTests
    {
        private string _tempDir;
        private ReadFileLines _reader;
        private ReplaceFileLines _writer;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "LMLocalRoundTrip", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

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

        // ── Symmetric (1:1) round-trips ───────────────────────────────────────

        [Test]
        public async Task NoOpReplace_Utf8NoBom_Crlf_WithNonAscii_IsByteIdenticalAndReadIsStable()
        {
            // Cyrillic and typographic characters are used here on purpose: this is the payload of the
            // real incident (a README with Russian text plus '->' / em-dash characters). It is test data,
            // not prose, so the English-only convention for code and comments does not apply to it.
            var text = "первая строка\r\narrow → and dash — здесь\r\nvar s = \"q\"; path = C:\\tmp\r\n";
            var bytes = Encoding.UTF8.GetBytes(text);
            var path = WriteRaw("crlf-nonbom.txt", bytes);

            var read1 = await ReadAllTextsAsync("crlf-nonbom.txt");
            Assert.That(read1, Is.EqualTo(new[] { "первая строка", "arrow → and dash — здесь", "var s = \"q\"; path = C:\\tmp" }));

            var response = await ReplaceNoOpAsync("crlf-nonbom.txt", startLine: 1, oldLines: string.Join("\r\n", read1));

            Assert.That(response.Success, Is.True, response.ErrorMessage);
            Assert.That(response.AutoCorrected, Is.Null, "A verbatim copy of the read content must not need auto-correction.");
            Assert.That(response.MatchedIgnoringFirstLineIndent, Is.Null, "First-line indentation must match exactly.");
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes), "A no-op replace must not change a single byte.");

            var read2 = await ReadAllTextsAsync("crlf-nonbom.txt");
            Assert.That(read2, Is.EqualTo(read1), "read → replace → read must return identical text.");
        }

        [Test]
        public async Task NoOpReplace_Utf8WithBom_KeepsBomAndBytes()
        {
            var text = "alpha\r\nbeta → gamma\r\n";
            var bytes = Concat(Encoding.UTF8.GetPreamble(), Encoding.UTF8.GetBytes(text));
            var path = WriteRaw("crlf-bom.txt", bytes);

            var read1 = await ReadAllTextsAsync("crlf-bom.txt");
            var response = await ReplaceNoOpAsync("crlf-bom.txt", startLine: 1, oldLines: string.Join("\r\n", read1));

            Assert.That(response.Success, Is.True, response.ErrorMessage);
            var after = File.ReadAllBytes(path);
            Assert.That(after[0], Is.EqualTo(0xEF));
            Assert.That(after[1], Is.EqualTo(0xBB));
            Assert.That(after[2], Is.EqualTo(0xBF));
            Assert.That(after, Is.EqualTo(bytes), "The BOM must survive exactly once and the body must be unchanged.");

            Assert.That(await ReadAllTextsAsync("crlf-bom.txt"), Is.EqualTo(read1));
        }

        [Test]
        public async Task NoOpReplace_Utf16LeWithBom_KeepsEncodingAndBytes()
        {
            var text = "one\r\ntwo → три\r\n";
            var bytes = Concat(Encoding.Unicode.GetPreamble(), Encoding.Unicode.GetBytes(text));
            var path = WriteRaw("utf16le.txt", bytes);

            var read1 = await ReadAllTextsAsync("utf16le.txt");
            Assert.That(read1[1], Is.EqualTo("two → три"), "The read path must honour the UTF-16 BOM.");

            var response = await ReplaceNoOpAsync("utf16le.txt", startLine: 1, oldLines: string.Join("\r\n", read1));

            Assert.That(response.Success, Is.True, response.ErrorMessage);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes), "UTF-16 LE + BOM must round-trip byte for byte.");
            Assert.That(await ReadAllTextsAsync("utf16le.txt"), Is.EqualTo(read1));
        }

        [Test]
        public async Task NoOpReplace_Utf8LfOnly_WithoutTrailingNewline_IsByteIdentical()
        {
            var text = "a\nb → c\nd — e";
            var bytes = Encoding.UTF8.GetBytes(text);
            var path = WriteRaw("lf-noeol.txt", bytes);

            var read1 = await ReadAllTextsAsync("lf-noeol.txt");
            Assert.That(read1, Has.Length.EqualTo(3), "Three lines expected without a phantom trailing line.");

            var response = await ReplaceNoOpAsync("lf-noeol.txt", startLine: 1, oldLines: string.Join("\n", read1));

            Assert.That(response.Success, Is.True, response.ErrorMessage);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes),
                "LF-only file without a trailing newline must round-trip unchanged (no EOL normalization, no added newline).");
        }

        [Test]
        public async Task RealEdit_ChangesOnlyTargetLine_AndKeepsTheRestOneToOne()
        {
            var text = "keep one\r\nkeep → two\r\nkeep three\r\n";
            var path = WriteRaw("edit.txt", Encoding.UTF8.GetBytes(text));

            var read1 = await ReadAllTextsAsync("edit.txt");
            var response = await ReplaceNoOpAsync("edit.txt", startLine: 2, oldLines: read1[1], newLines: "replaced — line");

            Assert.That(response.Success, Is.True, response.ErrorMessage);

            var read2 = await ReadAllTextsAsync("edit.txt");
            Assert.That(read2, Is.EqualTo(new[] { "keep one", "replaced — line", "keep three" }));
            Assert.That(read2[0], Is.EqualTo(read1[0]), "Lines before the edit must be unchanged.");
            Assert.That(read2[2], Is.EqualTo(read1[2]), "Lines after the edit must be unchanged.");

            var expected = "keep one\r\nreplaced — line\r\nkeep three\r\n";
            Assert.That(File.ReadAllText(path, Encoding.UTF8), Is.EqualTo(expected));
        }

        // ── Characterisation of known asymmetries ─────────────────────────────

        [Test]
        public async Task NoOpReplace_MixedEol_IsByteStable()
        {
            // P2-2: the no-op is decided on lines, so a call that asks
            // for exactly what the file already holds writes nothing — the mixed EOL of the untouched lines
            // survives. E5 (EOL unification on a real edit) stays a separate boundary with its own test.
            var bytes = Encoding.UTF8.GetBytes("a\r\nb\nc\r\n");
            var path = WriteRaw("mixed-eol.txt", bytes);

            var read1 = await ReadAllTextsAsync("mixed-eol.txt");
            var response = await ReplaceNoOpAsync("mixed-eol.txt", startLine: 1, oldLines: string.Join("\n", read1));

            Assert.That(response.Success, Is.True, response.ErrorMessage);
            Assert.That(response.IsNoOp, Is.True);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes),
                "A no-op replace must leave a mixed-EOL file byte-identical.");
        }

        [Test]
        public async Task ReadThenReplace_Cp1251WithoutBom_MatchesItsOwnRead()
        {
            // E1: both paths now go through FileEncodingDetector, so the
            // text read_file_lines shows can be matched by replace_file_lines even in a BOM-less non-UTF-8 file.
            Encoding cp1251;
            try
            {
                cp1251 = Encoding.GetEncoding(1251);
            }
            catch (ArgumentException)
            {
                Assert.Ignore("Windows-1251 codepage is not available on this machine.");
                return;
            }

            var bytes = cp1251.GetBytes("привет\r\nвторой\r\n");
            var path = WriteRaw("cp1251.txt", bytes);

            var read1 = await ReadAllTextsAsync("cp1251.txt");
            Assert.That(read1[0], Is.EqualTo(Encoding.Default.GetString(cp1251.GetBytes("привет"))),
                "Both paths share one decoder, so the ANSI bytes decode identically and no replacement chars appear.");

            var response = await ReplaceNoOpAsync("cp1251.txt", startLine: 1, oldLines: read1[0], newLines: read1[0]);

            Assert.That(response.Success, Is.True,
                "Verbatim content from read_file_lines must be matchable in a BOM-less non-UTF-8 file: " + response.ErrorMessage);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes), "A no-op replace must not change a single byte.");
        }

        // ── The encoding metadata reported to the model ───────────────────────

        [Test]
        public void FileLinesResponse_CarriesNoEncodingMetadata()
        {
            // Decision (rev. 9): read_file_lines reports content only. Encoding, encoding_source and
            // has_bom were dropped because no tool accepts them as input — every write path detects
            // the encoding itself — so the model could never act on them. This guard keeps a future
            // "convenience" field from creeping back into the contract.
            var success = new ReadFileLines.FileLinesResponse
            {
                Success = true,
                FilePath = "a.txt",
                Content = new List<ReadFileLines.FileLineEntry>
                {
                    new ReadFileLines.FileLineEntry { LineNumber = 1, Text = "x" }
                }
            };

            var json = JsonConvert.SerializeObject(success);
            Assert.That(json, Does.Contain("\"file_path\":\"a.txt\""));
            Assert.That(json, Does.Not.Contain("encoding"));
            Assert.That(json, Does.Not.Contain("has_bom"));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private string WriteRaw(string relativePath, byte[] bytes)
        {
            var absolutePath = Path.Combine(_tempDir, relativePath);
            File.WriteAllBytes(absolutePath, bytes);
            return absolutePath;
        }

        private static byte[] Concat(byte[] first, byte[] second)
        {
            var result = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, result, 0, first.Length);
            Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
            return result;
        }

        /// <summary>
        /// Runs read_file_lines with pagination until <c>has_more_results</c> is false and
        /// returns the raw line texts (no line numbers, no trailing newline), exactly as the model sees them.
        /// </summary>
        private async Task<string[]> ReadAllTextsAsync(string relativePath)
        {
            var texts = new List<string>();
            int startLine = 1;

            while (true)
            {
                var result = await _reader.ExecuteAsync(new Dictionary<string, object>
                {
                    ["file_path"] = relativePath,
                    ["start_line"] = startLine,
                    ["end_line"] = int.MaxValue
                }, CancellationToken.None).ConfigureAwait(false);

                var response = result as ReadFileLines.FileLinesResponse;
                Assert.That(response, Is.Not.Null);
                Assert.That(response.Success, Is.True, response.ErrorMessage);

                foreach (var entry in response.Content)
                    texts.Add(entry.Text);

                if (!response.HasMoreResults || response.Content.Count == 0)
                    break;

                startLine = response.Content[response.Content.Count - 1].LineNumber + 1;
            }

            return texts.ToArray();
        }

        private Task<ReplaceLinesResponse> ReplaceNoOpAsync(
            string relativePath, int startLine, string oldLines, string newLines = null)
        {
            return ReplaceAsync(relativePath, startLine, oldLines, newLines ?? oldLines);
        }

        private async Task<ReplaceLinesResponse> ReplaceAsync(
            string relativePath, int startLine, string oldLines, string newLines)
        {
            var result = await _writer.ExecuteAsync(new Dictionary<string, object>
            {
                ["file_path"] = relativePath,
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
