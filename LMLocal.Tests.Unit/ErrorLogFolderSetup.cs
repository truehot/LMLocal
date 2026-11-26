using System;
using System.IO;
using LMLocal.Core.Common;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    /// <summary>Points the daily log files of the whole test run to a temp folder, away from the real %LocalAppData% logs.</summary>
    [SetUpFixture]
    public class ErrorLogFolderSetup
    {
        private string _folder;

        [OneTimeSetUp]
        public void RedirectErrorLog()
        {
            _folder = Path.Combine(Path.GetTempPath(), "lmlocal-tests-log-" + Guid.NewGuid().ToString("N"));
            DailyLogFile.DirectoryOverride = _folder;
        }

        [OneTimeTearDown]
        public void RemoveErrorLog()
        {
            DailyLogFile.DirectoryOverride = null;

            try
            {
                if (Directory.Exists(_folder))
                    Directory.Delete(_folder, true);
            }
            catch (IOException)
            {
                // A leftover temp folder must not fail the run.
            }
            catch (UnauthorizedAccessException)
            {
                // A leftover temp folder must not fail the run.
            }
        }
    }
}
