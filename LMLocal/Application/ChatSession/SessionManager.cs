using System;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.SubAgents;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using LMLocal.Infrastructure.WebView;

namespace LMLocal.Application.ChatSession
{
    /// <summary>
    /// Manages single active chat session lifecycle.
    /// Acts as a proxy to the singleton ChatSessionOrchestrator, guarding access.
    /// </summary>
    internal interface ISessionManager
    {
        /// <summary>
        /// Atomically starts a new session if none is running.
        /// </summary>
        Task<bool> TryStartSessionAsync(
            GenerateStreamContext context,
            Func<WebView2ScriptMessage, Task> onMessage,
            CancellationToken cancellationToken);

        /// <summary>
        /// Attempts to stop the currently running session.
        /// </summary>
        bool TryStopSession();

        /// <summary>
        /// Checks if a session is currently running.
        /// </summary>
        bool IsSessionRunning { get; }

        /// <summary>
        /// Identifier of the current (or last started) session.
        /// </summary>
        long CurrentSessionId { get; }
    }

    internal class SessionManager : ISessionManager
    {
        private readonly IChatSessionOrchestrator _orchestrator;
        private readonly ISearchResultCache _searchCache;
        private readonly ISubAgentsConfigManager _subAgentsConfigManager;
        private readonly IAttentionNotifier _attentionNotifier;
        private readonly ITodoTool _todoTool;
        private readonly SemaphoreSlim _sessionLock = new SemaphoreSlim(1, 1);
        private volatile bool _isSessionRunning = false;

        private volatile bool _stopRequested;
        private long _sessionId;

        public bool IsSessionRunning => _isSessionRunning;

        public long CurrentSessionId => Interlocked.Read(ref _sessionId);

        public SessionManager(
            IChatSessionOrchestrator orchestrator,
            ISearchResultCache searchCache,
            ISubAgentsConfigManager subAgentsConfigManager,
            IAttentionNotifier attentionNotifier,
            ITodoTool todoTool)
        {
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _searchCache = searchCache ?? throw new ArgumentNullException(nameof(searchCache));
            _subAgentsConfigManager = subAgentsConfigManager ?? throw new ArgumentNullException(nameof(subAgentsConfigManager));
            _attentionNotifier = attentionNotifier ?? throw new ArgumentNullException(nameof(attentionNotifier));
            _todoTool = todoTool ?? throw new ArgumentNullException(nameof(todoTool));
        }

        /// <summary>
        /// How long a new session waits for the previous one to finish before it is rejected.
        /// </summary>
        private static readonly TimeSpan SessionLockTimeout = TimeSpan.FromSeconds(5);

        public async Task<bool> TryStartSessionAsync(
            GenerateStreamContext context,
            Func<WebView2ScriptMessage, Task> onMessage,
            CancellationToken cancellationToken)
        {
            if (!await WaitForSessionLockAsync(cancellationToken).ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested)
                    InternalLogger.Info("SessionManager: the new session was cancelled while waiting for the previous one");
                else
                    InternalLogger.Error("SessionManager: the new session was rejected: the previous one is still finishing");

                if (onMessage != null)
                {
                    await onMessage(new WebView2ScriptMessage
                    {
                        Type = WebView2MessageType.ChatSessionError,
                        Payload = "Previous session is still finishing. Try again in a moment."
                    }).ConfigureAwait(false);
                }

                return false;
            }

            try
            {
                if (_isSessionRunning)
                {
                    InternalLogger.Error("SessionManager: the new session was rejected: a session is already running");
                    return false;
                }

                _todoTool.Reset();
                _searchCache.Clear();

                Interlocked.Increment(ref _sessionId);
                _isSessionRunning = true;
                _stopRequested = false;
                InternalLogger.Info("SessionManager: Starting new session");

                try
                {
                    await _subAgentsConfigManager.RefreshAsync(cancellationToken).ConfigureAwait(false);

                    await _orchestrator.RunSessionAsync(
                        context,
                        onMessage,
                        cancellationToken).ConfigureAwait(false);

                    return true;
                }
                finally
                {
                    _isSessionRunning = false;
                    InternalLogger.Info("SessionManager: Session ended");

                    if (!_stopRequested)
                    {
                        _attentionNotifier.RequestAttention();
                    }
                }
            }
            finally
            {
                _sessionLock.Release();
            }
        }

        /// <summary>
        /// Waits a bounded time for the session lock.
        /// </summary>
        private async Task<bool> WaitForSessionLockAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await _sessionLock.WaitAsync(SessionLockTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        public bool TryStopSession()
        {
            if (!_isSessionRunning)
            {
                InternalLogger.Info("SessionManager: No session running, nothing to stop");
                return false;
            }

            try
            {
                InternalLogger.Info("SessionManager: Stopping current session");
                _stopRequested = true;
                _orchestrator.StopSession();
                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("SessionManager: Error stopping session", ex);
                return false;
            }
        }
    }
}
