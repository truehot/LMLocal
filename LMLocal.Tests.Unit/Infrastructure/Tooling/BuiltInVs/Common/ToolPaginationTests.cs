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
            // Benchmark A3.7: the first page of 50, then the model asked for 200 and lost 29 references.
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
