using System;
using System.Collections.Generic;
using LMLocal.Application.Chat;

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

                if (parameter.Value is string text && text.IndexOf(Marker, StringComparison.Ordinal) >= 0)
                    return RejectionMessage;
            }

            return null;
        }
    }
}
