using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.ChatSession;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.SubAgents;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.WebView;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Internal
{
    /// <summary>
    /// A second start must wait for the previous session instead of being refused outright: after Stop, the first
    /// session still compacts history for a moment, and a prompt sent in that window used to disappear silently.
    /// </summary>
    [TestFixture]
    public class SessionManagerConcurrencyTests
    {
        [Test]
        public async Task SecondStart_WaitsForTheFirstAndThenRuns()
        {
            var firstGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var runs = 0;

            var orchestrator = new Mock<IChatSessionOrchestrator>();
            orchestrator
                .Setup(o => o.RunSessionAsync(It.IsAny<GenerateStreamContext>(), It.IsAny<System.Func<WebView2ScriptMessage, Task>>(), It.IsAny<CancellationToken>()))
                .Returns<GenerateStreamContext, System.Func<WebView2ScriptMessage, Task>, CancellationToken>(async (ctx, onMessage, ct) =>
                {
                    int run = Interlocked.Increment(ref runs);
                    if (run == 1)
                        await firstGate.Task.ConfigureAwait(false);
                });

            var cache = new Mock<ISearchResultCache>();
            var config = new Mock<ISubAgentsConfigManager>();
            config.Setup(c => c.RefreshAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var manager = new SessionManager(orchestrator.Object, cache.Object, config.Object);

            var first = manager.TryStartSessionAsync(new GenerateStreamContext { Prompt = "one", ModelId = "m" }, _ => Task.CompletedTask, CancellationToken.None);

            await Task.Delay(50);
            Assert.That(manager.IsSessionRunning, Is.True);

            var second = manager.TryStartSessionAsync(new GenerateStreamContext { Prompt = "two", ModelId = "m" }, _ => Task.CompletedTask, CancellationToken.None);

            await Task.Delay(50);
            Assert.That(second.IsCompleted, Is.False, "the second start must wait, not fail");

            firstGate.SetResult(true);

            Assert.That(await first, Is.True);
            Assert.That(await second, Is.True, "the second start must run once the first finishes");
            Assert.That(runs, Is.EqualTo(2));
        }

        [Test]
        public async Task SecondStart_AfterStop_IsNotLost()
        {
            // The realistic shape of the problem: the user presses Stop and immediately sends a new prompt. The old
            // session is still unwinding, so the new one has to wait briefly rather than be rejected.
            var unwinding = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var orchestrator = new Mock<IChatSessionOrchestrator>();
            orchestrator
                .Setup(o => o.RunSessionAsync(It.IsAny<GenerateStreamContext>(), It.IsAny<System.Func<WebView2ScriptMessage, Task>>(), It.IsAny<CancellationToken>()))
                .Returns<GenerateStreamContext, System.Func<WebView2ScriptMessage, Task>, CancellationToken>(async (ctx, onMessage, ct) =>
                {
                    if ((string)ctx.Prompt == "first")
                        await unwinding.Task.ConfigureAwait(false);
                });

            var cache = new Mock<ISearchResultCache>();
            var config = new Mock<ISubAgentsConfigManager>();
            config.Setup(c => c.RefreshAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var manager = new SessionManager(orchestrator.Object, cache.Object, config.Object);

            var first = manager.TryStartSessionAsync(new GenerateStreamContext { Prompt = "first", ModelId = "m" }, _ => Task.CompletedTask, CancellationToken.None);
            await Task.Delay(50);

            manager.TryStopSession();

            var second = manager.TryStartSessionAsync(new GenerateStreamContext { Prompt = "second", ModelId = "m" }, _ => Task.CompletedTask, CancellationToken.None);

            unwinding.SetResult(true);

            Assert.That(await first, Is.True);
            Assert.That(await second, Is.True);
        }
    }
}
