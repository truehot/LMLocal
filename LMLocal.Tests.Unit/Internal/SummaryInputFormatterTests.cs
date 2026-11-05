using System;
using System.Collections.Generic;
using System.Linq;
using LMLocal.Application.Chat;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi.Requests;
using NUnit.Framework;
using ToolCall = LMLocal.Infrastructure.LlmApi.Requests.ToolCall;

namespace LMLocal.Tests.Unit
{
    /// <summary>
    /// The summarizer must see the tool rounds as traces, and the input must be bounded.
    /// </summary>
    [TestFixture]
    public class SummaryInputFormatterTests
    {
        private static ChatMessage AssistantWithCall(string id, string toolName, string arguments)
        {
            return new ChatMessage("assistant", null)
            {
                ToolCalls = new List<ToolCall>
                {
                    new ToolCall
                    {
                        Id = id,
                        Type = "function",
                        Function = new FunctionCallDetails { Name = toolName, Arguments = arguments }
                    }
                }
            };
        }

        /// <summary>
        /// One prompt followed by a single tool round, the shape the plain formatter used to drop entirely.
        /// </summary>
        private static List<ChatMessage> ToolRoundHistory()
        {
            return new List<ChatMessage>
            {
                new ChatMessage("user", "read the file and fix it"),
                AssistantWithCall("c1", "read_file_lines", "{\"file_path\":\"src/Api.cs\",\"start_line\":100}"),
                new ChatMessage("tool", "{\"success\":true,\"content\":\"line 100\\nline 101\\nline 102\"}", "c1"),
                AssistantWithCall("c2", "build_solution", "{}"),
                new ChatMessage("tool", "{\"success\":false,\"error_message\":\"CS0246 Api.cs:120\"}", "c2")
            };
        }

        [Test]
        public void Format_IncludesToolRoundsAsTraces()
        {
            var input = SummaryInputFormatter.Format(ToolRoundHistory(), 100_000);

            // The user prompt stays as prose.
            Assert.That(input, Does.Contain("user: read the file and fix it"));

            // Both tool rounds must be visible, with what they ran on and what came back.
            Assert.That(input, Does.Contain("read_file_lines"));
            Assert.That(input, Does.Contain("src/Api.cs:100"));
            Assert.That(input, Does.Contain("build_solution"));
            Assert.That(input, Does.Contain("CS0246"), "a failed tool must contribute its first error");
        }

        [Test]
        public void Format_ProseOnlyHistory_KeepsEveryMessage()
        {
            var history = new List<ChatMessage>
            {
                new ChatMessage("user", "first"),
                new ChatMessage("assistant", "answer one"),
                new ChatMessage("user", "second"),
                new ChatMessage("assistant", "answer two")
            };

            var input = SummaryInputFormatter.Format(history, 100_000);

            Assert.That(input, Does.Contain("user: first"));
            Assert.That(input, Does.Contain("assistant: answer one"));
            Assert.That(input, Does.Contain("user: second"));
            Assert.That(input, Does.Contain("assistant: answer two"));
            Assert.That(input, Does.Not.Contain("steps omitted"));
        }

        [Test]
        public void Format_StartsFromTheHelperToolInput()
        {
            var history = new List<ChatMessage>
            {
                new ChatMessage("user", "prompt"),
                AssistantWithCall("c1", "read_file_lines", "{\"file_path\":\"src/Api.cs\",\"start_line\":100}"),
                new ChatMessage("tool", "{\"success\":true,\"content\":\"a\"}", "c1")
            };

            var input = SummaryInputFormatter.Format(history, 100_000);

            // A single assistant message with its result must collapse to one trace line, not to two separate lines.
            var traceLines = input
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Count(line => line.Contains(ToolHistoryPruner.Marker));

            Assert.That(traceLines, Is.EqualTo(1));
        }

        [Test]
        public void Format_OverLimit_KeepsHeadAndTailWithGapMarker()
        {
            var history = new List<ChatMessage>();
            for (int i = 0; i < 40; i++)
                history.Add(new ChatMessage(i % 2 == 0 ? "user" : "assistant", "step number " + i + " of the work"));

            var input = SummaryInputFormatter.Format(history, 60);

            Assert.That(input, Does.Contain("steps omitted"), "the dropped middle must be marked");
            Assert.That(input, Does.Contain("step number 0 "), "the goal at the beginning must survive");
            Assert.That(input, Does.Contain("step number 39 "), "the most recent state must survive");
            Assert.That(input, Does.Not.Contain("step number 20 "), "the middle must be dropped");
        }

        [Test]
        public void Format_SmallerLimit_ProducesShorterInput()
        {
            var history = new List<ChatMessage>();
            for (int i = 0; i < 40; i++)
                history.Add(new ChatMessage(i % 2 == 0 ? "user" : "assistant", "step number " + i + " of the work"));

            var wide = SummaryInputFormatter.Format(history, 400);
            var narrow = SummaryInputFormatter.Format(history, 120);

            Assert.That(narrow.Length, Is.LessThan(wide.Length),
                "halving the budget must actually shorten the request");
        }

        [Test]
        public void Format_SingleOversizedStep_IsTrimmedLikeStageA()
        {
            var huge = string.Join("\n", Enumerable.Range(0, 300).Select(i => "source line " + i));
            var history = new List<ChatMessage> { new ChatMessage("user", huge) };

            var input = SummaryInputFormatter.Format(history, 100);

            Assert.That(input, Does.Contain(ToolHistoryPruner.Marker), "an oversized step must be trimmed like stage A");
            Assert.That(input.Length, Is.LessThan(huge.Length / 2));
        }
    }
}
