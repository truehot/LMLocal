using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace LMLocal.Core.Common
{
    /// <summary>Daily log files of a Debug build in %LocalAppData%\LMLocalChat\Logs: {name}-YYYY-MM-DD.log, {name}-e2e-YYYY-MM-DD.log in E2E runs.</summary>
    internal static class DailyLogFile
    {
        private const string LocalAppDataFolder = "LMLocalChat";
        private const string LogFolderName = "Logs";

        /// <summary>Set by the E2E VsLauncher on the VS it starts; LMLocalPackage reads it too.</summary>
        private const string E2eEnvironmentVariable = "LMLocal_IPC";

        private static readonly object _lock = new object();

        /// <summary>%LocalAppData%\LMLocalChat\Logs.</summary>
        internal static string DefaultDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LocalAppDataFolder, LogFolderName);

        /// <summary>Folder used instead of <see cref="DefaultDirectory"/>; set only by tests.</summary>
        internal static string DirectoryOverride { get; set; }

        /// <summary>Appends text to the file of the day of <paramref name="now"/>; a write failure goes to Debug output only.</summary>
        public static void Append(string name, DateTime now, string text)
        {
            try
            {
                string directory = DirectoryOverride ?? DefaultDirectory;
                string kind = Environment.GetEnvironmentVariable(E2eEnvironmentVariable) == "1" ? "-e2e-" : "-";
                string fileName = name + kind + now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log";

                lock (_lock)
                {
                    // AppendAllText does not create the folder.
                    Directory.CreateDirectory(directory);
                    File.AppendAllText(Path.Combine(directory, fileName), text, Encoding.UTF8);
                }
            }
            catch (Exception writeFailure)
            {
                System.Diagnostics.Debug.WriteLine($"[InternalLogger] Cannot write the {name} log file: {writeFailure.Message}");
            }
        }

        /// <summary>"[YYYY-MM-DD HH:mm:ss.fff]" in local time.</summary>
        public static string Timestamp(DateTime now)
        {
            return "[" + now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "]";
        }
    }
}
