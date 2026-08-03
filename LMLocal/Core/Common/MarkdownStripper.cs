using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using LMLocal.Core.Models;

namespace LMLocal.Core.Common
{
    internal static class MarkdownStripper
    {
        private static readonly Regex CodeBlockRegex = new Regex(@"```(.+?)```", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex HeaderRegex = new Regex(@"^#{1,6}\s+", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex BoldRegex = new Regex(@"(?<!\w)\*\*(.+?)\*\*(?!\w)", RegexOptions.Compiled);
        private static readonly Regex ItalicRegex = new Regex(@"(?<!\w)\*(.+?)\*(?!\w)", RegexOptions.Compiled);
        private static readonly Regex StrikethroughRegex = new Regex(@"~~(.+?)~~", RegexOptions.Compiled);
        private static readonly Regex InlineCodeRegex = new Regex(@"(?<!\w)`(.+?)`(?!\w)", RegexOptions.Compiled);
        private static readonly Regex LinkRegex = new Regex(@"\[(.+?)\]\(.+?\)", RegexOptions.Compiled);
        private static readonly Regex ImageRegex = new Regex(@"!\[(.+?)\]\(.+?\)", RegexOptions.Compiled);
        private static readonly Regex UnorderedListRegex = new Regex(@"^\s*[-*+]\s+", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex OrderedListRegex = new Regex(@"^\s*\d+\.\s+", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex BlockquoteRegex = new Regex(@"^\s*>\s+", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex HrRegex = new Regex(@"^[-_*]{3,}\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex MultipleNewlinesRegex = new Regex(@"\n{3,}", RegexOptions.Compiled);

        public static string Strip(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var codeBlocks = new List<string>();
            text = CodeBlockRegex.Replace(text, match =>
            {
                codeBlocks.Add(match.Groups[1].Value);
                return $"%%%CODE_{codeBlocks.Count - 1}%%%";
            });

            text = HeaderRegex.Replace(text, "");
            text = BoldRegex.Replace(text, "$1");
            text = ItalicRegex.Replace(text, "$1");
            text = StrikethroughRegex.Replace(text, "$1");
            text = InlineCodeRegex.Replace(text, "$1");
            text = LinkRegex.Replace(text, "$1");
            text = ImageRegex.Replace(text, "$1");
            text = UnorderedListRegex.Replace(text, "");
            text = OrderedListRegex.Replace(text, "");
            text = BlockquoteRegex.Replace(text, "");
            text = HrRegex.Replace(text, "");
            text = MultipleNewlinesRegex.Replace(text, "\n\n");

            if (codeBlocks.Count > 0)
            {
                var sb = new StringBuilder(text);
                for (int i = 0; i < codeBlocks.Count; i++)
                {
                    sb.Replace($"%%%CODE_{i}%%%", codeBlocks[i]);
                }
                text = sb.ToString();
            }

            return text.Trim();
        }

        /// <summary>
        /// Creates a new list of ChatMessages with stripped content, preserving all other fields.
        /// </summary>
        public static List<ChatMessage> StripMessages(IReadOnlyList<ChatMessage> messages)
        {
            if (messages == null || messages.Count == 0)
                return new List<ChatMessage>();

            var result = new List<ChatMessage>(messages.Count);
            foreach (var m in messages)
            {
                result.Add(new ChatMessage(m.Role, m.Content is string s ? Strip(s) : m.Content)
                {
                    ToolCalls = m.ToolCalls,
                    ToolCallId = m.ToolCallId
                });
            }
            return result;
        }
    }
}
