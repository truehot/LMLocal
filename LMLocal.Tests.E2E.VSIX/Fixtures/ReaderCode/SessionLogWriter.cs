using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BenchFixtures.ReaderCode
{
    /// <summary>
    /// Benchmark fixture (frozen, not compiled): writes a chat session to a plain-text log.
    /// Scenarios A2.3 and A2.5 depend on its content; MaxTitleLength and TruncateTitle sit at the
    /// end of the file on purpose, so a partial read misses them.
    /// </summary>
    internal sealed class SessionLogWriter
    {
        private const string Separator = "----------------------------------------";

        private readonly TextWriter _writer;

        public SessionLogWriter(TextWriter writer)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        /// <summary>Writes the header block: session id, start time and title.</summary>
        public void WriteSessionHeader(string sessionId, DateTime startedUtc, string title)
        {
            if (string.IsNullOrEmpty(sessionId))
                throw new ArgumentException("Session id is required.", nameof(sessionId));

            _writer.WriteLine(Separator);
            _writer.WriteLine("Session: " + sessionId);
            _writer.WriteLine("Started: " + startedUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC");
            _writer.WriteLine("Title: " + TruncateTitle(title));
            _writer.WriteLine(Separator);
        }

        /// <summary>Writes one message as "role: text", one line per source line.</summary>
        public void WriteMessage(string role, string text)
        {
            string prefix = string.IsNullOrEmpty(role) ? "unknown" : role;
            foreach (string line in SplitLines(text))
                _writer.WriteLine(prefix + ": " + line);
        }

        /// <summary>Writes the closing block with the message count.</summary>
        public void WriteSessionFooter(int messageCount)
        {
            _writer.WriteLine(Separator);
            _writer.WriteLine("Messages: " + messageCount.ToString(CultureInfo.InvariantCulture));
            _writer.WriteLine(Separator);
            _writer.Flush();
        }

        private static IEnumerable<string> SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                yield return string.Empty;
                yield break;
            }

            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                    yield return line;
            }
        }

        /// <summary>Longest title written to the header, in characters.</summary>
        internal const int MaxTitleLength = 120;

        /// <summary>Cuts a title to MaxTitleLength characters and appends an ellipsis when it was cut.</summary>
        internal static string TruncateTitle(string title)
        {
            if (string.IsNullOrEmpty(title) || title.Length <= MaxTitleLength)
                return title ?? string.Empty;

            var sb = new StringBuilder(title, 0, MaxTitleLength, MaxTitleLength + 3);
            sb.Append("...");
            return sb.ToString();
        }
    }
}
