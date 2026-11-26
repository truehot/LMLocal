using System.Collections.Generic;

namespace LMLocal.Application.Abstractions.Ports
{
    /// <summary>
    /// Converts raw tool result JSON (from tool messages in chat history) into human-readable Markdown.
    /// </summary>
    public interface IToolResultMarkdownFormatter
    {
        /// <summary>
        /// Formats the tool results of one step into a single Markdown section: the files read in the step, one
        /// block per file without the reads a later write made stale, and the files the step changed.
        /// </summary>
        string FormatToolResults(IEnumerable<(string FunctionName, string JsonContent)> toolResults);
    }
}
