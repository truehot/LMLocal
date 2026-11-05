using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search
{
    internal enum SearchFallbackKind
    {
        None,

        /// <summary>The text was split on ' OR ' / ' | ' / ' || ' into separate literal terms.</summary>
        OrSplit,

        /// <summary>Quotes wrapping the whole text were removed.</summary>
        Unquoted
    }

    /// <summary>
    /// Fallback variants for a search_file_content query that the model wrote as if the search supported boolean operators or phrase quotes.
    /// </summary>
    internal sealed class SearchQueryFallback
    {
        private static readonly Regex OrSeparator = new Regex(@"\s+(?:OR|\|\|?)\s+", RegexOptions.CultureInvariant);

        public static readonly SearchQueryFallback None = new SearchQueryFallback(SearchFallbackKind.None, new string[0]);

        public SearchFallbackKind Kind { get; }

        /// <summary>Distinct literal terms to match (case-insensitive), in the order they appear in the text.</summary>
        public IReadOnlyList<string> Terms { get; }

        public bool HasTerms => Terms.Count > 0;

        private SearchQueryFallback(SearchFallbackKind kind, IReadOnlyList<string> terms)
        {
            Kind = kind;
            Terms = terms;
        }

        public static SearchQueryFallback Create(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return None;

            string trimmed = text.Trim();

            string unquoted = Unquote(trimmed);

            string[] parts = OrSeparator.Split(unquoted);
            if (parts.Length > 1)
            {
                var terms = Distinct(parts.Select(p => Unquote(p.Trim())));
                if (terms.Count > 1)
                    return new SearchQueryFallback(SearchFallbackKind.OrSplit, terms);
                if (terms.Count == 1 && !string.Equals(terms[0], trimmed, StringComparison.Ordinal))
                    return new SearchQueryFallback(SearchFallbackKind.OrSplit, terms);
                return None;
            }

            if (unquoted.Length > 0 && !string.Equals(unquoted, trimmed, StringComparison.Ordinal))
                return new SearchQueryFallback(SearchFallbackKind.Unquoted, new[] { unquoted });

            return None;
        }

        /// <summary>
        /// Returns the first fallback term found in <paramref name="line"/> (case-insensitive), or null.
        /// </summary>
        public string FindTerm(string line)
        {
            if (string.IsNullOrEmpty(line))
                return null;

            foreach (var term in Terms)
            {
                if (line.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                    return term;
            }
            return null;
        }

        /// <summary>
        /// Explanation added to the response when the fallback was used, so the caller does not mistake the union of terms for a literal match of the original text.
        /// </summary>
        public string BuildNote(int totalMatches)
        {
            string quotedTerms = string.Join(", ", Terms.Select(t => "'" + t + "'"));
            string outcome = totalMatches > 0
                ? "Results below are for the fallback, not for the original text."
                : "The fallback found no matches either.";

            switch (Kind)
            {
                case SearchFallbackKind.OrSplit:
                    return "The literal text has no matches. The search is literal and does not support OR; the text was split and each term was searched separately: "
                        + quotedTerms + ". " + outcome + " Each match has 'matched_term'. Prefer one literal term per call.";
                case SearchFallbackKind.Unquoted:
                    return "The literal text (including its surrounding quotes) has no matches; the quotes were removed and "
                        + quotedTerms + " was searched instead. " + outcome;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Removes double quotes wrapping the whole value and unescapes inner \" sequences.
        /// </summary>
        private static string Unquote(string value)
        {
            if (value.Length < 2 || value[0] != '"' || value[value.Length - 1] != '"')
                return value;

            string inner = value.Substring(1, value.Length - 2);
            for (int i = 0; i < inner.Length; i++)
            {
                if (inner[i] == '"' && (i == 0 || inner[i - 1] != '\\'))
                    return value;
            }

            return inner.Replace("\\\"", "\"").Trim();
        }

        private static List<string> Distinct(IEnumerable<string> values)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            foreach (var v in values)
            {
                if (!string.IsNullOrEmpty(v) && seen.Add(v))
                    list.Add(v);
            }
            return list;
        }
    }
}
