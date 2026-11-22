using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common
{
    internal interface ISearchResultCache
    {
        bool TryGet<T>(string cacheKey, string solutionDirectory, out CachedToolResults<T> result) where T : class;
        void Set<T>(string cacheKey, string solutionDirectory, CachedToolResults<T> result) where T : class;

        /// <summary>
        /// The id of the entry stored under <paramref name="cacheKey"/> ("r7"): the first part of a cursor page_token.
        /// </summary>
        string GetId(string cacheKey);

        /// <summary>
        /// Finds an entry by its id.
        /// </summary>
        bool TryGetById<T>(string cacheId, out CachedToolResults<T> result) where T : class;

        void Clear();
    }

    internal class SearchResultCache : ISearchResultCache
    {
        // Ids are sequential and are never reused: Clear does not reset the counter, so a token of a cleared entry finds nothing instead
        // of another result.
        private static long _counter = new Random().Next(100, 900);

        private readonly ConcurrentDictionary<string, object> _cache = new ConcurrentDictionary<string, object>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, string> _idByKey = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, object> _byId = new ConcurrentDictionary<string, object>(StringComparer.Ordinal);

        public bool TryGet<T>(string cacheKey, string solutionDirectory, out CachedToolResults<T> result) where T : class
        {
            result = null;
            if (string.IsNullOrEmpty(cacheKey))
                return false;

            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                result = cached as CachedToolResults<T>;
                return result != null;
            }

            return false;
        }

        public void Set<T>(string cacheKey, string solutionDirectory, CachedToolResults<T> result) where T : class
        {
            if (string.IsNullOrEmpty(cacheKey) || result == null)
                return;

            _cache[cacheKey] = result;
            string id = _idByKey.GetOrAdd(cacheKey, _ => "r" + Interlocked.Increment(ref _counter).ToString(System.Globalization.CultureInfo.InvariantCulture));
            _byId[id] = result;
        }

        public string GetId(string cacheKey)
        {
            if (string.IsNullOrEmpty(cacheKey))
                return null;

            return _idByKey.TryGetValue(cacheKey, out string id) ? id : null;
        }

        public bool TryGetById<T>(string cacheId, out CachedToolResults<T> result) where T : class
        {
            result = null;
            if (string.IsNullOrEmpty(cacheId))
                return false;

            if (_byId.TryGetValue(cacheId, out var cached))
            {
                result = cached as CachedToolResults<T>;
                return result != null;
            }

            return false;
        }

        public void Clear()
        {
            _cache.Clear();
            _idByKey.Clear();
            _byId.Clear();
        }
    }

    internal class CachedToolResults<T> where T : class
    {
        public List<T> AllResults { get; set; }
        public int ItemsScanned { get; set; }

        /// <summary>Optional note returned with every page of this result set (e.g. a search fallback explanation).</summary>
        public string Note { get; set; }

        /// <summary>
        /// What the entry answers: the file name pattern (find_files), the searched text (search_file_content) or the symbol name
        /// (get_symbol_info_js).
        /// </summary>
        public string Subject { get; set; }
    }
}
