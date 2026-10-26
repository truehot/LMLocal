using System;
using System.IO;
using LMLocal.Commands;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Commands
{
    /// <summary>
    /// Covers the DTE-free disk-fallback of <see cref="CodeCommandHelper"/>: the last resort when an
    /// editor does not expose a text document (some web editors used for .html/.css).
    /// </summary>
    [TestFixture]
    public class CodeCommandHelperTests
    {
        private string _tempFile;

        [SetUp]
        public void SetUp()
        {
            _tempFile = Path.Combine(
                Path.GetTempPath(),
                "lmlocal-code-helper-" + Guid.NewGuid().ToString("N") + ".cs");
            File.WriteAllText(_tempFile, "class Temp {}");
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (!string.IsNullOrEmpty(_tempFile) && File.Exists(_tempFile))
                    File.Delete(_tempFile);
            }
            catch
            {
                // Best-effort cleanup; a leftover temp file must not fail the test.
            }
        }

        [Test]
        public void ReadFromDisk_NullPath_ReturnsNull()
        {
            Assert.That(CodeCommandHelper.ReadFromDisk(null), Is.Null);
        }

        [Test]
        public void ReadFromDisk_EmptyPath_ReturnsNull()
        {
            Assert.That(CodeCommandHelper.ReadFromDisk(string.Empty), Is.Null);
        }

        [Test]
        public void ReadFromDisk_ExistingFile_ReturnsContent()
        {
            Assert.That(CodeCommandHelper.ReadFromDisk(_tempFile), Is.EqualTo("class Temp {}"));
        }

        [Test]
        public void ReadFromDisk_MissingFile_ReturnsNullWithoutThrowing()
        {
            Assert.That(CodeCommandHelper.ReadFromDisk(_tempFile + ".missing"), Is.Null);
        }

        [Test]
        public void ReadFromDisk_DirectoryPath_ReturnsNullWithoutThrowing()
        {
            string directory = Path.GetDirectoryName(_tempFile);

            Assert.That(CodeCommandHelper.ReadFromDisk(directory), Is.Null);
        }

        [Test]
        public void ReadFromDisk_LockedFile_ReturnsNullWithoutThrowing()
        {
            // A sharing violation (or a failed existence probe) must degrade to null, never throw.
            using (new FileStream(_tempFile, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.That(CodeCommandHelper.ReadFromDisk(_tempFile), Is.Null);
            }
        }
    }
}
