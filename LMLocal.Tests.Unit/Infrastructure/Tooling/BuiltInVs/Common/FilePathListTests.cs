using System.Collections.Generic;
using System.Linq;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common
{
    [TestFixture]
    public class FilePathListTests
    {
        private static List<string> Parse(Dictionary<string, object> parameters)
        {
            Assert.That(FilePathList.TryParse(parameters, out var paths, out var error), Is.True, error);
            return paths;
        }

        [Test]
        public void SinglePath_IsReturnedAsIs()
        {
            var paths = Parse(new Dictionary<string, object> { ["file_path"] = @"A\B.cs" });

            Assert.That(paths, Is.EqualTo(new[] { @"A\B.cs" }));
        }

        [Test]
        public void CommaSeparatedFilePath_IsSplitAndTrimmed()
        {
            var paths = Parse(new Dictionary<string, object> { ["file_path"] = @" A.cs ,Tests\B.cs,  C.cs " });

            Assert.That(paths, Is.EqualTo(new[] { "A.cs", @"Tests\B.cs", "C.cs" }));
        }

        [Test]
        public void FilePathsArray_FromJson_IsRead()
        {
            var paths = Parse(new Dictionary<string, object> { ["file_paths"] = JArray.Parse("[\"A.cs\", \"B.cs\"]") });

            Assert.That(paths, Is.EqualTo(new[] { "A.cs", "B.cs" }));
        }

        [Test]
        public void FilePathsArray_AsClrList_IsRead()
        {
            var paths = Parse(new Dictionary<string, object> { ["file_paths"] = new List<object> { "A.cs", "B.cs" } });

            Assert.That(paths, Is.EqualTo(new[] { "A.cs", "B.cs" }));
        }

        [Test]
        public void ArrayPassedAsJsonString_IsRead()
        {
            var paths = Parse(new Dictionary<string, object> { ["file_paths"] = "[\"A.cs\", \"B.cs\"]" });

            Assert.That(paths, Is.EqualTo(new[] { "A.cs", "B.cs" }));
        }

        [Test]
        public void ArrayPassedInFilePath_IsRead()
        {
            var paths = Parse(new Dictionary<string, object> { ["file_path"] = JArray.Parse("[\"A.cs\", \"B.cs\"]") });

            Assert.That(paths, Is.EqualTo(new[] { "A.cs", "B.cs" }));
        }

        [Test]
        public void BothParameters_AreCombinedInOrderWithoutDuplicates()
        {
            var paths = Parse(new Dictionary<string, object>
            {
                ["file_path"] = "A.cs, b.cs",
                ["file_paths"] = JArray.Parse("[\"B.cs\", \"C.cs\"]")
            });

            Assert.That(paths, Is.EqualTo(new[] { "A.cs", "b.cs", "C.cs" }), "duplicates are compared case-insensitively");
        }

        [Test]
        public void QuotesAndEmptyEntries_AreDropped()
        {
            var paths = Parse(new Dictionary<string, object> { ["file_path"] = "\"A.cs\", , 'B.cs'," });

            Assert.That(paths, Is.EqualTo(new[] { "A.cs", "B.cs" }));
        }

        [Test]
        public void PathWithCommaThatExists_IsKeptWhole()
        {
            var parameters = new Dictionary<string, object> { ["file_path"] = @" Docs\Notes, draft.md " };

            Assert.That(FilePathList.TryParse(parameters, out var paths, out var error, p => p == @"Docs\Notes, draft.md"), Is.True, error);
            Assert.That(paths, Is.EqualTo(new[] { @"Docs\Notes, draft.md" }));
        }

        [Test]
        public void PathWithCommaThatDoesNotExist_IsSplit()
        {
            var parameters = new Dictionary<string, object> { ["file_path"] = "A.cs, B.cs" };

            Assert.That(FilePathList.TryParse(parameters, out var paths, out var error, p => false), Is.True, error);
            Assert.That(paths, Is.EqualTo(new[] { "A.cs", "B.cs" }));
        }

        [Test]
        public void ExistenceCheckThatThrows_FallsBackToSplitting()
        {
            var parameters = new Dictionary<string, object> { ["file_path"] = "A.cs, B.cs" };

            Assert.That(FilePathList.TryParse(parameters, out var paths, out var error,
                p => throw new System.IO.IOException("boom")), Is.True, error);
            Assert.That(paths, Is.EqualTo(new[] { "A.cs", "B.cs" }));
        }

        [Test]
        public void ExistenceCheck_IsNotCalledWithoutComma()
        {
            int calls = 0;
            var parameters = new Dictionary<string, object> { ["file_path"] = "A.cs" };

            Assert.That(FilePathList.TryParse(parameters, out var paths, out _, p => { calls++; return true; }), Is.True);
            Assert.That(paths, Is.EqualTo(new[] { "A.cs" }));
            Assert.That(calls, Is.EqualTo(0));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" , ")]
        public void NoPath_IsAnError(string value)
        {
            var parameters = new Dictionary<string, object> { ["file_path"] = value };

            Assert.That(FilePathList.TryParse(parameters, out var paths, out var error), Is.False);
            Assert.That(paths, Is.Empty);
            Assert.That(error, Does.Contain("file_path"));
        }

        [Test]
        public void NullParameters_IsAnError()
        {
            Assert.That(FilePathList.TryParse(null, out _, out var error), Is.False);
            Assert.That(error, Is.Not.Null);
        }

        [Test]
        public void TooManyPaths_IsAnError()
        {
            string many = string.Join(",", Enumerable.Range(0, FilePathList.MaxFiles + 1).Select(i => $"F{i}.cs"));

            Assert.That(FilePathList.TryParse(new Dictionary<string, object> { ["file_path"] = many }, out var paths, out var error), Is.False);
            Assert.That(paths, Is.Empty);
            Assert.That(error, Does.Contain(FilePathList.MaxFiles.ToString()));
        }
    }
}
