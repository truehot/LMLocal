using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using NUnit.Framework;

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
        public void EnumerateSolutionFiles_WithProjectFilter_ReturnsOnlyFilesFromMatchingProject()
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
            var scanner = new VsSolutionFilesScanner(dependencies, uiThreadGuard);

            // Act - Filter by ProjectB (case-insensitive substring match)
            var filter = new EnumerateSolutionFilesFilter
            {
                ExtensionFilter = ".cs",
                ProjectFilter = "ProjectB",
                ExcludeTemporaryDirectories = false
            };
            var results = scanner.EnumerateSolutionFiles(filter).ToList();

            // Assert
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0], Does.EndWith(Path.Combine("ProjectB", "sub", "c.cs")));
        }

        [Test]
        public void EnumerateSolutionFiles_WithProjectFilter_CaseInsensitive()
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
            var scanner = new VsSolutionFilesScanner(dependencies, uiThreadGuard);

            // Act - Use lowercase filter for "MyProject"
            var filter = new EnumerateSolutionFilesFilter
            {
                ExtensionFilter = ".cs",
                ProjectFilter = "myproject",
                ExcludeTemporaryDirectories = false
            };
            var results = scanner.EnumerateSolutionFiles(filter).ToList();

            // Assert
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0], Does.Contain("MyProject"));
        }

        //[Test]
        //public void EnumerateSolutionFiles_UsesInjectedProvider_FiltersAndLimitsAndReturnsRelative()
        //{
        //    // Arrange
        //    var fileA = Path.Combine(_root, "ProjectA", "a.cs");
        //    var fileB = Path.Combine(_root, "ProjectA", "b.txt");
        //    var fileC = Path.Combine(_root, "ProjectB", "sub", "c.cs");
        //    Directory.CreateDirectory(Path.GetDirectoryName(fileA));
        //    Directory.CreateDirectory(Path.GetDirectoryName(fileB));
        //    Directory.CreateDirectory(Path.GetDirectoryName(fileC));
        //    File.WriteAllText(fileA, "a");
        //    File.WriteAllText(fileB, "b");
        //    File.WriteAllText(fileC, "c");

        //    // Fake provider returns absolute file paths
        //    var provider = new TestFileProvider(_root);

        //    var scanner = new VsSolutionFilesScanner(_root, provider);

        //    // Act
        //    var results = scanner.EnumerateSolutionFiles(".cs", 2).ToList();

        //    // Assert
        //    Assert.That(results.Count, Is.EqualTo(2));
        //    Assert.That(results.Any(r => r.EndsWith(Path.Combine("ProjectA", "a.cs"))), Is.True);
        //    Assert.That(results.Any(r => r.EndsWith(Path.Combine("ProjectB", "sub", "c.cs"))), Is.True);
        //}

        private class TestFileProvider : ISolutionFileProvider
        {
            private readonly string _root;
            public TestFileProvider(string root) { _root = root; }
            public IEnumerable<string> GetFiles() => Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories);
        }

        private class TestVsDependencies : IVsDependencies
        {
            private readonly string _solutionDirectory;
            private readonly ISolutionFileProvider _fileProvider;

            public TestVsDependencies(string solutionDirectory, ISolutionFileProvider fileProvider = null)
            {
                _solutionDirectory = solutionDirectory;
                _fileProvider = fileProvider;
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
                return null;
            }

            public ISolutionFileProvider GetFileProvider()
            {
                return _fileProvider;
            }
        }
    }
}
