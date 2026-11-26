using System.Linq;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git.Parsers;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common.Git.Parsers
{
    [TestFixture]
    public class GitFileListParserTests
    {
        [Test]
        public void Parse_CombinesNumstatCountsWithNameStatus()
        {
            string counts = "12\t3\tsrc/A.cs\0-\t-\timg/logo.png\0";
            string names = "M\0src/A.cs\0M\0img/logo.png\0";

            var files = GitFileListParser.Parse(counts, names);

            Assert.That(files.Count, Is.EqualTo(2));
            Assert.That(files[0].Path, Is.EqualTo("src/A.cs"));
            Assert.That(files[0].Status, Is.EqualTo("modified"));
            Assert.That(files[0].Added, Is.EqualTo(12));
            Assert.That(files[0].Removed, Is.EqualTo(3));
            Assert.That(files[1].Binary, Is.True);
        }

        [Test]
        public void Parse_RenameHasOldAndNewPath()
        {
            // Shapes copied from real `git diff -M --numstat -z` / `--name-status -z` output (git 2.53).
            string counts = "3\t1\t\0src/Old.cs\0src/New.cs\0";
            string names = "R090\0src/Old.cs\0src/New.cs\0";

            var files = GitFileListParser.Parse(counts, names);

            Assert.That(files.Count, Is.EqualTo(1));
            Assert.That(files[0].Status, Is.EqualTo("renamed"));
            Assert.That(files[0].Path, Is.EqualTo("src/New.cs"));
            Assert.That(files[0].From, Is.EqualTo("src/Old.cs"));
            Assert.That(files[0].Added, Is.EqualTo(3));
            Assert.That(files[0].Removed, Is.EqualTo(1));
        }

        [Test]
        public void ParseNameStatus_HandlesCyrillicPaths()
        {
            var files = GitFileListParser.ParseNameStatus("A\0docs/описание.md\0");

            Assert.That(files.Single().Path, Is.EqualTo("docs/описание.md"));
            Assert.That(files.Single().Status, Is.EqualTo("added"));
        }
    }
}
