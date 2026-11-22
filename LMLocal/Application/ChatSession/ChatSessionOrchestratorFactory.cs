using System;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Application.SubAgents;
using LMLocal.Application.Tool;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;

namespace LMLocal.Application.ChatSession
{
    /// <summary>
    /// Factory for creating ChatSessionOrchestrator instances.
    /// </summary>
    internal interface IChatSessionOrchestratorFactory
    {
        IChatSessionOrchestrator CreateOrchestrator();
    }

    internal class ChatSessionOrchestratorFactory : IChatSessionOrchestratorFactory
    {
        private readonly IChatStreamService _chatService;
        private readonly IToolExecutionManager _toolManager;
        private readonly IHistoryCompactor _compactor;
        private readonly IChatHistoryManager _history;
        private readonly ISnapshotManager _snapshotManager;
        private readonly IToolCallLoopDetector _loopDetector;
        private readonly ISubAgentsParallelPolicy _parallelPolicy;
        private readonly ISubAgentsParallelRunner _parallelRunner;
        private readonly IActiveModelContext _activeModelContext;
        private readonly ISettingsManager _settingsManager;
        private readonly IUsageRecorder _usageRecorder;

        public ChatSessionOrchestratorFactory(
            IChatStreamService chatService,
            IToolExecutionManager toolManager,
            IHistoryCompactor compactor,
            IChatHistoryManager history,
            ISnapshotManager snapshotManager,
            IToolCallLoopDetector loopDetector,
            ISubAgentsParallelPolicy parallelPolicy,
            ISubAgentsParallelRunner parallelRunner,
            IActiveModelContext activeModelContext,
            ISettingsManager settingsManager,
            IUsageRecorder usageRecorder)
        {
            _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
            _toolManager = toolManager ?? throw new ArgumentNullException(nameof(toolManager));
            _compactor = compactor ?? throw new ArgumentNullException(nameof(compactor));
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
            _loopDetector = loopDetector ?? throw new ArgumentNullException(nameof(loopDetector));
            _parallelPolicy = parallelPolicy ?? throw new ArgumentNullException(nameof(parallelPolicy));
            _parallelRunner = parallelRunner ?? throw new ArgumentNullException(nameof(parallelRunner));
            _activeModelContext = activeModelContext ?? throw new ArgumentNullException(nameof(activeModelContext));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _usageRecorder = usageRecorder ?? throw new ArgumentNullException(nameof(usageRecorder));
        }

        public IChatSessionOrchestrator CreateOrchestrator()
        {
            return new ChatSessionOrchestrator(
                _chatService,
                _toolManager,
                _compactor,
                _history,
                _snapshotManager,
                _loopDetector,
                _parallelPolicy,
                _parallelRunner,
                _activeModelContext,
                _settingsManager,
                _usageRecorder);
        }
    }
}
