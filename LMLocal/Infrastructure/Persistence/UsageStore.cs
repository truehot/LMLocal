using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Usage;
using LMLocal.Core.Common;
using LMLocal.Core.Models.Usage;
using LMLocal.Infrastructure.Time;

namespace LMLocal.Infrastructure.Persistence
{
    /// <summary>
    /// Stores usage records in monthly JSON Lines files under <c>%LOCALAPPDATA%\&lt;app&gt;\Usage</c>.
    /// </summary>
    internal sealed class UsageStore : IUsageRecorder, IUsageReader
    {
        /// <summary>Folder under the app data directory that holds the usage files.</summary>
        internal const string UsageFolderName = "Usage";

        /// <summary>File name of the VSIX manifest that carries the real extension version.</summary>
        internal const string ManifestFileName = "extension.vsixmanifest";

        /// <summary>How far back a single session is searched, so a lost month cannot stall the dialog.</summary>
        internal const int MaxMonthsBack = 12;

        /// <summary>
        /// How many empty months the search may pass before it gives up.
        /// </summary>
        internal const int MaxLeadingEmptyMonths = 2;

        private const string IdentityElement = "<Identity";
        private const string VersionAttribute = "Version=\"";

        private readonly ISettingsManager _settingsManager;
        private readonly IFileSystem _fileSystem;
        private readonly IFileLockManager _fileLockManager;
        private readonly string _usageDirectory;
        private readonly string _appVersion;
        private readonly Func<string> _solutionDirectory;
        private readonly ITimeProvider _timeProvider;

        /// <summary>
        /// Store bound to the configured app data folder.
        /// </summary>
        public UsageStore(
            ISettingsManager settingsManager,
            IFileSystem fileSystem,
            IFileLockManager fileLockManager,
            Func<string> solutionDirectory,
            ITimeProvider timeProvider = null)
        {
            if (settingsManager == null)
                throw new ArgumentNullException(nameof(settingsManager));
            if (fileSystem == null)
                throw new ArgumentNullException(nameof(fileSystem));
            if (fileLockManager == null)
                throw new ArgumentNullException(nameof(fileLockManager));
            if (solutionDirectory == null)
                throw new ArgumentNullException(nameof(solutionDirectory));

            _settingsManager = settingsManager;
            _fileSystem = fileSystem;
            _fileLockManager = fileLockManager;
            _solutionDirectory = solutionDirectory;
            _timeProvider = timeProvider ?? new SystemTimeProvider();

            _usageDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                settingsManager.LocalAppDataFolder,
                UsageFolderName);

            _appVersion = ReadAppVersion(_fileSystem);

