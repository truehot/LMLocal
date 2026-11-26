using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git.Parsers;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common.Git.Parsers
{
    [TestFixture]
    public class GitStatusParserTests
    {
        // Record shapes are copied from real `git status --porcelain=v2 --branch -z` output (git 2.53).
        [Test]
        public void Parse_ReadsBranchAheadBehindAndAllFileCategories()
        {
            string output =
                "# branch.oid 863b43a071db54ba3e6973b1e77b81961a8c4014\0" +
                "# branch.head main\0" +
                "# branch.upstream origin/main\0" +
                "# branch.ab +1 -2\0" +
                "1 M. N... 100644 100644 100644 587be6b4c3f93f93c489c0111bba5596147a26cb b77b4eb1d946f923f61785536da9ca5af6909f06 src/A.cs\0" +
                "1 .M N... 100644 100644 100644 587be6b4c3f93f93c489c0111bba5596147a26cb 587be6b4c3f93f93c489c0111bba5596147a26cb src/B.cs\0" +
                "? src/C.cs\0" +
                "u UU N... 100644 100644 100644 100644 df967b96a579e45a18b8251732d16804b2e56a55 b19a1e93bec1317dc6097229e12afaffbfa74dc2 950b81b7eee953d050aa05a641f8e056c85dd1bd src/D.cs\0" +
                "2 R. N... 100644 100644 100644 940532533944dd159bfd11136fac2ee35872de38 940532533944dd159bfd11136fac2ee35872de38 R100 src/New File.cs\0src/Old.cs\0" +
                "1 .M SC.. 160000 160000 160000 aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa ext/lib\0" +
                "! ignored.log\0";

            var data = GitStatusParser.Parse(output);

            Assert.That(data.Branch, Is.EqualTo("main"));
            Assert.That(data.Upstream, Is.EqualTo("origin/main"));
            Assert.That(data.Ahead, Is.EqualTo(1));
            Assert.That(data.Behind, Is.EqualTo(2));
            Assert.That(data.Staged.Count, Is.EqualTo(2));
            Assert.That(data.Staged[0].Path, Is.EqualTo("src/A.cs"));
            Assert.That(data.Staged[0].Status, Is.EqualTo("modified"));
            Assert.That(data.Staged[1].Path, Is.EqualTo("src/New File.cs"));
            Assert.That(data.Staged[1].Status, Is.EqualTo("renamed"));
            Assert.That(data.Unstaged.Count, Is.EqualTo(2));
            Assert.That(data.Untracked, Is.EqualTo(new[] { "src/C.cs" }));
            Assert.That(data.Conflicted.Count, Is.EqualTo(1));
            Assert.That(data.Conflicted[0].Path, Is.EqualTo("src/D.cs"));
            Assert.That(data.Conflicted[0].Kind, Is.EqualTo("both_modified"));
            Assert.That(data.Renamed.Count, Is.EqualTo(1));
            Assert.That(data.Renamed[0].From, Is.EqualTo("src/Old.cs"));
            Assert.That(data.Renamed[0].To, Is.EqualTo("src/New File.cs"));
            Assert.That(data.Renamed[0].Staged, Is.True);
            Assert.That(data.Submodules.Count, Is.EqualTo(1));
            Assert.That(data.Submodules[0].Path, Is.EqualTo("ext/lib"));
            Assert.That(data.Submodules[0].Changed, Is.True);
        }

        [Test]
        public void Parse_RenameIsNotASubmodule_AndPlainFilesAreNotSubmodules()
        {
            var data = GitStatusParser.Parse(
                "2 R. N... 100644 100644 100644 9405325 9405325 R100 new.txt\0old.txt\0" +
                "1 M. N... 100644 100644 100644 587be6b b77b4eb keep.txt\0");

            Assert.That(data.Submodules, Is.Empty);
            Assert.That(data.Renamed[0].To, Is.EqualTo("new.txt"));
            Assert.That(data.Staged.Count, Is.EqualTo(2));
        }

        [TestCase("AA", "both_added")]
        [TestCase("DD", "both_deleted")]
        [TestCase("AU", "added_by_us")]
        [TestCase("UA", "added_by_them")]
        [TestCase("DU", "deleted_by_us")]
        [TestCase("UD", "deleted_by_them")]
        public void Parse_ConflictKindsUseUppercaseCodes(string xy, string kind)
        {
            var data = GitStatusParser.Parse($"u {xy} N... 100644 100644 100644 100644 a b c f.txt\0");

            Assert.That(data.Conflicted[0].Kind, Is.EqualTo(kind));
        }

        [Test]
        public void Parse_DetachedHeadHasNoBranch()
        {
            var data = GitStatusParser.Parse("# branch.head (detached)\n# branch.ab +0 -0\n");

            Assert.That(data.Branch, Is.Null);
            Assert.That(data.Ahead, Is.EqualTo(0));
            Assert.That(data.Behind, Is.EqualTo(0));
        }

        [Test]
        public void Parse_EmptyRepositoryHasNoFileEntries()
        {
            var data = GitStatusParser.Parse("# branch.oid (initial)\n# branch.head main\n");

            Assert.That(data.Staged, Is.Empty);
            Assert.That(data.Untracked, Is.Empty);
        }
    }
}
