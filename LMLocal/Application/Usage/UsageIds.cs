using System;

namespace LMLocal.Application.Usage
{
    /// <summary>
    /// Formats and compares the session ids written into usage records, so every writer and the reader agree on one form.
    /// </summary>
    internal static class UsageIds
    {
        /// <summary>
        /// Session id in the format of the chat log ("D"), so a script can match a usage record to its log file.
        /// Null only when there is no id at all.
        /// </summary>
        internal static string SessionId(Guid? sessionId) => sessionId?.ToString("D");

        /// <summary>
        /// Session id without dashes and in lower case, so a record written before the id was formatted like the chat log
        /// still matches its session.
        /// </summary>
        internal static string Normalize(string sessionId)
        {
            return string.IsNullOrEmpty(sessionId) ? string.Empty : sessionId.Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
