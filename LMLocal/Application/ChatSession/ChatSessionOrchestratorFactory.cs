using System;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using LMLocal.Services.Tool;

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
        private readonly ISnapshotManager _snapshotManager;

        public ChatSessionOrchestratorFactory(
            IChatStreamService chatService,
            IToolExecutionManager toolManager,
            IHistoryCompactor compactor,
            ISnapshotManager snapshotManager)
        {
            _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
            _toolManager = toolManager ?? throw new ArgumentNullException(nameof(toolManager));
            _compactor = compactor ?? throw new ArgumentNullException(nameof(compactor));
            _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
        }

        public IChatSessionOrchestrator CreateOrchestrator()
        {
            return new ChatSessionOrchestrator(_chatService, _toolManager, _compactor, _snapshotManager);
        }
    }
}
