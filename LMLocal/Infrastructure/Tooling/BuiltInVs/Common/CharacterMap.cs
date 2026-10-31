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
        /// Characters that disappear during canonicalization.
        /// </summary>
        public static bool IsDeleted(char c) => c == '\u200B' || c == '\uFEFF';

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

            // Step 1: pure ASCII is already NFC — return the same instance, no allocation, no globalization call.
            if (!hasNonAscii)
                return input;

            string result = hasTableChar ? MapChars(input) : input;

            // Step 2: IsNormalized scans without allocating and throws on a lone surrogate, so the surrogate check
            // comes first and only then is the NFC decision made — the check is explicit, not an exception handler.
            if (hasUnpairedSurrogate)
                return result;

            return result.IsNormalized(NormalizationForm.FormC)
                ? result
                : result.Normalize(NormalizationForm.FormC);
        }

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
                    builder.Append(c);
                    builder.Append(input[i + 1]);
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
