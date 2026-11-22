using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search
{
    /// <summary>
    /// The single pagination contract shared by every paged tool (get_symbol_info, get_symbol_info_js, find_files,
    /// search_file_content): the cache holds the whole ordered result, and the page_token the tool hands out is a cursor
    /// "r7:50" = the entry of the cache (<see cref="SearchResultCache"/> gives the id) plus the offset of the next page.
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
        /// A cursor token "r7:50" gives its part after the colon.
        /// </summary>
        public static int ParseOffset(string token, int pageSize)
        {
            if (pageSize < 0)
                pageSize = 0;

            int maxOffset = int.MaxValue - pageSize;

            if (string.IsNullOrWhiteSpace(token))
                return 0;

            token = token.Trim();

            int colon = token.LastIndexOf(':');
            if (colon >= 0)
                token = token.Substring(colon + 1);

            if (!long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
                return IsAllDigits(token) ? maxOffset : 0;

            if (parsed <= 0)
                return 0;

            return parsed > maxOffset ? maxOffset : (int)parsed;
        }

        /// <summary>
        /// Plain-offset token of the next page, or null when <paramref name="nextOffset"/> has reached the end of the result set.
        /// </summary>
        public static string NextToken(int nextOffset, int total)
        {
            return nextOffset < total ? nextOffset.ToString(CultureInfo.InvariantCulture) : null;
        }

        /// <summary>Token of the page that starts at <paramref name="offset"/> in the cache entry <paramref name="cacheId"/>: "r7:50".</summary>
        public static string MakeToken(string cacheId, int offset)
        {
            return cacheId + ":" + offset.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The next_page_token of a page: null at the end of the result set, otherwise the cursor "r7:50".
        /// </summary>
        public static string NextToken(int nextOffset, int total, string cacheId)
        {
            if (nextOffset >= total)
                return null;

            return string.IsNullOrEmpty(cacheId) ? nextOffset.ToString(CultureInfo.InvariantCulture) : MakeToken(cacheId, nextOffset);
        }

        /// <summary>
        /// The 'showing' text of a page: only data, never an instruction. "page 2 of 3", "page 1 of 1" for a result that fits one page,
        /// "past the end (120 total)" for an offset beyond the list; null for an empty result.
        /// </summary>
        public static string Describe(int offset, int shown, int total, bool hasNext, int pageSize)
        {
            if (total <= 0)
                return null;

            if (offset >= total || shown <= 0)
                return string.Format(CultureInfo.InvariantCulture, "past the end ({0} total)", total);

            if (pageSize <= 0)
                pageSize = shown;

            long last = (long)offset + shown;
            long before = ((long)offset + pageSize - 1) / pageSize;
            long left = Math.Max(0, total - last);
            long after = (left + pageSize - 1) / pageSize;
            return string.Format(CultureInfo.InvariantCulture, "page {0} of {1}", before + 1, before + 1 + after);
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

    internal enum PageTokenKind
    {
        /// <summary>No page_token in the call: the first page.</summary>
        None,

        /// <summary>A cursor "r7:50" or "r7:last" / "r7:first".</summary>
        Cursor,

        /// <summary>An entry id without the offset ("r7").</summary>
        IncompleteCursor,

        /// <summary>A bare number ("50"): not a token a tool hands out.</summary>
        Number,

        /// <summary>The bare word "last" or "first".</summary>
        Jump,

        /// <summary>Anything else.</summary>
        Unreadable
    }

    internal enum PageJump
    {
        None,
        First,
        Last
    }

    /// <summary>
    /// A page_token as the model wrote it.
    /// </summary>
    internal sealed class PageToken
    {
        private static readonly Regex CursorPattern = new Regex(@"^r(?<id>\d{1,9})\s*[:\-./]\s*(?<rest>\d+|last|first)$", RegexOptions.CultureInvariant);
        private static readonly Regex IdOnlyPattern = new Regex(@"^r\d{1,9}$", RegexOptions.CultureInvariant);
        private static readonly char[] Wrappers = { '"', '\'', '`', '(', ')', '[', ']', '<', '>', '{', '}', ' ', '\t' };

        public PageTokenKind Kind { get; private set; }

        /// <summary>The token as the call had it.</summary>
        public string Raw { get; private set; }

        /// <summary>Entry id of a cursor: "r7".</summary>
        public string Id { get; private set; }

        /// <summary>Offset of a cursor or a number; 0 for the others.</summary>
        public int Offset { get; private set; }

        public PageJump Jump { get; private set; }

        public bool IsGiven => Kind != PageTokenKind.None;

        public static PageToken Parse(string token)
        {
            var result = new PageToken { Kind = PageTokenKind.None, Raw = token };
            if (string.IsNullOrWhiteSpace(token))
                return result;

            string text = token.Trim(Wrappers).ToLowerInvariant();
            if (text.Length == 0)
            {
                result.Kind = PageTokenKind.Unreadable;
                return result;
            }

            if (text == "last" || text == "first")
            {
                result.Kind = PageTokenKind.Jump;
                result.Jump = text == "last" ? PageJump.Last : PageJump.First;
                return result;
            }

            var cursor = CursorPattern.Match(text);
            if (cursor.Success)
            {
                result.Kind = PageTokenKind.Cursor;
                result.Id = "r" + long.Parse(cursor.Groups["id"].Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

                string rest = cursor.Groups["rest"].Value;
                if (rest == "last")
                    result.Jump = PageJump.Last;
                else if (rest == "first")
                    result.Jump = PageJump.First;
                else
                    result.Offset = ToolPagination.ParseOffset(rest);
                return result;
            }

            if (IdOnlyPattern.IsMatch(text))
            {
                result.Kind = PageTokenKind.IncompleteCursor;
                return result;
            }

            if (IsDigits(text))
            {
                result.Kind = PageTokenKind.Number;
                result.Offset = ToolPagination.ParseOffset(text);
                return result;
            }

            result.Kind = PageTokenKind.Unreadable;
            return result;
        }

        private static bool IsDigits(string text)
        {
            foreach (char c in text)
            {
                if (c < '0' || c > '9')
                    return false;
            }

            return text.Length > 0;
        }
    }

    /// <summary>
    /// What a call does with its page_token: which cache entry it names, the offset of the page and the warning or error to give back.
    /// </summary>
    internal sealed class PageSelection
    {
        public const string StaleTokenWarning = "page_token is no longer valid (a file was edited, or the solution or chat session changed), so the result was computed again from the parameters of this call; it can differ from the pages you already have.";
        public const string ForeignTokenWarning = "page_token was ignored: it belongs to another request than this call names, so the result was computed from the parameters of this call.";

        private PageSelection(PageToken token)
        {
            Token = token;
        }

        public PageToken Token { get; }

        /// <summary>The call must fail with this message (a token that cannot be repaired: an entry id without an offset).</summary>
        public string Error { get; private set; }

        /// <summary>Set when the token could not be used as given; goes into the response as 'warning'.</summary>
        public string Warning { get; private set; }

        /// <summary>The token named an entry that exists and that belongs to this call: the page comes from it.</summary>
        public bool UseEntry { get; private set; }

        /// <summary>The entry exists but the call names another subject: the token is ignored and the result computed from the parameters.</summary>
        public bool Foreign { get; private set; }

        public bool IsCursor => Token.Kind == PageTokenKind.Cursor;

        public string Id => Token.Id;

        public static PageSelection From(Dictionary<string, object> parameters)
        {
            var selection = new PageSelection(PageToken.Parse(ToolPagination.ReadToken(parameters)));
            string raw = selection.Token.Raw;

            switch (selection.Token.Kind)
            {
                case PageTokenKind.IncompleteCursor:
                    selection.Error = $"page_token '{raw}' is incomplete: pass the next_page_token of the previous page exactly as returned, or 'last' / 'first'.";
                    break;
                case PageTokenKind.Number:
                    selection.Warning = $"page_token '{raw}' is not a next_page_token a response returned: the page was taken from the parameters of this call. Pass next_page_token exactly as returned.";
                    break;
                case PageTokenKind.Unreadable:
                    selection.Warning = $"page_token '{raw}' was not understood and was ignored: the first page is returned. Pass next_page_token exactly as returned, or 'last' / 'first'.";
                    break;
            }

            return selection;
        }

        /// <summary>
        /// Reports the lookup of a cursor: <paramref name="found"/> = an entry of the right type has this id, <paramref name="matchesCall"/> =
        /// the call names the same subject as the entry (or names none).
        /// </summary>
        public void Resolve(bool found, bool matchesCall)
        {
            if (!IsCursor)
                return;

            if (found && matchesCall)
            {
                UseEntry = true;
                return;
            }

            if (found)
            {
                Foreign = true;
                Warning = ForeignTokenWarning;
                return;
            }

            Warning = StaleTokenWarning;
        }

        /// <summary>
        /// Offset of the page. "last" = the last <paramref name="pageSize"/> items (all of them when the result is shorter), counted from
        /// <paramref name="total"/>, whatever offset the token had; "first" = 0.
        /// </summary>
        public int Offset(int pageSize, int total)
        {
            if (pageSize < 0)
                pageSize = 0;

            if (Token.Jump == PageJump.Last)
                return Math.Max(0, total - pageSize);

            if (Token.Jump == PageJump.First || Foreign)
                return 0;

            if (Token.Kind == PageTokenKind.Cursor || Token.Kind == PageTokenKind.Number)
                return Math.Min(Token.Offset, int.MaxValue - pageSize);

            return 0;
        }

        /// <summary>
        /// The message for a call that cannot compute the result itself (a required parameter is missing) when it carried a page_token
        /// that found nothing to continue.
        /// </summary>
        public string WithPagingHint(string error)
        {
            if (!Token.IsGiven)
                return error;

            return "page_token cannot be used (it is not the next_page_token of a result that is still available) and this call lacks the parameters to compute the result again: repeat the first request, with all its parameters, without page_token. " + error;
        }
    }
}
