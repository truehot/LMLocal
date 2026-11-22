using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common
{
    /// <summary>
    /// Single source of truth for characters that the transport rewrites (model tokenizer output) and for the ASCII equivalents used when a character cannot be encoded by the file's encoding.
    /// </summary>
    internal static class CharacterMap
    {
        /// <summary>
        /// Mapping table used by both projections. Every key is non-ASCII, so "contains a table character" implies "contains a non-ASCII character".
        /// </summary>
        private static readonly Dictionary<char, string> Table = new Dictionary<char, string>
        {
            // Spaces
            { '\u00A0', " " },  // no-break space
            { '\u2009', " " },  // thin space
            { '\u200A', " " },  // hair space
            { '\u2003', " " },  // em space
            { '\u3000', " " },  // ideographic space
            { '\u2028', " " },  // line separator
            { '\u0085', " " },  // next line (NEL)

            // Quotes
            { '\u2018', "'" }, { '\u2019', "'" }, { '\u201A', "'" }, { '\u201B', "'" },
            { '\u201C', "\"" }, { '\u201D', "\"" }, { '\u201E', "\"" },

            // Dashes
            { '\u2012', "-" }, { '\u2013', "-" }, { '\u2014', "-" }, { '\u2015', "-" },

            // Ellipsis
            { '\u2026', "..." },

            // Arrows
            { '\u2192', "->" }, { '\u2190', "<-" }
        };

        /// <summary>
        /// Length of the longest ASCII equivalent in the table (currently "..." = 3).
        /// </summary>
        public static readonly int MaxFallbackLength = Table.Values.Max(v => v.Length);

        /// <summary>
        /// Characters that disappear during canonicalization: invisible characters the model's tokenizer may drop or
        /// replace, so old_lines can come without them. The file keeps them on lines the model did not change (restore).
        /// Not deleted: tab, CR/LF (line structure), U+0085 and the other spaces of the table (mapped to a space), and
        /// ZWJ/ZWNJ (U+200D/U+200C: they shape emoji and scripts). A supplementary noncharacter is a surrogate pair -
        /// see <see cref="IsDeletedCodePoint"/>.
        /// </summary>
        public static bool IsDeleted(char c)
        {
            if (c < 0x20)
                return c != '\t' && c != '\n' && c != '\r';           // C0 controls

            if (c < 0x7F)
                return false;

            if (c <= 0x9F)
                return c != '\u0085';                                // DEL and C1 controls; NEL is a space

            switch (c)
            {
                case '\u00AD':                                       // soft hyphen
                case '\u200B':                                       // zero width space
                case '\u200E': case '\u200F':                        // LRM, RLM
                case '\u2060':                                       // word joiner
                case '\uFEFF':                                       // BOM used as content
                case '\uFFFD':                                       // replacement character
                    return true;
            }

            if ((c >= '\u202A' && c <= '\u202E') || (c >= '\u2066' && c <= '\u2069'))
                return true;                                         // bidi embeddings, overrides, isolates

            return IsNoncharacter(c);
        }

        /// <summary>
        /// Code points of <paramref name="line"/> that canonicalization deletes, as distinct "U+XXXX" labels in order of
        /// appearance; empty when there are none.
        /// </summary>
        public static List<string> DescribeDeleted(string line)
        {
            var labels = new List<string>();
            if (string.IsNullOrEmpty(line))
                return labels;

            for (int i = 0; i < line.Length; i++)
            {
                int codePoint = line[i];
                if (char.IsHighSurrogate(line[i]) && i + 1 < line.Length && char.IsLowSurrogate(line[i + 1]))
                {
                    codePoint = char.ConvertToUtf32(line[i], line[i + 1]);
                    i++;
                }

                if (!IsDeletedCodePoint(codePoint))
                    continue;

                string label = "U+" + codePoint.ToString("X4");
                if (!labels.Contains(label))
                    labels.Add(label);
            }

            return labels;
        }

        /// <summary>
        /// <see cref="IsDeleted(char)"/> for a whole code point, so a supplementary noncharacter (U+1FFFF, a surrogate
        /// pair) is deleted as one unit.
        /// </summary>
        public static bool IsDeletedCodePoint(int codePoint)
            => codePoint <= 0xFFFF ? IsDeleted((char)codePoint) : IsNoncharacter(codePoint);

        /// <summary>
        /// Read-only view of the table, so tests can assert table-wide invariants without duplicating the data.
        /// </summary>
        public static IReadOnlyDictionary<char, string> Entries => Table;

        /// <summary>
        /// Form of <paramref name="input"/> used for comparison.
        /// </summary>
        public static string Canonicalize(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            bool hasTableChar = false;
            bool hasNonAscii = false;
            bool hasUnpairedSurrogate = false;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c > 0x7F)
                    hasNonAscii = true;

                if (char.IsHighSurrogate(c) && i + 1 < input.Length && char.IsLowSurrogate(input[i + 1]))
                {
                    if (IsNoncharacter(char.ConvertToUtf32(c, input[i + 1])))
                        hasTableChar = true;
                    i++;
                    continue;
                }

                if (char.IsSurrogate(c))
                {
                    hasUnpairedSurrogate = true;
                    continue;
                }

                if (NeedsMapping(c))
                    hasTableChar = true;
            }

            // Step 1: printable ASCII is already NFC — return the same instance, no allocation, no globalization call.
            // ASCII controls are deleted, so they go through MapChars even without a non-ASCII character.
            if (!hasNonAscii && !hasTableChar)
                return input;

            string result = hasTableChar ? MapChars(input) : input;

            // Step 2: IsNormalized scans without allocating and throws on a lone surrogate or a noncharacter. Noncharacters
            // are already deleted by MapChars; a lone surrogate is checked explicitly before the NFC decision — the
            // check is explicit, not an exception handler.
            if (hasUnpairedSurrogate || !hasNonAscii)
                return result;

            return result.IsNormalized(NormalizationForm.FormC)
                ? result
                : result.Normalize(NormalizationForm.FormC);
        }

        /// <summary>
        /// Unicode noncharacter (U+FDD0..U+FDEF and the last two code points of every plane, such as U+FFFF):
        /// .NET Framework normalization throws "String contains invalid Unicode code points" on these, so they must be
        /// deleted before IsNormalized/Normalize.
        /// </summary>
        private static bool IsNoncharacter(int codePoint)
            => (codePoint >= 0xFDD0 && codePoint <= 0xFDEF) || (codePoint & 0xFFFE) == 0xFFFE;

        /// <summary>
        /// ASCII equivalent for a character the file encoding cannot represent.
        /// </summary>
        public static string Fallback(char c)
        {
            if (IsDeleted(c))
                return string.Empty;

            return Table.TryGetValue(c, out string replacement) ? replacement : "?";
        }

        private static bool NeedsMapping(char c) => IsDeleted(c) || Table.ContainsKey(c);

        private static string MapChars(string input)
        {
            var builder = new StringBuilder(input.Length);

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (char.IsHighSurrogate(c) && i + 1 < input.Length && char.IsLowSurrogate(input[i + 1]))
                {
                    if (!IsNoncharacter(char.ConvertToUtf32(c, input[i + 1])))
                    {
                        builder.Append(c);
                        builder.Append(input[i + 1]);
                    }
                    i++;
                    continue;
                }

                if (IsDeleted(c))
                    continue;

                builder.Append(Table.TryGetValue(c, out string replacement) ? replacement : c.ToString());
            }

            return builder.ToString();
        }
    }
}