            try
            {
                _fileSystem.CreateDirectory(_usageDirectory);
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"UsageStore: failed to create the usage folder '{_usageDirectory}': {ex.Message}");
            }
        }

        /// <summary>Folder the usage files are written to; exposed for diagnostics and tests.</summary>
        internal string UsageDirectory { get { return _usageDirectory; } }

        public async Task AppendAsync(UsageRecord record, CancellationToken cancellationToken = default)
        {
            if (record == null)
                return;

            try
            {
                // Checked at write time, exactly like the chat log: a request that ended after the option was turned
                // off is not written.
                if (_settingsManager.Current == null || !_settingsManager.Current.EnableChatLogging)
                    return;

                if (string.IsNullOrWhiteSpace(record.AppVersion))
                    record.AppVersion = _appVersion;

                if (string.IsNullOrWhiteSpace(record.SolutionDir))
                    record.SolutionDir = CurrentSolutionDirectory();

                string filePath = Path.Combine(_usageDirectory, UsageRecordSerializer.BuildFileName(record.Timestamp));
                byte[] data = Encoding.UTF8.GetBytes(UsageRecordSerializer.BuildLine(record));

                _fileSystem.EnsureDirectoryExistsForFile(filePath);

                await _fileLockManager.WaitAsync(filePath, cancellationToken).ConfigureAwait(false);

                try
                {
                    if (_fileSystem.FileExists(filePath))
                    {
                        await _fileSystem.AppendAllBytesAsync(filePath, data, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await _fileSystem.WriteAllBytesAsync(filePath, data, cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _fileLockManager.Release(filePath);
                }
            }
            catch (Exception ex)
            {
                // Usage is a metric: losing a line must never break the request that produced it.
                InternalLogger.Warn($"UsageStore: failed to append a usage record: {ex.Message}");
            }
        }

        /// <summary>
        /// Folder of the solution open at write time, or null when none is open. The folder is only a label on the record,
        /// so a provider that fails costs the label and nothing else.
        /// </summary>
        private string CurrentSolutionDirectory()
        {
            try
            {
                string directory = _solutionDirectory();

                return string.IsNullOrWhiteSpace(directory) ? null : directory;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"UsageStore: failed to read the solution folder: {ex.Message}");
                return null;
            }
        }

        public async Task<IReadOnlyList<UsageRecord>> ReadAsync(DateTime fromUtc, CancellationToken cancellationToken = default)
        {
            var records = new List<UsageRecord>();

            foreach (DateTime month in MonthStarts(fromUtc, _timeProvider.UtcNow))
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (UsageRecord record in await ReadFileAsync(FilePathOf(month), cancellationToken).ConfigureAwait(false))
                {
                    if (record.Timestamp >= fromUtc)
                        records.Add(record);
                }
            }

            return records;
        }

        public async Task<IReadOnlyList<UsageRecord>> ReadSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            var records = new List<UsageRecord>();

            if (string.IsNullOrWhiteSpace(sessionId))
                return records;

            string wanted = UsageIds.Normalize(sessionId);

            DateTime nowUtc = _timeProvider.UtcNow;
            DateTime month = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            bool foundAny = false;
            int emptyMonths = 0;

            for (int i = 0; i < MaxMonthsBack; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var found = new List<UsageRecord>();

                foreach (UsageRecord record in await ReadFileAsync(FilePathOf(month), cancellationToken).ConfigureAwait(false))
                {
                    if (string.Equals(UsageIds.Normalize(record.SessionId), wanted, StringComparison.Ordinal))
                        found.Add(record);
                }

                if (found.Count == 0)
                {
                    if (foundAny || ++emptyMonths > MaxLeadingEmptyMonths)
                        break;

                    month = month.AddMonths(-1);
                    continue;
                }

                foundAny = true;
                records.AddRange(found);
                month = month.AddMonths(-1);
            }

            return records;
        }

        private string FilePathOf(DateTime monthUtc)
        {
            return Path.Combine(_usageDirectory, UsageRecordSerializer.BuildFileName(monthUtc));
        }

        private async Task<List<UsageRecord>> ReadFileAsync(string filePath, CancellationToken cancellationToken)
        {
            var records = new List<UsageRecord>();

            if (!_fileSystem.FileExists(filePath))
                return records;

            string content = await _fileSystem.ReadAllTextWithSharedReadAsync(filePath, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrEmpty(content))
                return records;

            using (var reader = new StringReader(content))
            {
                string line;

                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    UsageRecord record = UsageRecordSerializer.ParseLine(line);

                    if (record != null)
                        records.Add(record);
                }
            }

            return records;
        }

        /// <summary>
        /// UTC months from <paramref name="fromUtc"/> to <paramref name="toUtc"/> inclusive.
        /// </summary>
        private static IEnumerable<DateTime> MonthStarts(DateTime fromUtc, DateTime toUtc)
        {
            if (fromUtc > toUtc)
                yield break;

            var month = new DateTime(fromUtc.Year, fromUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var last = new DateTime(toUtc.Year, toUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            while (month <= last)
            {
                yield return month;
                month = month.AddMonths(1);
            }
        }

        /// <summary>
        /// Version of the extension: the Identity version of the VSIX manifest, read once.
        /// </summary>
        private static string ReadAppVersion(IFileSystem fileSystem)
        {
            foreach (string folder in ManifestFolders())
            {
                if (string.IsNullOrEmpty(folder))
                    continue;

                try
                {
                    string manifestPath = Path.Combine(folder, ManifestFileName);

                    if (!fileSystem.FileExists(manifestPath))
                        continue;

                    string version = ExtractIdentityVersion(fileSystem.ReadAllText(manifestPath));

                    if (!string.IsNullOrWhiteSpace(version))
                        return version.Trim();
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"UsageStore: failed to read '{ManifestFileName}' in '{folder}': {ex.Message}");
                }
            }

            try
            {
                Assembly assembly = typeof(UsageStore).Assembly;
                var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

                if (informational != null && !string.IsNullOrWhiteSpace(informational.InformationalVersion))
                    return informational.InformationalVersion;

                Version version = assembly.GetName().Version;
                return version != null ? version.ToString() : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Folders that can hold the deployed VSIX manifest: next to the assembly first (the installed extension
        /// folder), then the base directory of the host process.
        /// </summary>
        private static string[] ManifestFolders()
        {
            string assemblyFolder = null;

            try
            {
                string location = Assembly.GetExecutingAssembly().Location;

                if (!string.IsNullOrEmpty(location))
                    assemblyFolder = Path.GetDirectoryName(location);
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"UsageStore: failed to locate the assembly folder: {ex.Message}");
            }

            return new[] { assemblyFolder, AppDomain.CurrentDomain.BaseDirectory };
        }

        /// <summary>
        /// Reads <c>Version</c> of the <c>&lt;Identity&gt;</c> element without pulling in an XML dependency.
        /// </summary>
        private static string ExtractIdentityVersion(string manifest)
        {
            if (string.IsNullOrEmpty(manifest))
                return null;

            int identity = manifest.IndexOf(IdentityElement, StringComparison.OrdinalIgnoreCase);

            if (identity < 0)
                return null;

            int version = manifest.IndexOf(VersionAttribute, identity, StringComparison.OrdinalIgnoreCase);

            if (version < 0)
                return null;

            int start = version + VersionAttribute.Length;
            int end = manifest.IndexOf('"', start);

            if (end <= start)
                return null;

            return manifest.Substring(start, end - start);
        }
    }
}
