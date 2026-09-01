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
    }
}
