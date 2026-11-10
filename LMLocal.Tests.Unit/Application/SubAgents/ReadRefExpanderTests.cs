using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.SubAgents.ReadRefs;
using LMLocal.Tests.Unit.Infrastructure;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.SubAgents
{
    [TestFixture]
    public class ReadRefExpanderTests
    {
        private const string RelativePath = @"src\Program.cs";
        private const string AbsolutePath = @"C:\sol\src\Program.cs";

        private static ReadRefExpander CreateExpander(ReadLedger ledger, InMemoryFileSystem fileSystem)
        {
            return new ReadRefExpander(ledger, fileSystem);
        }

        private static void WriteFile(InMemoryFileSystem fileSystem, int lineCount)
        {
            var sb = new StringBuilder();
            for (int i = 1; i <= lineCount; i++)
            {
                if (i > 1)
                    sb.Append('\n');
                sb.Append("line " + i);
            }

            fileSystem.WriteAllBytesAsync(AbsolutePath, Encoding.UTF8.GetBytes(sb.ToString())).Wait();
        }

        [Test]
        public async Task Expand_LedgeredMarker_IsReplacedWithRenderedContent()
        {
            var fileSystem = new InMemoryFileSystem();
            WriteFile(fileSystem, 10);

            var ledger = new ReadLedger();
            ledger.Add(new ReadRecord(RelativePath, 1, 10, AbsolutePath));

            var expander = CreateExpander(ledger, fileSystem);
            var result = await expander.ExpandAsync("Before\n@src\\Program.cs:1-10\nAfter", CancellationToken.None);

            Assert.That(result.ExpandedRefCount, Is.EqualTo(1));
            Assert.That(result.UnresolvedRefCount, Is.EqualTo(0));
            Assert.That(result.MalformedRefCount, Is.EqualTo(0));
            Assert.That(result.Content, Does.Contain("File: src\\Program.cs, lines 1-10"));
            Assert.That(result.Content, Does.Contain("````csharp\nline 1\n"));
            Assert.That(result.Content, Does.Contain("line 10\n````"));
            Assert.That(result.Content, Does.Not.Contain("1: line 1"));
            Assert.That(result.Content, Does.Not.Contain("@src\\Program.cs:1-10"));
            Assert.That(result.Content, Does.StartWith("Before"));
            Assert.That(result.Content, Does.EndWith("After"));
            Assert.That(result.Content, Does.Contain("````csharp"));
        }

        [TestCase(@"@C:\sol\src\Program.cs:2-4")]
        [TestCase("@C:/sol/src/Program.cs:2-4")]
        [TestCase(@"`@c:\SOL\src\Program.cs:2-4`")]
        public async Task Expand_AbsolutePathMarker_IsResolvedLikeRelative(string marker)
        {
            var fileSystem = new InMemoryFileSystem();
            WriteFile(fileSystem, 10);

            var ledger = new ReadLedger();
            ledger.Add(new ReadRecord(RelativePath, 1, 10, AbsolutePath));

            var result = await CreateExpander(ledger, fileSystem).ExpandAsync("Before\n" + marker + "\nAfter", CancellationToken.None);

            Assert.That(result.ExpandedRefCount, Is.EqualTo(1));
            Assert.That(result.UnresolvedRefCount, Is.EqualTo(0));
            Assert.That(result.MalformedRefCount, Is.EqualTo(0));
            Assert.That(result.Content, Does.Contain("File: src\\Program.cs, lines 2-4"), "header uses the solution-relative path");
            Assert.That(result.Content, Does.Contain("line 2\nline 3\nline 4\n"));
        }

        [Test]
        public async Task Expand_AbsolutePathOfAnotherFile_StaysUnresolved()
        {
            var fileSystem = new InMemoryFileSystem();
            WriteFile(fileSystem, 10);

            var ledger = new ReadLedger();
            ledger.Add(new ReadRecord(RelativePath, 1, 10, AbsolutePath));

            var result = await CreateExpander(ledger, fileSystem).ExpandAsync(@"@D:\other\src\Program.cs:2-4", CancellationToken.None);

            Assert.That(result.ExpandedRefCount, Is.EqualTo(0));
            Assert.That(result.UnresolvedRefCount, Is.EqualTo(1));
            Assert.That(result.Content, Is.EqualTo(@"@D:\other\src\Program.cs:2-4"));
        }

        [Test]
        public async Task Expand_AbsolutePathMarker_BeyondReadRange_StaysUnresolved()
        {
            var fileSystem = new InMemoryFileSystem();
            WriteFile(fileSystem, 200);

            var ledger = new ReadLedger();
            ledger.Add(new ReadRecord(RelativePath, 1, 50, AbsolutePath));

            var result = await CreateExpander(ledger, fileSystem).ExpandAsync(@"@C:\sol\src\Program.cs:1-120", CancellationToken.None);

            Assert.That(result.ExpandedRefCount, Is.EqualTo(0));
            Assert.That(result.UnresolvedRefCount, Is.EqualTo(1));
        }

        [Test]
        public async Task Expand_SubRangeOfRecordedRead_IsResolved()
        {
            var fileSystem = new InMemoryFileSystem();
            WriteFile(fileSystem, 200);

            var ledger = new ReadLedger();
            ledger.Add(new ReadRecord(RelativePath, 1, 200, AbsolutePath));

            var expander = CreateExpander(ledger, fileSystem);
            var result = await expander.ExpandAsync("@src\\Program.cs:30-39", CancellationToken.None);

            Assert.That(result.ExpandedRefCount, Is.EqualTo(1));
            Assert.That(result.Content, Does.Contain("File: src\\Program.cs, lines 30-39"));
            Assert.That(result.Content, Does.Contain("````csharp\nline 30\n"));
            Assert.That(result.Content, Does.Contain("line 39\n````"));
            Assert.That(result.Content, Does.Not.Contain("line 29"));
            Assert.That(result.Content, Does.Not.Contain("line 40"));
        }

        [Test]
        public async Task Expand_UnledgeredMarker_IsLeftUntouched()
        {
            var fileSystem = new InMemoryFileSystem();
            WriteFile(fileSystem, 10);

            var ledger = new ReadLedger(); // empty on purpose
            var expander = CreateExpander(ledger, fileSystem);

            var result = await expander.ExpandAsync("keep @src\\Program.cs:1-10 as-is", CancellationToken.None);

            Assert.That(result.ExpandedRefCount, Is.EqualTo(0));
            Assert.That(result.UnresolvedRefCount, Is.EqualTo(1));
            Assert.That(result.Content, Is.EqualTo("keep @src\\Program.cs:1-10 as-is"));
        }

        [Test]
        public async Task Expand_FabricatedRangeOutsideRecordedRead_IsLeftUntouched()
        {
            var fileSystem = new InMemoryFileSystem();
            WriteFile(fileSystem, 200);

            var ledger = new ReadLedger();
            ledger.Add(new ReadRecord(RelativePath, 1, 50, AbsolutePath)); // only 1..50 were actually read

            var expander = CreateExpander(ledger, fileSystem);
            var result = await expander.ExpandAsync("@src\\Program.cs:51-60", CancellationToken.None);

            Assert.That(result.ExpandedRefCount, Is.EqualTo(0));
            Assert.That(result.UnresolvedRefCount, Is.EqualTo(1));
            Assert.That(result.Content, Is.EqualTo("@src\\Program.cs:51-60"));
        }

        [Test]
        public async Task Expand_MarkerWithJsonEscapedBackslashes_IsResolved()
        {
            var fileSystem = new InMemoryFileSystem();
            WriteFile(fileSystem, 10);

            var ledger = new ReadLedger();
            ledger.Add(new ReadRecord(RelativePath, 1, 10, AbsolutePath));

            var expander = CreateExpander(ledger, fileSystem);
            // The model copied the ref out of the (JSON) tool payload, so backslashes are doubled.
            var result = await expander.ExpandAsync(@"@src\\Program.cs:1-10", CancellationToken.None);

            Assert.That(result.ExpandedRefCount, Is.EqualTo(1));
            Assert.That(result.Content, Does.Contain("File: src\\Program.cs, lines 1-10"));
        }

        [Test]
        public async Task Expand_NoMarkers_IsNoOp()
        {
            var fileSystem = new InMemoryFileSystem();
            var ledger = new ReadLedger();
            var expander = CreateExpander(ledger, fileSystem);

            const string content = "plain answer without any markers";
            var result = await expander.ExpandAsync(content, CancellationToken.None);

            Assert.That(result.Content, Is.EqualTo(content));
            Assert.That(result.ExpandedRefCount, Is.EqualTo(0));
            Assert.That(result.UnresolvedRefCount, Is.EqualTo(0));
        }

        [Test]
        public async Task Expand_MultipleMarkers_AreExpandedInOrder()
        {
            var fileSystem = new InMemoryFileSystem();
            WriteFile(fileSystem, 20);

            var ledger = new ReadLedger();
            ledger.Add(new ReadRecord(RelativePath, 1, 20, AbsolutePath));

            var expander = CreateExpander(ledger, fileSystem);
            var result = await expander.ExpandAsync("@src\\Program.cs:1-2 then @src\\Program.cs:19-20", CancellationToken.None);

            Assert.That(result.ExpandedRefCount, Is.EqualTo(2));

            int first = result.Content.IndexOf("File: src\\Program.cs, lines 1-2", System.StringComparison.Ordinal);
            int second = result.Content.IndexOf("File: src\\Program.cs, lines 19-20", System.StringComparison.Ordinal);
            Assert.That(first, Is.GreaterThanOrEqualTo(0));
            Assert.That(second, Is.GreaterThan(first));
        }

        [Test]
        public async Task Expand_NotRecursive_ContentContainingMarkersIsNotRescanned()
        {
            var fileSystem = new InMemoryFileSystem();
            // The file itself literally contains a marker-like line.
            fileSystem.WriteAllBytesAsync(AbsolutePath, Encoding.UTF8.GetBytes("@evil.cs:1-2\nsecond")).Wait();

            var ledger = new ReadLedger();
            ledger.Add(new ReadRecord(RelativePath, 1, 2, AbsolutePath));

            var expander = CreateExpander(ledger, fileSystem);
            var result = await expander.ExpandAsync("@src\\Program.cs:1-2", CancellationToken.None);

            Assert.That(result.ExpandedRefCount, Is.EqualTo(1));
            Assert.That(result.Content, Does.Contain("@evil.cs:1-2"));
        }

        [Test]
        public async Task Expand_RendersVerbatimBlockWithFileHeader()
        {
            var fileSystem = new InMemoryFileSystem();
            WriteFile(fileSystem, 10);

            var ledger = new ReadLedger();
            ledger.Add(new ReadRecord(RelativePath, 1, 10, AbsolutePath));

            var expander = CreateExpander(ledger, fileSystem);
            var result = await expander.ExpandAsync("@src\\Program.cs:1-10", CancellationToken.None);

            // The runner prints the "File: <path>, lines <start>-<end>" header and a verbatim fenced block
            // (no line-number prefixes), so the content can be copied straight into an edit tool.
            Assert.That(result.Content, Does.StartWith("File: src\\Program.cs, lines 1-10\n````csharp\nline 1\n"));
            Assert.That(result.Content, Does.EndWith("line 10\n````"));
            Assert.That(result.Content, Does.Not.Contain("1: line 1"));
        }

        [Test]
        public async Task Expand_MarkerWithoutRecordedAbsolutePath_IsLeftUntouched()
        {
            var fileSystem = new InMemoryFileSystem();
            WriteFile(fileSystem, 10);

            var ledger = new ReadLedger();
            ledger.Add(new ReadRecord(RelativePath, 1, 10)); // no absolute path captured

            var expander = CreateExpander(ledger, fileSystem);
            var result = await expander.ExpandAsync("@src\\Program.cs:1-10", CancellationToken.None);

            Assert.That(result.ExpandedRefCount, Is.EqualTo(0));
            Assert.That(result.UnresolvedRefCount, Is.EqualTo(1));
        }

        [Test]
        public async Task Expand_PagedReads_SingleMarkerForUnionIsResolved()
        {
            var fileSystem = new InMemoryFileSystem();
            WriteFile(fileSystem, 160);

            var ledger = new ReadLedger();
            ledger.Add(new ReadRecord(RelativePath, 1, 80, AbsolutePath));
            ledger.Add(new ReadRecord(RelativePath, 81, 160, AbsolutePath));

            var expander = CreateExpander(ledger, fileSystem);
            var result = await expander.ExpandAsync("@src\\Program.cs:1-160", CancellationToken.None);

            Assert.That(result.ExpandedRefCount, Is.EqualTo(1));
            Assert.That(result.Content, Does.Contain("File: src\\Program.cs, lines 1-160"));
            Assert.That(result.Content, Does.Contain("````csharp\nline 1\n"));
            Assert.That(result.Content, Does.Contain("line 160\n````"));
        }

        [Test]
        public async Task Expand_PreservesLeadingMarkdownQuotePrefix_Verbatim()
        {
            var fileSystem = new InMemoryFileSystem();
            const string fileContent = "> line a\n>   line b\n> - line c";
            fileSystem.WriteAllBytesAsync(AbsolutePath, Encoding.UTF8.GetBytes(fileContent)).Wait();

            var ledger = new ReadLedger();
            ledger.Add(new ReadRecord(RelativePath, 1, 3, AbsolutePath));

            var expander = CreateExpander(ledger, fileSystem);
            var result = await expander.ExpandAsync("@src\\Program.cs:1-3", CancellationToken.None);

            Assert.That(result.ExpandedRefCount, Is.EqualTo(1));
            // Leading '>' and indentation survive verbatim - no line numbers, no trimming.
            Assert.That(result.Content, Does.Contain("````csharp\n> line a\n>   line b\n> - line c\n````"));
            Assert.That(result.Content, Does.Not.Contain("1: >"));
        }

    }
}
