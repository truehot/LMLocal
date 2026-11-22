using System;
using System.Collections.Generic;
using System.Linq;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common
{
    /// <summary>
    /// The shared pagination contract: page_token is an offset from the start of the full result,
    /// next_page_token is the offset of the next page, and the page size may change between calls.
    /// </summary>
    [TestFixture]
    public class ToolPaginationTests
    {
        #region Cursor tokens

        [Test]
        public void CursorToken_RoundTripsIdAndOffset()
        {
            string token = ToolPagination.MakeToken("r7", 150);

            Assert.That(token, Is.EqualTo("r7:150"));

            var parsed = PageToken.Parse(token);
            Assert.That(parsed.Kind, Is.EqualTo(PageTokenKind.Cursor));
            Assert.That(parsed.Id, Is.EqualTo("r7"));
            Assert.That(parsed.Offset, Is.EqualTo(150));
            Assert.That(parsed.Jump, Is.EqualTo(PageJump.None));
            Assert.That(ToolPagination.ParseOffset(token, 50), Is.EqualTo(150));
        }

        // What models do while copying a token: quotes, backticks, brackets, upper case, spaces, other separators.
        [TestCase("r7:50")]
        [TestCase("R7:50")]
        [TestCase("\"r7:50\"")]
        [TestCase("'r7:50'")]
        [TestCase("`r7:50`")]
        [TestCase("(r7:50)")]
        [TestCase("  r7 : 50 ")]
        [TestCase("r7-50")]
        [TestCase("r7.50")]
        [TestCase("r7/50")]
        [TestCase("r07:50")]
        public void CopiedToken_IsReadAsTheSameCursor(string token)
        {
            var parsed = PageToken.Parse(token);

            Assert.That(parsed.Kind, Is.EqualTo(PageTokenKind.Cursor));
            Assert.That(parsed.Id, Is.EqualTo("r7"));
            Assert.That(parsed.Offset, Is.EqualTo(50));
        }

        // The enums are internal and a public test method cannot take them as parameters (CS0051),
        // so the expected value is passed by name.
        private static T EnumNamed<T>(string name) => (T)Enum.Parse(typeof(T), name);

        [TestCase("r7:last", nameof(PageJump.Last))]
        [TestCase("R7:LAST", nameof(PageJump.Last))]
        [TestCase("r7:first", nameof(PageJump.First))]
        public void CursorWithAWord_IsAJump(string token, string jump)
        {
            var parsed = PageToken.Parse(token);

            Assert.That(parsed.Kind, Is.EqualTo(PageTokenKind.Cursor));
            Assert.That(parsed.Id, Is.EqualTo("r7"));
            Assert.That(parsed.Jump, Is.EqualTo(EnumNamed<PageJump>(jump)));
        }

        [TestCase("last", nameof(PageJump.Last))]
        [TestCase(" LAST ", nameof(PageJump.Last))]
        [TestCase("\"first\"", nameof(PageJump.First))]
        public void BareWord_IsAJumpWithoutAnEntry(string token, string jump)
        {
            var parsed = PageToken.Parse(token);

            Assert.That(parsed.Kind, Is.EqualTo(PageTokenKind.Jump));
            Assert.That(parsed.Jump, Is.EqualTo(EnumNamed<PageJump>(jump)));
            Assert.That(parsed.Id, Is.Null);
        }

        [TestCase("50", nameof(PageTokenKind.Number))]
        [TestCase("r7", nameof(PageTokenKind.IncompleteCursor))]
        [TestCase("abc", nameof(PageTokenKind.Unreadable))]
        [TestCase("r7:x", nameof(PageTokenKind.Unreadable))]
        [TestCase("0a1b2c3d:50", nameof(PageTokenKind.Unreadable))]
        [TestCase("\"\"", nameof(PageTokenKind.Unreadable))]
        [TestCase("", nameof(PageTokenKind.None))]
        [TestCase(null, nameof(PageTokenKind.None))]
        [TestCase("   ", nameof(PageTokenKind.None))]
        public void OtherTokens_AreClassified(string token, string kind)
        {
            Assert.That(PageToken.Parse(token).Kind, Is.EqualTo(EnumNamed<PageTokenKind>(kind)));
        }

        [Test]
        public void NextCursorToken_IsNullAtTheEnd()
        {
            Assert.That(ToolPagination.NextToken(50, 120, "r7"), Is.EqualTo("r7:50"));
            Assert.That(ToolPagination.NextToken(120, 120, "r7"), Is.Null);
        }

        [Test]
        public void NextCursorToken_WithoutAnId_IsThePlainOffset()
        {
            Assert.That(ToolPagination.NextToken(50, 120, null), Is.EqualTo("50"));
        }

        #endregion

        #region PageSelection

        private static PageSelection Select(string token)
        {
            return PageSelection.From(token == null ? new Dictionary<string, object>() : new Dictionary<string, object> { ["page_token"] = token });
        }

        [Test]
        public void Selection_NoToken_IsTheFirstPageWithoutWarning()
        {
            var selection = Select(null);

            Assert.That(selection.Error, Is.Null);
            Assert.That(selection.Warning, Is.Null);
            Assert.That(selection.IsCursor, Is.False);
            Assert.That(selection.Offset(50, 120), Is.EqualTo(0));
        }

        [Test]
        public void Selection_CursorOfAMatchingEntry_UsesTheEntryAndItsOffset()
        {
            var selection = Select("r7:50");
            selection.Resolve(found: true, matchesCall: true);

            Assert.That(selection.UseEntry, Is.True);
            Assert.That(selection.Warning, Is.Null);
            Assert.That(selection.Offset(50, 120), Is.EqualTo(50));
        }

        [Test]
        public void Selection_CursorOfAnUnknownEntry_WarnsAndKeepsTheOffset()
        {
            var selection = Select("r7:50");
            selection.Resolve(found: false, matchesCall: false);

            Assert.That(selection.UseEntry, Is.False);
            Assert.That(selection.Warning, Is.EqualTo(PageSelection.StaleTokenWarning));
            Assert.That(selection.Offset(50, 120), Is.EqualTo(50));
        }

        [Test]
        public void Selection_CursorOfAnotherRequest_IsIgnoredWithAWarningAndStartsAtTheBeginning()
        {
            var selection = Select("r7:50");
            selection.Resolve(found: true, matchesCall: false);

            Assert.That(selection.UseEntry, Is.False);
            Assert.That(selection.Foreign, Is.True);
            Assert.That(selection.Warning, Is.EqualTo(PageSelection.ForeignTokenWarning));
            Assert.That(selection.Offset(50, 120), Is.EqualTo(0));
        }

        [Test]
        public void Selection_NumberToken_WarnsAndKeepsTheOffset()
        {
            var selection = Select("50");

            Assert.That(selection.IsCursor, Is.False);
            Assert.That(selection.Warning, Does.Contain("not a next_page_token"));
            Assert.That(selection.Offset(50, 120), Is.EqualTo(50));
        }

        [Test]
        public void Selection_IdWithoutOffset_IsAnError()
        {
            var selection = Select("r7");

            Assert.That(selection.Error, Does.Contain("incomplete"));
        }

        [Test]
        public void Selection_UnreadableToken_WarnsAndStartsAtTheBeginning()
        {
            var selection = Select("banana");

            Assert.That(selection.Warning, Does.Contain("was not understood"));
            Assert.That(selection.Offset(50, 120), Is.EqualTo(0));
        }

        // total, pageSize, last page offset: the last pageSize items, never an empty page
        [TestCase(120, 50, 70)]
        [TestCase(120, 200, 0)]
        [TestCase(0, 50, 0)]
        [TestCase(100, 50, 50)]
        public void Selection_Last_CountsFromTheTotalOfTheResult(int total, int pageSize, int offset)
        {
            Assert.That(Select("last").Offset(pageSize, total), Is.EqualTo(offset));

            var cursor = Select("r7:last");
            cursor.Resolve(found: true, matchesCall: true);
            Assert.That(cursor.Offset(pageSize, total), Is.EqualTo(offset));
        }

        [Test]
        public void Selection_Last_IgnoresTheOffsetOfTheToken_AndFirstIsZero()
        {
            Assert.That(Select("r7:last").Offset(50, 120), Is.EqualTo(70));
            Assert.That(Select("r7:first").Offset(50, 120), Is.EqualTo(0));
            Assert.That(Select("first").Offset(50, 120), Is.EqualTo(0));
        }

        [Test]
        public void Selection_BareJump_HasNoWarning()
        {
            var selection = Select("last");

            Assert.That(selection.Error, Is.Null);
            Assert.That(selection.Warning, Is.Null);
            Assert.That(selection.IsCursor, Is.False);
        }

        [Test]
        public void Selection_LastOfAnotherRequest_WarnsButStillJumpsInTheNewResult()
        {
            var selection = Select("r7:last");
            selection.Resolve(found: true, matchesCall: false);

            Assert.That(selection.Warning, Is.EqualTo(PageSelection.ForeignTokenWarning));
            Assert.That(selection.Offset(50, 120), Is.EqualTo(70));
        }

        [Test]
        public void Selection_HugeOffset_CannotOverflow()
        {
            var selection = Select("r7:99999999999");
            selection.Resolve(found: true, matchesCall: true);

            Assert.That(selection.Offset(200, 10), Is.EqualTo(int.MaxValue - 200));
        }

        [Test]
        public void Selection_WithPagingHint_ExplainsAMissingParameterOnlyWhenATokenWasGiven()
        {
            Assert.That(Select(null).WithPagingHint("symbol_name is required"), Is.EqualTo("symbol_name is required"));

            string hint = Select("r7:50").WithPagingHint("symbol_name is required");
            Assert.That(hint, Does.Contain("repeat the first request").And.EndWith("symbol_name is required"));
        }

        #endregion

        #region Describe (the 'showing' text)

        [TestCase(0, 50, 120, true, "page 1 of 3")]
        [TestCase(50, 50, 120, true, "page 2 of 3")]
        [TestCase(100, 20, 120, false, "page 3 of 3")]
        [TestCase(0, 20, 20, false, "page 1 of 1")]
        [TestCase(0, 1, 1, false, "page 1 of 1")]
        [TestCase(120, 0, 120, false, "past the end (120 total)")]
        [TestCase(500, 0, 120, false, "past the end (120 total)")]
        public void Describe_GivesThePageOnly(int offset, int shown, int total, bool hasNext, string expected)
        {
            Assert.That(ToolPagination.Describe(offset, shown, total, hasNext, 50), Is.EqualTo(expected));
        }

        // A1.8: 582 matches, 500 per page.
        [TestCase(0, 500, 582, true, 500, "page 1 of 2")]
        [TestCase(500, 82, 582, false, 500, "page 2 of 2")]
        // 'last' with 50 per page: offset 70 is not a multiple of 50; the page is still the last of three, as the first page said.
        [TestCase(70, 50, 120, false, 50, "page 3 of 3")]
        // The page size changed from 50 to 20 after page 1: ceil(50/20) = 3 pages before, this one, ceil(50/20) = 3 after.
        [TestCase(50, 20, 120, true, 20, "page 4 of 7")]
        // A short page in the middle (search_file_content file mode may end a page early): the pages left are counted from what is left.
        [TestCase(0, 40, 120, true, 50, "page 1 of 3")]
        // No page size: the size of this page is used.
        [TestCase(50, 50, 120, true, 0, "page 2 of 3")]
        public void Describe_PageNumbers(int offset, int shown, int total, bool hasNext, int pageSize, string expected)
        {
            Assert.That(ToolPagination.Describe(offset, shown, total, hasNext, pageSize), Is.EqualTo(expected));
        }

        // The item range ("501-582 of 582") next to the line numbers of the results misled the model (Script\showing-replay.csx).
        [Test]
        public void Describe_HasNoItemRange()
        {
            Assert.That(ToolPagination.Describe(500, 82, 582, false, 500), Does.Not.Contain("501").And.Not.Contain("582"));
        }

        [Test]
        public void Describe_EmptyResult_IsNull()
        {
            Assert.That(ToolPagination.Describe(0, 0, 0, false, 50), Is.Null);
        }

        [Test]
        public void Describe_NeverMentionsTheTokenParameter()
        {
            Assert.That(ToolPagination.Describe(0, 50, 120, true, 50), Does.Not.Contain("token"));
        }

        #endregion

        #region ReadToken

        [Test]
        public void ReadToken_StringToken_Returned()
        {
            var token = ToolPagination.ReadToken(new Dictionary<string, object> { ["page_token"] = "50" });

            Assert.That(token, Is.EqualTo("50"));
        }

        [Test]
        public void ReadToken_NumericToken_IsEquivalentToSameStringToken()
        {
            var asNumber = ToolPagination.ReadToken(new Dictionary<string, object> { ["page_token"] = 50 });
            var asString = ToolPagination.ReadToken(new Dictionary<string, object> { ["page_token"] = "50" });

            Assert.That(asNumber, Is.EqualTo(asString));
            Assert.That(ToolPagination.ParseOffset(asNumber, 50), Is.EqualTo(ToolPagination.ParseOffset(asString, 50)));
        }

        [Test]
        public void ReadToken_LongAndDoubleTokens_ConvertToIntegerOffset()
        {
            Assert.That(ToolPagination.ReadToken(new Dictionary<string, object> { ["page_token"] = 50L }), Is.EqualTo("50"));
            Assert.That(ToolPagination.ReadToken(new Dictionary<string, object> { ["page_token"] = 50.0 }), Is.EqualTo("50"));
        }

        [Test]
        public void ReadToken_MissingNullEmptyOrWhitespace_ReturnsNull()
        {
            Assert.That(ToolPagination.ReadToken(null), Is.Null);
            Assert.That(ToolPagination.ReadToken(new Dictionary<string, object>()), Is.Null);
            Assert.That(ToolPagination.ReadToken(new Dictionary<string, object> { ["page_token"] = null }), Is.Null);
            Assert.That(ToolPagination.ReadToken(new Dictionary<string, object> { ["page_token"] = "" }), Is.Null);
            Assert.That(ToolPagination.ReadToken(new Dictionary<string, object> { ["page_token"] = "   " }), Is.Null);
        }

        [Test]
        public void ReadToken_PaddedString_Trimmed()
        {
            var token = ToolPagination.ReadToken(new Dictionary<string, object> { ["page_token"] = "  50 " });

            Assert.That(token, Is.EqualTo("50"));
        }

        #endregion

        #region ParseOffset

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("abc")]
        [TestCase("1e3")]
        [TestCase("-5")]
        [TestCase("0")]
        public void ParseOffset_NonNumericNegativeOrZero_ReturnsFirstPage(string token)
        {
            Assert.That(ToolPagination.ParseOffset(token, 50), Is.EqualTo(0));
        }

        [Test]
        public void ParseOffset_NumericToken_ReturnsOffset()
        {
            Assert.That(ToolPagination.ParseOffset("50", 50), Is.EqualTo(50));
            Assert.That(ToolPagination.ParseOffset(" 50 ", 50), Is.EqualTo(50));
        }

        [Test]
        public void ParseOffset_DigitsBeyondLong_IsPastTheEndNotTheFirstPage()
        {
            // Restarting from the first page would silently repeat results, so a huge numeric token
            // is treated as an offset far past the end instead.
            Assert.That(ToolPagination.ParseOffset("99999999999999999999999", 50), Is.EqualTo(int.MaxValue - 50));
        }

        [Test]
        public void ParseOffset_ClampedSoThatOffsetPlusPageSizeCannotOverflow()
        {
            int offset = ToolPagination.ParseOffset("3000000000", 200);

            Assert.That(offset, Is.EqualTo(int.MaxValue - 200));
            Assert.That((long)offset + 200, Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void ParseOffset_WithoutPageSize_ClampsToIntMaxValue()
        {
            Assert.That(ToolPagination.ParseOffset("3000000000"), Is.EqualTo(int.MaxValue));
            Assert.That(ToolPagination.ParseOffset("abc"), Is.EqualTo(0));
        }

        #endregion

        #region NextToken

        [Test]
        public void NextToken_MoreResultsRemain_ReturnsNextOffset()
        {
            Assert.That(ToolPagination.NextToken(50, 79), Is.EqualTo("50"));
            Assert.That(ToolPagination.NextToken(1, 2), Is.EqualTo("1"));
        }

        [Test]
        public void NextToken_ExactlyAtTheEnd_ReturnsNull()
        {
            Assert.That(ToolPagination.NextToken(79, 79), Is.Null);
            Assert.That(ToolPagination.NextToken(0, 0), Is.Null);
        }

        [Test]
        public void NextToken_PastTheEnd_ReturnsNull()
        {
            Assert.That(ToolPagination.NextToken(int.MaxValue, 79), Is.Null);
        }

        #endregion

        #region Full walk

        [Test]
        public void Walk_SamePageSize_VisitsEveryItemExactlyOnce()
        {
            Assert.That(Walk(79, new[] { 50 }), Is.EqualTo(Items(79)));
            Assert.That(Walk(100, new[] { 25 }), Is.EqualTo(Items(100)));
            Assert.That(Walk(3, new[] { 500 }), Is.EqualTo(Items(3)));
        }

        [Test]
        public void Walk_PageSizeGrows_VisitsEveryItemExactlyOnce()
        {
            // Benchmark A1.29 (was A3.7): the first page of 50, then the model asked for 200 and lost 29 references.
            Assert.That(Walk(79, new[] { 50, 200 }), Is.EqualTo(Items(79)));
            Assert.That(Walk(79, new[] { 25, 50, 500 }), Is.EqualTo(Items(79)));
        }

        [Test]
        public void Walk_PageSizeShrinks_VisitsEveryItemExactlyOnce()
        {
            Assert.That(Walk(79, new[] { 200, 10 }), Is.EqualTo(Items(79)));
            Assert.That(Walk(79, new[] { 79, 1 }), Is.EqualTo(Items(79)));
        }

        [Test]
        public void Walk_ExactBoundary_LastPageHasNoNextToken()
        {
            var (seen, lastToken) = WalkWithLastToken(50, new[] { 25 });

            Assert.That(seen, Is.EqualTo(Items(50)));
            Assert.That(lastToken, Is.Null);
        }

        [Test]
        public void Walk_TokenPastTheEnd_ReturnsEmptyPageWithoutNextToken()
        {
            int total = 10;
            int offset = ToolPagination.ParseOffset("1000", 50);

            var page = Page(total, offset, 50);
            string token = ToolPagination.NextToken(offset + page.Count, total);

            Assert.That(page, Is.Empty);
            Assert.That(token, Is.Null);
        }

        private static List<int> Items(int count) => Enumerable.Range(0, count).ToList();

        private static List<int> Page(int total, int offset, int pageSize)
        {
            var page = new List<int>();
            for (int i = offset; i < total && page.Count < pageSize; i++)
                page.Add(i);
            return page;
        }

        private static List<int> Walk(int total, int[] pageSizes) => WalkWithLastToken(total, pageSizes).Items;

        private static (List<int> Items, string LastToken) WalkWithLastToken(int total, int[] pageSizes)
        {
            var seen = new List<int>();
            string token = null;

            // The call budget makes a broken contract fail the test instead of hanging it.
            for (int call = 0; call < 1000; call++)
            {
                int pageSize = pageSizes[Math.Min(call, pageSizes.Length - 1)];
                int offset = ToolPagination.ParseOffset(token, pageSize);

                var page = Page(total, offset, pageSize);
                seen.AddRange(page);

                token = ToolPagination.NextToken(offset + page.Count, total);
                if (token == null)
                    return (seen, null);
            }

            return (seen, token);
        }

        #endregion
    }
}
