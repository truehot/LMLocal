using System;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
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
        /// Returns true if a session was stopped, false if no session is running.
        /// </summary>
        bool TryStopSession();

        /// <summary>
        /// Checks if a session is currently running.
        /// </summary>
        bool IsSessionRunning { get; }
    }

    internal class SessionManager : ISessionManager
    {
        private readonly IChatSessionOrchestrator _orchestrator;
        private readonly ISearchResultCache _searchCache;
        private readonly SemaphoreSlim _sessionLock = new SemaphoreSlim(1, 1);
        private volatile bool _isSessionRunning = false;

        public bool IsSessionRunning => _isSessionRunning;

        public SessionManager(IChatSessionOrchestrator orchestrator, ISearchResultCache searchCache)
        {
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _searchCache = searchCache ?? throw new ArgumentNullException(nameof(searchCache));
        }

        public async Task<bool> TryStartSessionAsync(
            GenerateStreamContext context,
            Func<WebView2ScriptMessage, Task> onMessage,
            CancellationToken cancellationToken)
        {
            if (!await _sessionLock.WaitAsync(0).ConfigureAwait(false))
            {
                InternalLogger.Info("SessionManager: Session already in progress (lock unavailable)");
                return false;
            }

            try
            {
                if (_isSessionRunning)
                {
                    InternalLogger.Info("SessionManager: Session already running");
                    return false;
                }

                _searchCache.Clear();
                _isSessionRunning = true;
                InternalLogger.Info("SessionManager: Starting new session");

                try
                {
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
                }
            }
            finally
            {
                _sessionLock.Release();
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
