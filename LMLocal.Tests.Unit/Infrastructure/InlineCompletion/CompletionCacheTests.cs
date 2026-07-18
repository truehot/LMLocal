using System;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Autocompletions.InlineCompletion;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.InlineCompletion
{
    /// <summary>
    /// Unit tests for <see cref="CompletionCache"/>: LRU caching, eviction, key building, and thread safety.
    /// </summary>
    [TestFixture]
    public class CompletionCacheTests
    {
        private CompletionCache _cache;

        [SetUp]
        public void SetUp()
        {
            _cache = new CompletionCache();
        }

        [TearDown]
        public void TearDown()
        {
            _cache = null;
        }

        // =========================================================================
        // TryGet
        // =========================================================================

        [Test]
        public void TryGet_MissingKey_ReturnsFalse()
        {
            Assert.That(_cache.TryGet("missing", out string value), Is.False);
            Assert.That(value, Is.Null);
        }

        [Test]
        public void TryGet_ExistingKey_ReturnsValue()
        {
            _cache.Set("key1", "value1");
            Assert.That(_cache.TryGet("key1", out string value), Is.True);
            Assert.That(value, Is.EqualTo("value1"));
        }

        [Test]
        public void TryGet_CaseSensitive()
        {
            _cache.Set("Key", "value");
            Assert.That(_cache.TryGet("key", out string _), Is.False);
        }

        [Test]
        public void TryGet_NullKey_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _cache.TryGet(null, out _));
        }

        // =========================================================================
        // Set
        // =========================================================================

        [Test]
        public void Set_NullKey_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _cache.Set(null, "value"));
        }

        [Test]
        public void Set_NullValue_Skips()
        {
            _cache.Set("key", null);
            Assert.That(_cache.TryGet("key", out string _), Is.False);
        }

        [Test]
        public void Set_EmptyValue_Stored()
        {
            _cache.Set("key", "");
            Assert.That(_cache.TryGet("key", out string value), Is.True);
            Assert.That(value, Is.EqualTo(""));
        }

        [Test]
        public void Set_OverwritesExistingKey()
        {
            _cache.Set("key", "old");
            _cache.Set("key", "new");
            Assert.That(_cache.TryGet("key", out string value), Is.True);
            Assert.That(value, Is.EqualTo("new"));
        }

        [Test]
        public void Set_UpdatesLastAccess_DoesNotEvictRecentlyUsed()
        {
            // Fill cache to capacity.
            for (int i = 0; i < 64; i++)
            {
                _cache.Set("key" + i, "value" + i);
            }

            // Small delay so the touch below gets a distinct timestamp.
            Thread.Sleep(10);

            // Touch key0 to make it recently used.
            Assert.That(_cache.TryGet("key0", out string _), Is.True);

            // Add one more — should evict old entries, not key0.
            _cache.Set("overflow", "val");
            Assert.That(_cache.TryGet("key0", out string _), Is.True);
        }

        // =========================================================================
        // Eviction
        // =========================================================================

        [Test]
        public void Set_EvictsOnOverflow()
        {
            // Fill exactly to capacity.
            for (int i = 0; i < 64; i++)
            {
                _cache.Set("key" + i, "value" + i);
            }
            // All 64 are present.
            Assert.That(_cache.TryGet("key0", out string _), Is.True);
            Assert.That(_cache.TryGet("key63", out string _), Is.True);

            // Add one more — should evict 16 oldest.
            _cache.Set("overflow", "val");

            // New key is present, oldest evicted.
            Assert.That(_cache.TryGet("overflow", out string _), Is.True);
            Assert.That(_cache.TryGet("key63", out string _), Is.True);
        }

        [Test]
        public void EvictAll_PreservesNewEntries()
        {
            // Fill and overflow multiple times to force multiple eviction rounds.
            for (int round = 0; round < 5; round++)
            {
                for (int i = 0; i < 64; i++)
                {
                    _cache.Set($"round{round}_key{i}", $"val{i}");
                }
            }

            // Last round entries should survive.
            Assert.That(_cache.TryGet("round4_key63", out string _), Is.True);
        }

        // =========================================================================
        // Clear
        // =========================================================================

        [Test]
        public void Clear_RemovesAllEntries()
        {
            _cache.Set("a", "1");
            _cache.Set("b", "2");
            _cache.Clear();
            Assert.That(_cache.TryGet("a", out string _), Is.False);
            Assert.That(_cache.TryGet("b", out string _), Is.False);
        }

        [Test]
        public void Clear_EmptyCache_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _cache.Clear());
        }

        [Test]
        public void Clear_ThenSet_Works()
        {
            _cache.Set("a", "1");
            _cache.Clear();
            _cache.Set("b", "2");
            Assert.That(_cache.TryGet("b", out string v), Is.True);
            Assert.That(v, Is.EqualTo("2"));
        }

        // =========================================================================
        // InvalidateFile
        // =========================================================================

        [Test]
        public void InvalidateFile_RemovesEntriesWithFilePathPrefix()
        {
            _cache.Set(@"C:\file.cs:10:5:ABCDEF01:FEDCBA98", "suggestion1");
            _cache.Set(@"C:\file.cs:12:3:11111111:22222222", "suggestion2");
            _cache.Set(@"D:\other.cs:5:1:AAAAAAAA:BBBBBBBB", "suggestion3");

            _cache.InvalidateFile(@"C:\file.cs");

            Assert.That(_cache.TryGet(@"C:\file.cs:10:5:ABCDEF01:FEDCBA98", out string _), Is.False);
            Assert.That(_cache.TryGet(@"C:\file.cs:12:3:11111111:22222222", out string _), Is.False);
            Assert.That(_cache.TryGet(@"D:\other.cs:5:1:AAAAAAAA:BBBBBBBB", out string v), Is.True);
            Assert.That(v, Is.EqualTo("suggestion3"));
        }

        [Test]
        public void InvalidateFile_NullPath_DoesNotThrow()
        {
            _cache.Set("key", "value");
            Assert.DoesNotThrow(() => _cache.InvalidateFile(null));
            Assert.That(_cache.TryGet("key", out string _), Is.True);
        }

        [Test]
        public void InvalidateFile_EmptyPath_DoesNotThrow()
        {
            _cache.Set("key", "value");
            Assert.DoesNotThrow(() => _cache.InvalidateFile(""));
            Assert.That(_cache.TryGet("key", out string _), Is.True);
        }

        [Test]
        public void InvalidateFile_NonExistentPath_DoesNothing()
        {
            _cache.Set("key", "value");
            _cache.InvalidateFile(@"Z:\nonexistent.cs");
            Assert.That(_cache.TryGet("key", out string v), Is.True);
            Assert.That(v, Is.EqualTo("value"));
        }

        [Test]
        public void InvalidateFile_EmptyCache_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _cache.InvalidateFile(@"C:\file.cs"));
        }

        [Test]
        public void InvalidateFile_CaseInsensitive()
        {
            _cache.Set(@"C:\FILE.CS:1:1:AAAA:BBBB", "value");
            _cache.InvalidateFile(@"c:\File.cs");
            Assert.That(_cache.TryGet(@"C:\FILE.CS:1:1:AAAA:BBBB", out string _), Is.False);
        }

        // =========================================================================
        // BuildKey
        // =========================================================================

        [Test]
        public void BuildKey_SameInputs_ProduceSameKey()
        {
            var key1 = CompletionCache.BuildKey(@"C:\test.cs", 10, 5, "hello", "world");
            var key2 = CompletionCache.BuildKey(@"C:\test.cs", 10, 5, "hello", "world");
            Assert.That(key1, Is.EqualTo(key2));
        }

        [Test]
        public void BuildKey_DifferentPrefix_ProduceDifferentKeys()
        {
            var key1 = CompletionCache.BuildKey(@"C:\test.cs", 10, 5, "hello", "world");
            var key2 = CompletionCache.BuildKey(@"C:\test.cs", 10, 5, "other", "world");
            Assert.That(key1, Is.Not.EqualTo(key2));
        }

        [Test]
        public void BuildKey_DifferentSuffix_ProduceDifferentKeys()
        {
            var key1 = CompletionCache.BuildKey(@"C:\test.cs", 10, 5, "hello", "world");
            var key2 = CompletionCache.BuildKey(@"C:\test.cs", 10, 5, "hello", "different");
            Assert.That(key1, Is.Not.EqualTo(key2));
        }

        [Test]
        public void BuildKey_DifferentLine_ProduceDifferentKeys()
        {
            var key1 = CompletionCache.BuildKey(@"C:\test.cs", 10, 5, "hello", "world");
            var key2 = CompletionCache.BuildKey(@"C:\test.cs", 99, 5, "hello", "world");
            Assert.That(key1, Is.Not.EqualTo(key2));
        }

        [Test]
        public void BuildKey_DifferentColumn_ProduceDifferentKeys()
        {
            var key1 = CompletionCache.BuildKey(@"C:\test.cs", 10, 5, "hello", "world");
            var key2 = CompletionCache.BuildKey(@"C:\test.cs", 10, 99, "hello", "world");
            Assert.That(key1, Is.Not.EqualTo(key2));
        }

        [Test]
        public void BuildKey_NullFilePath_UsesEmptyString()
        {
            var key = CompletionCache.BuildKey(null, 1, 2, "prefix", "suffix");
            Assert.That(key, Does.StartWith("\01\02\06\06"));
        }

        [Test]
        public void BuildKey_NullPrefixOrSuffix_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                CompletionCache.BuildKey("file.cs", 0, 0, null, null));
        }

        [Test]
        public void BuildKey_NullPrefix_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                CompletionCache.BuildKey("file.cs", 0, 0, null, "suffix"));
        }

        [Test]
        public void BuildKey_NullSuffix_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                CompletionCache.BuildKey("file.cs", 0, 0, "prefix", null));
        }

        // =========================================================================
        // Thread safety (smoke test)
        // =========================================================================

        [Test]
        public void ConcurrentAccess_DoesNotCorrupt()
        {
            var exceptions = 0;
            var tasks = new Task[8];
            for (int t = 0; t < tasks.Length; t++)
            {
                int taskId = t;
                tasks[t] = Task.Run(() =>
                {
                    try
                    {
                        for (int i = 0; i < 100; i++)
                        {
                            string key = "key" + ((taskId * 100) + i);
                            _cache.Set(key, "value" + i);
                            _cache.TryGet(key, out string _);
                        }
                    }
                    catch
                    {
                        Interlocked.Increment(ref exceptions);
                    }
                });
            }

            Task.WaitAll(tasks);
            Assert.That(exceptions, Is.EqualTo(0));
        }

        [Test]
        public void ConcurrentReadWrite_DoesNotDeadlock()
        {
            var writeTask = Task.Run(() =>
            {
                for (int i = 0; i < 100; i++)
                    _cache.Set("key" + i, "value" + i);
            });

            var readTask = Task.Run(() =>
            {
                for (int i = 0; i < 100; i++)
                    _cache.TryGet("key" + i, out string _);
            });

            var clearTask = Task.Run(() =>
            {
                for (int i = 0; i < 10; i++)
                {
                    _cache.Clear();
                    Thread.Sleep(1);
                }
            });

            Assert.DoesNotThrow(() => Task.WaitAll(writeTask, readTask, clearTask));
        }
    }
}
