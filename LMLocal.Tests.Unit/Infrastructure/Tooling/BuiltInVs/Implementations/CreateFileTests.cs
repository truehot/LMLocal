using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Syntax;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    [TestFixture]
    public class CreateFileTests
    {
        private Mock<IVsDependencies> _vsMock;
        private Mock<IPathResolver> _pathResolverMock;
        private Mock<IFileSystem> _fileSystemMock;
        private Mock<ISnapshotManager> _snapshotMock;
        private Mock<ISyntaxChecker> _syntaxMock;
        private Mock<ISyntaxCheckerFactory> _syntaxFactoryMock;
        private CreateFile _tool;

        private const string SolutionDir = @"C:\solution";
        private const string FilePath = "test.cs";
        private const string AbsolutePath = @"C:\solution\test.cs";
        private const string FileContent = "public class Test {}";

        [SetUp]
        public void SetUp()
        {
            _vsMock = new Mock<IVsDependencies>();
            _pathResolverMock = new Mock<IPathResolver>();
            _fileSystemMock = new Mock<IFileSystem>();
            _snapshotMock = new Mock<ISnapshotManager>();
            _syntaxMock = new Mock<ISyntaxChecker>();
            _syntaxFactoryMock = new Mock<ISyntaxCheckerFactory>();

            _vsMock.Setup(v => v.IsSolutionOpen).Returns(true);
            _vsMock.Setup(v => v.GetSolutionDirectory()).Returns(SolutionDir);

            string resolvedPath = AbsolutePath;
            _pathResolverMock
                .Setup(p => p.TryResolveFilePath(It.IsAny<string>(), It.IsAny<string>(), out resolvedPath))
                .Returns(true);
            _pathResolverMock
                .Setup(p => p.IsPathInsideDirectory(AbsolutePath, SolutionDir))
                .Returns(true);
            string relativePath = FilePath;
            _pathResolverMock
                .Setup(p => p.TryGetRelativePath(AbsolutePath, SolutionDir, out relativePath))
                .Returns(true);

            _fileSystemMock.Setup(f => f.FileExists(AbsolutePath)).Returns(false);
            _fileSystemMock.Setup(f => f.ValidateFilePath(AbsolutePath));
            _syntaxFactoryMock.Setup(f => f.GetChecker(It.IsAny<string>())).Returns((ISyntaxChecker)null);

            _tool = new CreateFile(
                _vsMock.Object, _pathResolverMock.Object, _fileSystemMock.Object,
                _snapshotMock.Object, _syntaxFactoryMock.Object);
        }

        private static Dictionary<string, object> CreateParams(
            string filePath = FilePath, string content = FileContent)
        {
            return new Dictionary<string, object>
            {
                ["file_path"] = filePath,
                ["content"] = content
            };
        }

        [Test]
        public async Task ExecuteAsync_NullParameters_ReturnsError()
        {
            var result = await _tool.ExecuteAsync(null);

            var resp = result as CreateFileResponse;
            Assert.That(resp, Is.Not.Null);
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Is.EqualTo("Parameters cannot be null."));
        }

        [Test]
        public async Task ExecuteAsync_MissingFilePath_ReturnsError()
        {
            var parameters = new Dictionary<string, object> { ["content"] = FileContent };

            var result = await _tool.ExecuteAsync(parameters);

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("file_path"));
        }

        [Test]
        public async Task ExecuteAsync_MissingContent_ReturnsError()
        {
            var parameters = new Dictionary<string, object> { ["file_path"] = FilePath };

            var result = await _tool.ExecuteAsync(parameters);

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("content"));
        }

        [Test]
        public async Task ExecuteAsync_SolutionNotOpen_ReturnsError()
        {
            _vsMock.Setup(v => v.IsSolutionOpen).Returns(false);

            var result = await _tool.ExecuteAsync(CreateParams());

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Is.EqualTo("No solution is currently open."));
        }

        [Test]
        public async Task ExecuteAsync_FileAlreadyExists_ReturnsError()
        {
            _fileSystemMock.Setup(f => f.FileExists(AbsolutePath)).Returns(true);

            var result = await _tool.ExecuteAsync(CreateParams());

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("already exists"));
        }

        [Test]
        public async Task ExecuteAsync_FileOutsideSolution_ReturnsError()
        {
            _pathResolverMock
                .Setup(p => p.IsPathInsideDirectory(AbsolutePath, SolutionDir))
                .Returns(false);

            var result = await _tool.ExecuteAsync(CreateParams());

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("outside the solution directory"));
        }

        [Test]
        public async Task ExecuteAsync_InvalidFilePath_ReturnsError()
        {
            _fileSystemMock
                .Setup(f => f.ValidateFilePath(AbsolutePath))
                .Throws(new ArgumentException("Invalid path characters"));

            var result = await _tool.ExecuteAsync(CreateParams());

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("Invalid file path"));
        }

        [Test]
        public async Task ExecuteAsync_Success_CreatesFileWithSnapshot()
        {
            var result = await _tool.ExecuteAsync(CreateParams());

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.CreatedSuccessfully, Is.True);
            Assert.That(resp.FilePath, Is.EqualTo(FilePath));

            _snapshotMock.Verify(s =>
                s.SnapshotFileAsync(AbsolutePath, SnapshotChangeStatus.BeforeCreate,
                    It.IsAny<CancellationToken>()));
            _fileSystemMock.Verify(f => f.EnsureDirectoryExistsForFile(AbsolutePath));
            _fileSystemMock.Verify(f =>
                f.WriteAllBytesWithEncodingAsync(AbsolutePath, FileContent, Encoding.UTF8, true,
                    It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExecuteAsync_Success_CreatesRelativePathWhenGetRelativeFails()
        {
            string nullRelative = null;
            _pathResolverMock
                .Setup(p => p.TryGetRelativePath(AbsolutePath, SolutionDir, out nullRelative))
                .Returns(false);

            var result = await _tool.ExecuteAsync(CreateParams());

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.FilePath, Is.EqualTo(AbsolutePath));
        }

        [Test]
        public async Task ExecuteAsync_Cancellation_ReturnsError()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();

            var result = await _tool.ExecuteAsync(CreateParams(), cts.Token);

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.False);
        }
    }
}
