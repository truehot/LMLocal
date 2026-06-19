using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot.Infrastructure;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Snapshot
{
    [TestFixture]
    public class SnapshotManagerTests
    {
        private Mock<IVsDependencies> _vsDependenciesMock;
        private Mock<IPathResolver> _pathResolverMock;
        private Mock<IFileSystem> _fileSystemMock;
        private Mock<ISnapshotPathsFactory> _pathsFactoryMock;
        private Mock<IFileLockManager> _lockManagerMock;

        private const string SolutionDir = @"C:\Solution";
        private SnapshotPaths _testPaths;

        [SetUp]
        public void SetUp()
        {
            _vsDependenciesMock = new Mock<IVsDependencies>();
            _pathResolverMock = new Mock<IPathResolver>();
            _fileSystemMock = new Mock<IFileSystem>();
            _pathsFactoryMock = new Mock<ISnapshotPathsFactory>();
            _lockManagerMock = new Mock<IFileLockManager>();

            _vsDependenciesMock.Setup(v => v.IsSolutionOpen).Returns(true);
            _vsDependenciesMock.Setup(v => v.GetSolutionDirectory()).Returns(SolutionDir);

            _testPaths = new SnapshotPaths(
                snapshotDir: @"C:\Snapshots\hash",
                filesDirectory: @"C:\Snapshots\hash\files",
                tmpDirectory: @"C:\Snapshots\hash\tmp",
                manifestPath: @"C:\Snapshots\hash\manifest.json");

            _pathsFactoryMock.Setup(f => f.Create()).Returns(_testPaths);

            _fileSystemMock.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(false);
            _fileSystemMock.Setup(fs => fs.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("{}");
        }

        #region Constructor Tests

        [Test]
        public void Constructor_ThrowsArgumentNullException_WhenVsDependenciesIsNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new SnapshotManager(null, _pathResolverMock.Object, _fileSystemMock.Object,
                    _pathsFactoryMock.Object, _lockManagerMock.Object));
        }

        [Test]
        public void Constructor_ThrowsArgumentNullException_WhenPathResolverIsNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new SnapshotManager(_vsDependenciesMock.Object, null, _fileSystemMock.Object,
                    _pathsFactoryMock.Object, _lockManagerMock.Object));
        }

        [Test]
        public void Constructor_ThrowsArgumentNullException_WhenFileSystemIsNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new SnapshotManager(_vsDependenciesMock.Object, _pathResolverMock.Object, null,
                    _pathsFactoryMock.Object, _lockManagerMock.Object));
        }

        [Test]
        public void Constructor_ThrowsArgumentNullException_WhenPathsFactoryIsNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new SnapshotManager(_vsDependenciesMock.Object, _pathResolverMock.Object, _fileSystemMock.Object,
                    null, _lockManagerMock.Object));
        }

        [Test]
        public void Constructor_ThrowsArgumentNullException_WhenLockManagerIsNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new SnapshotManager(_vsDependenciesMock.Object, _pathResolverMock.Object, _fileSystemMock.Object,
                    _pathsFactoryMock.Object, null));
        }

        [Test]
        public void Constructor_DoesNotThrow_WhenAllDependenciesProvided()
        {
            Assert.DoesNotThrow(() => CreateManager());
        }

        #endregion

        #region LoadSnapshotAsync Tests

        [Test]
        public async Task LoadSnapshotAsync_WhenManifestAlreadyLoaded_DoesNothing()
        {
            var manager = CreateManager();
            await manager.LoadSnapshotAsync(CancellationToken.None);
            // First call loads successfully
            Assert.DoesNotThrowAsync(async () => await manager.LoadSnapshotAsync(CancellationToken.None));
            // Create should only be called once
            _pathsFactoryMock.Verify(f => f.Create(), Times.Once);
        }

        [Test]
        public async Task LoadSnapshotAsync_WhenSolutionNotOpen_ReturnsEarly()
        {
            _vsDependenciesMock.Setup(v => v.IsSolutionOpen).Returns(false);

            var manager = CreateManager();
            await manager.LoadSnapshotAsync(CancellationToken.None);

            _pathsFactoryMock.Verify(f => f.Create(), Times.Never);
        }

        [Test]
        public async Task LoadSnapshotAsync_WhenPathsFactoryReturnsNull_ReturnsEarly()
        {
            _pathsFactoryMock.Setup(f => f.Create()).Returns((SnapshotPaths)null);

            var manager = CreateManager();
            await manager.LoadSnapshotAsync(CancellationToken.None);
        }

        [Test]
        public async Task LoadSnapshotAsync_WhenNoManifestOnDisk_CreatesNewManifest()
        {
            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(false);

            var manager = CreateManager();
            await manager.LoadSnapshotAsync(CancellationToken.None);

            _fileSystemMock.Verify(fs => fs.WriteAllBytesAsync(_testPaths.ManifestPath + ".tmp",
                It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Once);
            _fileSystemMock.Verify(fs => fs.ReplaceOrCreate(_testPaths.ManifestPath + ".tmp",
                _testPaths.ManifestPath), Times.Once);
        }

        [Test]
        public async Task LoadSnapshotAsync_WhenManifestExists_LoadsFromDisk()
        {
            var manifestJson = @"{""solutionRoot"":""C:\\Solution"",""createdAt"":""2024-01-01T00:00:00Z"",""files"":[]}";
            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.ReadAllTextAsync(_testPaths.ManifestPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(manifestJson);

            var manager = CreateManager();
            await manager.LoadSnapshotAsync(CancellationToken.None);

            _fileSystemMock.Verify(fs => fs.WriteAllBytesAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task LoadSnapshotAsync_WhenManifestHasCorruptedJson_ArchivesAndCreatesNew()
        {
            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.ReadAllTextAsync(_testPaths.ManifestPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync("invalid json");

            _pathsFactoryMock.SetupSequence(f => f.Create())
                .Returns(_testPaths)
                .Returns(_testPaths);

            var manager = CreateManager();
            await manager.LoadSnapshotAsync(CancellationToken.None);

            // Archive should have been called - the directory was moved
            // Then new manifest written
            _fileSystemMock.Verify(fs => fs.WriteAllBytesAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Test]
        public async Task LoadSnapshotAsync_PopulatesPendingChanges_FromExistingEntries()
        {
            var manifestJson = @"{
                ""solutionRoot"":""C:\\Solution"",
                ""createdAt"":""2024-01-01T00:00:00Z"",
                ""files"":[
                    {""relativePath"":""file1.txt"",""backupId"":""aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"",""existedAtSnapshot"":true,""fileSize"":100,""lastWriteTimeUtc"":""2024-01-01T00:00:00Z""},
                    {""relativePath"":""file2.txt"",""backupId"":""bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"",""existedAtSnapshot"":false,""fileSize"":0,""lastWriteTimeUtc"":""0001-01-01T00:00:00Z""}
                ]
            }";

            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.ReadAllTextAsync(_testPaths.ManifestPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(manifestJson);

            // file1.txt exists (modified), file2.txt doesn't exist originally (created)
            _fileSystemMock.Setup(fs => fs.FileExists(Path.Combine(SolutionDir, "file1.txt"))).Returns(true);
            _fileSystemMock.Setup(fs => fs.FileExists(Path.Combine(SolutionDir, "file2.txt"))).Returns(false);

            var manager = CreateManager();
            var changedFiles = await manager.GetChangedFilesWithStatusAsync(CancellationToken.None);

            Assert.That(changedFiles, Is.Empty);

            await manager.LoadSnapshotAsync(CancellationToken.None);
            changedFiles = await manager.GetChangedFilesWithStatusAsync(CancellationToken.None);

            Assert.That(changedFiles.Count, Is.EqualTo(2));

            var file1 = changedFiles.First(cf => cf.RelativePath == "file1.txt");
            Assert.That(file1.Status, Is.EqualTo("modified"));

            var file2 = changedFiles.First(cf => cf.RelativePath == "file2.txt");
            Assert.That(file2.Status, Is.EqualTo("created"));
        }

        [Test]
        public async Task LoadSnapshotAsync_FiresSnapshotChangedEvent_WhenManifestLoaded()
        {
            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(false);

            var manager = CreateManager();
            IReadOnlyList<string> notifiedFiles = null;
            manager.SnapshotChanged += files => notifiedFiles = files;

            await manager.LoadSnapshotAsync(CancellationToken.None);

            Assert.That(notifiedFiles, Is.Not.Null);
        }

        #endregion

        #region ResetAsync Tests

        [Test]
        public async Task ResetAsync_ClearsStateAndFiresEvent()
        {
            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(false);

            var manager = CreateManager();
            await manager.LoadSnapshotAsync(CancellationToken.None);

            IReadOnlyList<string> notifiedFiles = null;
            manager.SnapshotChanged += files => notifiedFiles = files;

            await manager.ResetAsync(CancellationToken.None);

            var changedFiles = await manager.GetChangedFilesWithStatusAsync(CancellationToken.None);
            Assert.That(changedFiles, Is.Empty);

            var snapshotFiles = await manager.GetChangedFilesAsync(CancellationToken.None);
            Assert.That(snapshotFiles, Is.Empty);

            Assert.That(notifiedFiles, Is.Not.Null);
            Assert.That(notifiedFiles.Count, Is.EqualTo(0));
        }

        [Test]
        public void ResetAsync_WhenNotLoaded_DoesNotThrow()
        {
            var manager = CreateManager();
            Assert.DoesNotThrowAsync(async () => await manager.ResetAsync(CancellationToken.None));
        }

        #endregion

        #region GetChangedFilesAsync Tests

        [Test]
        public async Task GetChangedFilesAsync_ReturnsEmpty_WhenNotLoaded()
        {
            var manager = CreateManager();
            var files = await manager.GetChangedFilesAsync(CancellationToken.None);
            Assert.That(files, Is.Empty);
        }

        [Test]
        public async Task GetChangedFilesAsync_ReturnsFilePaths_AfterLoading()
        {
            var manifestJson = @"{
                ""solutionRoot"":""C:\\Solution"",
                ""createdAt"":""2024-01-01T00:00:00Z"",
                ""files"":[
                    {""relativePath"":""test.txt"",""backupId"":""aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"",""existedAtSnapshot"":true,""fileSize"":100,""lastWriteTimeUtc"":""2024-01-01T00:00:00Z""}
                ]
            }";

            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.ReadAllTextAsync(_testPaths.ManifestPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(manifestJson);

            var manager = CreateManager();
            await manager.LoadSnapshotAsync(CancellationToken.None);

            var files = await manager.GetChangedFilesAsync(CancellationToken.None);
            Assert.That(files, Has.Member("test.txt"));
        }

        #endregion

        #region GetChangedFilesWithStatusAsync Tests

        [Test]
        public async Task GetChangedFilesWithStatusAsync_ReturnsEmpty_WhenNotLoaded()
        {
            var manager = CreateManager();
            var files = await manager.GetChangedFilesWithStatusAsync(CancellationToken.None);
            Assert.That(files, Is.Empty);
        }

        #endregion

        #region GetSnapshotFilePathAsync Tests

        [Test]
        public async Task GetSnapshotFilePathAsync_ReturnsNull_WhenRelativePathIsNull()
        {
            var manager = CreateManager();
            var result = await manager.GetSnapshotFilePathAsync(null, CancellationToken.None);
            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task GetSnapshotFilePathAsync_ReturnsNull_WhenRelativePathIsEmpty()
        {
            var manager = CreateManager();
            var result = await manager.GetSnapshotFilePathAsync(string.Empty, CancellationToken.None);
            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task GetSnapshotFilePathAsync_ReturnsNull_WhenPathsNotLoaded()
        {
            var manager = CreateManager();
            var result = await manager.GetSnapshotFilePathAsync("test.txt", CancellationToken.None);
            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task GetSnapshotFilePathAsync_ReturnsNull_WhenEntryNotFound()
        {
            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(false);
            SetupValidRelativePath("test.txt");

            var manager = CreateManager();
            await manager.LoadSnapshotAsync(CancellationToken.None);

            var result = await manager.GetSnapshotFilePathAsync("test.txt", CancellationToken.None);
            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task GetSnapshotFilePathAsync_ReturnsBackupPath_WhenEntryExistsAndBackupFileExists()
        {
            var backupId = Guid.NewGuid();
            var manifestJson = CreateManifestJsonWithFile(backupId, "test.txt", existedAtSnapshot: true);

            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.ReadAllTextAsync(_testPaths.ManifestPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(manifestJson);
            SetupValidRelativePath("test.txt");

            var expectedBakPath = _testPaths.GetBackupPath(backupId);
            _fileSystemMock.Setup(fs => fs.FileExists(expectedBakPath)).Returns(true);

            var manager = CreateManager();
            await manager.LoadSnapshotAsync(CancellationToken.None);

            var result = await manager.GetSnapshotFilePathAsync("test.txt", CancellationToken.None);
            Assert.That(result, Is.EqualTo(expectedBakPath));
        }

        [Test]
        public async Task GetSnapshotFilePathAsync_ReturnsNull_WhenBackupFileMissing()
        {
            var backupId = Guid.NewGuid();
            var manifestJson = CreateManifestJsonWithFile(backupId, "test.txt", existedAtSnapshot: true);

            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.ReadAllTextAsync(_testPaths.ManifestPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(manifestJson);
            SetupValidRelativePath("test.txt");

            var expectedBakPath = _testPaths.GetBackupPath(backupId);
            _fileSystemMock.Setup(fs => fs.FileExists(expectedBakPath)).Returns(false);

            var manager = CreateManager();
            await manager.LoadSnapshotAsync(CancellationToken.None);

            var result = await manager.GetSnapshotFilePathAsync("test.txt", CancellationToken.None);
            Assert.That(result, Is.Null);
        }

        #endregion

        #region GetCurrentFilePath Tests

        [Test]
        public void GetCurrentFilePath_ReturnsNull_WhenRelativePathIsEmpty()
        {
            var manager = CreateManager();
            Assert.That(manager.GetCurrentFilePath(string.Empty), Is.Null);
        }

        [Test]
        public void GetCurrentFilePath_ReturnsNull_WhenRelativePathIsNull()
        {
            var manager = CreateManager();
            Assert.That(manager.GetCurrentFilePath(null), Is.Null);
        }

        [Test]
        public void GetCurrentFilePath_ReturnsNull_WhenSolutionNotOpen()
        {
            _vsDependenciesMock.Setup(v => v.IsSolutionOpen).Returns(false);
            var manager = CreateManager();
            Assert.That(manager.GetCurrentFilePath("test.txt"), Is.Null);
        }

        [Test]
        public void GetCurrentFilePath_ReturnsNull_WhenPathCannotBeResolved()
        {
            var resolved = "resolved";
            _pathResolverMock.Setup(r => r.TryResolveFilePath("test.txt", SolutionDir, out resolved))
                .Returns(false);

            var manager = CreateManager();
            Assert.That(manager.GetCurrentFilePath("test.txt"), Is.Null);
        }

        [Test]
        public void GetCurrentFilePath_ReturnsNull_WhenPathOutsideSolution()
        {
            var resolved = Path.Combine(SolutionDir, "test.txt");
            _pathResolverMock.Setup(r => r.TryResolveFilePath("test.txt", SolutionDir, out resolved))
                .Returns(true);
            _pathResolverMock.Setup(r => r.IsPathInsideDirectory(resolved, SolutionDir))
                .Returns(false);

            var manager = CreateManager();
            Assert.That(manager.GetCurrentFilePath("test.txt"), Is.Null);
        }

        [Test]
        public void GetCurrentFilePath_ReturnsNull_WhenFileDoesNotExist()
        {
            var resolved = Path.Combine(SolutionDir, "test.txt");
            _pathResolverMock.Setup(r => r.TryResolveFilePath("test.txt", SolutionDir, out resolved))
                .Returns(true);
            _pathResolverMock.Setup(r => r.IsPathInsideDirectory(resolved, SolutionDir))
                .Returns(true);
            _fileSystemMock.Setup(fs => fs.FileExists(resolved)).Returns(false);

            var manager = CreateManager();
            Assert.That(manager.GetCurrentFilePath("test.txt"), Is.Null);
        }

        [Test]
        public void GetCurrentFilePath_ReturnsAbsolutePath_WhenFileExists()
        {
            var expectedAbsPath = Path.Combine(SolutionDir, "test.txt");
            _pathResolverMock.Setup(r => r.TryResolveFilePath("test.txt", SolutionDir, out expectedAbsPath))
                .Returns(true);
            _pathResolverMock.Setup(r => r.IsPathInsideDirectory(expectedAbsPath, SolutionDir))
                .Returns(true);
            _fileSystemMock.Setup(fs => fs.FileExists(expectedAbsPath)).Returns(true);

            var manager = CreateManager();
            var result = manager.GetCurrentFilePath("test.txt");
            Assert.That(result, Is.EqualTo(expectedAbsPath));
        }

        #endregion

        #region GetTmpDirectoryPath Tests

        [Test]
        public void GetTmpDirectoryPath_ReturnsNull_WhenNotLoaded()
        {
            var manager = CreateManager();
            Assert.That(manager.GetTmpDirectoryPath(), Is.Null);
        }

        [Test]
        public async Task GetTmpDirectoryPath_ReturnsTmpPath_WhenLoaded()
        {
            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(false);

            var manager = CreateManager();
            await manager.LoadSnapshotAsync(CancellationToken.None);

            Assert.That(manager.GetTmpDirectoryPath(), Is.EqualTo(_testPaths.TmpDirectory));
        }

        #endregion

        #region SnapshotFileAsync Tests

        [Test]
        public void SnapshotFileAsync_Throws_WhenNotInBatchMode()
        {
            var manager = CreateManager();

            Assert.That(async () => await manager.SnapshotFileAsync("C:\\test.txt", SnapshotChangeStatus.BeforeModify, CancellationToken.None),
                Throws.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public async Task SnapshotFileAsync_DoesNotLoadManifest_IfAlreadyLoaded()
        {
            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(false);

            var manager = CreateManager();
            await manager.LoadSnapshotAsync(CancellationToken.None);
            await manager.BeginBatchAsync(CancellationToken.None);

            var relPath = "test.txt";
            var absPath = Path.Combine(SolutionDir, relPath);
            var resolved = absPath;

            _pathResolverMock.Setup(r => r.IsPathInsideDirectory(absPath, SolutionDir)).Returns(true);
            _pathResolverMock.Setup(r => r.TryGetRelativePath(absPath, SolutionDir, out relPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.FileExists(absPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.GetFileInfo(absPath)).Returns((100L, DateTime.UtcNow));

            await manager.SnapshotFileAsync(absPath, SnapshotChangeStatus.BeforeModify, CancellationToken.None);

            // Verify we didn't try to read the manifest again
            _fileSystemMock.Verify(fs => fs.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task SnapshotFileAsync_LoadsManifest_IfNotLoaded()
        {
            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(false);

            var manager = CreateManager();
            await manager.BeginBatchAsync(CancellationToken.None);

            var relPath = "test.txt";
            var absPath = Path.Combine(SolutionDir, relPath);

            _pathResolverMock.Setup(r => r.IsPathInsideDirectory(absPath, SolutionDir)).Returns(true);
            _pathResolverMock.Setup(r => r.TryGetRelativePath(absPath, SolutionDir, out relPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.FileExists(absPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.GetFileInfo(absPath)).Returns((100L, DateTime.UtcNow));

            await manager.SnapshotFileAsync(absPath, SnapshotChangeStatus.BeforeModify, CancellationToken.None);

            // Manifest should have been written
            _fileSystemMock.Verify(fs => fs.WriteAllBytesAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }
        public void SnapshotFileAsync_Throws_WhenFileOutsideSolution()
        {
            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(false);

            var manager = CreateManager();
            manager.BeginBatchAsync(CancellationToken.None).GetAwaiter().GetResult();

            _pathResolverMock.Setup(r => r.IsPathInsideDirectory(@"C:\Outside\test.txt", SolutionDir)).Returns(false);

            Assert.That(async () => await manager.SnapshotFileAsync(@"C:\Outside\test.txt", SnapshotChangeStatus.BeforeModify, CancellationToken.None),
                Throws.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public async Task SnapshotFileAsync_WithBeforeCreate_SetsStatusToCreated()
        {
            var manager = await SetupManagerWithManifestLoaded();

            var relPath = "newfile.txt";
            var absPath = Path.Combine(SolutionDir, relPath);

            _pathResolverMock.Setup(r => r.IsPathInsideDirectory(absPath, SolutionDir)).Returns(true);
            _pathResolverMock.Setup(r => r.TryGetRelativePath(absPath, SolutionDir, out relPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.FileExists(absPath)).Returns(false);

            await manager.SnapshotFileAsync(absPath, SnapshotChangeStatus.BeforeCreate, CancellationToken.None);

            var changes = await manager.GetChangedFilesWithStatusAsync(CancellationToken.None);
            var change = changes.FirstOrDefault(c => c.RelativePath == "newfile.txt");
            Assert.That(change, Is.Not.Null);
            Assert.That(change.Status, Is.EqualTo("created"));
        }

        [Test]
        public async Task SnapshotFileAsync_WithBeforeDelete_SetsStatusToDeleted()
        {
            var manager = await SetupManagerWithManifestLoaded();

            var relPath = "deletefile.txt";
            var absPath = Path.Combine(SolutionDir, relPath);

            _pathResolverMock.Setup(r => r.IsPathInsideDirectory(absPath, SolutionDir)).Returns(true);
            _pathResolverMock.Setup(r => r.TryGetRelativePath(absPath, SolutionDir, out relPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.FileExists(absPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.GetFileInfo(absPath)).Returns((100L, DateTime.UtcNow));

            await manager.SnapshotFileAsync(absPath, SnapshotChangeStatus.BeforeDelete, CancellationToken.None);

            var changes = await manager.GetChangedFilesWithStatusAsync(CancellationToken.None);
            var change = changes.FirstOrDefault(c => c.RelativePath == "deletefile.txt");
            Assert.That(change, Is.Not.Null);
            Assert.That(change.Status, Is.EqualTo("deleted"));
        }

        [Test]
        public async Task SnapshotFileAsync_WithBeforeModify_SetsStatusToModified()
        {
            var manager = await SetupManagerWithManifestLoaded();

            var relPath = "modifyfile.txt";
            var absPath = Path.Combine(SolutionDir, relPath);

            _pathResolverMock.Setup(r => r.IsPathInsideDirectory(absPath, SolutionDir)).Returns(true);
            _pathResolverMock.Setup(r => r.TryGetRelativePath(absPath, SolutionDir, out relPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.FileExists(absPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.GetFileInfo(absPath)).Returns((100L, DateTime.UtcNow));

            await manager.SnapshotFileAsync(absPath, SnapshotChangeStatus.BeforeModify, CancellationToken.None);

            var changes = await manager.GetChangedFilesWithStatusAsync(CancellationToken.None);
            var change = changes.FirstOrDefault(c => c.RelativePath == "modifyfile.txt");
            Assert.That(change, Is.Not.Null);
            Assert.That(change.Status, Is.EqualTo("modified"));
        }

        [Test]
        public async Task SnapshotFileAsync_CopiesBackup_ForExistingFile()
        {
            var manager = await SetupManagerWithManifestLoaded();

            var relPath = "backupfile.txt";
            var absPath = Path.Combine(SolutionDir, relPath);

            _pathResolverMock.Setup(r => r.IsPathInsideDirectory(absPath, SolutionDir)).Returns(true);
            _pathResolverMock.Setup(r => r.TryGetRelativePath(absPath, SolutionDir, out relPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.FileExists(absPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.GetFileInfo(absPath)).Returns((100L, DateTime.UtcNow));

            await manager.SnapshotFileAsync(absPath, SnapshotChangeStatus.BeforeModify, CancellationToken.None);

            _fileSystemMock.Verify(fs => fs.CopyFileAsync(absPath,
                It.Is<string>(s => s.StartsWith(_testPaths.FilesDirectory)), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task SnapshotFileAsync_DoesNotCopyBackup_ForNewFile()
        {
            var manager = await SetupManagerWithManifestLoaded();

            var relPath = "newfile.txt";
            var absPath = Path.Combine(SolutionDir, relPath);

            _pathResolverMock.Setup(r => r.IsPathInsideDirectory(absPath, SolutionDir)).Returns(true);
            _pathResolverMock.Setup(r => r.TryGetRelativePath(absPath, SolutionDir, out relPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.FileExists(absPath)).Returns(false);

            await manager.SnapshotFileAsync(absPath, SnapshotChangeStatus.BeforeCreate, CancellationToken.None);

            _fileSystemMock.Verify(fs => fs.CopyFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        #endregion

        #region BeginBatchAsync / EndBatchAsync / CancelBatchAsync Tests

        [Test]
        public async Task BeginBatchAsync_SetsBatchingMode()
        {
            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(false);

            var manager = CreateManager();
            await manager.BeginBatchAsync(CancellationToken.None);

            // Should not throw when SnapshotFileAsync is called (batch is active)
            var relPath = "batchfile.txt";
            var absPath = Path.Combine(SolutionDir, relPath);

            _pathResolverMock.Setup(r => r.IsPathInsideDirectory(absPath, SolutionDir)).Returns(true);
            _pathResolverMock.Setup(r => r.TryGetRelativePath(absPath, SolutionDir, out relPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.FileExists(absPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.GetFileInfo(absPath)).Returns((100L, DateTime.UtcNow));

            Assert.DoesNotThrowAsync(async () =>
                await manager.SnapshotFileAsync(absPath, SnapshotChangeStatus.BeforeModify, CancellationToken.None));
        }

        [Test]
        public async Task EndBatchAsync_RemovesUnchangedFiles_FileCreatedThenDeleted()
        {
            var backupId = Guid.NewGuid();
            var manifestJson = CreateManifestJsonWithFile(backupId, "temp.txt", existedAtSnapshot: false);

            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.ReadAllTextAsync(_testPaths.ManifestPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(manifestJson);
            SetupValidRelativePath("temp.txt");

            var manager = CreateManager();
            await manager.LoadSnapshotAsync(CancellationToken.None);

            // Simulate: file was created in batch but then doesn't exist anymore
            await manager.BeginBatchAsync(CancellationToken.None);
            var absPath = Path.Combine(SolutionDir, "temp.txt");
            _fileSystemMock.Setup(fs => fs.FileExists(absPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.GetFileInfo(absPath)).Returns((100L, DateTime.UtcNow));

            var relPath = "temp.txt";
            _pathResolverMock.Setup(r => r.IsPathInsideDirectory(absPath, SolutionDir)).Returns(true);
            _pathResolverMock.Setup(r => r.TryGetRelativePath(absPath, SolutionDir, out relPath)).Returns(true);

            await manager.SnapshotFileAsync(absPath, SnapshotChangeStatus.BeforeCreate, CancellationToken.None);

            var bakPath = _testPaths.GetBackupPath(backupId);
            _fileSystemMock.Setup(fs => fs.FileExists(bakPath)).Returns(false);
            // Simulate the file was deleted after being created in batch
            _fileSystemMock.Setup(fs => fs.FileExists(absPath)).Returns(false);
            await manager.EndBatchAsync(CancellationToken.None);

            var files = await manager.GetChangedFilesAsync(CancellationToken.None);
            Assert.That(files, Does.Not.Contain("temp.txt"));
        }

        [Test]
        public async Task EndBatchAsync_RemovesUnchangedFiles_WhenFileRevertedToOriginal()
        {
            var backupId = Guid.NewGuid();
            var manifestJson = CreateManifestJsonWithFile(backupId, "revert.txt", existedAtSnapshot: true);

            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.ReadAllTextAsync(_testPaths.ManifestPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(manifestJson);
            SetupValidRelativePath("revert.txt");

            var bakPath = _testPaths.GetBackupPath(backupId);
            var absPath = Path.Combine(SolutionDir, "revert.txt");

            // The file exists and has the same size and time as backup (unchanged)
            _fileSystemMock.Setup(fs => fs.FileExists(absPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.FileExists(bakPath)).Returns(true);
            var sameTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            _fileSystemMock.Setup(fs => fs.GetFileInfo(absPath)).Returns((100L, sameTime));
            _fileSystemMock.Setup(fs => fs.GetFileInfo(bakPath)).Returns((100L, sameTime));

            var manager = CreateManager();
            await manager.LoadSnapshotAsync(CancellationToken.None);

            await manager.BeginBatchAsync(CancellationToken.None);
            var relPath = "revert.txt";
            _pathResolverMock.Setup(r => r.IsPathInsideDirectory(absPath, SolutionDir)).Returns(true);
            _pathResolverMock.Setup(r => r.TryGetRelativePath(absPath, SolutionDir, out relPath)).Returns(true);

            await manager.SnapshotFileAsync(absPath, SnapshotChangeStatus.BeforeModify, CancellationToken.None);
            await manager.EndBatchAsync(CancellationToken.None);

            var files = await manager.GetChangedFilesAsync(CancellationToken.None);
            Assert.That(files, Does.Not.Contain("revert.txt"));
        }

        [Test]
        public async Task EndBatchAsync_SavesManifestAndFiresEvent()
        {
            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(false);

            var manager = CreateManager();
            await manager.LoadSnapshotAsync(CancellationToken.None);

            IReadOnlyList<string> notifiedFiles = null;
            manager.SnapshotChanged += files => notifiedFiles = files;

            await manager.BeginBatchAsync(CancellationToken.None);
            await manager.EndBatchAsync(CancellationToken.None);

            Assert.That(notifiedFiles, Is.Not.Null);
        }

        [Test]
        public async Task CancelBatchAsync_ClearsBatchState()
        {
            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(false);

            var manager = CreateManager();
            await manager.LoadSnapshotAsync(CancellationToken.None);
            await manager.BeginBatchAsync(CancellationToken.None);
            await manager.CancelBatchAsync(CancellationToken.None);

            // After cancel, SnapshotFileAsync should throw because batch not active
            Assert.That(async () =>
                await manager.SnapshotFileAsync(@"C:\test.txt", SnapshotChangeStatus.BeforeModify, CancellationToken.None),
                Throws.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void CancelBatchAsync_WhenNotBatching_DoesNothing()
        {
            var manager = CreateManager();
            Assert.DoesNotThrowAsync(async () => await manager.CancelBatchAsync(CancellationToken.None));
        }

        #endregion

        #region RollbackAllAsync Tests

        [Test]
        public void RollbackAllAsync_Throws_WhenBatchActive()
        {
            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(false);

            var manager = CreateManager();
            manager.LoadSnapshotAsync(CancellationToken.None).GetAwaiter().GetResult();
            manager.BeginBatchAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(async () => await manager.RollbackAllAsync(CancellationToken.None),
                Throws.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void RollbackAllAsync_DoesNothing_WhenNoManifest()
        {
            var manager = CreateManager();
            Assert.DoesNotThrowAsync(async () => await manager.RollbackAllAsync(CancellationToken.None));
        }

        [Test]
        public void RollbackAllAsync_DoesNothing_WhenNoFiles()
        {
            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(false);

            var manager = CreateManager();
            manager.LoadSnapshotAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.DoesNotThrowAsync(async () => await manager.RollbackAllAsync(CancellationToken.None));
        }

        [Test]
        public async Task RollbackAllAsync_RestoresExistedFilesAndDeletesCreatedFiles()
        {
            var backupIdExisting = Guid.NewGuid();
            var backupIdNew = Guid.NewGuid();
            var manifestJson = $@"{{
                ""solutionRoot"":""C:\\Solution"",
                ""createdAt"":""2024-01-01T00:00:00Z"",
                ""files"":[
                    {{""relativePath"":""existing.txt"",""backupId"":""{backupIdExisting:N}"",""existedAtSnapshot"":true,""fileSize"":100,""lastWriteTimeUtc"":""2024-01-01T00:00:00Z""}},
                    {{""relativePath"":""newly_created.txt"",""backupId"":""{backupIdNew:N}"",""existedAtSnapshot"":false,""fileSize"":0,""lastWriteTimeUtc"":""0001-01-01T00:00:00Z""}}
                ]
            }}";

            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.ReadAllTextAsync(_testPaths.ManifestPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(manifestJson);
            SetupValidRelativePath("existing.txt");
            SetupValidRelativePath("newly_created.txt");

            var existingBakPath = _testPaths.GetBackupPath(backupIdExisting);
            var newBakPath = _testPaths.GetBackupPath(backupIdNew);

            _fileSystemMock.Setup(fs => fs.FileExists(existingBakPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.FileExists(Path.Combine(SolutionDir, "newly_created.txt"))).Returns(true);

            var manager = CreateManager();
            await manager.LoadSnapshotAsync(CancellationToken.None);

            await manager.RollbackAllAsync(CancellationToken.None);

            // Existing file should be restored from backup
            _fileSystemMock.Verify(fs => fs.CopyFileAsync(existingBakPath,
                Path.Combine(SolutionDir, "existing.txt"), It.IsAny<CancellationToken>()), Times.Once);

            // Newly created file should be deleted
            _fileSystemMock.Verify(fs => fs.Delete(Path.Combine(SolutionDir, "newly_created.txt")), Times.AtLeastOnce);

            // Backup files should be cleaned
            _fileSystemMock.Verify(fs => fs.Delete(existingBakPath), Times.AtLeastOnce);
        }

        [Test]
        public async Task RollbackAllAsync_FiresSnapshotChanged_WithEmptyList()
        {
            var backupId = Guid.NewGuid();
            var manifestJson = CreateManifestJsonWithFile(backupId, "rollback.txt", existedAtSnapshot: true);

            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.ReadAllTextAsync(_testPaths.ManifestPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(manifestJson);
            SetupValidRelativePath("rollback.txt");

            var bakPath = _testPaths.GetBackupPath(backupId);
            _fileSystemMock.Setup(fs => fs.FileExists(bakPath)).Returns(true);

            var manager = CreateManager();
            await manager.LoadSnapshotAsync(CancellationToken.None);

            IReadOnlyList<string> notifiedFiles = null;
            manager.SnapshotChanged += files => notifiedFiles = files;

            await manager.RollbackAllAsync(CancellationToken.None);

            Assert.That(notifiedFiles, Is.Not.Null);
            Assert.That(notifiedFiles.Count, Is.EqualTo(0));
        }

        #endregion

        #region RollbackFilesAsync Tests

        [Test]
        public void RollbackFilesAsync_Throws_WhenBatchActive()
        {
            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(false);

            var manager = CreateManager();
            manager.LoadSnapshotAsync(CancellationToken.None).GetAwaiter().GetResult();
            manager.BeginBatchAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(async () => await manager.RollbackFilesAsync(new[] { "file.txt" }, CancellationToken.None),
                Throws.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public async Task RollbackFilesAsync_ThrowsAggregateException_WhenAllPathsInvalid()
        {
            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(false);

            var manager = CreateManager();
            await manager.LoadSnapshotAsync(CancellationToken.None);

            var resolved = (string)null;
            _pathResolverMock.Setup(r => r.TryResolveFilePath("outside.txt", SolutionDir, out resolved))
                .Returns(false);

            Assert.That(async () =>
                await manager.RollbackFilesAsync(new[] { "outside.txt" }, CancellationToken.None),
                Throws.InstanceOf<AggregateException>());
        }

        [Test]
        public void RollbackFilesAsync_DoesNothing_WhenNoManifest()
        {
            SetupValidRelativePath("file.txt");
            var manager = CreateManager();
            Assert.DoesNotThrowAsync(async () =>
                await manager.RollbackFilesAsync(new[] { "file.txt" }, CancellationToken.None));
        }

        [Test]
        public async Task RollbackFilesAsync_RestoresSpecifiedFiles()
        {
            var backupId = Guid.NewGuid();
            var manifestJson = $@"{{
                ""solutionRoot"":""C:\\Solution"",
                ""createdAt"":""2024-01-01T00:00:00Z"",
                ""files"":[
                    {{""relativePath"":""rollback_this.txt"",""backupId"":""{backupId:N}"",""existedAtSnapshot"":true,""fileSize"":100,""lastWriteTimeUtc"":""2024-01-01T00:00:00Z""}},
                    {{""relativePath"":""keep_this.txt"",""backupId"":""{Guid.NewGuid():N}"",""existedAtSnapshot"":true,""fileSize"":200,""lastWriteTimeUtc"":""2024-01-01T00:00:00Z""}}
                ]
            }}";

            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.ReadAllTextAsync(_testPaths.ManifestPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(manifestJson);
            SetupValidRelativePath("rollback_this.txt");
            SetupValidRelativePath("keep_this.txt");

            var rollbackBakPath = _testPaths.GetBackupPath(backupId);
            _fileSystemMock.Setup(fs => fs.FileExists(rollbackBakPath)).Returns(true);

            var manager = CreateManager();
            await manager.LoadSnapshotAsync(CancellationToken.None);

            await manager.RollbackFilesAsync(new[] { "rollback_this.txt" }, CancellationToken.None);

            _fileSystemMock.Verify(fs => fs.CopyFileAsync(rollbackBakPath,
                Path.Combine(SolutionDir, "rollback_this.txt"), It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region CommitAllAsync Tests

        [Test]
        public void CommitAllAsync_Throws_WhenBatchActive()
        {
            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(false);

            var manager = CreateManager();
            manager.LoadSnapshotAsync(CancellationToken.None).GetAwaiter().GetResult();
            manager.BeginBatchAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(async () => await manager.CommitAllAsync(CancellationToken.None),
                Throws.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void CommitAllAsync_DoesNothing_WhenNoManifest()
        {
            var manager = CreateManager();
            Assert.DoesNotThrowAsync(async () => await manager.CommitAllAsync(CancellationToken.None));
        }

        [Test]
        public async Task CommitAllAsync_DeletesAllBackupsAndClearsState()
        {
            var backupId1 = Guid.NewGuid();
            var backupId2 = Guid.NewGuid();
            var manifestJson = $@"{{
                ""solutionRoot"":""C:\\Solution"",
                ""createdAt"":""2024-01-01T00:00:00Z"",
                ""files"":[
                    {{""relativePath"":""file1.txt"",""backupId"":""{backupId1:N}"",""existedAtSnapshot"":true,""fileSize"":100,""lastWriteTimeUtc"":""2024-01-01T00:00:00Z""}},
                    {{""relativePath"":""file2.txt"",""backupId"":""{backupId2:N}"",""existedAtSnapshot"":false,""fileSize"":0,""lastWriteTimeUtc"":""0001-01-01T00:00:00Z""}}
                ]
            }}";

            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.ReadAllTextAsync(_testPaths.ManifestPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(manifestJson);
            SetupValidRelativePath("file1.txt");
            SetupValidRelativePath("file2.txt");

            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.GetBackupPath(backupId1))).Returns(true);
            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.GetBackupPath(backupId2))).Returns(true);

            var manager = CreateManager();
            await manager.LoadSnapshotAsync(CancellationToken.None);

            await manager.CommitAllAsync(CancellationToken.None);

            _fileSystemMock.Verify(fs => fs.Delete(_testPaths.GetBackupPath(backupId1)), Times.AtLeastOnce);
            _fileSystemMock.Verify(fs => fs.Delete(_testPaths.GetBackupPath(backupId2)), Times.AtLeastOnce);

            var files = await manager.GetChangedFilesAsync(CancellationToken.None);
            Assert.That(files, Is.Empty);
        }

        #endregion

        #region CommitFilesAsync Tests

        [Test]
        public void CommitFilesAsync_Throws_WhenBatchActive()
        {
            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(false);

            var manager = CreateManager();
            manager.LoadSnapshotAsync(CancellationToken.None).GetAwaiter().GetResult();
            manager.BeginBatchAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(async () => await manager.CommitFilesAsync(new[] { "file.txt" }, CancellationToken.None),
                Throws.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public async Task CommitFilesAsync_DeletesSpecifiedBackups()
        {
            var backupIdCommit = Guid.NewGuid();
            var backupIdKeep = Guid.NewGuid();
            var manifestJson = $@"{{
                ""solutionRoot"":""C:\\Solution"",
                ""createdAt"":""2024-01-01T00:00:00Z"",
                ""files"":[
                    {{""relativePath"":""commit_this.txt"",""backupId"":""{backupIdCommit:N}"",""existedAtSnapshot"":true,""fileSize"":100,""lastWriteTimeUtc"":""2024-01-01T00:00:00Z""}},
                    {{""relativePath"":""keep_this.txt"",""backupId"":""{backupIdKeep:N}"",""existedAtSnapshot"":true,""fileSize"":200,""lastWriteTimeUtc"":""2024-01-01T00:00:00Z""}}
                ]
            }}";

            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(true);
            _fileSystemMock.Setup(fs => fs.ReadAllTextAsync(_testPaths.ManifestPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(manifestJson);
            SetupValidRelativePath("commit_this.txt");
            SetupValidRelativePath("keep_this.txt");

            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.GetBackupPath(backupIdCommit))).Returns(true);
            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.GetBackupPath(backupIdKeep))).Returns(true);

            var manager = CreateManager();
            await manager.LoadSnapshotAsync(CancellationToken.None);

            await manager.CommitFilesAsync(new[] { "commit_this.txt" }, CancellationToken.None);

            _fileSystemMock.Verify(fs => fs.Delete(_testPaths.GetBackupPath(backupIdCommit)), Times.AtLeastOnce);

            // The non-committed file should remain
            var files = await manager.GetChangedFilesAsync(CancellationToken.None);
            Assert.That(files, Has.Member("keep_this.txt"));
            Assert.That(files, Does.Not.Contain("commit_this.txt"));
        }

        #endregion

        #region Dispose Tests

        [Test]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            var manager = CreateManager();
            Assert.DoesNotThrow(() =>
            {
                manager.Dispose();
                manager.Dispose();
            });
        }

        #endregion

        #region SnapshotChanged Event Tests

        [Test]
        public async Task SnapshotChanged_IsFired_OnEndBatch()
        {
            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(false);

            var manager = CreateManager();
            await manager.LoadSnapshotAsync(CancellationToken.None);

            IReadOnlyList<string> notifiedFiles = null;
            manager.SnapshotChanged += files => notifiedFiles = files;

            await manager.BeginBatchAsync(CancellationToken.None);
            await manager.EndBatchAsync(CancellationToken.None);

            Assert.That(notifiedFiles, Is.Not.Null);
        }

        #endregion

        #region Helpers

        private SnapshotManager CreateManager()
        {
            return new SnapshotManager(
                _vsDependenciesMock.Object,
                _pathResolverMock.Object,
                _fileSystemMock.Object,
                _pathsFactoryMock.Object,
                _lockManagerMock.Object);
        }

        private async Task<SnapshotManager> SetupManagerWithManifestLoaded()
        {
            _fileSystemMock.Setup(fs => fs.FileExists(_testPaths.ManifestPath)).Returns(false);

            var manager = CreateManager();
            await manager.LoadSnapshotAsync(CancellationToken.None);
            await manager.BeginBatchAsync(CancellationToken.None);

            return manager;
        }

        private void SetupValidRelativePath(string relativePath)
        {
            var resolved = Path.Combine(SolutionDir, relativePath);
            _pathResolverMock.Setup(r => r.TryResolveFilePath(relativePath, SolutionDir, out resolved))
                .Returns(true);
            _pathResolverMock.Setup(r => r.IsPathInsideDirectory(resolved, SolutionDir))
                .Returns(true);
        }

        private static string CreateManifestJsonWithFile(Guid backupId, string relativePath, bool existedAtSnapshot)
        {
            return $@"{{
                ""solutionRoot"":""C:\\Solution"",
                ""createdAt"":""2024-01-01T00:00:00Z"",
                ""files"":[
                    {{""relativePath"":""{relativePath}"",""backupId"":""{backupId:N}"",""existedAtSnapshot"":{existedAtSnapshot.ToString().ToLower()},""fileSize"":100,""lastWriteTimeUtc"":""2024-01-01T00:00:00Z""}}
                ]
            }}";
        }

        #endregion
    }
}