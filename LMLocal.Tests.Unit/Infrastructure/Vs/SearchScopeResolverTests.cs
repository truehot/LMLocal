using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Vs
{
    [TestFixture]
    public class SearchScopeResolverTests
    {
        private string _root;
        private string _solutionDir;
        private SearchScopeResolver _resolver;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "SearchScopeResolverTests", Guid.NewGuid().ToString());
            _solutionDir = Path.Combine(_root, "Solution");
            Directory.CreateDirectory(_solutionDir);
            _resolver = new SearchScopeResolver(new PathResolver(), new DefaultFileSystem());
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }

        private string Create(string relativePath, string content = "x")
        {
            string full = Path.Combine(_solutionDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, content);
            return full;
        }

        private static Func<string, Task<IReadOnlyList<string>>> Projects(Dictionary<string, string> byName)
        {
            return name => Task.FromResult<IReadOnlyList<string>>(
                byName.TryGetValue(name, out string dir) ? new[] { dir } : new string[0]);
        }

        private static Func<string, Task<IReadOnlyList<string>>> NoProjects => Projects(new Dictionary<string, string>());

        // ---- ResolveAsync -------------------------------------------------------------------------

        [TestCase(@"App\Chat\Compactor.cs")]
        [TestCase("App/Chat/Compactor.cs")]
        [TestCase(@"App\\Chat\\Compactor.cs")]
        [TestCase(@" App\Chat\Compactor.cs ")]
        public async Task Resolve_ExistingFile_IsFileScope(string path)
        {
            string file = Create(@"App\Chat\Compactor.cs");

            var (scope, error) = await _resolver.ResolveAsync(path, _solutionDir, NoProjects);

            Assert.That(error, Is.Null);
            Assert.That(scope.Kind, Is.EqualTo(SearchScopeKind.File));
            Assert.That(scope.FilePath, Is.EqualTo(file).IgnoreCase);
        }

        [Test]
        public async Task Resolve_AbsoluteFile_IsFileScope()
        {
            string file = Create(@"App\Compactor.cs");

            var (scope, error) = await _resolver.ResolveAsync(file, _solutionDir, NoProjects);

            Assert.That(error, Is.Null);
            Assert.That(scope.Kind, Is.EqualTo(SearchScopeKind.File));
        }

        [TestCase(@"App\Chat")]
        [TestCase(@"App\Chat\")]
        [TestCase("App/Chat")]
        [TestCase(@"App\\Chat")]
        public async Task Resolve_ExistingFolder_IsFolderScope(string path)
        {
            Create(@"App\Chat\Compactor.cs");

            var (scope, error) = await _resolver.ResolveAsync(path, _solutionDir, NoProjects);

            Assert.That(error, Is.Null);
            Assert.That(scope.Kind, Is.EqualTo(SearchScopeKind.Folder));
            Assert.That(scope.Directories, Is.EqualTo(new[] { Path.Combine(_solutionDir, "App", "Chat") }).AsCollection.IgnoreCase);
        }

        [Test]
        public async Task Resolve_SolutionRoot_IsFolderScope()
        {
            var (scope, error) = await _resolver.ResolveAsync(".", _solutionDir, NoProjects);

            Assert.That(error, Is.Null);
            Assert.That(scope.Kind, Is.EqualTo(SearchScopeKind.Folder));
        }

        [Test]
        public async Task Resolve_ProjectName_IsProjectFolder()
        {
            Create(@"src\Deep\My.Project\Class1.cs");
            string projectDir = Path.Combine(_solutionDir, "src", "Deep", "My.Project");

            var (scope, error) = await _resolver.ResolveAsync("My.Project", _solutionDir,
                Projects(new Dictionary<string, string> { ["My.Project"] = projectDir }));

            Assert.That(error, Is.Null);
            Assert.That(scope.Kind, Is.EqualTo(SearchScopeKind.Folder));
            Assert.That(scope.Directories, Is.EqualTo(new[] { projectDir }).AsCollection);
        }

        [Test]
        public async Task Resolve_ExistingFolder_WinsOverProjectName()
        {
            Create(@"Shared\a.cs");
            Create(@"src\Shared\b.cs");
            bool projectLookupCalled = false;

            var (scope, error) = await _resolver.ResolveAsync("Shared", _solutionDir, name =>
            {
                projectLookupCalled = true;
                return Task.FromResult<IReadOnlyList<string>>(new[] { Path.Combine(_solutionDir, "src", "Shared") });
            });

            Assert.That(error, Is.Null);
            Assert.That(projectLookupCalled, Is.False, "an existing path must not fall through to the project lookup");
            Assert.That(scope.Directories, Is.EqualTo(new[] { Path.Combine(_solutionDir, "Shared") }).AsCollection.IgnoreCase);
        }

        [TestCase("Tests")]
        [TestCase("Compactor.cs")]
        [TestCase(@"Chat\Compactor.cs")]
        [TestCase(@"App\Missing")]
        public async Task Resolve_NotAPathNorProject_Fails(string path)
        {
            Create(@"App\Chat\Compactor.cs");
            Create(@"My.Tests\t.cs");

            var (scope, error) = await _resolver.ResolveAsync(path, _solutionDir,
                Projects(new Dictionary<string, string> { ["My.Tests"] = Path.Combine(_solutionDir, "My.Tests") }));

            Assert.That(scope, Is.Null);
            Assert.That(error, Does.StartWith("Path not found"));
            Assert.That(error, Does.Contain("find_files"));
        }

        [Test]
        public async Task Resolve_FolderOutsideSolution_Fails()
        {
            string outside = Path.Combine(_root, "Outside");
            Directory.CreateDirectory(outside);

            var (scope, error) = await _resolver.ResolveAsync(outside, _solutionDir, NoProjects);

            Assert.That(scope, Is.Null);
            Assert.That(error, Does.Contain("outside the solution directory"));
        }

        [Test]
        public async Task Resolve_RelativeEscapeOutsideSolution_Fails()
        {
            string outsideFile = Path.Combine(_root, "secret.txt");
            File.WriteAllText(outsideFile, "x");

            var (scope, error) = await _resolver.ResolveAsync(@"..\secret.txt", _solutionDir, NoProjects);

            Assert.That(scope, Is.Null);
            Assert.That(error, Does.Contain("outside the solution directory"));
        }

        [Test]
        public async Task Resolve_ProjectOutsideSolution_IsIgnored()
        {
            string outside = Path.Combine(_root, "External");
            Directory.CreateDirectory(outside);

            var (scope, error) = await _resolver.ResolveAsync("External", _solutionDir,
                Projects(new Dictionary<string, string> { ["External"] = outside }));

            Assert.That(scope, Is.Null);
            Assert.That(error, Does.StartWith("Path not found"));
        }

        [Test]
        public async Task Resolve_SameScope_HasSameKey()
        {
            Create(@"App\Chat\Compactor.cs");

            var (a, _) = await _resolver.ResolveAsync(@"App\Chat", _solutionDir, NoProjects);
            var (b, _) = await _resolver.ResolveAsync("app/chat/", _solutionDir, NoProjects);
            var (c, _) = await _resolver.ResolveAsync(@"App\Chat\Compactor.cs", _solutionDir, NoProjects);

            Assert.That(b.Key, Is.EqualTo(a.Key));
            Assert.That(c.Key, Is.Not.EqualTo(a.Key));
        }

        // ---- EnumerateFolderFilesAsync ------------------------------------------------------------

        [Test]
        public async Task Enumerate_SkipsGeneratedFoldersAndBinaries()
        {
            Create(@"App\a.cs");
            Create(@"App\Sub\b.md");
            Create(@"App\bin\Debug\c.cs");
            Create(@"App\obj\d.cs");
            Create(@"App\node_modules\pkg\e.js");
            Create(@"App\logo.png");
            Create(@"App\lib.dll");

            var files = await _resolver.EnumerateFolderFilesAsync(new[] { Path.Combine(_solutionDir, "App") }, null, 0);
            var names = files.Select(Path.GetFileName).OrderBy(n => n).ToList();

            Assert.That(names, Is.EqualTo(new[] { "a.cs", "b.md" }));
        }

        [Test]
        public async Task Enumerate_ExtensionFilter_KeepsOnlyThoseExtensions()
        {
            Create(@"App\a.cs");
            Create(@"App\Sub\b.md");
            Create(@"App\Sub\c.CS");

            var files = await _resolver.EnumerateFolderFilesAsync(new[] { Path.Combine(_solutionDir, "App") }, "cs", 0);
            var names = files.Select(Path.GetFileName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

            Assert.That(names, Is.EqualTo(new[] { "a.cs", "c.CS" }));
        }

        [Test]
        public async Task Enumerate_StopsAtLimit()
        {
            for (int i = 0; i < 10; i++)
                Create($@"App\Sub{i % 3}\f{i}.cs");

            var files = await _resolver.EnumerateFolderFilesAsync(new[] { Path.Combine(_solutionDir, "App") }, null, 4);

            Assert.That(files.Count, Is.EqualTo(4));
        }

        [Test]
        public async Task Enumerate_OverlappingFolders_ReturnsEachFileOnce()
        {
            Create(@"App\a.cs");
            Create(@"App\Inner\b.cs");

            var files = await _resolver.EnumerateFolderFilesAsync(
                new[] { Path.Combine(_solutionDir, "App"), Path.Combine(_solutionDir, "App", "Inner") }, null, 0);

            Assert.That(files.Count, Is.EqualTo(2));
        }
    }
}
