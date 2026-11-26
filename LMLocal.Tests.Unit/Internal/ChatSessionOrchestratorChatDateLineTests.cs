using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Core.Models;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    /// <summary>
    /// The line itself is the business of the history - it owns the clock and the system message, see
    /// <see cref="ChatHistoryManagerChatDateLineTests"/>. The request only has to ask for it, once, before its first
    /// round, so the very first request of a chat is not sent without it.
    /// </summary>
    [TestFixture]
    public class ChatSessionOrchestratorChatDateLineTests
    {
        [Test]
        public async Task Request_AsksTheHistoryForTheDateLineOnce()
        {
            var builder = new OrchestratorTestBuilder();
            StreamOneAnswer(builder);

            await builder.Build()
                .RunSessionAsync(
                    new GenerateStreamContext { Prompt = "first prompt", ModelId = "qwen3-coder-30b" },
                    _ => Task.CompletedTask,
                    CancellationToken.None)
                .ConfigureAwait(false);

            builder.History.Verify(h => h.EnsureChatDateLine(), Times.Once,
                "the request takes the line before its first round");
        }

        /// <summary>Answers the generation round of a session that uses no tools.</summary>
        private static void StreamOneAnswer(OrchestratorTestBuilder builder)
        {
            builder.ChatService
                .Setup(s => s.GenerateStreamAsync(
                    It.IsAny<GenerateStreamContext>(),
                    It.IsAny<List<ToolResultMessage>>(),
                    It.IsAny<Func<TextStreamChunk, TokenGenerationStats, Task>>(),
                    It.IsAny<Func<StreamCompletionResult, Task>>(),
                    It.IsAny<Func<RateLimitWaitInfo, Task>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<GenerateStreamContext, List<ToolResultMessage>, Func<TextStreamChunk, TokenGenerationStats, Task>, Func<StreamCompletionResult, Task>, Func<RateLimitWaitInfo, Task>, CancellationToken>(
                    async (context, toolResults, onChunk, onComplete, onRateLimitWait, ct) =>
                    {
                        if (onComplete != null)
                        {
                            await onComplete(new StreamCompletionResult
                            {
                                FinishReason = "stop",
                                StopReason = StreamStopReason.Completed
                            }).ConfigureAwait(false);
                        }
                    });
        }
    }
}
