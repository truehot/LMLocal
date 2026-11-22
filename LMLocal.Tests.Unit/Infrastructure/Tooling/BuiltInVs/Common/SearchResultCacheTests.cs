using System.Collections.Generic;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common
{
    /// <summary>
    /// Entry ids ("r7") are what a cursor page_token names: stable for a live entry, never reused after Clear.
    /// </summary>
    [TestFixture]
    public class SearchResultCacheTests
    {
        private static CachedToolResults<string> Entry(params string[] items)
        {
            return new CachedToolResults<string> { AllResults = new List<string>(items), ItemsScanned = items.Length };
        }

        [Test]
        public void Id_IsRPlusANumber_AndTheSameKeyKeepsItsId()
        {
            var cache = new SearchResultCache();

            cache.Set("k1", "", Entry("a"));
            string id = cache.GetId("k1");
            cache.Set("k1", "", Entry("a", "b"));

            Assert.That(id, Does.Match("^r[0-9]+$"));
            Assert.That(cache.GetId("k1"), Is.EqualTo(id));
        }

        [Test]
        public void DifferentKeys_GetDifferentIds()
        {
            var cache = new SearchResultCache();

            cache.Set("k1", "", Entry("a"));
            cache.Set("k2", "", Entry("b"));

            Assert.That(cache.GetId("k1"), Is.Not.EqualTo(cache.GetId("k2")));
        }

        [Test]
        public void TryGetById_FindsTheLatestEntryOfTheKey()
        {
            var cache = new SearchResultCache();
            cache.Set("k1", "", Entry("a"));
            string id = cache.GetId("k1");
            cache.Set("k1", "", Entry("a", "b"));

            Assert.That(cache.TryGetById(id, out CachedToolResults<string> found), Is.True);
            Assert.That(found.AllResults, Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void TryGetById_OfAnotherEntryType_IsAMiss()
        {
            var cache = new SearchResultCache();
            cache.Set("k1", "", Entry("a"));

            Assert.That(cache.TryGetById(cache.GetId("k1"), out CachedToolResults<object> _), Is.False);
        }

        [Test]
        public void Clear_DropsTheEntries_AndANewEntryOfTheSameKeyGetsANewId()
        {
            var cache = new SearchResultCache();
            cache.Set("k1", "", Entry("a"));
            string oldId = cache.GetId("k1");

            cache.Clear();

            Assert.That(cache.GetId("k1"), Is.Null);
            Assert.That(cache.TryGetById(oldId, out CachedToolResults<string> _), Is.False);

            cache.Set("k1", "", Entry("a"));

            Assert.That(cache.GetId("k1"), Is.Not.EqualTo(oldId));
            Assert.That(cache.TryGetById(oldId, out CachedToolResults<string> _), Is.False);
        }

        [Test]
        public void IdsAreNotReusedByAnotherCacheInTheSameProcess()
        {
            var first = new SearchResultCache();
            var second = new SearchResultCache();
            first.Set("k", "", Entry("a"));
            second.Set("k", "", Entry("a"));

            Assert.That(first.GetId("k"), Is.Not.EqualTo(second.GetId("k")));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("r1")]
        public void UnknownOrEmpty_IsAMiss(string id)
        {
            var cache = new SearchResultCache();

            Assert.That(cache.TryGetById(id, out CachedToolResults<string> _), Is.False);
            Assert.That(cache.GetId(id), Is.Null);
        }
    }
}
