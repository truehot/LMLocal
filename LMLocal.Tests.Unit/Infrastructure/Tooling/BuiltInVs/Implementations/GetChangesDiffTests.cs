using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Changes.Models;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    [TestFixture]
    public class GetChangesDiffTests
    {
        private const string SolutionDir = @"C:\solution";

        private Mock<IVsDependencies> _vsMock;
        private Mock<IPathResolver> _pathResolverMock;
        private Mock<IChangesReader> _readerMock;
        private GetChangesDiff _tool;

        [SetUp]
        public void SetUp()
        {
            _vsMock = new Mock<IVsDependencies>();
            _pathResolverMock = new Mock<IPathResolver>();
            _readerMock = new Mock<IChangesReader>();

            _vsMock.Setup(v => v.IsSolutionOpen).Returns(true);
            _vsMock.Setup(v => v.GetSolutionDirectory()).Returns(SolutionDir);

            string resolvedPath = @"C:\solution\src\Foo.cs";
            _pathResolverMock
                .Setup(p => p.TryResolveFilePath(It.IsAny<string>(), It.IsAny<string>(), out resolvedPath))
                .Returns(true);

            _pathResolverMock
                .Setup(p => p.IsPathInsideDirectory(It.IsAny<string>(), SolutionDir))
                .Returns((string path, string directory) => path.StartsWith(@"C:\solution", StringComparison.OrdinalIgnoreCase));

            string relativePath = @"src\Foo.cs";
            _pathResolverMock
                .Setup(p => p.TryGetRelativePath(It.IsAny<string>(), SolutionDir, out relativePath))
                .Returns(true);

            _readerMock
                .Setup(r => r.GetChangesAsync(
                    It.IsAny<IReadOnlyCollection<string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChangesResult { Success = true, Files = new List<ChangesFile>() });

            _tool = new GetChangesDiff(_vsMock.Object, _pathResolverMock.Object, _readerMock.Object);
        }

        // ── Tool definition ──────────────────────────────────────────────────

        [Test]
        public void GetToolInfo_ExposesNameAccessLevelAndParameters()
        {
            ToolDefinition info = _tool.GetToolInfo();

            Assert.That(_tool.ToolName, Is.EqualTo("get_changes_diff"));
            Assert.That(_tool.AccessLevel, Is.EqualTo(ToolAccessLevel.ReadOnly));
            Assert.That(info.Name, Is.EqualTo("get_changes_diff"));
            Assert.That(info.Description, Does.Contain("include_diff"));
            Assert.That(info.Description, Does.Contain("never edits files"));
            Assert.That(info.Parameters.Type, Is.EqualTo("object"));
            Assert.That(info.Parameters.Properties.Keys,
                Is.EquivalentTo(new[] { "include_diff", "files", "max_files", "max_lines" }));
            Assert.That(info.Parameters.Properties["files"].Type, Is.EqualTo("array"));
            Assert.That(info.Parameters.Properties["files"].Items.Type, Is.EqualTo("string"));
            Assert.That(info.Parameters.Required, Is.Null);
        }

        // ── Parameter validation ─────────────────────────────────────────────

        [Test]
        public async Task ExecuteAsync_NoSolutionOpen_ReturnsFailure()
        {
            _vsMock.Setup(v => v.IsSolutionOpen).Returns(false);

            var result = await _tool.ExecuteAsync(new Dictionary<string, object>()) as ChangesResult;

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("No solution is currently open."));
            VerifyReaderNeverCalled();
        }

        [Test]
        public async Task ExecuteAsync_IncludeDiffAsString_ReturnsFailure()
        {
            var parameters = new Dictionary<string, object> { ["include_diff"] = "yes" };

            var result = await _tool.ExecuteAsync(parameters) as ChangesResult;

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("include_diff"));
            VerifyReaderNeverCalled();
        }

        [Test]
        public async Task ExecuteAsync_FilesAsSingleString_ReturnsFailure()
        {
            var parameters = new Dictionary<string, object> { ["files"] = "src/Foo.cs" };

            var result = await _tool.ExecuteAsync(parameters) as ChangesResult;

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("array of strings"));
            VerifyReaderNeverCalled();
        }

        [Test]
        public async Task ExecuteAsync_TooManyFiles_ReturnsFailure()
        {
            var files = new List<object>();
            for (int i = 0; i < GetChangesDiff.MaxFilesFilterEntries + 1; i++)
                files.Add("src/File" + i + ".cs");

            var parameters = new Dictionary<string, object> { ["files"] = files };

            var result = await _tool.ExecuteAsync(parameters) as ChangesResult;

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain(GetChangesDiff.MaxFilesFilterEntries.ToString()));
            VerifyReaderNeverCalled();
        }

        // ── Defaults and clamping ────────────────────────────────────────────

        [Test]
        public async Task ExecuteAsync_NullParameters_UsesDefaults()
        {
            await _tool.ExecuteAsync(null);

            _readerMock.Verify(
                r => r.GetChangesAsync(
                    (IReadOnlyCollection<string>)null,
                    false,
                    GetChangesDiff.DefaultMaxFiles,
                    GetChangesDiff.DefaultMaxLines,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ExecuteAsync_ClampsLimitsToTheirRanges()
        {
            var parameters = new Dictionary<string, object>
            {
                ["include_diff"] = true,
                ["max_files"] = 500,
                ["max_lines"] = 5
            };

            await _tool.ExecuteAsync(parameters);

            _readerMock.Verify(
                r => r.GetChangesAsync(
                    (IReadOnlyCollection<string>)null,
                    true,
                    GetChangesDiff.MaxMaxFiles,
                    GetChangesDiff.MinMaxLines,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ExecuteAsync_EmptyFilesArray_IsTreatedAsAllFiles()
        {
            var parameters = new Dictionary<string, object> { ["files"] = new List<object>() };

            await _tool.ExecuteAsync(parameters);

            _readerMock.Verify(
                r => r.GetChangesAsync(
                    (IReadOnlyCollection<string>)null,
                    false,
                    GetChangesDiff.DefaultMaxFiles,
                    GetChangesDiff.DefaultMaxLines,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        // ── Path filter ──────────────────────────────────────────────────────

        [Test]
        public async Task ExecuteAsync_FileFilter_IsNormalizedAndDeduplicated()
        {
            var parameters = new Dictionary<string, object>
            {
                ["files"] = new List<object> { @"src\Foo.cs", "src/Foo.cs", "./src/Foo.cs" }
            };

            await _tool.ExecuteAsync(parameters);

            _readerMock.Verify(
                r => r.GetChangesAsync(
                    It.Is<IReadOnlyCollection<string>>(files => files.Count == 1 && files.Contains("src/Foo.cs")),
                    false,
                    GetChangesDiff.DefaultMaxFiles,
                    GetChangesDiff.DefaultMaxLines,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ExecuteAsync_AbsolutePathInsideSolution_IsPassedAsRelative()
        {
            var parameters = new Dictionary<string, object>
            {
                ["files"] = new List<object> { @"C:\solution\src\Foo.cs" }
            };

            await _tool.ExecuteAsync(parameters);

            _readerMock.Verify(
                r => r.GetChangesAsync(
                    It.Is<IReadOnlyCollection<string>>(files => files.Count == 1 && files.Contains("src/Foo.cs")),
                    false,
                    GetChangesDiff.DefaultMaxFiles,
                    GetChangesDiff.DefaultMaxLines,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ExecuteAsync_AllPathsOutsideSolution_ReturnsEmptySuccessWithoutReading()
        {
            var parameters = new Dictionary<string, object>
            {
                ["files"] = new List<object> { @"D:\other\X.cs" }
            };

            var result = await _tool.ExecuteAsync(parameters) as ChangesResult;

            Assert.That(result.Success, Is.True);
            Assert.That(result.TotalFiles, Is.EqualTo(0));
            Assert.That(result.Files, Is.Empty);
            Assert.That(result.HasMoreResults, Is.False);
            VerifyReaderNeverCalled();
        }

        // ── Failure propagation ──────────────────────────────────────────────

        [Test]
        public async Task ExecuteAsync_ReaderThrows_ReturnsFailure()
        {
            _readerMock
                .Setup(r => r.GetChangesAsync(
                    It.IsAny<IReadOnlyCollection<string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

            var result = await _tool.ExecuteAsync(new Dictionary<string, object>()) as ChangesResult;

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("boom"));
        }

        [Test]
        public async Task ExecuteAsync_NegativeOrZeroLimits_AreClampedToMinimums()
        {
            var parameters = new Dictionary<string, object>
            {
                ["max_files"] = -5,
                ["max_lines"] = 0
            };

            await _tool.ExecuteAsync(parameters);

            _readerMock.Verify(
                r => r.GetChangesAsync(
                    (IReadOnlyCollection<string>)null,
                    false,
                    1,
                    GetChangesDiff.MinMaxLines,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ExecuteAsync_NonNumericLimits_FallBackToDefaults()
        {
            var parameters = new Dictionary<string, object>
            {
                ["max_files"] = "abc",
                ["max_lines"] = null
            };

            await _tool.ExecuteAsync(parameters);

            _readerMock.Verify(
                r => r.GetChangesAsync(
                    (IReadOnlyCollection<string>)null,
                    false,
                    GetChangesDiff.DefaultMaxFiles,
                    GetChangesDiff.DefaultMaxLines,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        // ── Messages ─────────────────────────────────────────────────────────

        [Test]
        public void GetCompletionMessage_ReportsCountsAndTruncation()
        {
            var result = new ChangesResult
            {
                Success = true,
                TotalFiles = 3,
                TotalAdded = 10,
                TotalRemoved = 2,
                HasMoreResults = true,
                Files = new List<ChangesFile> { new ChangesFile { FilePath = "src/A.cs" } },
                Outcome = ChangesOutcome.Success
            };

            string message = _tool.GetCompletionMessage(result);

            Assert.That(message, Does.Contain("1 of 3"));
            Assert.That(message, Does.Contain("truncated"));
        }

        [Test]
        public void GetCompletionMessage_NoPendingChanges()
        {
            var result = new ChangesResult
            {
                Success = true,
                Files = new List<ChangesFile>(),
                Outcome = ChangesOutcome.NoPendingChanges
            };

            Assert.That(_tool.GetCompletionMessage(result), Is.EqualTo("No pending file changes."));
        }

        [Test]
        public void GetProcessingMessage_ReflectsIncludeDiff()
        {
            Assert.That(_tool.GetProcessingMessage(new Dictionary<string, object>()), Does.Contain("pending changes"));
            Assert.That(
                _tool.GetProcessingMessage(new Dictionary<string, object> { ["include_diff"] = true }),
                Does.Contain("with diff"));
        }

        private void VerifyReaderNeverCalled()
        {
            _readerMock.Verify(
                r => r.GetChangesAsync(
                    It.IsAny<IReadOnlyCollection<string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
