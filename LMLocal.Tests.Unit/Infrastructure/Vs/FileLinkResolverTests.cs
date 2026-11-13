using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Vs
{
    [TestFixture]
    public class FileLinkResolverTests
    {
        private const string TargetLink = @"services\app.data.service.js";

        private string _root;
        private List<string> _opened;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "FileLinkResolverTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _opened = new List<string>();
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }

        [Test]
        public void NormalizeLink_ConvertsSeparatorsAndTrimsPrefixes()
        {
            Assert.That(FileLinkResolver.NormalizeLink("LMLocal/Resources/js/a.js"), Is.EqualTo(@"LMLocal\Resources\js\a.js"));
            Assert.That(FileLinkResolver.NormalizeLink(@".\LMLocal\a.js"), Is.EqualTo(@"LMLocal\a.js"));
            Assert.That(FileLinkResolver.NormalizeLink(@"\LMLocal\\a.js"), Is.EqualTo(@"LMLocal\a.js"));
        }

        [Test]
        public void NormalizeLink_StripsLocationSuffix()
        {
            Assert.That(FileLinkResolver.NormalizeLink(@"src\a.js:42"), Is.EqualTo(@"src\a.js"));
            Assert.That(FileLinkResolver.NormalizeLink(@"src\a.js:10-20"), Is.EqualTo(@"src\a.js"));
            Assert.That(FileLinkResolver.NormalizeLink(@"src\a.js:10–20"), Is.EqualTo(@"src\a.js"));
            Assert.That(FileLinkResolver.NormalizeLink(@"src\a.js::Foo.Bar"), Is.EqualTo(@"src\a.js"));
        }

        [Test]
        public void NormalizeLink_RejectsInvalidLinks()
        {
            Assert.That(FileLinkResolver.NormalizeLink(null), Is.Null);
            Assert.That(FileLinkResolver.NormalizeLink("   "), Is.Null);
            Assert.That(FileLinkResolver.NormalizeLink(@"src\*.js"), Is.Null, "wildcards are scanner patterns");
            Assert.That(FileLinkResolver.NormalizeLink(@"src\a?.js"), Is.Null);
            Assert.That(FileLinkResolver.NormalizeLink(@"..\..\secret.txt"), Is.Null);
            Assert.That(FileLinkResolver.NormalizeLink(@"src\..\a.js"), Is.Null);
            Assert.That(FileLinkResolver.NormalizeLink(new string('a', FileLinkResolver.MaxLinkLength + 1)), Is.Null);
        }

        [Test]
        public async Task OpenAsync_SolutionRelativePath_OpensDirectly()
        {
            string full = CreateFile(@"LMLocal\Resources\js\services\app.data.service.js");
            var resolver = CreateResolver();

            var result = await resolver.OpenAsync(@"LMLocal\Resources\js\services\app.data.service.js");

            Assert.That(result.Status, Is.EqualTo(FileLinkStatus.Opened));
            Assert.That(result.Via, Is.EqualTo("direct"));
            Assert.That(result.Path, Is.EqualTo(@"LMLocal\Resources\js\services\app.data.service.js"));
            Assert.That(_opened, Is.EqualTo(new[] { full }));
        }

        [Test]
        public async Task OpenAsync_AbsolutePathInsideSolution_OpensDirectly()
        {
            string full = CreateFile(@"LMLocal\Resources\js\services\app.data.service.js");
            var resolver = CreateResolver();

            var result = await resolver.OpenAsync(full);

            Assert.That(result.Status, Is.EqualTo(FileLinkStatus.Opened));
            Assert.That(result.Via, Is.EqualTo("direct"));
            Assert.That(_opened, Is.EqualTo(new[] { full }));
        }

        [Test]
        public async Task OpenAsync_AbsolutePathOutsideSolution_ReturnsNotFound()
        {
            CreateFile(@"LMLocal\App.cs");
            string outsideRoot = Path.Combine(Path.GetTempPath(), "FileLinkResolverTestsOutside", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outsideRoot);
            string outsideFile = Path.Combine(outsideRoot, "File.cs");
            File.WriteAllText(outsideFile, "x");

            try
            {
                var resolver = CreateResolver();

                var result = await resolver.OpenAsync(outsideFile);

                Assert.That(result.Status, Is.EqualTo(FileLinkStatus.NotFound));
                Assert.That(_opened, Is.Empty);
            }
            finally
            {
                try { Directory.Delete(outsideRoot, true); } catch { }
            }
        }

        [Test]
        public async Task OpenAsync_ParentSegment_ReturnsNotFoundWithoutOpening()
        {
            CreateFile(@"LMLocal\App.cs");
            var resolver = CreateResolver();

            var result = await resolver.OpenAsync(@"..\..\secret.txt");

            Assert.That(result.Status, Is.EqualTo(FileLinkStatus.NotFound));
            Assert.That(_opened, Is.Empty);
        }

        [Test]
        public async Task OpenAsync_SuffixOfRealPath_IsResolved()
        {
            string full = CreateFile(@"LMLocal\Resources\js\services\app.data.service.js");
            var resolver = CreateResolver();

            var result = await resolver.OpenAsync(TargetLink);

            Assert.That(result.Status, Is.EqualTo(FileLinkStatus.Opened));
            Assert.That(result.Via, Is.EqualTo("suffix"));
            Assert.That(result.Path, Is.EqualTo(@"LMLocal\Resources\js\services\app.data.service.js"));
            Assert.That(_opened, Is.EqualTo(new[] { full }));
        }

        [Test]
        public async Task OpenAsync_SuffixRespectsSegmentBoundary()
        {
            CreateFile(@"myservices\x.js");
            string expected = CreateFile(@"services\x.js");
            var resolver = CreateResolver();

            var result = await resolver.OpenAsync(@"services\x.js");

            Assert.That(result.Status, Is.EqualTo(FileLinkStatus.Opened));
            Assert.That(_opened, Is.EqualTo(new[] { expected }));
        }

        [Test]
        public async Task OpenAsync_ExcludedFolder_IsNotSearched()
        {
            CreateFile(@"bin\app.data.service.js");
            var resolver = CreateResolver();

            var result = await resolver.OpenAsync(TargetLink);

            Assert.That(result.Status, Is.EqualTo(FileLinkStatus.NotFound));
            Assert.That(_opened, Is.Empty);
        }

        [Test]
        public async Task OpenAsync_TwoSuffixMatches_ReturnsAmbiguousWithoutOpening()
        {
            CreateFile(@"A\Common\Util.cs");
            CreateFile(@"B\Common\Util.cs");
            var resolver = CreateResolver();

            var result = await resolver.OpenAsync(@"Common\Util.cs");

            Assert.That(result.Status, Is.EqualTo(FileLinkStatus.Ambiguous));
            Assert.That(result.Candidates, Is.EquivalentTo(new[] { @"A\Common\Util.cs", @"B\Common\Util.cs" }));
            Assert.That(_opened, Is.Empty);
        }

        [Test]
        public async Task OpenAsync_WrongDirectory_DoesNotFallBackToFileName()
        {
            CreateFile(@"LMLocal\Resources\js\services\app.data.service.js");
            var resolver = CreateResolver();

            var result = await resolver.OpenAsync(@"wrongdir\app.data.service.js");

            Assert.That(result.Status, Is.EqualTo(FileLinkStatus.NotFound));
            Assert.That(_opened, Is.Empty);
        }

        [Test]
        public async Task OpenAsync_BareFileNameWithSingleCandidate_IsResolved()
        {
            string full = CreateFile(@"LMLocal\Resources\js\services\app.data.service.js");
            var resolver = CreateResolver();

            var result = await resolver.OpenAsync("app.data.service.js");

            Assert.That(result.Status, Is.EqualTo(FileLinkStatus.Opened));
            Assert.That(_opened, Is.EqualTo(new[] { full }));
        }

        [Test]
        public async Task OpenAsync_BareFileNameWithTwoCandidates_ReturnsAmbiguous()
        {
            CreateFile(@"A\Util.cs");
            CreateFile(@"B\Util.cs");
            var resolver = CreateResolver();

            var result = await resolver.OpenAsync("Util.cs");

            Assert.That(result.Status, Is.EqualTo(FileLinkStatus.Ambiguous));
            Assert.That(_opened, Is.Empty);
        }

        [Test]
        public async Task OpenAsync_MoreThanMaxCandidates_TruncatesList()
        {
            for (int i = 0; i < FileLinkResolver.MaxCandidates + 2; i++)
                CreateFile($@"d{i}\Common\Util.cs");

            var resolver = CreateResolver();

            var result = await resolver.OpenAsync(@"Common\Util.cs");

            Assert.That(result.Status, Is.EqualTo(FileLinkStatus.Ambiguous));
            Assert.That(result.Candidates.Count, Is.EqualTo(FileLinkResolver.MaxCandidates));
            Assert.That(_opened, Is.Empty);
        }

        [Test]
        public async Task OpenAsync_NoSolution_ReturnsNotFound()
        {
            CreateFile(@"LMLocal\Resources\js\services\app.data.service.js");
            var resolver = CreateResolver(solutionOpen: false);

            var result = await resolver.OpenAsync(TargetLink);

            Assert.That(result.Status, Is.EqualTo(FileLinkStatus.NotFound));
            Assert.That(_opened, Is.Empty);
        }

        [Test]
        public async Task OpenAsync_FileSystemThrows_ReturnsErrorInsteadOfThrowing()
        {
            var fileSystem = new Mock<IFileSystem>();
            fileSystem
                .Setup(f => f.EnumerateDirectoryAsync(It.IsAny<string>(), It.IsAny<HashSet<string>>(), It.IsAny<System.Threading.CancellationToken>()))
                .Throws(new IOException("boom"));

            var resolver = CreateResolver(fileSystem: fileSystem.Object);

            var result = await resolver.OpenAsync(TargetLink);

            Assert.That(result.Status, Is.EqualTo(FileLinkStatus.Error));
            Assert.That(_opened, Is.Empty);
        }

        private FileLinkResolver CreateResolver(bool solutionOpen = true, IFileSystem fileSystem = null)
        {
            var dependencies = new Mock<IVsDependencies>();
            dependencies.SetupGet(d => d.IsSolutionOpen).Returns(solutionOpen);
            dependencies.Setup(d => d.GetSolutionDirectory()).Returns(_root);

            return new FileLinkResolver(
                dependencies.Object,
                new PathResolver(),
                fileSystem ?? new DefaultFileSystem(),
                path =>
                {
                    _opened.Add(path);
                    return Task.CompletedTask;
                });
        }

        private string CreateFile(string relativePath)
        {
            string full = Path.Combine(_root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, "x");
            return full;
        }
    }
}
