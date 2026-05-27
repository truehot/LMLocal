using System.IO;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class PathHelperTests
    {
        [Test]
        public void TryGetRelativePath_ReturnsRelativePath_WhenChildPath()
        {
            var basePath = Path.GetFullPath(Path.Combine("base", "dir"));
            var filePath = Path.GetFullPath(Path.Combine("base", "dir", "sub", "file.txt"));

            var resolver = new PathResolver();
            var ok = resolver.TryGetRelativePath(filePath, basePath, out string relative);

            var expected = Path.Combine("sub", "file.txt");
            Assert.That(ok, Is.True);
            Assert.That(relative, Is.EqualTo(expected));
        }

        [Test]
        public void IsPathInsideDirectory_ReturnsTrueForDirectChild_AndFalseForSiblingPrefix()
        {
            var dir = Path.GetFullPath("MyFolder");
            var inside = Path.GetFullPath(Path.Combine("MyFolder", "file.txt"));
            var sibling = Path.GetFullPath(Path.Combine("MyFolder2", "file.txt"));

            var resolver = new PathResolver();
            Assert.That(resolver.IsPathInsideDirectory(inside, dir), Is.True);
            Assert.That(resolver.IsPathInsideDirectory(sibling, dir), Is.False);
        }

        [Test]
        public void TryGetRelativePath_ReturnsFalseWhenDifferentRoots()
        {
            var currentRoot = Path.GetPathRoot(Path.GetFullPath("."));
            var altRoot = currentRoot.StartsWith("C:", System.StringComparison.OrdinalIgnoreCase) ? "D:\\" : "C:\\";

            var basePath = Path.Combine(currentRoot, "base", "dir");
            var filePath = Path.Combine(altRoot, "other", "file.txt");

            var resolver = new PathResolver();
            var ok = resolver.TryGetRelativePath(filePath, basePath, out string relative);
            Assert.That(ok, Is.False);
            Assert.That(relative, Is.Null);
        }
    }
}
