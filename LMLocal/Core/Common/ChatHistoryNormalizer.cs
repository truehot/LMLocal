using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using LMLocal.Core.Models;

namespace LMLocal.Core.Common
{
    /// <summary>
    /// Normalizes chat message content before it is sent to the model for token-efficient
    /// history compression. Only safe transformations are applied - nothing that can change
    /// the semantic content:
    /// <list type="bullet">
    /// <item>runs of spaces/tabs are collapsed to a single space;</item>
    /// <item>three or more consecutive newlines are collapsed to two;</item>
    /// <item>leading/trailing whitespace is trimmed per line and overall;</item>
    /// <item>fenced code blocks are preserved verbatim (markdown inside is untouched).</item>
    /// </list>
    /// </summary>
    internal static class ChatHistoryNormalizer
    {
        private static readonly Regex CodeBlockRegex = new Regex(@"[`]{3,}(.+?)[`]{3,}", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex MultipleNewlinesRegex = new Regex(@"\n{3,}", RegexOptions.Compiled);

        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var codeBlocks = new List<string>();
            text = CodeBlockRegex.Replace(text, match =>
            {
                codeBlocks.Add(match.Groups[1].Value);
                return $"%%%CODE_{codeBlocks.Count - 1}%%%";
            });

            var result = Regex.Replace(text, @"[ \t]+", " ");
            result = MultipleNewlinesRegex.Replace(result, "\n\n");
            result = Regex.Replace(result, @"^ +| +$", "", RegexOptions.Multiline);
            result = result.Trim();

            if (codeBlocks.Count > 0)
            {
                var sb = new StringBuilder(result);
                for (int i = 0; i < codeBlocks.Count; i++)
                    sb.Replace($"%%%CODE_{i}%%%", codeBlocks[i]);
                result = sb.ToString();
            }

            return result;
        }

        public static List<ChatMessage> NormalizeMessages(IReadOnlyList<ChatMessage> messages)
        {
            if (messages == null || messages.Count == 0)
                return new List<ChatMessage>();

            var result = new List<ChatMessage>(messages.Count);
            foreach (var m in messages)
            {
                result.Add(new ChatMessage(m.Role, m.Content is string s ? Normalize(s) : m.Content)
                {
                    ToolCalls = m.ToolCalls,
                    ToolCallId = m.ToolCallId
                });
            }
            return result;
        }
    }
}
