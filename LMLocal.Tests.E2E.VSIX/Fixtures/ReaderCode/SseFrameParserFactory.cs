using System;

namespace BenchFixtures.ReaderCode
{
    /// <summary>
    /// Benchmark fixture (frozen, not compiled): the distractor for scenario A2.2, which must read
    /// SseFrameParser.cs and not this file.
    /// </summary>
    internal static class SseFrameParserFactory
    {
        /// <summary>Creates a parser configured for the given endpoint kind.</summary>
        public static SseFrameParser CreateForEndpoint(string endpointKind)
        {
            if (string.IsNullOrWhiteSpace(endpointKind))
                throw new ArgumentException("Endpoint kind is required.", nameof(endpointKind));

            char separator = endpointKind == "legacy" ? '|' : '\n';
            return new SseFrameParser(new SseLineSplitter(separator));
        }
    }

    /// <summary>Splits one event payload into parts.</summary>
    internal sealed class SseLineSplitter
    {
        private readonly char _separator;

        public SseLineSplitter(char separator)
        {
            _separator = separator;
        }

        public string[] Split(string payload) => (payload ?? string.Empty).Split(_separator);
    }
}
