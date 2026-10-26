using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Changes.Models;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Snapshot
{
    [TestFixture]
    public class ChangesServiceTests
    {
        private const string BackupPath = @"C:\snapshots\a.bak";
        private const string CurrentPath = @"C:\solution\src\Foo.cs";
        private const string RelativePath = @"src\Foo.cs";

        private readonly List<SnapshotFileChange> _changes = new List<SnapshotFileChange>();

        private Mock<ISnapshotManager> _snapshotMock;
        private Mock<IFileSystem> _fileSystemMock;
        private ChangesService _service;

        [SetUp]
        public void SetUp()
        {
            _changes.Clear();

            _snapshotMock = new Mock<ISnapshotManager>();
            _fileSystemMock = new Mock<IFileSystem>();

            _snapshotMock
                .Setup(s => s.GetChangedFilesWithStatusAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => (IReadOnlyList<SnapshotFileChange>)_changes.ToArray());

            _service = new ChangesService(_snapshotMock.Object, _fileSystemMock.Object);
        }

        // ── Empty change set ─────────────────────────────────────────────────

        [Test]
        public async Task GetChangesAsync_NoPendingChanges_ReturnsEmptySuccess()
        {
            ChangesResult result = await _service.GetChangesAsync(null, false, 50, 2000);

            Assert.That(result.Success, Is.True);
            Assert.That(result.TotalFiles, Is.EqualTo(0));
            Assert.That(result.TotalAdded, Is.EqualTo(0));
            Assert.That(result.TotalRemoved, Is.EqualTo(0));
            Assert.That(result.Files, Is.Empty);
            Assert.That(result.HasMoreResults, Is.False);
            Assert.That(result.Outcome, Is.EqualTo(ChangesOutcome.NoPendingChanges));
        }

        // ── Status mapping ───────────────────────────────────────────────────

        [Test]
        public async Task GetChangesAsync_ModifiedFile_ReturnsCountsWithoutHunks()
        {
            SetupModified(RelativePath, BackupPath, CurrentPath, "a\nb\nc\n", "a\nB\nc\n");

            ChangesResult result = await _service.GetChangesAsync(null, false, 50, 2000);

            Assert.That(result.Success, Is.True);
            Assert.That(result.TotalFiles, Is.EqualTo(1));
            Assert.That(result.TotalAdded, Is.EqualTo(1));
            Assert.That(result.TotalRemoved, Is.EqualTo(1));
            Assert.That(result.Files.Count, Is.EqualTo(1));
            Assert.That(result.Files[0].FilePath, Is.EqualTo(RelativePath));
            Assert.That(result.Files[0].Status, Is.EqualTo("modified"));
            Assert.That(result.Files[0].Added, Is.EqualTo(1));
            Assert.That(result.Files[0].Removed, Is.EqualTo(1));
            Assert.That(result.Files[0].Hunks, Is.Null);
            Assert.That(result.HasMoreResults, Is.False);
            Assert.That(result.Outcome, Is.EqualTo(ChangesOutcome.Success));
        }

        [Test]
        public async Task GetChangesAsync_IncludeDiff_ReturnsHunkText()
        {
            SetupModified(RelativePath, BackupPath, CurrentPath, "a\nb\nc\n", "a\nB\nc\n");

            ChangesResult result = await _service.GetChangesAsync(null, true, 50, 2000);

            Assert.That(result.Files[0].Hunks, Is.Not.Null);
            Assert.That(result.Files[0].Hunks.Count, Is.EqualTo(1));
            Assert.That(result.Files[0].Hunks[0], Is.EqualTo("@@ -1,3 +1,3 @@\n a\n-b\n+B\n c\n"));
        }

        [Test]
        public async Task GetChangesAsync_CreatedFile_DiffsFromEmptyWithoutReadingBackup()
        {
            _changes.Add(new SnapshotFileChange { RelativePath = RelativePath, Status = "created" });
            _snapshotMock.Setup(s => s.GetCurrentFilePath(RelativePath)).Returns(CurrentPath);
            SetupFile(CurrentPath, "one\ntwo\n");

            ChangesResult result = await _service.GetChangesAsync(null, true, 50, 2000);

            Assert.That(result.TotalAdded, Is.EqualTo(2));
            Assert.That(result.TotalRemoved, Is.EqualTo(0));
            Assert.That(result.Files[0].Status, Is.EqualTo("created"));
            Assert.That(result.Files[0].Hunks[0], Does.StartWith("@@ -0,0 +1,2 @@"));
            _snapshotMock.Verify(
                s => s.GetSnapshotFilePathAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task GetChangesAsync_DeletedFile_DiffsToEmpty()
        {
            _changes.Add(new SnapshotFileChange { RelativePath = RelativePath, Status = "deleted" });
            _snapshotMock
                .Setup(s => s.GetSnapshotFilePathAsync(RelativePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(BackupPath);
            SetupFile(BackupPath, "one\ntwo\n");

            ChangesResult result = await _service.GetChangesAsync(null, true, 50, 2000);

            Assert.That(result.TotalAdded, Is.EqualTo(0));
            Assert.That(result.TotalRemoved, Is.EqualTo(2));
            Assert.That(result.Files[0].Status, Is.EqualTo("deleted"));
            Assert.That(result.Files[0].Hunks[0], Does.StartWith("@@ -1,2 +0,0 @@"));
        }

        // ── Skipped files ────────────────────────────────────────────────────

        [Test]
        public async Task GetChangesAsync_MissingBackup_SkipsFileAndReportsUnavailable()
        {
            _changes.Add(new SnapshotFileChange { RelativePath = RelativePath, Status = "modified" });
            _snapshotMock
                .Setup(s => s.GetSnapshotFilePathAsync(RelativePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync((string)null);
            _snapshotMock.Setup(s => s.GetCurrentFilePath(RelativePath)).Returns(CurrentPath);
            SetupFile(CurrentPath, "a\n");

            ChangesResult result = await _service.GetChangesAsync(null, true, 50, 2000);

            Assert.That(result.Success, Is.True);
            Assert.That(result.TotalFiles, Is.EqualTo(0));
            Assert.That(result.Files, Is.Empty);
            Assert.That(result.HasMoreResults, Is.False);
            Assert.That(result.Outcome, Is.EqualTo(ChangesOutcome.SnapshotHistoryUnavailable));
        }

        [Test]
        public async Task GetChangesAsync_BackupReadFails_SkipsFile()
        {
            _changes.Add(new SnapshotFileChange { RelativePath = RelativePath, Status = "modified" });
            _snapshotMock
                .Setup(s => s.GetSnapshotFilePathAsync(RelativePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(BackupPath);
            _snapshotMock.Setup(s => s.GetCurrentFilePath(RelativePath)).Returns(CurrentPath);

            // The backup "exists" and has a size, but reading yields nothing: an I/O failure.
            _fileSystemMock.Setup(f => f.FileExists(BackupPath)).Returns(true);
            _fileSystemMock.Setup(f => f.GetFileInfo(BackupPath)).Returns((100L, DateTime.UtcNow));
            _fileSystemMock
                .Setup(f => f.ReadAllTextWithSharedReadAsync(BackupPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(string.Empty);

            SetupFile(CurrentPath, "a\n");

            ChangesResult result = await _service.GetChangesAsync(null, true, 50, 2000);

            Assert.That(result.Success, Is.True);
            Assert.That(result.TotalFiles, Is.EqualTo(0));
            Assert.That(result.Outcome, Is.EqualTo(ChangesOutcome.SnapshotHistoryUnavailable));
        }

        [Test]
        public async Task GetChangesAsync_OversizedFile_SkipsFile()
        {
            _changes.Add(new SnapshotFileChange { RelativePath = RelativePath, Status = "modified" });
            _snapshotMock
                .Setup(s => s.GetSnapshotFilePathAsync(RelativePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(BackupPath);
            _snapshotMock.Setup(s => s.GetCurrentFilePath(RelativePath)).Returns(CurrentPath);

            _fileSystemMock.Setup(f => f.FileExists(BackupPath)).Returns(true);
            _fileSystemMock.Setup(f => f.GetFileInfo(BackupPath)).Returns((ChangesService.MaxFileBytes + 1, DateTime.UtcNow));

            ChangesResult result = await _service.GetChangesAsync(null, false, 50, 2000);

            Assert.That(result.TotalFiles, Is.EqualTo(0));
            Assert.That(result.Files, Is.Empty);
            _fileSystemMock.Verify(
                f => f.ReadAllTextWithSharedReadAsync(BackupPath, It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task GetChangesAsync_NoLineDifferences_SkipsFile()
        {
            SetupModified(RelativePath, BackupPath, CurrentPath, "a\nb\n", "a\nb\n");

            ChangesResult result = await _service.GetChangesAsync(null, true, 50, 2000);

            Assert.That(result.Success, Is.True);
            Assert.That(result.TotalFiles, Is.EqualTo(0));
            Assert.That(result.Files, Is.Empty);
        }

        // ── Caps ─────────────────────────────────────────────────────────────

        [Test]
        public async Task GetChangesAsync_MaxFilesCapsResponseButNotTotals()
        {
            SetupModified(@"src\A.cs", @"C:\snapshots\a.bak", @"C:\solution\src\A.cs", "a\n", "b\n");
            SetupModified(@"src\B.cs", @"C:\snapshots\b.bak", @"C:\solution\src\B.cs", "a\n", "b\n");

            ChangesResult result = await _service.GetChangesAsync(null, false, 1, 2000);

            Assert.That(result.TotalFiles, Is.EqualTo(2));
            Assert.That(result.TotalAdded, Is.EqualTo(2));
            Assert.That(result.TotalRemoved, Is.EqualTo(2));
            Assert.That(result.Files.Count, Is.EqualTo(1));
            Assert.That(result.Files[0].FilePath, Is.EqualTo(@"src\A.cs"));
            Assert.That(result.HasMoreResults, Is.True);
        }

        [Test]
        public async Task GetChangesAsync_MaxLinesBudget_OmitsWholeFilesOnly()
        {
            SetupModified(@"src\A.cs", @"C:\snapshots\a.bak", @"C:\solution\src\A.cs", "a\nb\nc\n", "a\nB\nc\n");
            SetupModified(@"src\B.cs", @"C:\snapshots\b.bak", @"C:\solution\src\B.cs", "a\nb\nc\n", "a\nB\nc\n");

            // Each diff renders 5 lines (header + 4 hunk lines); only the first one fits.
            ChangesResult result = await _service.GetChangesAsync(null, true, 50, 6);

            Assert.That(result.TotalFiles, Is.EqualTo(2));
            Assert.That(result.Files.Count, Is.EqualTo(1));
            Assert.That(result.Files[0].Hunks, Is.Not.Null);
            Assert.That(result.Files[0].Hunks.Count, Is.EqualTo(1));
            Assert.That(result.HasMoreResults, Is.True);
        }

        [Test]
        public async Task GetChangesAsync_IncludeDiffFalse_IgnoresMaxLines()
        {
            SetupModified(@"src\A.cs", @"C:\snapshots\a.bak", @"C:\solution\src\A.cs", "a\nb\nc\n", "a\nB\nc\n");
            SetupModified(@"src\B.cs", @"C:\snapshots\b.bak", @"C:\solution\src\B.cs", "a\nb\nc\n", "a\nB\nc\n");

            ChangesResult result = await _service.GetChangesAsync(null, false, 50, 1);

            Assert.That(result.Files.Count, Is.EqualTo(2));
            Assert.That(result.HasMoreResults, Is.False);
        }

        // ── Path filter ──────────────────────────────────────────────────────

        [Test]
        public async Task GetChangesAsync_FilesFilter_MatchesSeparatorAndCaseInsensitive()
        {
            SetupModified(@"src\A.cs", @"C:\snapshots\a.bak", @"C:\solution\src\A.cs", "a\n", "b\n");
            SetupModified(@"src\B.cs", @"C:\snapshots\b.bak", @"C:\solution\src\B.cs", "a\n", "b\n");

            ChangesResult result = await _service.GetChangesAsync(new[] { "src/a.cs", "./SRC/A.CS" }, false, 50, 2000);

            Assert.That(result.TotalFiles, Is.EqualTo(1));
            Assert.That(result.Files.Count, Is.EqualTo(1));
            Assert.That(result.Files[0].FilePath, Is.EqualTo(@"src\A.cs"));
            Assert.That(result.HasMoreResults, Is.False);
            _snapshotMock.Verify(
                s => s.GetSnapshotFilePathAsync(@"src\B.cs", It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task GetChangesAsync_FilesFilterWithoutMatches_ReturnsEmptySuccess()
        {
            SetupModified(@"src\A.cs", @"C:\snapshots\a.bak", @"C:\solution\src\A.cs", "a\n", "b\n");

            ChangesResult result = await _service.GetChangesAsync(new[] { @"src\Missing.cs" }, true, 50, 2000);

            Assert.That(result.Success, Is.True);
            Assert.That(result.TotalFiles, Is.EqualTo(0));
            Assert.That(result.Files, Is.Empty);
            Assert.That(result.Outcome, Is.EqualTo(ChangesOutcome.NoPendingChanges));
        }

        [Test]
        public async Task GetChangesAsync_EmptyFilter_TreatsAllFilesAsSelected()
        {
            SetupModified(@"src\A.cs", @"C:\snapshots\a.bak", @"C:\solution\src\A.cs", "a\n", "b\n");

            ChangesResult result = await _service.GetChangesAsync(new string[0], false, 50, 2000);

            Assert.That(result.TotalFiles, Is.EqualTo(1));
        }

        // ── Ordering ─────────────────────────────────────────────────────────

        [Test]
        public async Task GetChangesAsync_OrdersFilesByPath()
        {
            SetupModified(@"src\B.cs", @"C:\snapshots\b.bak", @"C:\solution\src\B.cs", "a\n", "b\n");
            SetupModified(@"src\A.cs", @"C:\snapshots\a.bak", @"C:\solution\src\A.cs", "a\n", "b\n");

            ChangesResult result = await _service.GetChangesAsync(null, false, 50, 2000);

            Assert.That(result.Files.Count, Is.EqualTo(2));
            Assert.That(result.Files[0].FilePath, Is.EqualTo(@"src\A.cs"));
            Assert.That(result.Files[1].FilePath, Is.EqualTo(@"src\B.cs"));
        }

        // ── Read failures: one bad file must not fail the call ───────────────

        [Test]
        public async Task GetChangesAsync_MetadataReadThrows_SkipsOnlyThatFile()
        {
            SetupModified(@"src\A.cs", @"C:\snapshots\a.bak", @"C:\solution\src\A.cs", "a\n", "b\n");
            SetupModified(@"src\B.cs", @"C:\snapshots\b.bak", @"C:\solution\src\B.cs", "a\n", "b\n");

            // The ".bak" vanished between the existence check and the metadata read (snapshot commit
            // running concurrently), or the path is locked by another process.
            _fileSystemMock
                .Setup(f => f.GetFileInfo(@"C:\snapshots\b.bak"))
                .Throws(new System.IO.IOException("The process cannot access the file because it is being used by another process."));

            ChangesResult result = await _service.GetChangesAsync(null, false, 50, 2000);

            Assert.That(result.Success, Is.True);
            Assert.That(result.TotalFiles, Is.EqualTo(1));
            Assert.That(result.Files.Count, Is.EqualTo(1));
            Assert.That(result.Files[0].FilePath, Is.EqualTo(@"src\A.cs"));
            Assert.That(result.HasMoreResults, Is.False);
            Assert.That(result.Outcome, Is.EqualTo(ChangesOutcome.Success));
        }

        [Test]
        public async Task GetChangesAsync_UnauthorizedAccess_SkipsOnlyThatFile()
        {
            SetupModified(@"src\A.cs", @"C:\snapshots\a.bak", @"C:\solution\src\A.cs", "a\n", "b\n");
            SetupModified(@"src\B.cs", @"C:\snapshots\b.bak", @"C:\solution\src\B.cs", "a\n", "b\n");

            _fileSystemMock
                .Setup(f => f.GetFileInfo(@"C:\solution\src\B.cs"))
                .Throws(new UnauthorizedAccessException("Access to the path is denied."));

            ChangesResult result = await _service.GetChangesAsync(null, true, 50, 2000);

            Assert.That(result.Success, Is.True);
            Assert.That(result.TotalFiles, Is.EqualTo(1));
            Assert.That(result.Files[0].FilePath, Is.EqualTo(@"src\A.cs"));
            Assert.That(result.HasMoreResults, Is.False);
        }

        [Test]
        public async Task GetChangesAsync_EveryFileFails_ReportsUnavailableWithoutFailing()
        {
            SetupModified(RelativePath, BackupPath, CurrentPath, "a\n", "b\n");

            _fileSystemMock
                .Setup(f => f.GetFileInfo(BackupPath))
                .Throws(new UnauthorizedAccessException("Access to the path is denied."));

            ChangesResult result = await _service.GetChangesAsync(null, true, 50, 2000);

            Assert.That(result.Success, Is.True);
            Assert.That(result.TotalFiles, Is.EqualTo(0));
            Assert.That(result.Files, Is.Empty);
            Assert.That(result.HasMoreResults, Is.False);
            Assert.That(result.Outcome, Is.EqualTo(ChangesOutcome.SnapshotHistoryUnavailable));
        }

        [Test]
        public void GetChangesAsync_CancellationDuringFileRead_IsNotSwallowed()
        {
            SetupModified(RelativePath, BackupPath, CurrentPath, "a\n", "b\n");

            _fileSystemMock
                .Setup(f => f.ReadAllTextWithSharedReadAsync(BackupPath, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());

            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await _service.GetChangesAsync(null, true, 50, 2000));
        }

        [Test]
        public void GetChangesAsync_OutOfMemoryDuringFileRead_IsNotSwallowedAsSkip()
        {
            SetupModified(RelativePath, BackupPath, CurrentPath, "a\n", "b\n");

            _fileSystemMock
                .Setup(f => f.ReadAllTextWithSharedReadAsync(BackupPath, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OutOfMemoryException());

            // A process-level failure must not be downgraded to "this file was skipped".
            Assert.ThrowsAsync<OutOfMemoryException>(
                async () => await _service.GetChangesAsync(null, true, 50, 2000));
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void SetupModified(string relativePath, string backupPath, string currentPath, string before, string after)
        {
            _changes.Add(new SnapshotFileChange { RelativePath = relativePath, Status = "modified" });
            _snapshotMock
                .Setup(s => s.GetSnapshotFilePathAsync(relativePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(backupPath);
            _snapshotMock.Setup(s => s.GetCurrentFilePath(relativePath)).Returns(currentPath);

            SetupFile(backupPath, before);
            SetupFile(currentPath, after);
        }

        private void SetupFile(string path, string content)
        {
            _fileSystemMock.Setup(f => f.FileExists(path)).Returns(true);
            _fileSystemMock.Setup(f => f.GetFileInfo(path)).Returns(((long)content.Length, DateTime.UtcNow));
            _fileSystemMock
                .Setup(f => f.ReadAllTextWithSharedReadAsync(path, It.IsAny<CancellationToken>()))
                .ReturnsAsync(content);
        }
    }
}
