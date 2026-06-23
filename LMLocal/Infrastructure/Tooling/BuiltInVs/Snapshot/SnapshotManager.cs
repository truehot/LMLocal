using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot.Infrastructure;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot
{
    internal interface ISnapshotManager
    {
        event Func<IReadOnlyList<SnapshotFileChange>, Task> SnapshotChangedAsync;
        Task LoadSnapshotAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<string>> GetChangedFilesAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SnapshotFileChange>> GetChangedFilesWithStatusAsync(CancellationToken cancellationToken = default);
        Task SnapshotFileAsync(string absolutePath, SnapshotChangeStatus changeStatus, CancellationToken cancellationToken = default);
        Task RollbackAllAsync(CancellationToken cancellationToken = default);
        Task RollbackFilesAsync(IEnumerable<string> relativePaths, CancellationToken cancellationToken = default);
        Task CommitAllAsync(CancellationToken cancellationToken = default);
        Task CommitFilesAsync(IEnumerable<string> relativePaths, CancellationToken cancellationToken = default);
        Task<string> GetSnapshotFilePathAsync(string relativePath, CancellationToken cancellationToken = default);
        string GetCurrentFilePath(string relativePath);
        string GetTmpDirectoryPath();
        Task BeginBatchAsync(CancellationToken cancellationToken = default);
        Task EndBatchAsync(CancellationToken cancellationToken = default);
        Task CancelBatchAsync(CancellationToken cancellationToken = default);
        Task ResetAsync(CancellationToken ct = default);
    }

    internal sealed class SnapshotManager : ISnapshotManager, IDisposable
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly IFileSystem _fileSystem;
        private readonly ISnapshotPathsFactory _pathsFactory;
        private readonly IFileLockManager _lockManager;

        private readonly AsyncLock _lock = new AsyncLock();

        private SnapshotPaths _paths;
        private SnapshotManifestInfo _manifest;
        private bool _manifestDirty;

        private readonly Dictionary<string, SnapshotFileChange> _pendingChanges = new Dictionary<string, SnapshotFileChange>(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _batchPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _isBatching;

        public event Func<IReadOnlyList<SnapshotFileChange>, Task> SnapshotChangedAsync;

        public SnapshotManager(
            IVsDependencies vsDependencies,
            IPathResolver pathResolver,
            IFileSystem fileSystem,
            ISnapshotPathsFactory pathsFactory,
            IFileLockManager lockManager)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _pathsFactory = pathsFactory ?? throw new ArgumentNullException(nameof(pathsFactory));
            _lockManager = lockManager ?? throw new ArgumentNullException(nameof(lockManager));
        }

        public async Task LoadSnapshotAsync(CancellationToken ct)
        {
            IReadOnlyList<SnapshotFileChange> filesToNotify = null;

            using (await _lock.LockAsync(ct).ConfigureAwait(false))
            {
                if (_manifest != null)
                    return;

                if (!_vsDependencies.IsSolutionOpen)
                    return;

                var paths = _pathsFactory.Create();
                if (paths == null)
                    return;

                EnsureDirectories(paths);

                SnapshotManifestInfo manifest = null;

                if (_fileSystem.FileExists(paths.ManifestPath))
                {
                    try
                    {
                        manifest = await LoadManifestFromDiskAsync(paths.ManifestPath, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is JsonException || ex is InvalidDataException)
                    {
                        InternalLogger.Error($"Failed to parse snapshot manifest: {ex.Message}. Archiving corrupted data.");
                        ArchiveCorrupted(paths.SnapshotDir);
                        paths = _pathsFactory.Create();
                        if (paths == null) return;
                        EnsureDirectories(paths);
                    }
                }

                if (manifest == null)
                {
                    manifest = CreateNewManifest();
                    _manifestDirty = true;
                    await SaveManifestAsync(paths.ManifestPath, manifest, ct).ConfigureAwait(false);
                    _manifestDirty = false;
                }

                _paths = paths;
                _manifest = manifest;
                _manifestDirty = false;

                foreach (var entry in manifest.Files)
                {
                    string status;
                    if (entry.ExistedAtSnapshot)
                    {
                        string absPath = Path.Combine(manifest.SolutionRoot, entry.RelativePath);
                        status = _fileSystem.FileExists(absPath) ? "modified" : "deleted";
                    }
                    else
                    {
                        status = "created";
                    }

                    _pendingChanges[entry.RelativePath] = new SnapshotFileChange
                    {
                        RelativePath = entry.RelativePath,
                        Status = status
                    };
                }

                filesToNotify = _pendingChanges.Values.ToList().AsReadOnly();
            }

            var handlers = SnapshotChangedAsync;
            if (handlers != null)
                await handlers.Invoke(filesToNotify);
        }

        public async Task ResetAsync(CancellationToken ct = default)
        {
            using (await _lock.LockAsync(ct).ConfigureAwait(false))
            {
                _pendingChanges.Clear();
                _batchPaths.Clear();
                _isBatching = false;
                _manifest = null;
                _paths = null;
                _manifestDirty = false;
            }
            var handlers = SnapshotChangedAsync;
            if (handlers != null)
                await handlers.Invoke(Array.Empty<SnapshotFileChange>());
        }

        public async Task<IReadOnlyList<string>> GetChangedFilesAsync(CancellationToken ct = default)
        {
            using (await _lock.LockAsync(ct).ConfigureAwait(false))
            {
                var files = _manifest?.Files?.Select(f => f.RelativePath).ToList() ?? new List<string>();
                return files.AsReadOnly();
            }
        }

        public async Task<IReadOnlyList<SnapshotFileChange>> GetChangedFilesWithStatusAsync(CancellationToken ct = default)
        {
            using (await _lock.LockAsync(ct).ConfigureAwait(false))
            {
                return _pendingChanges.Values.ToList().AsReadOnly();
            }
        }

        public async Task<string> GetSnapshotFilePathAsync(string relativePath, CancellationToken ct = default)
        {
            using (await _lock.LockAsync(ct).ConfigureAwait(false))
            {
                if (string.IsNullOrEmpty(relativePath) || _paths == null || _manifest == null)
                    return null;

                if (!IsValidRelativePath(relativePath))
                    return null;

                var entry = _manifest.Files.Find(f => string.Equals(f.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                    return null;

                string bakPath = _paths.GetBackupPath(entry.BackupId);
                return _fileSystem.FileExists(bakPath) ? bakPath : null;
            }
        }

        public string GetCurrentFilePath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath) || !_vsDependencies.IsSolutionOpen)
                return null;

            string solutionDir = _vsDependencies.GetSolutionDirectory();
            if (!_pathResolver.TryResolveFilePath(relativePath, solutionDir, out string absolutePath))
                return null;
            if (!_pathResolver.IsPathInsideDirectory(absolutePath, solutionDir))
                return null;
            if (!_fileSystem.FileExists(absolutePath))
                return null;

            return absolutePath;
        }

        public string GetTmpDirectoryPath()
        {
            using (_lock.Lock())
                return _paths?.TmpDirectory;
        }

        public async Task SnapshotFileAsync(string absolutePath, SnapshotChangeStatus changeStatus, CancellationToken ct)
        {
            if (!_isBatching)
                throw new InvalidOperationException("Cannot perform snapshot while a batch is not active.");

            if (_manifest == null)
                await LoadSnapshotAsync(ct).ConfigureAwait(false);

            IReadOnlyList<SnapshotFileChange> filesToNotify = null;

            using (await _lock.LockAsync(ct).ConfigureAwait(false))
            {
                if (_paths == null || _manifest == null)
                    return;

                string solutionDir = _vsDependencies.GetSolutionDirectory();

                if (!_pathResolver.IsPathInsideDirectory(absolutePath, solutionDir))
                    throw new InvalidOperationException($"File '{absolutePath}' is outside the solution directory.");

                if (!_pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath))
                    throw new InvalidOperationException($"Cannot compute relative path for '{absolutePath}'.");

                var existingEntry = _manifest.Files.Find(f => string.Equals(f.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
                if (existingEntry == null)
                {
                    bool fileExists = _fileSystem.FileExists(absolutePath);

                    if (fileExists)
                    {
                        var backupId = Guid.NewGuid();
                        string bakPath = _paths.GetBackupPath(backupId);
                        _fileSystem.EnsureDirectoryExistsForFile(bakPath);
                        await _fileSystem.CopyFileAsync(absolutePath, bakPath, ct).ConfigureAwait(false);

                        var (Length, LastWriteTimeUtc) = _fileSystem.GetFileInfo(absolutePath);
                        _manifest.Files.Add(new SnapshotFileEntry
                        {
                            RelativePath = relativePath,
                            BackupId = backupId,
                            ExistedAtSnapshot = true,
                            FileSize = Length,
                            LastWriteTimeUtc = LastWriteTimeUtc
                        });
                    }
                    else
                    {
                        _manifest.Files.Add(new SnapshotFileEntry
                        {
                            RelativePath = relativePath,
                            BackupId = Guid.NewGuid(),
                            ExistedAtSnapshot = false,
                            FileSize = 0,
                            LastWriteTimeUtc = DateTime.MinValue
                        });
                    }

                    _manifestDirty = true;
                }

                string statusStr;
                switch (changeStatus)
                {
                    case SnapshotChangeStatus.BeforeCreate:
                        statusStr = "created";
                        break;
                    case SnapshotChangeStatus.BeforeDelete:
                        statusStr = "deleted";
                        break;
                    case SnapshotChangeStatus.BeforeModify:
                        if (existingEntry != null && !existingEntry.ExistedAtSnapshot)
                            statusStr = "created";
                        else
                            statusStr = "modified";
                        break;
                    default:
                        statusStr = "modified";
                        break;
                }

                _pendingChanges[relativePath] = new SnapshotFileChange
                {
                    RelativePath = relativePath,
                    Status = statusStr
                };

                if (_isBatching)
                {
                    _batchPaths.Add(relativePath);
                }
                else
                {
                    await SaveManifestIfDirtyAsync(ct).ConfigureAwait(false);
                    filesToNotify = _pendingChanges.Values.ToList().AsReadOnly();
                }
            }

            if (filesToNotify != null)
            {
                var handlers = SnapshotChangedAsync;
                if (handlers != null)
                    await handlers.Invoke(filesToNotify);
            }
        }

        public async Task BeginBatchAsync(CancellationToken ct)
        {
            if (_manifest == null)
                await LoadSnapshotAsync(ct).ConfigureAwait(false);

            using (await _lock.LockAsync(ct).ConfigureAwait(false))
            {
                _batchPaths.Clear();
                _isBatching = true;
            }
        }

        public async Task EndBatchAsync(CancellationToken ct)
        {
            IReadOnlyList<SnapshotFileChange> filesToNotify = null;

            using (await _lock.LockAsync(ct).ConfigureAwait(false))
            {
                if (_manifest == null || _paths == null)
                    return;

                var toRemove = new List<SnapshotFileEntry>();
                foreach (string relPath in _batchPaths)
                {
                    var entry = _manifest.Files.Find(f => string.Equals(f.RelativePath, relPath, StringComparison.OrdinalIgnoreCase));
                    if (entry == null) continue;

                    string absolutePath = Path.Combine(_manifest.SolutionRoot, relPath);

                    if (!entry.ExistedAtSnapshot && !_fileSystem.FileExists(absolutePath))
                    {
                        string bakPath = _paths.GetBackupPath(entry.BackupId);
                        if (_fileSystem.FileExists(bakPath))
                            _fileSystem.Delete(bakPath);
                        toRemove.Add(entry);
                        _pendingChanges.Remove(relPath);
                        continue;
                    }

                    if (entry.ExistedAtSnapshot && _fileSystem.FileExists(absolutePath))
                    {
                        string bakPath = _paths.GetBackupPath(entry.BackupId);
                        if (_fileSystem.FileExists(bakPath))
                        {
                            var (Length, LastWriteTimeUtc) = _fileSystem.GetFileInfo(absolutePath);
                            var bakFi = _fileSystem.GetFileInfo(bakPath);
                            if (Length == bakFi.Length && LastWriteTimeUtc == bakFi.LastWriteTimeUtc)
                            {
                                _fileSystem.Delete(bakPath);
                                toRemove.Add(entry);
                                _pendingChanges.Remove(relPath);
                                continue;
                            }
                        }
                    }
                }

                foreach (var entry in toRemove)
                    _manifest.Files.Remove(entry);

                if (toRemove.Count > 0)
                    _manifestDirty = true;

                _batchPaths.Clear();
                _isBatching = false;

                await SaveManifestIfDirtyAsync(ct).ConfigureAwait(false);
                filesToNotify = _pendingChanges.Values.ToList().AsReadOnly();
            }

            if (filesToNotify != null)
            {
                var handlers = SnapshotChangedAsync;
                if (handlers != null)
                    await handlers.Invoke(filesToNotify);
            }
        }

        public async Task CancelBatchAsync(CancellationToken ct = default)
        {
            using (await _lock.LockAsync(ct).ConfigureAwait(false))
            {
                if (!_isBatching)
                    return;

                _batchPaths.Clear();
                _isBatching = false;
            }
        }

        public async Task RollbackAllAsync(CancellationToken ct)
        {
            List<(SnapshotFileEntry entry, string bakPath)> entriesWithBak;
            string solutionDir;

            using (await _lock.LockAsync(ct).ConfigureAwait(false))
            {
                if (_isBatching)
                    throw new InvalidOperationException("Cannot perform rollback while a batch is active. Call EndBatchAsync first.");

                if (_manifest == null || _manifest.Files.Count == 0)
                    return;
                solutionDir = _manifest.SolutionRoot;
                entriesWithBak = _manifest.Files
                    .Select(entry => (entry, _paths.GetBackupPath(entry.BackupId)))
                    .ToList();
            }

            var errors = new List<Exception>();

            foreach (var (entry, bakPath) in entriesWithBak)
            {
                try
                {
                    await RollbackOneAsync(entry, solutionDir, bakPath, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    InternalLogger.Error($"RollbackAll error for '{entry.RelativePath}': {ex}");
                    errors.Add(new Exception($"Failed to rollback '{entry.RelativePath}': {ex.Message}", ex));
                }
            }

            string dirToDelete = null;
            using (await _lock.LockAsync(ct).ConfigureAwait(false))
            {
                if (_manifest == null || _paths == null)
                    return;

                _manifest.Files.Clear();
                _pendingChanges.Clear();
                _manifestDirty = true;

                dirToDelete = await CleanupAndGetDeletionPathAsync(ct).ConfigureAwait(false);
            }

            if (dirToDelete != null)
            {
                await Task.Run(() => Directory.Delete(dirToDelete, recursive: true), ct).ConfigureAwait(false);
            }

            var handlers = SnapshotChangedAsync;
            if (handlers != null)
                await handlers.Invoke(Array.Empty<SnapshotFileChange>());

            if (errors.Count > 0)
                throw new AggregateException("RollbackAll completed with errors.", errors);
        }
        public async Task RollbackFilesAsync(IEnumerable<string> relativePaths, CancellationToken ct)
        {
            if (_isBatching)
                throw new InvalidOperationException("Cannot perform rollback while a batch is active. Call EndBatchAsync first.");

            var validPaths = new List<string>();
            var pathErrors = new List<Exception>();

            foreach (string rel in relativePaths)
            {
                if (!IsValidRelativePath(rel))
                {
                    pathErrors.Add(new ArgumentException($"Path '{rel}' is not inside solution."));
                }
                else
                {
                    validPaths.Add(rel);
                }
            }

            if (validPaths.Count == 0)
            {
                if (pathErrors.Count > 0)
                    throw new AggregateException("RollbackFiles completed with errors.", pathErrors);

                return;
            }

            if (_manifest == null || _manifest.Files.Count == 0)
                return;

            List<(SnapshotFileEntry entry, string bakPath)> entriesToRollback;
            string solutionDir;

            using (await _lock.LockAsync(ct).ConfigureAwait(false))
            {
                if (_isBatching)
                    throw new InvalidOperationException("Cannot perform rollback while a batch is active. Call EndBatchAsync first.");

                solutionDir = _manifest.SolutionRoot;
                entriesToRollback = validPaths
                    .Select(p => _manifest.Files.Find(
                        f => string.Equals(f.RelativePath, p, StringComparison.OrdinalIgnoreCase)))
                    .Where(e => e != null)
                    .Select(e => (e, _paths.GetBackupPath(e.BackupId)))
                    .ToList();
            }

            var errors = new List<Exception>();

            foreach (var (entry, bakPath) in entriesToRollback)
            {
                try
                {
                    await RollbackOneAsync(entry, solutionDir, bakPath, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    InternalLogger.Error($"RollbackFiles error for '{entry.RelativePath}': {ex}");
                    errors.Add(new Exception($"Failed to rollback '{entry.RelativePath}': {ex.Message}", ex));
                }
            }

            string dirToDelete = null;
            using (await _lock.LockAsync(ct).ConfigureAwait(false))
            {
                if (_manifest == null || _paths == null)
                    return;

                foreach (var entry in entriesToRollback.Select(t => t.entry))
                {
                    if (_manifest.Files.Contains(entry))
                        _manifest.Files.Remove(entry);
                    _pendingChanges.Remove(entry.RelativePath);
                }
                if (entriesToRollback.Count > 0)
                    _manifestDirty = true;

                dirToDelete = await CleanupAndGetDeletionPathAsync(ct).ConfigureAwait(false);
            }

            if (dirToDelete != null)
            {
                await Task.Run(() => Directory.Delete(dirToDelete, recursive: true), ct).ConfigureAwait(false);
            }

            var handlers = SnapshotChangedAsync;
            if (handlers != null)
                await handlers.Invoke(Array.Empty<SnapshotFileChange>());

            var allErrors = pathErrors.Concat(errors).ToList();
            if (allErrors.Count > 0)
                throw new AggregateException("RollbackFiles completed with errors.", allErrors);
        }

        public async Task CommitAllAsync(CancellationToken ct)
        {
            List<(SnapshotFileEntry entry, string bakPath)> entriesWithBak;

            using (await _lock.LockAsync(ct).ConfigureAwait(false))
            {
                if (_isBatching)
                    throw new InvalidOperationException("Cannot perform commit while a batch is active. Call EndBatchAsync first.");

                if (_manifest == null || _manifest.Files.Count == 0)
                    return;

                entriesWithBak = _manifest.Files
                    .Select(entry => (entry, _paths.GetBackupPath(entry.BackupId)))
                    .ToList();
            }

            var errors = new List<Exception>();

            foreach (var (entry, bakPath) in entriesWithBak)
            {
                try
                {
                    await CommitOneAsync(bakPath, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    InternalLogger.Error($"CommitAll error for '{entry.RelativePath}': {ex}");
                    errors.Add(new Exception($"Failed to commit '{entry.RelativePath}': {ex.Message}", ex));
                }
            }

            string dirToDelete = null;
            using (await _lock.LockAsync(ct).ConfigureAwait(false))
            {
                if (_manifest == null || _paths == null)
                    return;

                _manifest.Files.Clear();
                _pendingChanges.Clear();
                _manifestDirty = true;

                dirToDelete = await CleanupAndGetDeletionPathAsync(ct).ConfigureAwait(false);
            }

            if (dirToDelete != null)
            {
                await Task.Run(() => Directory.Delete(dirToDelete, recursive: true), ct).ConfigureAwait(false);
            }

            var handlers = SnapshotChangedAsync;
            if (handlers != null)
                await handlers.Invoke(Array.Empty<SnapshotFileChange>());

            if (errors.Count > 0)
                throw new AggregateException("CommitAll completed with errors.", errors);
        }

        public async Task CommitFilesAsync(IEnumerable<string> relativePaths, CancellationToken ct)
        {
            if (_isBatching)
                throw new InvalidOperationException("Cannot perform rollback while a batch is active. Call EndBatchAsync first.");

            var validPaths = new List<string>();
            var pathErrors = new List<Exception>();

            foreach (string rel in relativePaths)
            {
                if (!IsValidRelativePath(rel))
                {
                    pathErrors.Add(new ArgumentException($"Path '{rel}' is not inside solution."));
                }
                else
                {
                    validPaths.Add(rel);
                }
            }

            if (validPaths.Count == 0)
            {
                if (pathErrors.Count > 0)
                    throw new AggregateException("CommitFiles completed with errors.", pathErrors);

                return;
            }

            List<(SnapshotFileEntry entry, string bakPath)> entriesToCommit;
            using (await _lock.LockAsync(ct).ConfigureAwait(false))
            {
                if (_isBatching)
                    throw new InvalidOperationException("Cannot perform commit while a batch is active. Call EndBatchAsync first.");

                if (_manifest == null || _manifest.Files.Count == 0)
                    return;

                entriesToCommit = validPaths
                    .Select(p => _manifest.Files.Find(
                        f => string.Equals(f.RelativePath, p, StringComparison.OrdinalIgnoreCase)))
                    .Where(e => e != null)
                    .Select(e => (e, _paths.GetBackupPath(e.BackupId)))
                    .ToList();
            }

            var errors = new List<Exception>();

            foreach (var (entry, bakPath) in entriesToCommit)
            {
                try
                {
                    await CommitOneAsync(bakPath, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    InternalLogger.Error($"CommitFiles error for '{entry.RelativePath}': {ex}");
                    errors.Add(new Exception($"Failed to commit '{entry.RelativePath}': {ex.Message}", ex));
                }
            }

            string dirToDelete = null;
            using (await _lock.LockAsync(ct).ConfigureAwait(false))
            {
                if (_manifest == null || _paths == null)
                    return;

                foreach (var entry in entriesToCommit.Select(t => t.entry))
                {
                    if (_manifest.Files.Contains(entry))
                        _manifest.Files.Remove(entry);

                    _pendingChanges.Remove(entry.RelativePath);
                }
                if (entriesToCommit.Count > 0)
                    _manifestDirty = true;

                dirToDelete = await CleanupAndGetDeletionPathAsync(ct).ConfigureAwait(false);
            }

            if (dirToDelete != null)
            {
                await Task.Run(() => Directory.Delete(dirToDelete, recursive: true), ct).ConfigureAwait(false);
            }

            var handlers = SnapshotChangedAsync;
            if (handlers != null)
                await handlers.Invoke(Array.Empty<SnapshotFileChange>());

            var allErrors = pathErrors.Concat(errors).ToList();
            if (allErrors.Count > 0)
                throw new AggregateException("CommitFiles completed with errors.", allErrors);
        }

        private async Task RollbackOneAsync(SnapshotFileEntry entry, string solutionDir, string bakPath, CancellationToken ct)
        {
            string originalPath = Path.Combine(solutionDir, entry.RelativePath);

            await _lockManager.WaitAsync(originalPath, ct).ConfigureAwait(false);
            try
            {
                if (entry.ExistedAtSnapshot)
                {
                    if (!_fileSystem.FileExists(bakPath))
                        throw new InvalidOperationException($"Backup missing for '{entry.RelativePath}', cannot rollback.");

                    await _fileSystem.CopyFileAsync(bakPath, originalPath, ct).ConfigureAwait(false);
                    if (_fileSystem.FileExists(bakPath))
                        _fileSystem.Delete(bakPath);
                }
                else
                {
                    if (_fileSystem.FileExists(originalPath))
                        _fileSystem.Delete(originalPath);

                    if (_fileSystem.FileExists(bakPath))
                        _fileSystem.Delete(bakPath);
                }
            }
            finally
            {
                _lockManager.Release(originalPath);
            }
        }

        private async Task CommitOneAsync(string bakPath, CancellationToken ct)
        {
            await _lockManager.WaitAsync(bakPath, ct).ConfigureAwait(false);
            try
            {
                if (_fileSystem.FileExists(bakPath))
                    _fileSystem.Delete(bakPath);
            }
            finally
            {
                _lockManager.Release(bakPath);
            }
        }

        private async Task<SnapshotManifestInfo> LoadManifestFromDiskAsync(string manifestPath, CancellationToken ct)
        {
            string json = await _fileSystem.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(false);
            return json.FromJson<SnapshotManifestInfo>() ?? throw new InvalidDataException("Manifest deserialized to null");
        }

        private async Task SaveManifestAsync(string manifestPath, SnapshotManifestInfo manifest, CancellationToken ct)
        {
            string json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
            byte[] data = Encoding.UTF8.GetBytes(json);
            string tmpPath = manifestPath + ".tmp";

            await _fileSystem.WriteAllBytesAsync(tmpPath, data, ct).ConfigureAwait(false);
            _fileSystem.ReplaceOrCreate(tmpPath, manifestPath);
        }

        private async Task SaveManifestIfDirtyAsync(CancellationToken ct)
        {
            if (!_manifestDirty || _manifest == null || _paths == null)
                return;
            await SaveManifestAsync(_paths.ManifestPath, _manifest, ct).ConfigureAwait(false);
            _manifestDirty = false;
        }

        private SnapshotManifestInfo CreateNewManifest()
        {
            return new SnapshotManifestInfo
            {
                SolutionRoot = _vsDependencies.GetSolutionDirectory(),
                CreatedAt = DateTime.UtcNow,
                Files = new List<SnapshotFileEntry>()
            };
        }

        private void ArchiveCorrupted(string snapshotDir)
        {
            if (!Directory.Exists(snapshotDir))
                return;

            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            string archiveDir = snapshotDir + "_corrupted_" + timestamp;
            Directory.Move(snapshotDir, archiveDir);
            InternalLogger.Info($"Archived corrupted snapshot to {archiveDir}");
        }

        private async Task<string> CleanupAndGetDeletionPathAsync(CancellationToken ct)
        {
            if (_manifest == null || _paths == null)
                return null;

            if (_manifest.Files.Count == 0)
            {
                string dirToDelete = _paths.SnapshotDir;

                _paths = null;
                _manifest = null;
                _manifestDirty = false;
                _pendingChanges.Clear();

                return dirToDelete;
            }
            else
            {
                await SaveManifestIfDirtyAsync(ct).ConfigureAwait(false);
                return null;
            }
        }

        private void EnsureDirectories(SnapshotPaths paths)
        {
            if (!Directory.Exists(paths.SnapshotDir))
                Directory.CreateDirectory(paths.SnapshotDir);
            if (!Directory.Exists(paths.FilesDirectory))
                Directory.CreateDirectory(paths.FilesDirectory);
            if (!Directory.Exists(paths.TmpDirectory))
                Directory.CreateDirectory(paths.TmpDirectory);
        }

        private bool IsValidRelativePath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return false;
            if (Path.IsPathRooted(relativePath)) return false;
            if (relativePath.Contains("..")) return false;
            if (relativePath.Contains(':')) return false;

            string solutionDir = _vsDependencies.GetSolutionDirectory();
            if (!_pathResolver.TryResolveFilePath(relativePath, solutionDir, out string absolutePath))
                return false;
            if (!_pathResolver.IsPathInsideDirectory(absolutePath, solutionDir))
                return false;

            return true;
        }

        public void Dispose()
        {
            _lock?.Dispose();
        }
    }
}
