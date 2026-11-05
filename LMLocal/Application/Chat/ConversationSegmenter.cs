using System.Collections.Generic;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi.Requests;

namespace LMLocal.Application.Chat
{
    /// <summary>
    /// Inclusive message range of one completed step in the past: user follow-ed by an assistant message with text.
    /// </summary>
    internal readonly struct ChatStepRange
    {
        public readonly int Start;
        public readonly int End;

        public ChatStepRange(int start, int end)
        {
            Start = start;
            End = end;
        }
    }

    /// <summary>
    /// Inclusive message range of one round of the current tool cycle: an assistant message with tool calls plus all tool results that answer it.
    /// </summary>
    internal readonly struct ConversationRound
    {
        public readonly int Start;
        public readonly int End;

        public ConversationRound(int start, int end)
        {
            Start = start;
            End = end;
        }
    }

    /// <summary>
    /// Splits a conversation into the parts the compaction ladder treats differently: the last user prompt, the completed steps before it, and the rounds of the tool cycle that follows it.
    /// </summary>
    internal static class ConversationSegmenter
    {
        /// <summary>
        /// Index of the last user message, or -1 when the history has none.
        /// </summary>
        public static int FindLastUser(IReadOnlyList<ChatMessage> messages)
        {
            if (messages == null)
                return -1;

            for (int i = messages.Count - 1; i >= 0; i--)
            {
                var message = messages[i];
                if (message != null && message.Role == "user")
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Completed steps in the past: a user message followed (not necessarily directly) by an assistant message with text.
        /// </summary>
        public static List<ChatStepRange> GetCompletedSteps(IReadOnlyList<ChatMessage> messages)
        {
            var steps = new List<ChatStepRange>();
            if (messages == null)
                return steps;

            int end = -1;

            for (int i = messages.Count - 1; i >= 0; i--)
            {
                var message = messages[i];
                if (message == null)
                    continue;

                if (end == -1 && message.Role == "assistant" && HasText(message))
                {
                    end = i;
                    continue;
                }

                if (message.Role == "user" && end != -1)
                {
                    steps.Add(new ChatStepRange(i, end));
                    end = -1;
                }
            }

            steps.Reverse();
            return steps;
        }

        /// <summary>
        /// Rounds of the current tool cycle, in chronological order.
        /// </summary>
        public static List<ConversationRound> GetCurrentCycleRounds(IReadOnlyList<ChatMessage> messages, int lastUser)
        {
            var rounds = new List<ConversationRound>();
            if (messages == null)
                return rounds;

            int i = lastUser < 0 ? 0 : lastUser + 1;

            while (i < messages.Count)
            {
                var message = messages[i];
                if (message == null || message.Role != "assistant" || !HasToolCalls(message))
                {
                    i++;
                    continue;
                }

                int end = i;
                int j = i + 1;
                while (j < messages.Count && messages[j] != null && messages[j].Role == "tool")
                {
                    end = j;
                    j++;
                }

                rounds.Add(new ConversationRound(i, end));
                i = j;
            }

            return rounds;
        }

        /// <summary>
        /// True when the message carries visible text (plain string or text content parts).
        /// </summary>
        public static bool HasText(ChatMessage message)
        {
            return message != null && !string.IsNullOrWhiteSpace(ContentTextExtractor.ExtractTextContent(message.Content));
        }

        /// <summary>
        /// True when the message is an assistant message that requests at least one tool call.
        /// </summary>
        public static bool HasToolCalls(ChatMessage message)
        {
            return message?.ToolCalls is List<ToolCall> calls && calls.Count > 0;
        }

        /// <summary>
        /// Maps every tool call id to its call, so a tool result can be traced back to the tool that produced it.
        /// </summary>
        public static Dictionary<string, ToolCall> MapToolCallsById(IReadOnlyList<ChatMessage> messages)
        {
            var map = new Dictionary<string, ToolCall>();
            if (messages == null)
                return map;

            foreach (var message in messages)
            {
                if (!HasToolCalls(message))
                    continue;

                foreach (var call in (List<ToolCall>)message.ToolCalls)
                {
                    if (call == null || string.IsNullOrEmpty(call.Id))
                        continue;

                    if (!map.ContainsKey(call.Id))
                        map[call.Id] = call;
                }
            }

            return map;
        }
    }
}
