using System;
using System.Collections;
using System.Collections.Generic;
using LMLocal.Application.Chat;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common
{
    /// <summary>
    /// Rejects write calls that carry compacted-history markers instead of real content.
    /// </summary>
    internal static class PrunedContentGuard
    {
        /// <summary>
        /// Marker the history pruner leaves in compressed bodies and arguments.
        /// </summary>
        internal const string Marker = ToolHistoryPruner.Marker;

        /// <summary>
        /// Key the history pruner adds to compressed write-tool arguments.
        /// </summary>
        internal const string PrunedKey = ToolHistoryPruner.PrunedKey;

        private const string RejectionMessage =
            "This argument is a marker of compacted history, not real content. " +
            "Call read_file_lines for the file, then send the actual text.";

        /// <summary>
        /// Returns the rejection message when the call carries a pruning marker, or null when it is a normal call.
        /// </summary>
        internal static string Validate(Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return null;

            foreach (var parameter in parameters)
            {
                if (string.Equals(parameter.Key, PrunedKey, StringComparison.OrdinalIgnoreCase))
                    return RejectionMessage;

                if (CarriesMarker(parameter.Value, 0))
                    return RejectionMessage;
            }

            return null;
        }

        /// <summary>Nesting depth checked inside array arguments such as replace_file_lines 'edits'.</summary>
        private const int MaxDepth = 4;

        /// <summary>
        /// True when the value is a string with the marker, or an object / array (replace_file_lines 'edits') that holds one
        /// or a pruned-flag key.
        /// </summary>
        private static bool CarriesMarker(object value, int depth)
        {
            if (value == null || depth > MaxDepth)
                return false;

            if (value is JValue jv)
                value = jv.Value;

            if (value is string text)
                return text.IndexOf(Marker, StringComparison.Ordinal) >= 0;

            if (value is JObject obj)
            {
                foreach (var property in obj.Properties())
                {
                    if (string.Equals(property.Name, PrunedKey, StringComparison.OrdinalIgnoreCase) || CarriesMarker(property.Value, depth + 1))
                        return true;
                }
                return false;
            }

            if (value is IDictionary<string, object> dict)
            {
                foreach (var entry in dict)
                {
                    if (string.Equals(entry.Key, PrunedKey, StringComparison.OrdinalIgnoreCase) || CarriesMarker(entry.Value, depth + 1))
                        return true;
                }
                return false;
            }

            if (value is IEnumerable items)
            {
                foreach (var item in items)
                {
                    if (CarriesMarker(item, depth + 1))
                        return true;
                }
            }

            return false;
        }
    }
}
