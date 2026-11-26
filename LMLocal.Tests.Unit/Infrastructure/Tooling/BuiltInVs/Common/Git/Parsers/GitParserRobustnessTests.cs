using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git.Parsers;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common.Git.Parsers
{
    [TestFixture]
    public class GitParserRobustnessTests
    {
        [Test]
        public void StatusParser_DoesNotThrowOnMalformedRecords()
        {
            var data = GitStatusParser.Parse("garbage\0# branch.head main\n1 broken\0? \0u short\0");

            Assert.That(data.Branch, Is.EqualTo("main"));
            Assert.That(data.Staged, Is.Empty);
            Assert.That(data.Conflicted, Is.Empty);
        }

        [Test]
        public void FileListParser_DoesNotThrowOnMalformedRecords()
        {
            var files = GitFileListParser.Parse("bad\0-\t-\0", "X\0only-status\0R100\0only-new\0");

            Assert.That(files.Count, Is.EqualTo(2));
            Assert.That(files[0].Path, Is.EqualTo("only-status"));
            Assert.That(files[1].Path, Is.EqualTo("only-new"));
        }

        [Test]
        public void LogParser_DoesNotThrowOnTruncatedRecord()
        {
            var commits = GitLogParser.Parse("not a commit\u001e\nshort\u001fname\u001e\n", true);

            Assert.That(commits, Is.Empty);
        }

        [Test]
        public void BlameParser_DoesNotThrowOnMalformedHeader()
        {
            var data = GitBlameParser.Parse("garbage\naaaa 1\naaaa 1 x y\n\tsome text\n");

            Assert.That(data.Ranges, Is.Empty);
        }

        [Test]
        public void DiffParser_KeepsTruncatedHunkWithoutCrashing()
        {
            var data = GitDiffParser.Parse(
                "diff --git a/x.cs b/x.cs\n--- a/x.cs\n+++ b/x.cs\n@@ -1 +1 @@\n+unfinished", 50, 2000);

            Assert.That(data.Files.Count, Is.EqualTo(1));
            Assert.That(data.Files[0].Hunks.Count, Is.EqualTo(1));
            Assert.That(data.Files[0].Hunks[0], Does.Contain("+unfinished"));
        }
    }
}
