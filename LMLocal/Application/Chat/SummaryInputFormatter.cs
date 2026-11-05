using System;
using System.Collections.Generic;
using System.Text;
using LMLocal.Core.Common;
using LMLocal.Core.Models;

namespace LMLocal.Application.Chat
{
    /// <summary>
    /// Turns the fragment that is about to be summarized into a bounded text.
    /// </summary>
    internal static class SummaryInputFormatter
    {
        /// <summary>
        /// Lines kept when one summary input block alone exceeds the budget: the head and the tail, like stage A.
        /// </summary>
        private const int StageATrimHeadLines = 20;
        private const int StageATrimTailLines = 20;

        /// <summary>
        /// Formats the fragment that is about to be summarized.
        /// </summary>
        internal static string Format(IReadOnlyList<ChatMessage> messages, int inputLimit)
        {
            var blocks = BuildSummaryBlocks(messages);
            if (blocks.Count == 0)
                return string.Empty;

            for (int i = 0; i < blocks.Count; i++)
                blocks[i] = FitBlockToLimit(blocks[i], inputLimit);

            if (EstimateTextTokens(JoinBlocks(blocks, null, 0)) <= inputLimit)
                return JoinBlocks(blocks, null, 0);

            int tailStart = blocks.Count - 1;
            int tailTokens = EstimateTextTokens(blocks[tailStart]);

            while (tailStart > 0 && tailTokens + EstimateTextTokens(blocks[tailStart - 1]) <= inputLimit / 2)
            {
                tailStart--;
                tailTokens += EstimateTextTokens(blocks[tailStart]);
            }

            var kept = new List<string>();

            int used = EstimateTextTokens(OmittedStepsMarker(blocks.Count));
            int headEnd = 0;

            while (headEnd < tailStart && used + EstimateTextTokens(blocks[headEnd]) <= inputLimit)
            {
                kept.Add(blocks[headEnd]);
                used += EstimateTextTokens(blocks[headEnd]);
                headEnd++;
            }

            var tail = new List<string>();
            for (int i = tailStart; i < blocks.Count; i++)
                tail.Add(blocks[i]);

            return JoinBlocks(kept, tail, blocks.Count - kept.Count - tail.Count);
        }

        /// <summary>
        /// Trims one block until it fits the budget, using the stage A shape (head, gap marker, tail).
        /// </summary>
        private static string FitBlockToLimit(string block, int limit)
        {
            if (EstimateTextTokens(block) <= limit)
                return block;

            int head = StageATrimHeadLines;
            int tail = StageATrimTailLines;

            while (head >= 1 || tail >= 1)
            {
                var trimmed = ToolHistoryPruner.TrimToHeadAndTail(block, head, tail);
                if (trimmed == null)
                    break;

                if (EstimateTextTokens(trimmed) <= limit)
                    return trimmed;

                head /= 2;
                tail /= 2;
            }

            int maxChars = Math.Max(1, limit * 4);
            return block.Length <= maxChars
                ? block
                : block.Substring(0, maxChars) + ToolHistoryPruner.Marker.TrimStart('\u27E6') + "\u27E7";
        }

        /// <summary>
        /// Splits the fragment into summary blocks: one block per prose message, and one block per tool round - the assistant message that requested the calls plus the results that answered them.
        /// </summary>
        private static List<string> BuildSummaryBlocks(IReadOnlyList<ChatMessage> messages)
        {
            var blocks = new List<string>();
            if (messages == null)
                return blocks;

            var describedResults = new HashSet<int>();

            for (int i = 0; i < messages.Count; i++)
            {
                var message = messages[i];
                if (message == null)
                    continue;

                if (message.Role == "assistant" && ConversationSegmenter.HasToolCalls(message))
                {
                    var builder = new StringBuilder();

                    string text = ContentTextExtractor.ExtractTextContent(message.Content);
                    if (!string.IsNullOrWhiteSpace(text))
                        builder.Append("assistant: ").Append(text.Trim());

                    foreach (var call in (List<LMLocal.Infrastructure.LlmApi.Requests.ToolCall>)message.ToolCalls)
                    {
                        if (call == null)
                            continue;

                        string body = null;
                        for (int j = i + 1; j < messages.Count; j++)
                        {
                            if (messages[j] != null && messages[j].Role == "tool" && messages[j].ToolCallId == call.Id)
                            {
                                body = messages[j].Content as string;
                                describedResults.Add(j);
                                break;
                            }
                        }

                        if (builder.Length > 0)
                            builder.AppendLine();

                        builder.Append(ToolHistoryPruner.DescribeTrace(call.Function?.Name, call.Function?.Arguments, body));
                    }

                    blocks.Add(builder.ToString());
                    continue;
                }

                if (describedResults.Contains(i))
                    continue;

                string content = ContentTextExtractor.ExtractTextContent(message.Content);
                if (string.IsNullOrWhiteSpace(content))
                    continue;

                blocks.Add(message.Role == "tool"
                    ? ToolHistoryPruner.DescribeTrace(null, null, content)
                    : message.Role + ": " + content.Trim());
            }

            return blocks;
        }

        /// <summary>
        /// Joins blocks with a blank line between them, optionally inserting a gap marker between the kept head and the kept tail.
        /// </summary>
        private static string JoinBlocks(List<string> head, List<string> tail, int omitted)
        {
            var builder = new StringBuilder();

            foreach (var block in head)
            {
                if (builder.Length > 0)
                    builder.AppendLine();
                builder.AppendLine(block);
            }

            if (omitted > 0)
            {
                if (builder.Length > 0)
                    builder.AppendLine();
                builder.AppendLine(OmittedStepsMarker(omitted));
            }

            if (tail == null)
                return builder.ToString();

            foreach (var block in tail)
            {
                if (builder.Length > 0)
                    builder.AppendLine();
                builder.AppendLine(block);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Placeholder that tells the summarizer how many steps were dropped between the kept head and tail.
        /// </summary>
        private static string OmittedStepsMarker(int omitted)
        {
            return "\u2026 [" + omitted + " steps omitted] \u2026";
        }

        /// <summary>
        /// Token estimate of a piece of text.
        /// </summary>
        private static int EstimateTextTokens(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            var estimator = new StreamedTokenEstimator();
            estimator.Add(text);
            return estimator.EstimatedTokens;
        }
    }
}
