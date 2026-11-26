using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git.Parsers;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common.Git.Parsers
{
    [TestFixture]
    public class GitDiffParserTests
    {
        private const string Diff =
            "diff --git a/src/Foo.cs b/src/Foo.cs\n" +
            "index aaa..bbb 100644\n" +
            "--- a/src/Foo.cs\n" +
            "+++ b/src/Foo.cs\n" +
            "@@ -1,3 +1,4 @@\n" +
            " context\n" +
            "+added\n" +
            " more\n" +
            "@@ -10,2 +11,2 @@\n" +
            "-old\n" +
            "+new\n" +
            "diff --git a/img/logo.png b/img/logo.png\n" +
            "index aaa..bbb 100644\n" +
            "Binary files a/img/logo.png and b/img/logo.png differ\n" +
            "diff --git a/src/Old.cs b/src/New.cs\n" +
            "similarity index 100%\n" +
            "rename from src/Old.cs\n" +
            "rename to src/New.cs\n";

        [Test]
        public void Parse_ReadsHunksCountsBinaryAndRename()
        {
            var data = GitDiffParser.Parse(Diff, 50, 2000);

            Assert.That(data.Files.Count, Is.EqualTo(3));
            Assert.That(data.Files[0].Path, Is.EqualTo("src/Foo.cs"));
            Assert.That(data.Files[0].Hunks.Count, Is.EqualTo(2));
            Assert.That(data.Files[0].Hunks[0], Does.StartWith("@@ -1,3 +1,4 @@"));
            Assert.That(data.Files[0].Added, Is.EqualTo(2));
            Assert.That(data.Files[0].Removed, Is.EqualTo(1));
            Assert.That(data.Files[1].Binary, Is.True);
            Assert.That(data.Files[2].Status, Is.EqualTo("renamed"));
            Assert.That(data.Files[2].From, Is.EqualTo("src/Old.cs"));
            Assert.That(data.Files[2].Path, Is.EqualTo("src/New.cs"));
            Assert.That(data.HasMoreResults, Is.False);
        }

        [Test]
        public void Parse_CapsByWholeHunks()
        {
            var data = GitDiffParser.Parse(Diff, 50, 4);

            Assert.That(data.Files[0].Hunks.Count, Is.EqualTo(1));
            Assert.That(data.Files[0].Added, Is.EqualTo(1));
            Assert.That(data.Files[0].Removed, Is.EqualTo(0));
            Assert.That(data.HasMoreResults, Is.True);
        }

        [Test]
        public void Parse_CapsByFiles()
        {
            var data = GitDiffParser.Parse(Diff, 2, 2000);

            Assert.That(data.Files.Count, Is.EqualTo(2));
            Assert.That(data.HasMoreResults, Is.True);
        }

        [Test]
        public void Parse_HandlesCyrillicPath()
        {
            string diff = "diff --git a/docs/описание.md b/docs/описание.md\n" +
                          "--- a/docs/описание.md\n" +
                          "+++ b/docs/описание.md\n" +
                          "@@ -1 +1 @@\n" +
                          "-старое\n" +
                          "+новое\n";

            var data = GitDiffParser.Parse(diff, 50, 2000);

            Assert.That(data.Files.Count, Is.EqualTo(1));
            Assert.That(data.Files[0].Path, Is.EqualTo("docs/описание.md"));
        }
    }
}
