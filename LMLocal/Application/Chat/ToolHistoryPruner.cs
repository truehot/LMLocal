using System;
using System.Collections.Generic;
using System.Text;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi.Requests;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LMLocal.Application.Chat
{
    /// <summary>
    /// Tuning of the prune ladder.
    /// </summary>
    internal sealed class PruneOptions
    {
        /// <summary>Newest rounds of the current cycle that are never compressed below stage A.</summary>
        public int KeepRecentRounds { get; set; } = 3;

        /// <summary>Lines of the body kept at the start of a stage A trace.</summary>
        public int StageAHeadLines { get; set; } = 20;

        /// <summary>Lines of the body kept at the end of a stage A trace.</summary>
        public int StageATailLines { get; set; } = 20;
    }

    /// <summary>
    /// Result of one prune pass. <see cref="PrunedCount"/> counts only the messages this call actually changed, so pruning an already pruned history returns zero.
    /// </summary>
    internal sealed class PruneResult
    {
        public PruneResult(IReadOnlyList<ChatMessage> messages, int prunedCount, bool fits)
        {
            Messages = messages;
            PrunedCount = prunedCount;
            Fits = fits;
        }

        public IReadOnlyList<ChatMessage> Messages { get; }

        public int PrunedCount { get; }

        public bool Fits { get; }
    }

    /// <summary>
    /// Compresses tool results and write-tool arguments without an LLM.
    /// </summary>
    internal static class ToolHistoryPruner
    {
        /// <summary>Marker written into a compressed body. Recognising it keeps pruning idempotent.</summary>
        internal const string Marker = "\u27E6pruned";

        /// <summary>Argument flag that marks compressed write-tool arguments.</summary>
        internal const string PrunedKey = "_pruned";

        /// <summary>Argument flag that records how many lines were removed.</summary>
        internal const string LinesKey = "_lines";

        private const string TraceMarker = Marker + "\u27E7 ";

        /// <summary>
        /// Content arguments removed per write tool. Everything else (file path, line numbers) is kept so the
        /// model still knows what it already did.
        /// </summary>
        private static readonly Dictionary<string, string[]> WriteToolContentArguments =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "create_file", new[] { "content" } },
                { "replace_file_content", new[] { "content" } },
                { "replace_file_lines", new[] { "old_lines", "new_lines" } },
                { "insert_file_lines", new[] { "new_lines" } }
            };

        /// <summary>
        /// True when the tool writes content into a file and therefore carries its payload in the arguments.
        /// </summary>
        internal static bool IsWriteTool(string toolName)
        {
            return !string.IsNullOrEmpty(toolName) && WriteToolContentArguments.ContainsKey(toolName);
        }

        /// <summary>
        /// Compresses history until it fits <paramref name="limit"/> or until nothing is left to compress.
        /// </summary>
        public static PruneResult Prune(
            IReadOnlyList<ChatMessage> messages,
            PruneOptions options,
            Func<IReadOnlyList<ChatMessage>, int> realTokens,
            int limit)
        {
            if (messages == null || messages.Count == 0)
                return new PruneResult(new List<ChatMessage>(), 0, true);

            if (options == null)
                options = new PruneOptions();

            var work = new List<ChatMessage>(messages);
            int edits = 0;

            int CurrentTokens() => realTokens == null ? 0 : realTokens(work);

            if (CurrentTokens() <= limit)
                return new PruneResult(work, 0, true);

            int lastUser = ConversationSegmenter.FindLastUser(work);
            var roundIndex = BuildRoundIndex(work, lastUser);
            var callsById = ConversationSegmenter.MapToolCallsById(messages);

            for (int i = 0; i < work.Count; i++)
            {
                if (!TryPruneToolCallArguments(work[i], out var replacedArguments))
                    continue;

                work[i] = replacedArguments;
                edits++;

                if (CurrentTokens() <= limit)
                    return new PruneResult(work, edits, true);
            }

            for (int pass = 0; pass < 2; pass++)
            {
                bool stageB = pass == 1;

                for (int i = 0; i < work.Count; i++)
                {
                    var message = work[i];
                    if (message == null || message.Role != "tool")
                        continue;

                    string body = message.Content as string;
                    if (string.IsNullOrEmpty(body))
                        continue;

                    bool isNewestRound = roundIndex.TryGetValue(i, out int roundFromEnd) && roundFromEnd == 1;
                    if (stageB && isNewestRound)
                        continue;

                    if (!stageB && body.IndexOf(Marker, StringComparison.Ordinal) >= 0)
                        continue;
                    if (stageB && body.StartsWith(Marker, StringComparison.Ordinal))
                        continue;

                    string replacement;
                    if (stageB)
                    {
                        callsById.TryGetValue(message.ToolCallId ?? string.Empty, out var call);
                        replacement = BuildStageB(body, call);
                    }
                    else
                    {
                        replacement = BuildStageA(body, options);
                        if (replacement == null)
                            continue;
                    }

                    work[i] = Rebuild(message, replacement);
                    edits++;

                    if (CurrentTokens() <= limit)
                        return new PruneResult(work, edits, true);
                }
            }

            return new PruneResult(work, edits, CurrentTokens() <= limit);
        }

        /// <summary>
        /// Maps every index inside the current cycle to its round number counted from the end (1 = newest).
        /// </summary>
        private static Dictionary<int, int> BuildRoundIndex(IReadOnlyList<ChatMessage> messages, int lastUser)
        {
            var index = new Dictionary<int, int>();
            var rounds = ConversationSegmenter.GetCurrentCycleRounds(messages, lastUser);

            for (int i = 0; i < rounds.Count; i++)
            {
                int numberFromEnd = rounds.Count - i;
                for (int j = rounds[i].Start; j <= rounds[i].End; j++)
                    index[j] = numberFromEnd;
            }

            return index;
        }

        /// <summary>
        /// Replaces an assistant message whose write-tool arguments still carry content with a copy that does not.
        /// </summary>
        private static bool TryPruneToolCallArguments(ChatMessage message, out ChatMessage replaced)
        {
            replaced = null;

            if (message == null || message.Role != "assistant")
                return false;

            if (!(message.ToolCalls is List<ToolCall> calls) || calls.Count == 0)
                return false;

            List<ToolCall> prunedCalls = null;

            for (int i = 0; i < calls.Count; i++)
            {
                var pruned = TryPruneSingleCall(calls[i]);
                if (pruned == null)
                    continue;

                if (prunedCalls == null)
                    prunedCalls = new List<ToolCall>(calls);

                prunedCalls[i] = pruned;
            }

            if (prunedCalls == null)
                return false;

            replaced = new ChatMessage(message.Role, message.Content, message.ToolCallId)
            {
                ToolCalls = prunedCalls
            };
            return true;
        }

        /// <summary>
        /// Removes the content arguments of one write-tool call.
        /// </summary>
        private static ToolCall TryPruneSingleCall(ToolCall call)
        {
            if (call?.Function == null || !IsWriteTool(call.Function.Name))
                return null;

            JObject arguments;
            try
            {
                arguments = JObject.Parse(string.IsNullOrWhiteSpace(call.Function.Arguments) ? "{}" : call.Function.Arguments);
            }
            catch (JsonException)
            {
                return null;
            }

            if (arguments.Property(PrunedKey) != null)
                return null;

            int removedLines = 0;
            bool removed = false;

            foreach (var field in WriteToolContentArguments[call.Function.Name])
            {
                var property = arguments.Property(field);
                if (property == null)
                    continue;

                removedLines += CountLines(property.Value.Type == JTokenType.String
                    ? property.Value.Value<string>()
                    : property.Value.ToString(Formatting.None));

                property.Remove();
                removed = true;
            }

            if (!removed)
                return null;

            arguments[PrunedKey] = true;
            arguments[LinesKey] = removedLines;

            return new ToolCall
            {
                Id = call.Id,
                Type = call.Type,
                Function = new FunctionCallDetails
                {
                    Name = call.Function.Name,
                    Arguments = arguments.ToString(Formatting.None)
                },
                ExtraContent = call.ExtraContent
            };
        }

        /// <summary>
        /// Stage A: the head and the tail of the body, with a marker in place of the middle.
        /// </summary>
        private static string BuildStageA(string body, PruneOptions options)
        {
            return TrimToHeadAndTail(body, options.StageAHeadLines, options.StageATailLines);
        }

        /// <summary>
        /// Keeps the first <paramref name="headLines"/> and the last <paramref name="tailLines"/> lines and replaces the middle with a marker.
        /// </summary>
        internal static string TrimToHeadAndTail(string text, int headLines, int tailLines)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            var lines = SplitLines(text);
            int head = Math.Max(0, headLines);
            int tail = Math.Max(0, tailLines);

            if (lines.Length <= head + tail + 1)
                return null;

            int omitted = lines.Length - head - tail;
            var builder = new StringBuilder(text.Length / 2 + 64);

            for (int i = 0; i < head; i++)
                builder.Append(lines[i]).Append('\n');

            builder.Append(Marker).Append(": ").Append(omitted).Append(" lines\u27E7");

            for (int i = lines.Length - tail; i < lines.Length; i++)
                builder.Append('\n').Append(lines[i]);

            return builder.ToString();
        }

        /// <summary>
        /// Stage B: the whole body becomes one trace line that says which tool ran, on what, and with what verdict.
        /// </summary>
        private static string BuildStageB(string body, ToolCall call)
        {
            return DescribeTrace(call?.Function?.Name, call?.Function?.Arguments, body);
        }

        /// <summary>
        /// One trace line for a tool call and its result.
        /// </summary>
        internal static string DescribeTrace(string toolName, string argumentsJson, string body)
        {
            if (string.IsNullOrEmpty(toolName))
                toolName = "tool";

            var builder = new StringBuilder(TraceMarker.Length + 64);
            builder.Append(TraceMarker).Append(toolName);

            string arguments = DescribeArguments(argumentsJson);
            if (!string.IsNullOrEmpty(arguments))
                builder.Append(' ').Append(arguments);

            builder.Append(" \u2192 ").Append(DescribeVerdict(body));
            return builder.ToString();
        }

        /// <summary>
        /// Short argument summary for a trace line: the file path with its line range when the arguments carry one.
        /// </summary>
        private static string DescribeArguments(string argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
                return null;

            JObject arguments;
            try
            {
                arguments = JObject.Parse(argumentsJson);
            }
            catch (JsonException)
            {
                return null;
            }

            string filePath = arguments.Value<string>("file_path") ?? arguments.Value<string>("path");
            if (string.IsNullOrEmpty(filePath))
            {
                string query = arguments.Value<string>("query") ?? arguments.Value<string>("symbol");
                return string.IsNullOrEmpty(query) ? null : Quote(query);
            }

            int? startLine = arguments.Value<int?>("start_line");
            if (startLine.HasValue)
            {
                int? endLine = arguments.Value<int?>("end_line");
                return endLine.HasValue
                    ? $"{filePath}:{startLine}-{endLine}"
                    : $"{filePath}:{startLine}";
            }

            return filePath;
        }

        /// <summary>
        /// One-line verdict for a trace: the first error when the tool reported failure, otherwise the size of the result.
        /// </summary>
        private static string DescribeVerdict(string body)
        {
            JObject parsed = null;
            try
            {
                parsed = JObject.Parse(body);
            }
            catch (JsonException)
            {
                // Not JSON - fall through to the line count.
            }

            if (parsed != null)
            {
                bool? success = parsed.Value<bool?>("success");
                if (success == false)
                {
                    string error = parsed.Value<string>("error_message")
                                   ?? parsed.Value<string>("error")
                                   ?? parsed.Value<string>("message");
                    return "FAILED" + (string.IsNullOrEmpty(error) ? string.Empty : ": " + Shorten(error, 160));
                }

                string content = parsed.Value<string>("content");
                if (!string.IsNullOrEmpty(content))
                    return "ok, " + CountLines(content) + " lines";
            }

            return "ok, " + CountLines(body) + " lines";
        }

        private static string Quote(string value)
        {
            return "\"" + Shorten(value, 60) + "\"";
        }

        private static string Shorten(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value ?? string.Empty;

            return value.Substring(0, maxLength) + "\u2026";
        }

        /// <summary>
        /// Replaces the content of a message while keeping its role, tool call id and tool calls.
        /// </summary>
        private static ChatMessage Rebuild(ChatMessage message, string content)
        {
            return new ChatMessage(message.Role, content, message.ToolCallId)
            {
                ToolCalls = message.ToolCalls
            };
        }

        private static string[] SplitLines(string text)
        {
            return text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        }

        private static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            return SplitLines(text).Length;
        }
    }
}
