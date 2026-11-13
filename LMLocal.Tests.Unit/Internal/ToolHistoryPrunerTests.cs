using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LMLocal.Application.Chat;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi.Requests;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    /// <summary>
    /// Covers the prune ladder: what is compressed, in which order, and that running it twice changes nothing.
    /// </summary>
    [TestFixture]
    public class ToolHistoryPrunerTests
    {
        /// <summary>
        /// Charge that never improves, so every eligible message is compressed and the result never fits.
        /// Used to observe the whole ladder in one pass.
        /// </summary>
        private static int FlatCost(IReadOnlyList<ChatMessage> messages) => messages.Count * 1000;

        /// <summary>
        /// Rough text-only cost, so the budget really drops as bodies shrink.
        /// </summary>
        private static int TextCost(IReadOnlyList<ChatMessage> messages)
        {
            int total = 0;
            foreach (var message in messages)
            {
                total += ((message.Content as string)?.Length ?? 0) / 4 + 1;

                if (message.ToolCalls is List<ToolCall> calls)
                {
                    foreach (var call in calls)
                        total += ((call?.Function?.Arguments)?.Length ?? 0) / 4;
                }
            }
            return total;
        }

        private static string Body(int lines)
        {
            return string.Join("\n", Enumerable.Range(0, lines).Select(i => "line " + i));
        }

        private static ChatMessage ReadCall(int index, string filePath)
        {
            return new ChatMessage("assistant", null)
            {
                ToolCalls = new List<ToolCall>
                {
                    new ToolCall
                    {
                        Id = "c" + index,
                        Type = "function",
                        Function = new FunctionCallDetails
                        {
                            Name = "read_file_lines",
                            Arguments = "{\"file_path\":\"" + filePath + "\"}"
                        }
                    }
                }
            };
        }

        private static ChatMessage WriteCall(int index, string filePath, int contentLines)
        {
            var arguments = new JObject
            {
                { "file_path", filePath },
                { "content", Body(contentLines) }
            };

            return new ChatMessage("assistant", null)
            {
                ToolCalls = new List<ToolCall>
                {
                    new ToolCall
                    {
                        Id = "w" + index,
                        Type = "function",
                        Function = new FunctionCallDetails
                        {
                            Name = "create_file",
                            Arguments = arguments.ToString(Newtonsoft.Json.Formatting.None)
                        }
                    }
                }
            };
        }

        /// <summary>
        /// One prompt followed by <paramref name="rounds"/> tool rounds; the newest round is the last one.
        /// </summary>
        private static List<ChatMessage> History(int rounds, int bodyLines)
        {
            var messages = new List<ChatMessage> { new ChatMessage("user", "do the work") };

            for (int i = 1; i <= rounds; i++)
            {
                messages.Add(ReadCall(i, "src/A" + i + ".cs"));
                messages.Add(new ChatMessage("tool", Body(bodyLines), "c" + i));
            }

            return messages;
        }

        private static PruneOptions Options(int keepRecentRounds = 3)
        {
            return new PruneOptions { KeepRecentRounds = keepRecentRounds };
        }

        [Test]
        public void Prune_StageAMarker_ReportsOmittedLines()
        {
            // 62 lines with the default 20 + 20 kept means 22 were dropped, and the marker has to say so.
            var history = History(1, 62);
            var result = ToolHistoryPruner.Prune(history, Options(), FlatCost, 0);

            Assert.That((string)result.Messages[2].Content, Does.Contain(ToolHistoryPruner.Marker + ": 22 lines"));
        }

        [Test]
        public void Prune_AlreadyWithinBudget_ChangesNothing()
        {
            var history = History(4, 30);

            var result = ToolHistoryPruner.Prune(history, Options(), FlatCost, 10_000_000);

            Assert.That(result.Fits, Is.True);
            Assert.That(result.PrunedCount, Is.EqualTo(0));
            Assert.That(result.Messages.Count, Is.EqualTo(history.Count));
        }

        [Test]
        public void Prune_CompressesWriteArgumentsInEveryRound_IncludingTheNewest()
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage("user", "do the work"),
                WriteCall(1, "src/Old.cs", 50),
                new ChatMessage("tool", "{\"success\":true}", "w1"),
                WriteCall(2, "src/New.cs", 400),
                new ChatMessage("tool", "{\"success\":true}", "w2")
            };

            var result = ToolHistoryPruner.Prune(messages, Options(), FlatCost, 0);

            foreach (var assistant in result.Messages.Where(m => m.Role == "assistant"))
            {
                var call = ((List<ToolCall>)assistant.ToolCalls).Single();
                var arguments = JObject.Parse(call.Function.Arguments);

                Assert.That(arguments.Property("content"), Is.Null, "content must be removed: {0}", call.Function.Arguments);
                Assert.That(arguments.Value<bool>(ToolHistoryPruner.PrunedKey), Is.True);
                Assert.That(arguments.Value<int>(ToolHistoryPruner.LinesKey), Is.GreaterThan(0));
                Assert.That(arguments.Value<string>("file_path"), Is.Not.Null, "file_path must survive");
                Assert.That(call.Id, Is.Not.Null);
            }
        }

        [Test]
        public void Prune_NewestRoundGetsStageA_OlderRoundsReachStageB()
        {
            var history = History(4, 200);

            var result = ToolHistoryPruner.Prune(history, Options(), FlatCost, 0);

            var toolMessages = result.Messages.Where(m => m.Role == "tool").ToList();
            Assert.That(toolMessages.Count, Is.EqualTo(4));

            var newest = (string)toolMessages[3].Content;
            Assert.That(newest.StartsWith(ToolHistoryPruner.Marker + "\u27E7"), Is.False,
                "the newest round is never reduced to a one-line trace");
            Assert.That(newest.Contains(ToolHistoryPruner.Marker), Is.True, "the newest round still gets stage A");

            foreach (var older in toolMessages.Take(3))
            {
                var text = (string)older.Content;
                Assert.That(text.StartsWith(ToolHistoryPruner.Marker + "\u27E7"), Is.True,
                    "older rounds must reach the one-line trace");
                Assert.That(text, Does.Contain("read_file_lines"));
            }
        }

        [Test]
        public void Prune_PreservesMessageCountAndToolCallIds()
        {
            var history = History(4, 200);

            var result = ToolHistoryPruner.Prune(history, Options(), FlatCost, 0);

            Assert.That(result.Messages.Count, Is.EqualTo(history.Count));

            for (int i = 0; i < history.Count; i++)
            {
                Assert.That(result.Messages[i].Role, Is.EqualTo(history[i].Role));
                Assert.That(result.Messages[i].ToolCallId, Is.EqualTo(history[i].ToolCallId));

                var after = result.Messages[i].ToolCalls as List<ToolCall>;
                if (!(history[i].ToolCalls is List<ToolCall> before))
                {
                    Assert.That(after, Is.Null);
                    continue;
                }

                Assert.That(after.Select(c => c.Id), Is.EqualTo(before.Select(c => c.Id)));
            }
        }

        [Test]
        public void Prune_IsIdempotent()
        {
            var history = History(4, 200);

            var first = ToolHistoryPruner.Prune(history, Options(), FlatCost, 0);
            Assert.That(first.PrunedCount, Is.GreaterThan(0));

            var second = ToolHistoryPruner.Prune(first.Messages, Options(), FlatCost, 0);

            Assert.That(second.PrunedCount, Is.EqualTo(0), "already compressed content must not be compressed again");

            var firstTexts = first.Messages.Select(m => m.Content as string).ToList();
            var secondTexts = second.Messages.Select(m => m.Content as string).ToList();
            Assert.That(secondTexts, Is.EqualTo(firstTexts));
        }

        [Test]
        public void Prune_DoesNotMutateTheInputMessages()
        {
            var history = History(2, 200);
            var originalBody = (string)history[2].Content;
            var originalArguments = ((List<ToolCall>)history[1].ToolCalls)[0].Function.Arguments;

            var result = ToolHistoryPruner.Prune(history, Options(), FlatCost, 0);

            Assert.That((string)history[2].Content, Is.EqualTo(originalBody));
            Assert.That(((List<ToolCall>)history[1].ToolCalls)[0].Function.Arguments, Is.EqualTo(originalArguments));
            Assert.That(result.Messages[2], Is.Not.SameAs(history[2]));
        }

        [Test]
        public void Prune_StopsAsSoonAsTheBudgetIsMet()
        {
            var history = History(4, 200);
            int before = TextCost(history);

            var result = ToolHistoryPruner.Prune(history, Options(), TextCost, before / 2);

            Assert.That(result.Fits, Is.True);
            Assert.That(result.PrunedCount, Is.GreaterThan(0));

            var everything = ToolHistoryPruner.Prune(history, Options(), FlatCost, 0);
            Assert.That(result.PrunedCount, Is.LessThan(everything.PrunedCount),
                "pruning must stop once the history fits instead of compressing everything");
        }

        [Test]
        public void Prune_RebuiltAssistantMessage_PreservesModelId()
        {
            var write = WriteCall(1, "src/A.cs", 60);
            write.ModelId = "qwen-7b";

            var messages = new List<ChatMessage>
            {
                new ChatMessage("user", "do the work"),
                write,
                new ChatMessage("tool", "{\"success\":true}", "w1")
            };

            var result = ToolHistoryPruner.Prune(messages, Options(), FlatCost, 0);

            var assistant = result.Messages.Single(m => m.Role == "assistant");
            Assert.That(assistant.ModelId, Is.EqualTo("qwen-7b"),
                "pruning tool-call arguments must not drop the assistant message's model id");
        }
    }
}
