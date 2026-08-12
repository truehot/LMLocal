using System.Collections.Generic;
using System.Linq;
using LMLocal.Core.Models;

namespace LMLocal.Application.Chat
{
    /// <summary>
    /// Extracts text from chat message Content, which is either a plain string or a multimodal List<ContentPart> (base64 images in memory).
    /// </summary>
    internal static class ContentTextExtractor
    {
        /// <summary>
        /// Returns the concatenated text of a message content, ignoring image parts.
        /// </summary>
        internal static string ExtractTextContent(object content)
        {
            if (content is string s) return s;
            if (content is List<ContentPart> parts)
                return string.Concat(parts.Where(p => p.Type == "text").Select(p => p.Text));
            return content?.ToString() ?? "";
        }

        /// <summary>
        /// Returns the length of the textual portion of a message content (used for compaction heuristics).
        /// </summary>
        internal static int ExtractTextLength(object content)
        {
            if (content is string s) return s.Length;
            if (content is List<ContentPart> parts)
                return parts.Where(p => p.Type == "text").Sum(p => p.Text?.Length ?? 0);
            return content?.ToString()?.Length ?? 0;
        }
    }
}
