using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Persistence;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class DefaultFileSystemTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "LMLocalTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        [Test]
        public void ValidateFilePath_Throws_OnInvalidOrEmpty()
        {
            var fs = new DefaultFileSystem();
            Assert.Throws<ArgumentNullException>(() => fs.ValidateFilePath(""));

            // construct an invalid file name using invalid chars
            var invalidChars = Path.GetInvalidFileNameChars();
            if (invalidChars.Length > 0)
            {
                var badFile = "bad" + invalidChars[0] + ".txt";
                // Pass just the file name to ValidateFilePath to avoid Path.Combine validation earlier
                Assert.Throws<ArgumentException>(() => fs.ValidateFilePath(badFile));
            }
        }

        [Test]
        public void EnsureDirectoryExistsForFile_CreatesDirectory()
        {
            var fs = new DefaultFileSystem();
            var filePath = Path.Combine(_tempDir, "sub", "file.txt");
            fs.EnsureDirectoryExistsForFile(filePath);
            var dir = Path.GetDirectoryName(filePath);
            Assert.That(Directory.Exists(dir), Is.True);
        }

        [Test]
        public async Task WriteAndReadAllBytesAsync_WritesAndReadsContent()
        {
            var fs = new DefaultFileSystem();
            var filePath = Path.Combine(_tempDir, "file.txt");
            var content = "hello world";
            var data = Encoding.UTF8.GetBytes(content);

            await fs.WriteAllBytesAsync(filePath, data).ConfigureAwait(false);

            Assert.That(File.Exists(filePath), Is.True);

            var read = await fs.ReadAllTextAsync(filePath).ConfigureAwait(false);
            Assert.That(read, Is.EqualTo(content));
        }

        [Test]
        public async Task ReadAllTextAsync_StripsUtf8Bom()
        {
            var fs = new DefaultFileSystem();
            var filePath = Path.Combine(_tempDir, "bom.json");

            var content = "{ \"agents\": [] }";
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            var body = Encoding.UTF8.GetBytes(content);
            var withBom = new byte[bom.Length + body.Length];
            Array.Copy(bom, 0, withBom, 0, bom.Length);
            Array.Copy(body, 0, withBom, bom.Length, body.Length);

            await fs.WriteAllBytesAsync(filePath, withBom).ConfigureAwait(false);

            // Like File.ReadAllText, the BOM must not leak into the returned text.
            var read = await fs.ReadAllTextAsync(filePath).ConfigureAwait(false);
            Assert.That(read, Is.EqualTo(content));
        }

        [Test]
        public async Task ReadAllTextAsync_HandlesMultibyteCharsAcrossBufferBoundary()
        {
            var fs = new DefaultFileSystem();
            var filePath = Path.Combine(_tempDir, "multibyte.txt");

            // '€' is 3 bytes in UTF-8; 2000 of them = 6000 bytes, so the internal 4096-byte
            // read chunk splits the 1366th character. A byte-chunk Encoding.UTF8.GetString
            // decoder would emit U+FFFD here; StreamReader-based decoding must not.
            var content = new string('€', 2000);
            await fs.WriteAllBytesAsync(filePath, Encoding.UTF8.GetBytes(content)).ConfigureAwait(false);

            var read = await fs.ReadAllTextAsync(filePath).ConfigureAwait(false);
            Assert.That(read, Is.EqualTo(content));
            Assert.That(read, Does.Not.Contain('\uFFFD'));
        }

        // ---------------------------------------------------------------------
        // Characterization tests for the encoding behaviour that the file tools
        // (read_file_lines / replace_file_lines) depend on.
        // ---------------------------------------------------------------------

        [Test]
        public async Task ReadAllTextWithDetectedEncodingAsync_Utf8Bom_ReturnsTextWithoutBom()
        {
            var fs = new DefaultFileSystem();
            var filePath = Path.Combine(_tempDir, "bom-utf8.txt");

            var content = "first\r\nвторой → строка";
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            var body = Encoding.UTF8.GetBytes(content);
            var withBom = new byte[bom.Length + body.Length];
            Array.Copy(bom, 0, withBom, 0, bom.Length);
            Array.Copy(body, 0, withBom, bom.Length, body.Length);
            await fs.WriteAllBytesAsync(filePath, withBom).ConfigureAwait(false);

            var (read, encoding, hasBom) = await fs.ReadAllTextWithDetectedEncodingAsync(filePath).ConfigureAwait(false);

            Assert.That(hasBom, Is.True);
            Assert.That(encoding.WebName, Is.EqualTo("utf-8"));
            Assert.That(read, Is.EqualTo(content));
            Assert.That(read, Does.Not.Contain('\uFEFF'),
                "The BOM must not leak into the text handed to the model/compare layer.");
        }

        [Test]
        public async Task WriteAllBytesWithEncodingAsync_AfterDetectedRead_KeepsSingleBomOnDisk()
        {
            var fs = new DefaultFileSystem();
            var filePath = Path.Combine(_tempDir, "bom-roundtrip.txt");

            var content = "код: →\r\nвторая строка\r\n";
            var body = Encoding.UTF8.GetBytes(content);
            var original = new byte[3 + body.Length];
            original[0] = 0xEF;
            original[1] = 0xBB;
            original[2] = 0xBF;
            Array.Copy(body, 0, original, 3, body.Length);
            await fs.WriteAllBytesAsync(filePath, original).ConfigureAwait(false);

            var (read, encoding, hasBom) = await fs.ReadAllTextWithDetectedEncodingAsync(filePath).ConfigureAwait(false);
            await fs.WriteAllBytesWithEncodingAsync(filePath, read, encoding, hasBom).ConfigureAwait(false);

            // Byte-identical rewrite: no BOM loss and no BOM duplication on every edit.
            Assert.That(File.ReadAllBytes(filePath), Is.EqualTo(original));
        }

        [Test]
        public async Task ReadLinesRangeAsync_Cp1251WithoutBom_DecodesLikeDetectedRead()
        {
            if (Encoding.Default.WebName == "utf-8")
            {
                Assert.Ignore("System default encoding is UTF-8; the ANSI fallback branch is unreachable here.");
                return;
            }

            var fs = new DefaultFileSystem();
            var filePath = Path.Combine(_tempDir, "cp1251.txt");

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

            var bytes = cp1251.GetBytes("привет");
            await fs.WriteAllBytesAsync(filePath, bytes).ConfigureAwait(false);

            var lines = await fs.ReadLinesRangeAsync(filePath, 1, 10).ConfigureAwait(false);
            var (detected, _, hasBom) = await fs.ReadAllTextWithDetectedEncodingAsync(filePath).ConfigureAwait(false);

            // Compare against what the system ANSI codepage makes of these bytes, so the assertion holds
            // on any Windows locale (Encoding.Default is 1251 on a Russian system, 1252 on an English one).
            var expected = Encoding.Default.GetString(bytes);

            Assert.That(hasBom, Is.False);
            Assert.That(lines.Count, Is.EqualTo(1));
            Assert.That(lines[0], Is.EqualTo(expected),
                "Both read paths use the same decoder, so non-UTF-8 bytes are no longer turned into replacement chars.");
            Assert.That(detected, Is.EqualTo(lines[0]),
                "Reading lines and reading all text must decode the same file identically.");
        }

        [Test]
        public async Task ReadLinesRangeAsync_Utf8WithBom_SkipsPreamble()
        {
            var fs = new DefaultFileSystem();
            var filePath = Path.Combine(_tempDir, "probe-bom.txt");

            var content = "one\r\ntwo → три";
            var preamble = Encoding.UTF8.GetPreamble();
            var body = Encoding.UTF8.GetBytes(content);
            var bytes = new byte[preamble.Length + body.Length];
            Array.Copy(preamble, 0, bytes, 0, preamble.Length);
            Array.Copy(body, 0, bytes, preamble.Length, body.Length);
            await fs.WriteAllBytesAsync(filePath, bytes).ConfigureAwait(false);

            var lines = await fs.ReadLinesRangeAsync(filePath, 1, 10).ConfigureAwait(false);

            Assert.That(lines, Is.EqualTo(new[] { "one", "two → три" }),
                "The preamble must be skipped instead of leaking into the first line as U+FEFF.");
        }

        [Test]
        public async Task ReadLinesRangeAsync_MultibyteCharAtFourthByte_DecodesAsUtf8()
        {
            // E2: the old probe classified this file as ANSI because its window ended inside the
            // three-byte sequence of '→'. The 8 KB probe validates the whole (short) file.
            var fs = new DefaultFileSystem();
            var filePath = Path.Combine(_tempDir, "probe-boundary.txt");
            await fs.WriteAllBytesAsync(filePath, Encoding.UTF8.GetBytes("abc→\n")).ConfigureAwait(false);

            var lines = await fs.ReadLinesRangeAsync(filePath, 1, 10).ConfigureAwait(false);

            Assert.That(lines[0], Is.EqualTo("abc→"));
        }
    }
}



