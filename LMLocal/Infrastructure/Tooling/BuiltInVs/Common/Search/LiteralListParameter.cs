using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search
{
    /// <summary>
    /// Reads a list-of-strings tool parameter ('texts' of search_file_content, 'file_names' of find_files).
    /// </summary>
    internal static class LiteralListParameter
    {
        /// <summary>
        /// Adds the items of <paramref name="value"/>: an array, or an array serialized into a string. A plain string is
        /// one item: it is not split on commas, because a literal may contain them. Returns false for any other value.
        /// </summary>
        internal static bool TryAdd(object value, List<string> items, HashSet<string> seen)
        {
            if (value is JValue scalar)
                value = scalar.Value;

            if (value is string text)
            {
                string trimmed = text.Trim();
                if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
                {
                    try
                    {
                        return TryAdd(JArray.Parse(trimmed), items, seen);
                    }
                    catch (JsonException)
                    {
                        // Not JSON: a literal that starts with '['.
                    }
                }

                Add(text, items, seen);
                return true;
            }

            if (value is IEnumerable list)
            {
                foreach (object item in list)
                {
                    object v = item is JValue j ? j.Value : item;
                    if (v == null)
                        continue;
                    if (!(v is string s))
                        return false;
                    Add(s, items, seen);
                }
                return true;
            }

            return false;
        }

        /// <summary>Adds a non-empty item once; <paramref name="seen"/> decides what counts as a duplicate.</summary>
        internal static void Add(string item, List<string> items, HashSet<string> seen)
        {
            if (!string.IsNullOrEmpty(item) && seen.Add(item))
                items.Add(item);
        }

        /// <summary>True when the parameter is absent or JSON null.</summary>
        internal static bool IsMissing(object value)
        {
            return value == null || (value is JValue jv && jv.Value == null);
        }
    }
}
