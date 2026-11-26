using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git.Parsers;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common.Git.Parsers
{
    // Shapes copied from real `git log -z --format=%H%x00%h%x00%an%x00%aI%x00%s [--name-status]` output (git 2.53).
    [TestFixture]
    public class GitLogParserTests
    {
        private static readonly string Sha1 = "634a8ae" + new string('1', 33);
        private static readonly string Sha2 = "863b43a" + new string('2', 33);

        private static string Header(string sha, string author, string date, string subject)
        {
            return sha + "\0" + sha.Substring(0, 7) + "\0" + author + "\0" + date + "\0" + subject + "\0";
        }

        [Test]
        public void Parse_ReadsCommitsWithoutFiles()
        {
            string output =
                Header(Sha2, "Bob", "2026-11-24T10:00:00+02:00", "fix: кириллица") +
                Header(Sha1, "Alice", "2026-11-23T14:37:44+02:00", "v2 pipelines");

            var commits = GitLogParser.Parse(output, false);

            Assert.That(commits.Count, Is.EqualTo(2));
            Assert.That(commits[0].Sha, Is.EqualTo(Sha2));
            Assert.That(commits[0].Short, Is.EqualTo("863b43a"));
            Assert.That(commits[0].Author, Is.EqualTo("Bob"));
            Assert.That(commits[0].Date, Is.EqualTo("2026-11-24T10:00:00+02:00"));
            Assert.That(commits[0].Subject, Is.EqualTo("fix: кириллица"));
            Assert.That(commits[0].Files, Is.Null);
            Assert.That(commits[1].Subject, Is.EqualTo("v2 pipelines"));
        }

        [Test]
        public void Parse_NameStatusWithRename_AttachesFilesToTheirCommit()
        {
            string output =
                Header(Sha2, "t", "2026-11-25T17:58:35+02:00", "rename") + "\nM\0keep.txt\0R100\0old.txt\0new.txt\0" +
                Header(Sha1, "t", "2026-11-25T17:56:46+02:00", "init") + "\nA\0keep.txt\0A\0old.txt\0";

            var commits = GitLogParser.Parse(output, true);

            Assert.That(commits.Count, Is.EqualTo(2));
            Assert.That(commits[0].Files.Count, Is.EqualTo(2));
            Assert.That(commits[0].Files[1].Status, Is.EqualTo("renamed"));
            Assert.That(commits[0].Files[1].Path, Is.EqualTo("new.txt"));
            Assert.That(commits[0].Files[1].From, Is.EqualTo("old.txt"));
            Assert.That(commits[1].Files.Count, Is.EqualTo(2));
            Assert.That(commits[1].Files[1].Path, Is.EqualTo("old.txt"));
        }

        [Test]
        public void Parse_SubjectWithSeparatorsAndFakeFields_DoesNotForgeACommit()
        {
            // A real commit subject may contain 0x1E/0x1F; it cannot contain NUL (git cuts the message there).
            string forged = "harmless fix\u001e" + new string('d', 40) + "\u001fddddddd\u001fLinus Torvalds\u001f2001-01-01T00:00:00+00:00\u001fApproved";

            var commits = GitLogParser.Parse(Header(Sha1, "Alice", "2026-11-25T18:24:40+02:00", forged), false);

            Assert.That(commits.Count, Is.EqualTo(1));
            Assert.That(commits[0].Author, Is.EqualTo("Alice"));
            Assert.That(commits[0].Subject, Is.EqualTo(forged));
        }

        [Test]
        public void Parse_EmptySubjectKeepsFieldPositions()
        {
            var commits = GitLogParser.Parse(Header(Sha1, "Alice", "2026-11-25T18:24:40+02:00", "") + Header(Sha2, "Bob", "d", "next"), false);

            Assert.That(commits.Count, Is.EqualTo(2));
            Assert.That(commits[0].Subject, Is.EqualTo(string.Empty));
            Assert.That(commits[1].Author, Is.EqualTo("Bob"));
        }

        [Test]
        public void Parse_RecordCutByTheOutputLimit_IsDropped()
        {
            var commits = GitLogParser.Parse(Header(Sha1, "Alice", "d", "one") + Sha2 + "\0863b43a\0Bo", false);

            Assert.That(commits.Count, Is.EqualTo(1));
            Assert.That(commits[0].Sha, Is.EqualTo(Sha1));
        }
    }
}
