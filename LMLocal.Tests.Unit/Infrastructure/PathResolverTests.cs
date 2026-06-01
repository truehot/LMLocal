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

        [Test]
        public void TryGetRelativePath_ReturnsFalseForSiblingFolder()
        {
            var resolver = new PathResolver();

            var temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var basePath = Path.GetFullPath(Path.Combine(temp, "Folder"));
            var siblingFile = Path.GetFullPath(Path.Combine(temp, "Folder2", "file.txt"));

            Assert.That(resolver.TryGetRelativePath(siblingFile, basePath, out string rel), Is.False);
            Assert.That(rel, Is.Null);
        }

        [Test]
        public void TryGetRelativePath_SamePath_ReturnsDot()
        {
            var resolver = new PathResolver();

            var temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var basePath = Path.GetFullPath(Path.Combine(temp, "FolderSame"));
            var samePath = Path.GetFullPath(basePath);

            Assert.That(resolver.TryGetRelativePath(samePath, basePath, out string rel), Is.True);
            Assert.That(rel, Is.EqualTo("."));
        }

        [Test]
        public void TryGetRelativeNormalizedPath_ReturnsRelativeForChild()
        {
            var resolver = new PathResolver();

            var temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var basePath = Path.GetFullPath(Path.Combine(temp, "BaseDirNorm"));
            var filePath = Path.GetFullPath(Path.Combine(basePath, "sub", "file.txt"));

            Assert.That(resolver.TryGetRelativeNormalizedPath(filePath, basePath, out string rel), Is.True);
            Assert.That(rel, Is.EqualTo(Path.Combine("sub", "file.txt")));
        }

        [Test]
        public void TryGetRelativeNormalizedPath_SamePath_ReturnsDot()
        {
            var resolver = new PathResolver();

            var temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var basePath = Path.GetFullPath(Path.Combine(temp, "BaseSame"));
            var samePath = Path.GetFullPath(basePath);

            Assert.That(resolver.TryGetRelativeNormalizedPath(samePath, basePath, out string rel), Is.True);
            Assert.That(rel, Is.EqualTo("."));
        }

        [Test]
        public void TryGetRelativeNormalizedPath_FailsForSiblingPrefix()
        {
            var resolver = new PathResolver();

            var temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var basePath = Path.GetFullPath(Path.Combine(temp, "MyFolder"));
            var sibling = Path.GetFullPath(Path.Combine(temp, "MyFolder2", "file.txt"));

            Assert.That(resolver.TryGetRelativeNormalizedPath(sibling, basePath, out string rel), Is.False);
            Assert.That(rel, Is.Null);
        }

        [Test]
        public void TryGetRelativeNormalizedPath_TrailingSlash_Normalizes()
        {
            var resolver = new PathResolver();

            var temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var basePath = Path.GetFullPath(Path.Combine(temp, "BaseTrail"));
            var baseWithSlash = basePath + Path.DirectorySeparatorChar;
            var file = Path.GetFullPath(Path.Combine(basePath, "file.txt"));

            Assert.That(resolver.TryGetRelativeNormalizedPath(file, baseWithSlash, out string rel), Is.True);
            Assert.That(rel, Is.EqualTo("file.txt"));
        }

        [Test]
        public void IsPathInsideDirectory_SamePath_ReturnsTrue()
        {
            var resolver = new PathResolver();

            var temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var dir = Path.GetFullPath(Path.Combine(temp, "SameFolderTest"));
            var same = Path.GetFullPath(dir);

            Assert.That(resolver.IsPathInsideDirectory(same, dir), Is.True);
        }

        [Test]
        public void TryGetRelativeNormalizedPath_ReturnsFalseForNullOrEmpty()
        {
            var resolver = new PathResolver();

            Assert.That(resolver.TryGetRelativeNormalizedPath(null, Path.GetFullPath(Path.GetTempPath()), out string r1), Is.False);
            Assert.That(r1, Is.Null);

            Assert.That(resolver.TryGetRelativeNormalizedPath(Path.GetFullPath(Path.GetTempPath()), null, out string r2), Is.False);
            Assert.That(r2, Is.Null);

            Assert.That(resolver.TryGetRelativeNormalizedPath(string.Empty, string.Empty, out string r3), Is.False);
            Assert.That(r3, Is.Null);
        }

        [Test]
        public void TryGetRelativePath_TrailingSlash_Normalizes()
        {
            var resolver = new PathResolver();

            var temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var basePath = Path.GetFullPath(Path.Combine(temp, "FolderTrail"));
            var baseWithSlash = basePath + Path.DirectorySeparatorChar;
            var file = Path.GetFullPath(Path.Combine(basePath, "file.txt"));

            Assert.That(resolver.TryGetRelativePath(file, baseWithSlash, out string rel), Is.True);
            Assert.That(rel, Is.EqualTo("file.txt"));
        }

        [Test]
        public void TryGetRelativePath_NormalizesParentSegments()
        {
            var resolver = new PathResolver();

            var temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var basePath = Path.GetFullPath(Path.Combine(temp, "FolderNorm"));
            // construct a path with parent segments that resolves to FolderNorm\file.txt
            var fileWithParents = Path.GetFullPath(Path.Combine(basePath, "..", "FolderNorm", "file.txt"));

            Assert.That(resolver.TryGetRelativePath(fileWithParents, basePath, out string rel), Is.True);
            Assert.That(rel, Is.EqualTo("file.txt"));
        }
    }
}
