using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common
{
    internal interface ISearchResultCache
    {
        bool TryGet<T>(string cacheKey, string solutionDirectory, out CachedToolResults<T> result) where T : class;
        void Set<T>(string cacheKey, string solutionDirectory, CachedToolResults<T> result) where T : class;
        void Clear();
    }

    internal class CachedToolResults<T> where T : class
    {
        public List<T> AllResults { get; set; }
        public int ItemsScanned { get; set; }
    }

    internal class SearchResultCache : ISearchResultCache
    {
        private readonly ConcurrentDictionary<string, object> _cache = 
            new ConcurrentDictionary<string, object>(StringComparer.Ordinal);

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
        }

        public void Clear()
        {
            _cache.Clear();
        }
    }
}
