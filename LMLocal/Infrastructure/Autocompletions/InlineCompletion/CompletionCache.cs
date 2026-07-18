using System;
using System.Collections.Generic;

namespace LMLocal.Infrastructure.Autocompletions.InlineCompletion
{
    /// <summary>
    /// Thread-safe LRU cache for inline completion suggestions.
    /// </summary>
    internal class CompletionCache
    {
        private readonly Dictionary<string, LinkedListNode<CacheEntry>> _map;
        private readonly LinkedList<CacheEntry> _order;
        private readonly object _lock = new object();
        private const int MaxEntries = 64;
        private const int EvictCount = 16;

        internal CompletionCache()
        {
            _map = new Dictionary<string, LinkedListNode<CacheEntry>>(MaxEntries);
            _order = new LinkedList<CacheEntry>();
        }

        /// <summary>
        /// Tries to get a cached value. Moves the entry to the most-recent end of the LRU list on hit.
        /// </summary>
        internal bool TryGet(string key, out string value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            lock (_lock)
            {
                if (_map.TryGetValue(key, out var node))
                {
                    _order.Remove(node);
                    _order.AddLast(node);
                    value = node.Value.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Stores a value. Evicts oldest entries if at capacity and the key is new. If the key already exists, its value is updated and it becomes the most-recent entry.
        /// </summary>
        internal void Set(string key, string value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (value == null) return;

            lock (_lock)
            {
                if (_map.TryGetValue(key, out var existingNode))
                {
                    existingNode.Value.Value = value;
                    _order.Remove(existingNode);
                    _order.AddLast(existingNode);
                }
                else
                {
                    if (_map.Count >= MaxEntries)
                    {
                        EvictOldest();
                    }

                    var entry = new CacheEntry { Key = key, Value = value };
                    var node = _order.AddLast(entry);
                    _map[key] = node;
                }
            }
        }

        /// <summary>
        /// Invalidates cache entries for a specific file.
        /// </summary>
        internal void InvalidateFile(string filePath)
        {
            if (filePath == null) return;

            lock (_lock)
            {
                var prefix = filePath + ":";

                var keysToRemove = new List<string>();
                foreach (var kvp in _map)
                {
                    if (kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        keysToRemove.Add(kvp.Key);
                }

                foreach (var key in keysToRemove)
                {
                    if (_map.TryGetValue(key, out var node))
                    {
                        _order.Remove(node);
                        _map.Remove(key);
                    }
                }
            }
        }

        /// <summary>
        /// Clears all cached entries.
        /// </summary>
        internal void Clear()
        {
            lock (_lock)
            {
                _map.Clear();
                _order.Clear();
            }
        }

        /// <summary>
        /// Evicts the oldest entries from the front of the LRU list. Called under lock.
        /// </summary>
        private void EvictOldest()
        {
            for (int i = 0; i < EvictCount && _order.First != null; i++)
            {
                var node = _order.First;
                _order.RemoveFirst();
                _map.Remove(node.Value.Key);
            }
        }

        private class CacheEntry
        {
            internal string Key;
            internal string Value;
        }

        /// <summary>
        /// Builds a cache key from file path, caret position, and prefix/suffix text.
        /// </summary>
        internal static string BuildKey(
            string filePath,
            int caretLine,
            int caretColumn,
            string prefix,
            string suffix)
        {
            return $"{filePath ?? string.Empty}\0{caretLine}\0{caretColumn}\0{prefix?.Length ?? 0}\0{suffix?.Length ?? 0}\0{StableHash(prefix ?? string.Empty):X8}\0{StableHash(suffix ?? string.Empty):X8}";
        }

        /// <summary>
        /// Deterministic, platform-independent hash function (FNV-1a style).
        /// </summary>
        private static int StableHash(string s)
        {
            unchecked
            {
                int hash = 17;
                foreach (char c in s)
                {
                    hash = hash * 31 + c;
                }

                return hash;
            }
        }
    }
}
