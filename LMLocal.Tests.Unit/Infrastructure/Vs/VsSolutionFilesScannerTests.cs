using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EnvDTE80;

using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Moq;
using NUnit.Framework;
using static LMLocal.Infrastructure.Tooling.BuiltInVs.Common.VsSolutionFilesScanner;

namespace LMLocal.Tests.Unit.Infrastructure.Vs
{
    [TestFixture]
    public class VsSolutionFilesScannerTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "VsSolutionFilesScannerTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }

        [Test]
        public async Task EnumerateSolutionFiles_WithProjectFilter_ReturnsOnlyFilesFromMatchingProject()
        {
            // Arrange
            var fileA = Path.Combine(_root, "ProjectA", "a.cs");
            var fileB = Path.Combine(_root, "ProjectA", "b.txt");
            var fileC = Path.Combine(_root, "ProjectB", "sub", "c.cs");
            var fileD = Path.Combine(_root, "OtherProject", "d.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(fileA));
            Directory.CreateDirectory(Path.GetDirectoryName(fileB));
            Directory.CreateDirectory(Path.GetDirectoryName(fileC));
            Directory.CreateDirectory(Path.GetDirectoryName(fileD));
            File.WriteAllText(fileA, "a");
            File.WriteAllText(fileB, "b");
            File.WriteAllText(fileC, "c");
            File.WriteAllText(fileD, "d");

            var provider = new TestFileProvider(_root);
            var dependencies = new TestVsDependencies(_root, provider);
            var uiThreadGuard = new TestUiThreadGuard();
            var scanner = new VsSolutionFilesScanner(dependencies, uiThreadGuard, new PathResolver(), provider);

            // Act - Filter by ProjectB (case-insensitive substring match)
            var filter = new EnumerateSolutionFilesFilter
            {
                ExtensionFilter = ".cs",
                ProjectFilter = "ProjectB",
            };
            var results = (await scanner.EnumerateSolutionFilesAsync(filter)).ToList();

            // Assert
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0], Does.EndWith(Path.Combine("ProjectB", "sub", "c.cs")));
        }

        [Test]
        public async Task EnumerateSolutionFiles_WithProjectFilter_CaseInsensitive()
        {
            // Arrange
            var fileA = Path.Combine(_root, "ProjectA", "a.cs");
            var fileB = Path.Combine(_root, "MyProject", "b.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(fileA));
            Directory.CreateDirectory(Path.GetDirectoryName(fileB));
            File.WriteAllText(fileA, "a");
            File.WriteAllText(fileB, "b");

            var provider = new TestFileProvider(_root);
            var dependencies = new TestVsDependencies(_root, provider);
            var uiThreadGuard = new TestUiThreadGuard();
            var scanner = new VsSolutionFilesScanner(dependencies, uiThreadGuard, new PathResolver(), provider);

            // Act - Use lowercase filter for "MyProject"
            var filter = new EnumerateSolutionFilesFilter
            {
                ExtensionFilter = ".cs",
                ProjectFilter = "myproject",
            };
            var results = (await scanner.EnumerateSolutionFilesAsync(filter)).ToList();

            // Assert
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0], Does.Contain("MyProject"));
        }

        [Test]
        public async Task EnumerateSolutionFilesAsync_ExtensionFilter_ReturnsOnlyCsFiles()
        {
            // Arrange
            var fileA = Path.Combine(_root, "Folder1", "a.cs");
            var fileB = Path.Combine(_root, "Folder1", "b.txt");
            var fileC = Path.Combine(_root, "Folder2", "c.cs");
            var fileD = Path.Combine(_root, "Folder3", "d.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(fileA));
            Directory.CreateDirectory(Path.GetDirectoryName(fileB));
            Directory.CreateDirectory(Path.GetDirectoryName(fileC));
            Directory.CreateDirectory(Path.GetDirectoryName(fileD));
            File.WriteAllText(fileA, "a");
            File.WriteAllText(fileB, "b");
            File.WriteAllText(fileC, "c");
            File.WriteAllText(fileD, "d");

            var provider = new TestFileProvider(_root);
            var dependencies = new TestVsDependencies(_root, provider);
            var uiThreadGuard = new TestUiThreadGuard();
            var scanner = new VsSolutionFilesScanner(dependencies, uiThreadGuard, new PathResolver(), provider);

            // Act
            var filter = new EnumerateSolutionFilesFilter
            {
                ExtensionFilter = ".cs",
            };
            var results = (await scanner.EnumerateSolutionFilesAsync(filter)).ToList();

            // Assert
            Assert.That(results.Count, Is.EqualTo(3));
            Assert.That(results.All(r => r.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)));
        }

        [Test]
        public async Task EnumerateSolutionFilesAsync_LimitRespected()
        {
            // Arrange
            for (int i = 1; i <= 5; i++)
            {
                var f = Path.Combine(_root, $"P", $"file{i}.cs");
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllText(f, "x");
            }

            var provider = new TestFileProvider(_root);
            var dependencies = new TestVsDependencies(_root, provider);
            var uiThreadGuard = new TestUiThreadGuard();
            var scanner = new VsSolutionFilesScanner(dependencies, uiThreadGuard, new PathResolver(), provider);

            // Act
            var filter = new EnumerateSolutionFilesFilter
            {
                ExtensionFilter = ".cs",
                Limit = 3
            };
            var results = (await scanner.EnumerateSolutionFilesAsync(filter)).ToList();

            // Assert
            Assert.That(results.Count, Is.EqualTo(3));
        }

        [TestCase(@"LMLocal\Application\Chat")]
        [TestCase("LMLocal/Application/Chat")]
        [TestCase(@"LMLocal\\Application\\Chat")]
        [TestCase(@"\LMLocal\Application\Chat\")]
        [TestCase(@"application\chat")]
        public async Task EnumerateSolutionFiles_WithPathProjectFilter_ReturnsOnlyFilesUnderThatFolder(string projectFilter)
        {
            // Arrange
            var inChat = Path.Combine(_root, "LMLocal", "Application", "Chat", "HistoryCompactor.cs");
            var inChatSub = Path.Combine(_root, "LMLocal", "Application", "Chat", "Sub", "Nested.cs");
            var inSession = Path.Combine(_root, "LMLocal", "Application", "ChatSession", "Orchestrator.cs");
            var otherChat = Path.Combine(_root, "Other", "Chat", "Other.cs");
            foreach (var f in new[] { inChat, inChatSub, inSession, otherChat })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllText(f, "x");
            }

            var scanner = CreateScanner();

            // Act
            var results = (await scanner.EnumerateSolutionFilesAsync(new EnumerateSolutionFilesFilter
            {
                ExtensionFilter = ".cs",
                ProjectFilter = projectFilter,
                ReturnRelative = true
            })).ToList();

            // Assert: path segments match whole folder names, so the sibling 'ChatSession' folder is excluded.
            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results, Has.Some.EndWith(Path.Combine("Chat", "HistoryCompactor.cs")));
            Assert.That(results, Has.Some.EndWith(Path.Combine("Chat", "Sub", "Nested.cs")));
            Assert.That(results, Has.None.Contains("ChatSession"));
            Assert.That(results, Has.None.Contains("Other"));
        }

        [TestCase(@"LMLocal\")]
        [TestCase("LMLocal/")]
        public async Task EnumerateSolutionFiles_WithTrailingSeparatorFilter_MatchesOnlyThatFolder(string projectFilter)
        {
            var inLmLocal = Path.Combine(_root, "LMLocal", "Core", "A.cs");
            var inTests = Path.Combine(_root, "LMLocal.Tests.Unit", "B.cs");
            foreach (var f in new[] { inLmLocal, inTests })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllText(f, "x");
            }

            var results = (await CreateScanner().EnumerateSolutionFilesAsync(new EnumerateSolutionFilesFilter
            {
                ExtensionFilter = ".cs",
                ProjectFilter = projectFilter
            })).ToList();

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0], Does.EndWith(Path.Combine("LMLocal", "Core", "A.cs")));
        }

        [Test]
        public async Task EnumerateSolutionFiles_WithAbsolutePathProjectFilter_IsMadeRelative()
        {
            var inModels = Path.Combine(_root, "Core", "Models", "A.cs");
            var outside = Path.Combine(_root, "Core", "Other", "B.cs");
            foreach (var f in new[] { inModels, outside })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllText(f, "x");
            }

            var results = (await CreateScanner().EnumerateSolutionFilesAsync(new EnumerateSolutionFilesFilter
            {
                ExtensionFilter = ".cs",
                ProjectFilter = Path.Combine(_root, "Core", "Models")
            })).ToList();

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0], Does.EndWith(Path.Combine("Models", "A.cs")));
        }

        [TestCase("LMLocal.Tests.Unit\\Infrastructure\\Vs\\X.cs", "Tests", true)]
        [TestCase("LMLocal\\Core\\Models\\X.cs", "Models", true)]
        [TestCase("LMLocal\\Core\\Models\\X.cs", "X.cs", true)]
        [TestCase("LMLocal\\Core\\Models\\X.cs", "Core\\Models", true)]
        [TestCase("LMLocal\\Core\\Models\\X.cs", "core/models", true)]
        [TestCase("LMLocal\\Core\\Models\\X.cs", "LMLocal\\Core\\Models\\X.cs", true)]
        [TestCase("LMLocal\\Core\\Models\\X.cs", "Core\\Common", false)]
        [TestCase("LMLocal\\Core\\Models\\X.cs", "Models\\Core", false)]
        [TestCase("LMLocal\\Core\\Models\\X.cs", "Nope", false)]
        [TestCase("LMLocal\\Core\\Models\\X.cs", "Local\\Core", false)]
        [TestCase("LMLocal\\Core\\Models\\X.cs", "Core\\Mod", false)]
        [TestCase("LMLocal\\Application\\ChatSession\\X.cs", "LMLocal\\Application\\Chat", false)]
        [TestCase("LMLocal\\Application\\ChatSessionStream\\X.cs", "Application\\Chat", false)]
        [TestCase("LMLocal\\Application\\Chat\\X.cs", "Application\\Chat", true)]
        [TestCase("LMLocal.Tests.Unit\\A\\X.cs", "LMLocal\\", false)]
        [TestCase("LMLocal\\A\\X.cs", "LMLocal/", true)]
        [TestCase("LMLocal.Tests.Unit\\A\\X.cs", "LMLocal", true)]
        [TestCase("LMLocal\\Core\\Models\\X.cs", "  ", true)]
        [TestCase("LMLocal\\Core\\Models\\X.cs", "\\", true)]
        public void MatchesProjectFilter_Cases(string relativePath, string filter, bool expected)
        {
            Assert.That(VsSolutionFilesScanner.MatchesProjectFilter(relativePath, filter), Is.EqualTo(expected));
        }

        [Test]
        public void MatchesProjectFilter_WithoutSeparator_DoesNotMatchAcrossComponents()
        {
            // Pre-existing semantics are kept: a separator-free filter must lie within a single component.
            Assert.That(VsSolutionFilesScanner.MatchesProjectFilter("Core\\Models\\X.cs", "CoreModels"), Is.False);
            Assert.That(VsSolutionFilesScanner.MatchesProjectFilter("Core\\Models\\X.cs", "reMod"), Is.False);
        }

        [TestCase(0, 3, false, 0)]
        [TestCase(3, 3, false, 3)]
        [TestCase(4, 3, true, 3)]
        [TestCase(10, 3, true, 3)]
        public void TrimToLimit_ReportsAndRemovesOverflow(int count, int limit, bool expectedTruncated, int expectedCount)
        {
            var files = Enumerable.Range(1, count).Select(i => "f" + i).ToList();

            bool truncated = VsSolutionFilesScanner.TrimToLimit(files, limit);

            Assert.That(truncated, Is.EqualTo(expectedTruncated));
            Assert.That(files, Has.Count.EqualTo(expectedCount));
            Assert.That(files, Is.EqualTo(Enumerable.Range(1, expectedCount).Select(i => "f" + i)), "order of kept files must be preserved");
        }

        [Test]
        public async Task EnumerateSolutionFiles_LimitPlusOneProbe_DetectsCapExactlyAtLimit()
        {
            // The tools enumerate with Limit = cap + 1: exactly 'cap' files must not be reported as truncated,
            // 'cap + 1' files must.
            for (int i = 1; i <= 4; i++)
            {
                var f = Path.Combine(_root, "P", $"file{i}.cs");
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllText(f, "x");
            }

            var atCap = (await CreateScanner().EnumerateSolutionFilesAsync(new EnumerateSolutionFilesFilter { ExtensionFilter = ".cs", Limit = 4 + 1 })).ToList();
            var overCap = (await CreateScanner().EnumerateSolutionFilesAsync(new EnumerateSolutionFilesFilter { ExtensionFilter = ".cs", Limit = 3 + 1 })).ToList();

            Assert.That(VsSolutionFilesScanner.TrimToLimit(atCap, 4), Is.False);
            Assert.That(atCap, Has.Count.EqualTo(4));
            Assert.That(VsSolutionFilesScanner.TrimToLimit(overCap, 3), Is.True);
            Assert.That(overCap, Has.Count.EqualTo(3));
        }

        private VsSolutionFilesScanner CreateScanner()
        {
            var provider = new TestFileProvider(_root);
            var dependencies = new TestVsDependencies(_root, provider);
            return new VsSolutionFilesScanner(dependencies, new TestUiThreadGuard(), new PathResolver(), provider);
        }



        private class TestFileProvider : ISolutionFileProvider
        {
            private readonly string _root;
            public TestFileProvider(string root) { _root = root; }
            public IEnumerable<string> GetFiles(Microsoft.VisualStudio.Shell.Interop.IVsSolution solution, bool includeProjects = false) => Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories);
        }

        private class TestVsDependencies : IVsDependencies
        {
            private readonly string _solutionDirectory;
            private readonly ISolutionFileProvider _fileProvider;
            private readonly Microsoft.VisualStudio.Shell.Interop.IVsSolution _solution;

#pragma warning disable CS0067
            public event Action SolutionOpened;
            public event Action SolutionClosed;
#pragma warning restore CS0067

            public bool IsSolutionOpen => true;

            public TestVsDependencies(string solutionDirectory, ISolutionFileProvider fileProvider = null)
            {
                _solutionDirectory = solutionDirectory;
                _fileProvider = fileProvider;
                // Create a mock IVsSolution
                var solutionMock = new Mock<Microsoft.VisualStudio.Shell.Interop.IVsSolution>();
                _solution = solutionMock.Object;
            }

            public Task InitializeAsync()
            {
                return Task.CompletedTask;
            }

            public string GetSolutionDirectory()
            {
                return _solutionDirectory;
            }

            public Microsoft.VisualStudio.Shell.Interop.IVsSolution GetSolution()
            {
                return _solution;
            }

            public DTE2 GetDTE()
            {
                return null;
            }


            public ISolutionFileProvider GetFileProvider()
            {
                return _fileProvider;
            }
        }
    }
}