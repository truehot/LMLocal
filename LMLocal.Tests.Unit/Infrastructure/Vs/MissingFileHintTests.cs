using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Vs
{
    /// <summary>
    /// The hint that read_file_lines, get_symbol_info and get_symbol_info_js add to "File not found": where a file of that name exists.
    /// Runs on real temporary folders, like <see cref="SearchScopeResolverTests"/>.
    /// </summary>
    [TestFixture]
    public class MissingFileHintTests
    {
        private string _root;
        private string _solutionDir;
        private MissingFileHint _hint;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "MissingFileHintTests", Guid.NewGuid().ToString());
            _solutionDir = Path.Combine(_root, "Solution");
            Directory.CreateDirectory(_solutionDir);
            _hint = new MissingFileHint(new PathResolver(), new DefaultFileSystem());
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }

        private void Create(string relativePath)
        {
            string full = Path.Combine(_solutionDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, "x");
        }

        private Task<string> BuildAsync(string requested, string shown = null, CancellationToken token = default)
        {
            return _hint.BuildFileNotFoundErrorAsync(requested, shown ?? requested, _solutionDir, token);
        }

        [Test]
        public async Task OneFileWithThatName_NamesItsPath()
        {
            Create(@"LMLocal\Application\ChatSession\ChatSessionState.cs");

            string error = await BuildAsync(@"LMLocal\Application\ChatSessionState.cs");

            Assert.That(error, Is.EqualTo(
                @"File not found: LMLocal\Application\ChatSessionState.cs. The file 'ChatSessionState.cs' exists at 'LMLocal\Application\ChatSession\ChatSessionState.cs': call again with that path."));
        }

        [Test]
        public async Task ShownPathIsPrintedAndTheRequestedOneLooksTheNameUp()
        {
            Create(@"App\Chat\Compactor.cs");
            string shown = Path.Combine(_solutionDir, @"Wrong\Compactor.cs");

            string error = await BuildAsync(@"Wrong\Compactor.cs", shown);

            Assert.That(error, Does.StartWith("File not found: " + shown + ". "));
            Assert.That(error, Does.Contain(@"exists at 'App\Chat\Compactor.cs'"));
        }

        [Test]
        public async Task PathWithAFolderSplitAtTheWrongDot_NamesTheRealProjectFolder()
        {
            // A model wrote "LMLocal\Tests.Unit\..." for the project folder "LMLocal.Tests.Unit".
            Create(@"LMLocal.Tests.Unit\Internal\ToolHistoryPrunerTests.cs");

            string error = await BuildAsync(@"LMLocal\Tests.Unit\Internal\ToolHistoryPrunerTests.cs");

            Assert.That(error, Does.Contain(@"exists at 'LMLocal.Tests.Unit\Internal\ToolHistoryPrunerTests.cs'"));
        }

        [Test]
        public async Task SeveralFilesWithThatName_ListsThemShortestFirst()
        {
            Create(@"App\Chat\Compactor.cs");
            Create(@"Lib\Compactor.cs");

            string error = await BuildAsync(@"Other\Compactor.cs");

            Assert.That(error, Is.EqualTo(
                @"File not found: Other\Compactor.cs. Files named 'Compactor.cs' exist at: 'Lib\Compactor.cs', 'App\Chat\Compactor.cs'. Call again with the right one."));
        }

        [Test]
        public async Task NoFileWithThatName_SaysSo()
        {
            Create(@"App\Chat\Compactor.cs");

            string error = await BuildAsync(@"App\NoSuchSerializer.cs");

            Assert.That(error, Is.EqualTo(
                @"File not found: App\NoSuchSerializer.cs. No file named 'NoSuchSerializer.cs' exists anywhere in the solution."));
        }

        [Test]
        public async Task NameWithoutExtension_IsPlainFileNotFound()
        {
            Create(@"App\Makefile");

            string error = await BuildAsync(@"Other\Makefile");

            Assert.That(error, Is.EqualTo(@"File not found: Other\Makefile"));
        }

        [Test]
        public async Task EmptyPath_IsPlainFileNotFound()
        {
            string error = await BuildAsync(string.Empty, "x");

            Assert.That(error, Is.EqualTo("File not found: x"));
        }

        [Test]
        public void CancelledByTheCaller_Propagates()
        {
            Create(@"App\Chat\Compactor.cs");
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                Assert.CatchAsync<OperationCanceledException>(() => BuildAsync(@"Other\Compactor.cs", null, cts.Token));
            }
        }
    }
}
