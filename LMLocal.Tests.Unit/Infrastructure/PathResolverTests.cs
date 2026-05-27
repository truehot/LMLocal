using System;
using System.IO;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class PathResolverTests
    {
        [Test]
        public void IsPathInsideDirectory_ReturnsTrueForChildAndFalseForSiblingPrefix()
        {
            var temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var dir = Path.GetFullPath(Path.Combine(temp, "MyFolder"));
            var inside = Path.GetFullPath(Path.Combine(dir, "file.txt"));
            var sibling = Path.GetFullPath(Path.Combine(temp, "MyFolder2", "file.txt"));

            var resolver = new PathResolver();

            Assert.That(resolver.IsPathInsideDirectory(inside, dir), Is.True);
            Assert.That(resolver.IsPathInsideDirectory(sibling, dir), Is.False);
        }

        [Test]
        public void IsPathInsideDirectory_WorksWhenDirectoryHasTrailingSeparator()
        {
            var temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var dir = Path.GetFullPath(Path.Combine(temp, "Folder"));
            var dirWithSep = dir + Path.DirectorySeparatorChar;
            var file = Path.GetFullPath(Path.Combine(dir, "f.txt"));

            var resolver = new PathResolver();
            Assert.That(resolver.IsPathInsideDirectory(file, dirWithSep), Is.True);
            Assert.That(resolver.IsPathInsideDirectory(file, dir), Is.True);
        }

        [Test]
        public void TryResolveFilePath_RootedAndRelativeBehaveAsExpected()
        {
            var resolver = new PathResolver();

            // Rooted path with solutionDir (solutionDir is ignored for rooted paths)
            var abs = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "a.txt"));
            var solutionDir1 = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ignored"));
            Assert.That(resolver.TryResolveFilePath(abs, solutionDir1, out string resolvedAbs), Is.True);
            Assert.That(resolvedAbs, Is.EqualTo(abs));

            // Relative with solutionDir
            var solutionDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "solroot"));
            var rel = Path.Combine("sub", "file.txt");
            Assert.That(resolver.TryResolveFilePath(rel, solutionDir, out string resolvedRel), Is.True);
            Assert.That(resolvedRel, Is.EqualTo(Path.GetFullPath(Path.Combine(solutionDir, rel))));

            // Relative without solutionDir -> false
            Assert.That(resolver.TryResolveFilePath(rel, null, out string _), Is.False);
        }

        [Test]
        public void TryGetRelativePath_ReturnsRelativeForChild_AndFalseForDifferentRoots()
        {
            var resolver = new PathResolver();

            var basePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "proj", "dir"));
            var filePath = Path.GetFullPath(Path.Combine(basePath, "sub", "file.txt"));

            Assert.That(resolver.TryGetRelativePath(filePath, basePath, out string rel));
            Assert.That(rel, Is.EqualTo(Path.Combine("sub", "file.txt")));

            // Different roots -> false
            var baseRootForTest = Path.GetPathRoot(basePath);
            var altRoot = baseRootForTest.StartsWith("C:", StringComparison.OrdinalIgnoreCase) ? "D:\\" : "C:\\";
            var other = Path.Combine(altRoot, "other", "file.txt");
            Assert.That(resolver.TryGetRelativePath(other, basePath, out string rel2), Is.False);
            Assert.That(rel2, Is.Null);
        }
    }
}
