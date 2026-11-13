using System;
using System.Collections.Generic;
using System.Globalization;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search
{
    /// <summary>
    /// The single pagination contract shared by every paged tool (get_symbol_info, get_symbol_info_js,
    /// find_files, search_file_content): the cache holds the whole ordered result and page_token is an
    /// offset from the start of that list.
    /// </summary>
    internal static class ToolPagination
    {
        public const string TokenParameter = "page_token";

        /// <summary>
        /// Value of page_token as text, whatever JSON type it arrived as ("50" and 50 are equivalent);
        /// null when the parameter is missing, null, empty or whitespace.
        /// </summary>
        public static string ReadToken(Dictionary<string, object> parameters)
        {
            if (parameters == null || !parameters.TryGetValue(TokenParameter, out object value) || value == null)
                return null;

            if (!(value is string token))
                token = Convert.ToString(value, CultureInfo.InvariantCulture);

            token = token?.Trim();
            return string.IsNullOrEmpty(token) ? null : token;
        }

        /// <summary>
        /// Offset from the token: a non-numeric or negative token gives 0 (first page); a numeric token
        /// beyond int.MaxValue gives int.MaxValue, so it is treated as past the end of the list instead of
        /// silently restarting from the first page.
        /// </summary>
        public static int ParseOffset(string token)
        {
            return ParseOffset(token, 0);
        }

        /// <summary>
        /// Offset from the token, capped to int.MaxValue - pageSize so that offset + pageSize (the next offset) can never overflow.
        /// </summary>
        public static int ParseOffset(string token, int pageSize)
        {
            if (pageSize < 0)
                pageSize = 0;

            int maxOffset = int.MaxValue - pageSize;

            if (string.IsNullOrWhiteSpace(token))
                return 0;

            token = token.Trim();

            if (!long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
                return IsAllDigits(token) ? maxOffset : 0;

            if (parsed <= 0)
                return 0;

            return parsed > maxOffset ? maxOffset : (int)parsed;
        }

        /// <summary>
        /// Token of the next page, or null when <paramref name="nextOffset"/> has reached the end of the
        /// result set.
        /// </summary>
        public static string NextToken(int nextOffset, int total)
        {
            return nextOffset < total ? nextOffset.ToString(CultureInfo.InvariantCulture) : null;
        }

        private static bool IsAllDigits(string token)
        {
            if (string.IsNullOrEmpty(token))
                return false;

            foreach (char c in token)
            {
                if (c < '0' || c > '9')
                    return false;
            }

            return true;
        }
    }
}
