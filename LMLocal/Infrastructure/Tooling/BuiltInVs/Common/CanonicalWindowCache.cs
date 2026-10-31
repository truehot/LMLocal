using System;
using System.Collections.Generic;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common
{
    /// <summary>
    /// Lazy cache of canonical forms (<see cref="CharacterMap.Canonicalize"/>) for the lines a single tool call.
    /// </summary>
    internal sealed class CanonicalWindowCache
    {
        private readonly List<string> _lines;
        private readonly string[] _forms;
        private readonly int _firstLine;
        private readonly int _lastLine;

        public CanonicalWindowCache(
            List<string> lines, int aroundLine, int blockLength, int windowLines = LineMatcher.MaxSearchWindowLines)
        {
            _lines = lines ?? throw new ArgumentNullException(nameof(lines));

            int lower = Math.Max(0, aroundLine - 1 - windowLines);
            int upper = Math.Min(lines.Count - 1, aroundLine - 1 + windowLines + Math.Max(0, blockLength - 1));

            _firstLine = lower;
            _lastLine = upper;
            _forms = upper >= lower ? new string[upper - lower + 1] : new string[0];
        }

        /// <summary>
        /// Canonical form of <paramref name="lineIndex"/> (0-based) in the underlying list, or null when the index is out of range.
        /// </summary>
        public string GetExact(int lineIndex)
        {
            if (lineIndex < 0 || lineIndex >= _lines.Count)
                return null;

            if (lineIndex >= _firstLine && lineIndex <= _lastLine)
            {
                int slot = lineIndex - _firstLine;
                if (_forms[slot] == null)
                    _forms[slot] = CharacterMap.Canonicalize(_lines[lineIndex]);

                return _forms[slot];
            }

            return CharacterMap.Canonicalize(_lines[lineIndex]);
        }
    }
}
