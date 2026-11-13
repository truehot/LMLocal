using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BenchFixtures.ReaderCode
{
    /// <summary>
    /// Benchmark fixture (frozen, not compiled): reads a server-sent events stream and collects
    /// the text and the tool call frames it carries. Scenarios A2.2 and A2.3 depend on its content.
    /// </summary>
    internal sealed class SseFrameParser
    {
        private const string DataPrefix = "data:";
        private const string DoneMarker = "[DONE]";

        private readonly SseLineSplitter _splitter;

        public SseFrameParser(SseLineSplitter splitter)
        {
            _splitter = splitter ?? throw new ArgumentNullException(nameof(splitter));
        }

        /// <summary>
        /// Reads frames until the stream ends or the done marker arrives.
        /// </summary>
        public async Task<IReadOnlyList<FrameRecord>> ParseFramesAsync(Stream stream, CancellationToken cancellationToken)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            var frames = new List<FrameRecord>();
            var text = new StringBuilder();

            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                string line;
                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!line.StartsWith(DataPrefix, StringComparison.Ordinal))
                        continue;

                    string payload = line.Substring(DataPrefix.Length).Trim();
                    if (payload == DoneMarker)
                        break;

                    foreach (string part in _splitter.Split(payload))
                    {
                        if (part.StartsWith("tool:", StringComparison.Ordinal))
                            frames.Add(new FrameRecord(frames.Count, part.Substring(5)));
                        else
                            text.Append(part);
                    }
                }
            }

            frames.Add(new FrameRecord(frames.Count, text.ToString()) { IsText = true });
            return frames;
        }
    }

    /// <summary>One parsed frame: a tool call payload or the accumulated text.</summary>
    internal sealed class FrameRecord
    {
        public FrameRecord(int index, string payload)
        {
            Index = index;
            Payload = payload ?? string.Empty;
        }

        public int Index { get; }

        public string Payload { get; }

        public bool IsText { get; set; }
    }
}
