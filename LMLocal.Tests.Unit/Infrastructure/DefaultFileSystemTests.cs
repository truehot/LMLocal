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
    }
}
