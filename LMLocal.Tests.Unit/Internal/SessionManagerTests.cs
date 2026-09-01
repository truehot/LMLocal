using System;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.ChatSession;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.SubAgents;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.WebView;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Internal
{
    [TestFixture]
    public class SessionManagerTests
    {
        [Test]
        public async Task TryStartSessionAsync_ConcurrentStart_ReturnsFalseForSecondCaller()
        {
            var orchestratorMock = new Mock<IChatSessionOrchestrator>();
            var cacheMock = new Mock<ISearchResultCache>();
            var subAgentsConfigMock = new Mock<ISubAgentsConfigManager>();

            var tcs = new TaskCompletionSource<object>();
            // First call to GenerateWithToolsAsync will not complete until we set the TCS
            orchestratorMock.Setup(o => o.RunSessionAsync(It.IsAny<GenerateStreamContext>(), It.IsAny<System.Func<WebView2ScriptMessage, Task>>(), It.IsAny<CancellationToken>()))
                .Returns(() => tcs.Task);

            var manager = new SessionManager(orchestratorMock.Object, cacheMock.Object, subAgentsConfigMock.Object);

            var ctx = new GenerateStreamContext { Prompt = "p", ModelId = "m" };

            // Start first session (do not await)
            var first = manager.TryStartSessionAsync(ctx, async (m) => { await Task.CompletedTask; }, CancellationToken.None);

            // Give a tiny delay to ensure the first has started and acquired lock
            await Task.Delay(10).ConfigureAwait(false);

            // Second attempt should return false immediately
            var secondResult = await manager.TryStartSessionAsync(ctx, async (m) => { await Task.CompletedTask; }, CancellationToken.None).ConfigureAwait(false);

            Assert.That(secondResult, Is.False);

            // Complete the first orchestrator task so that it can finish
            tcs.SetResult(null);
            await first.ConfigureAwait(false);

            // Verify that Clear was called on the cache
            cacheMock.Verify(c => c.Clear(), Times.AtLeastOnce);
        }
    }
}
