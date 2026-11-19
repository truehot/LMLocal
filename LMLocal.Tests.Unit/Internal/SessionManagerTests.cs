using System;
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

            var manager = new SessionManager(orchestratorMock.Object, cacheMock.Object, subAgentsConfigMock.Object, new Mock<IAttentionNotifier>().Object);

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

        [Test]
        public async Task SessionCompleted_RequestsAttentionOnce()
        {
            var orchestrator = new Mock<IChatSessionOrchestrator>();
            orchestrator
                .Setup(o => o.RunSessionAsync(It.IsAny<GenerateStreamContext>(), It.IsAny<Func<WebView2ScriptMessage, Task>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var attention = new Mock<IAttentionNotifier>();
            var manager = CreateManager(orchestrator, attention);

            Assert.That(await manager.TryStartSessionAsync(Context("hi"), _ => Task.CompletedTask, CancellationToken.None).ConfigureAwait(false), Is.True);

            attention.Verify(a => a.RequestAttention(), Times.Once);
        }

        [Test]
        public void SessionFailed_RequestsAttentionOnce()
        {
            var orchestrator = new Mock<IChatSessionOrchestrator>();
            orchestrator
                .Setup(o => o.RunSessionAsync(It.IsAny<GenerateStreamContext>(), It.IsAny<Func<WebView2ScriptMessage, Task>>(), It.IsAny<CancellationToken>()))
                .Returns<GenerateStreamContext, Func<WebView2ScriptMessage, Task>, CancellationToken>(
                    (ctx, onMessage, ct) => Task.FromException(new InvalidOperationException("boom")));

            var attention = new Mock<IAttentionNotifier>();
            var manager = CreateManager(orchestrator, attention);

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await manager.TryStartSessionAsync(Context("hi"), _ => Task.CompletedTask, CancellationToken.None).ConfigureAwait(false));

            attention.Verify(a => a.RequestAttention(), Times.Once);
        }

        [Test]
        public async Task SessionStoppedByUser_DoesNotRequestAttention()
        {
            var gate = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            var orchestrator = new Mock<IChatSessionOrchestrator>();
            orchestrator
                .Setup(o => o.RunSessionAsync(It.IsAny<GenerateStreamContext>(), It.IsAny<Func<WebView2ScriptMessage, Task>>(), It.IsAny<CancellationToken>()))
                .Returns(() => gate.Task);

            var attention = new Mock<IAttentionNotifier>();
            var manager = CreateManager(orchestrator, attention);

            var session = manager.TryStartSessionAsync(Context("hi"), _ => Task.CompletedTask, CancellationToken.None);
            await Task.Delay(50).ConfigureAwait(false);

            Assert.That(manager.TryStopSession(), Is.True);

            gate.SetResult(null);
            Assert.That(await session.ConfigureAwait(false), Is.True);

            // The user is looking at the window and asked for the stop: nothing to draw attention to.
            attention.Verify(a => a.RequestAttention(), Times.Never);
        }

        [Test]
        public async Task SessionAfterStop_RequestsAttentionAgain()
        {
            var gate = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            var orchestrator = new Mock<IChatSessionOrchestrator>();
            orchestrator
                .Setup(o => o.RunSessionAsync(It.IsAny<GenerateStreamContext>(), It.IsAny<Func<WebView2ScriptMessage, Task>>(), It.IsAny<CancellationToken>()))
                .Returns<GenerateStreamContext, Func<WebView2ScriptMessage, Task>, CancellationToken>(
                    (ctx, onMessage, ct) => (string)ctx.Prompt == "first" ? gate.Task : Task.CompletedTask);

            var attention = new Mock<IAttentionNotifier>();
            var manager = CreateManager(orchestrator, attention);

            var first = manager.TryStartSessionAsync(Context("first"), _ => Task.CompletedTask, CancellationToken.None);
            await Task.Delay(50).ConfigureAwait(false);
            manager.TryStopSession();

            gate.SetResult(null);
            Assert.That(await first.ConfigureAwait(false), Is.True);

            // The suppression belongs to the stopped session only: the next one notifies as usual.
            Assert.That(
                await manager.TryStartSessionAsync(Context("second"), _ => Task.CompletedTask, CancellationToken.None).ConfigureAwait(false),
                Is.True);

            attention.Verify(a => a.RequestAttention(), Times.Once);
        }

        [Test]
        public async Task SubAgentsRefreshFailed_SessionIsNotLeftRunning()
        {
            var orchestrator = new Mock<IChatSessionOrchestrator>();
            orchestrator
                .Setup(o => o.RunSessionAsync(It.IsAny<GenerateStreamContext>(), It.IsAny<Func<WebView2ScriptMessage, Task>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var config = new Mock<ISubAgentsConfigManager>();
            config.SetupSequence(c => c.RefreshAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.FromException(new InvalidOperationException("refresh failed")))
                .Returns(Task.CompletedTask);

            var attention = new Mock<IAttentionNotifier>();
            var manager = new SessionManager(orchestrator.Object, new Mock<ISearchResultCache>().Object, config.Object, attention.Object);

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await manager.TryStartSessionAsync(Context("first"), _ => Task.CompletedTask, CancellationToken.None).ConfigureAwait(false));

            Assert.That(manager.IsSessionRunning, Is.False);

            // The failed start must not block the next one as "already running".
            Assert.That(
                await manager.TryStartSessionAsync(Context("second"), _ => Task.CompletedTask, CancellationToken.None).ConfigureAwait(false),
                Is.True);
            orchestrator.Verify(
                o => o.RunSessionAsync(It.IsAny<GenerateStreamContext>(), It.IsAny<Func<WebView2ScriptMessage, Task>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        private static SessionManager CreateManager(
            Mock<IChatSessionOrchestrator> orchestrator,
            Mock<IAttentionNotifier> attention)
        {
            var cache = new Mock<ISearchResultCache>();

            var config = new Mock<ISubAgentsConfigManager>();
            config.Setup(c => c.RefreshAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            return new SessionManager(orchestrator.Object, cache.Object, config.Object, attention.Object);
        }

        private static GenerateStreamContext Context(string prompt) => new GenerateStreamContext { Prompt = prompt, ModelId = "m" };
    }
}
