using System;
using System.Collections.Generic;
using LMLocal.Core.Models;

namespace LMLocal.Application.Tool
{
    /// <summary>
    /// Compares two rounds of tool calls for complete equality.
    /// </summary>
    internal interface IToolCallLoopDetector
    {
        /// <summary>
        /// Returns true if the two lists of tool calls are the same, false otherwise.
        /// </summary>
        bool AreSameToolCalls(IReadOnlyList<ToolCallRecord> current, IReadOnlyList<ToolCallRecord> previous);
    }

    internal sealed class ToolCallLoopDetector : IToolCallLoopDetector
    {
        public bool AreSameToolCalls(IReadOnlyList<ToolCallRecord> current, IReadOnlyList<ToolCallRecord> previous)
        {
            if (current == null || previous == null)
                return false;

            if (current.Count != previous.Count)
                return false;

            for (int i = 0; i < current.Count; i++)
            {
                if (!string.Equals(current[i].FunctionName, previous[i].FunctionName, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (!string.Equals(current[i].ArgumentsJson, previous[i].ArgumentsJson, StringComparison.Ordinal))
                    return false;
            }

            return true;
        }
    }
}
