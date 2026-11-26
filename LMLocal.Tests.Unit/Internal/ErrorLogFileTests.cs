using System;
using System.IO;
using System.Linq;
using System.Threading;
using LMLocal.Core.Common;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    /// <summary>The daily error file of a Debug build, written into a per-test temp folder.</summary>
    [TestFixture]
    public class ErrorLogFileTests
    {
        private string _folder;
        private string _previousOverride;

        [SetUp]
        public void SetUp()
        {
            _previousOverride = DailyLogFile.DirectoryOverride;
            _folder = Path.Combine(Path.GetTempPath(), "lmlocal-log-" + Guid.NewGuid().ToString("N"));
            DailyLogFile.DirectoryOverride = _folder;
        }

        [TearDown]
        public void TearDown()
        {
            DailyLogFile.DirectoryOverride = _previousOverride;
            InternalLogger.SetLogger(new DebugLogger());

            try
            {
                if (Directory.Exists(_folder))
                    Directory.Delete(_folder, true);
            }
            catch (IOException)
            {
                // A leftover temp folder must not fail the test.
            }
        }

        [Test]
        public void DefaultDirectory_IsUnderLocalAppData()
        {
            string expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LMLocalChat",
                "Logs");

            Assert.That(DailyLogFile.DefaultDirectory, Is.EqualTo(expected));
        }

#if DEBUG
        // Write is [Conditional("DEBUG")]: these calls are removed in Release.
        [Test]
        public void Write_CreatesTheFileOfTheDay()
        {
            string expectedName = "lmlocal-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log";

            ErrorLogFile.Write("boom", null);

            string[] files = Directory.GetFiles(_folder);
            Assert.That(files.Select(Path.GetFileName), Is.EquivalentTo(new[] { expectedName }), "unexpected files: " + string.Join(", ", files));
        }

        [Test]
        public void Write_InE2eRun_CreatesTheE2eFileOfTheDay()
        {
            string expectedName = "lmlocal-e2e-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log";
            string previous = Environment.GetEnvironmentVariable("LMLocal_IPC");
            Environment.SetEnvironmentVariable("LMLocal_IPC", "1");

            try
            {
                ErrorLogFile.Write("boom", null);
            }
            finally
            {
                Environment.SetEnvironmentVariable("LMLocal_IPC", previous);
            }

            string[] files = Directory.GetFiles(_folder);
            Assert.That(files.Select(Path.GetFileName), Is.EquivalentTo(new[] { expectedName }), "unexpected files: " + string.Join(", ", files));
        }

        [Test]
        public void Write_Record_HasTimeLevelThreadAndMessage()
        {
            ErrorLogFile.Write("Something failed", null);

            string text = ReadSingleLog();
            Assert.That(text, Does.Match(@"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\]"));
            Assert.That(text, Does.Contain("[ERROR] [tid:" + Thread.CurrentThread.ManagedThreadId + "] Something failed"));
        }

        [Test]
        public void Write_Record_HasTheWholeInnerExceptionChain()
        {
            var ex = new InvalidOperationException("outer", new ArgumentException("inner"));

            ErrorLogFile.Write("Something failed", ex);

            string text = ReadSingleLog();
            Assert.That(text, Does.Contain("System.InvalidOperationException: outer"));
            Assert.That(text, Does.Contain("System.ArgumentException: inner"));
        }

        [Test]
        public void Write_SecondRecord_GoesToTheSameFile_AfterTheFirst()
        {
            ErrorLogFile.Write("first record", null);
            ErrorLogFile.Write("second record", null);

            string text = ReadSingleLog();
            Assert.That(text.IndexOf("first record", StringComparison.Ordinal),
                Is.LessThan(text.IndexOf("second record", StringComparison.Ordinal)));
        }

        [Test]
        public void Write_DoesNotThrow_WhenTheFolderCannotBeCreated()
        {
            Directory.CreateDirectory(_folder);
            string notAFolder = Path.Combine(_folder, "file-in-the-way.txt");
            File.WriteAllText(notAFolder, "x");
            DailyLogFile.DirectoryOverride = notAFolder;

            Assert.DoesNotThrow(() => ErrorLogFile.Write("boom", new Exception("boom")));
        }

        [Test]
        public void Error_OfTheDefaultLogger_ReachesTheFile()
        {
            InternalLogger.SetLogger(new DebugLogger());

            InternalLogger.Error("wired to the file", new InvalidOperationException("wired"));

            string text = ReadSingleLog();
            Assert.That(text, Does.Contain("wired to the file"));
            Assert.That(text, Does.Contain("System.InvalidOperationException: wired"));
        }

        [Test]
        public void Warn_OfTheDefaultLogger_DoesNotReachTheFile()
        {
            InternalLogger.SetLogger(new DebugLogger());

            InternalLogger.Warn("warning only", new InvalidOperationException("warning only"));

            Assert.That(Directory.Exists(_folder), Is.False, "a warning must not create the error file");
        }

        private string ReadSingleLog()
        {
            string[] files = Directory.GetFiles(_folder, "lmlocal-*.log");
            Assert.That(files, Has.Length.EqualTo(1), "files: " + string.Join(", ", files));
            return File.ReadAllText(files[0]);
        }
#endif
    }
}
