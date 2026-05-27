using System.Text.RegularExpressions;

namespace LMLocal.Common
{
    internal static class MarkdownStripper
    {
        public static string Strip(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = Regex.Replace(text, @"(?s)```[\w-]*(?:\s+[^\r\n]*)?\r?\n(.*?)\r?\n```", "$1");
            text = Regex.Replace(text, @"#{1,6}\s+", "");
            text = Regex.Replace(text, @"\*\*(.+?)\*\*", "$1");
            text = Regex.Replace(text, @"__(.+?)__", "$1");
            text = Regex.Replace(text, @"\*(.+?)\*", "$1");
            text = Regex.Replace(text, @"_(.+?)_", "$1");
            text = Regex.Replace(text, @"~~(.+?)~~", "$1");
            text = Regex.Replace(text, @"`(.+?)`", "$1");
            text = Regex.Replace(text, @"\[(.+?)\]\(.+?\)", "$1");
            text = Regex.Replace(text, @"!\[(.+?)\]\(.+?\)", "$1");
            text = Regex.Replace(text, @"^\s*[-*+]\s+", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"^\s*\d+\.\s+", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"^\s*>\s+", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"^[-_*]{3,}\s*$", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"\n{3,}", "\n\n");
            return text.Trim();
        }
    }
}
