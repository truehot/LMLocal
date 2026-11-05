using System.Text;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common
{
    /// <summary>
    /// Detects text that a model passed with one extra level of JSON string escaping (e.g. <c>\"</c> for <c>"</c>), a known failure mode when tool arguments are written from JSON-looking text.
    /// </summary>
    internal static class JsonEscapes
    {
        /// <summary>
        /// Removes one level of JSON string escaping in a single left-to-right pass: <c>\\</c>, <c>\"</c>, <c>\/</c>, <c>\n</c>, <c>\r</c>, <c>\t</c>. Any other backslash sequence is kept as-is.
        /// </summary>
        public static bool TryUnescape(string text, out string unescaped)
        {
            unescaped = text;
            if (string.IsNullOrEmpty(text) || text.IndexOf('\\') < 0)
                return false;

            var sb = new StringBuilder(text.Length);
            bool changed = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c != '\\' || i + 1 >= text.Length)
                {
                    sb.Append(c);
                    continue;
                }

                char next = text[i + 1];
                string replacement;
                switch (next)
                {
                    case '\\': replacement = "\\"; break;
                    case '"': replacement = "\""; break;
                    case '/': replacement = "/"; break;
                    case 'n': replacement = "\n"; break;
                    case 'r': replacement = "\r"; break;
                    case 't': replacement = "\t"; break;
                    default: replacement = null; break;
                }

                if (replacement == null)
                {
                    sb.Append(c);
                    continue;
                }

                sb.Append(replacement);
                changed = true;
                i++;
            }

            if (!changed)
                return false;

            unescaped = sb.ToString();
            return true;
        }
    }
}
