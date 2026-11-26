using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git.Parsers;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common.Git.Parsers
{
    [TestFixture]
    public class GitBlameParserTests
    {
        [Test]
        public void Parse_GroupsConsecutiveLinesAndReportsUncommitted()
        {
            string output =
                "23e8a40111111111111111111111111111111111 10 10 5\n" +
                "author Alice\n" +
                "author-time 1763900000\n" +
                "summary fix parser\n" +
                "\tline ten\n" +
                "23e8a40111111111111111111111111111111111 11 11 4\n" +
                "\tline eleven\n" +
                "0000000000000000000000000000000000000000 15 15 1\n" +
                "author Not Committed Yet\n" +
                "\tline fifteen\n";

            var data = GitBlameParser.Parse(output);

            Assert.That(data.Ranges.Count, Is.EqualTo(1));
            Assert.That(data.Ranges[0].StartLine, Is.EqualTo(10));
            Assert.That(data.Ranges[0].EndLine, Is.EqualTo(11));
            Assert.That(data.Ranges[0].Sha, Is.EqualTo("23e8a40111111111111111111111111111111111"));
            Assert.That(data.Ranges[0].Author, Is.EqualTo("Alice"));
            Assert.That(data.Ranges[0].Summary, Is.EqualTo("fix parser"));
            Assert.That(data.Ranges[0].Date, Does.StartWith("2025-"));
            Assert.That(data.UncommittedLines, Is.EqualTo(new[] { 15 }));
            Assert.That(data.Boundary, Is.False);
        }

        [Test]
        public void Parse_MarksBoundaryCommits()
        {
            string output =
                "boundary\n" +
                "aaaaaaaa11111111111111111111111111111111 1 1 1\n" +
                "author Old\n" +
                "\tline\n";

            var data = GitBlameParser.Parse(output);

            Assert.That(data.Boundary, Is.True);
            Assert.That(data.Ranges.Count, Is.EqualTo(1));
        }
    }
}
