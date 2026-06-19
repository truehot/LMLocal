using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

            public ISolutionFileProvider GetFileProvider()
            {
                return _fileProvider;
            }
        }
    }
}
