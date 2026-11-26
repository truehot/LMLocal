using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSession;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Application.SubAgents;
using LMLocal.Application.Tool;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using Moq;

namespace LMLocal.Tests.Unit
{
    /// <summary>
    /// Builds the orchestrator under test from the mocks a test sets up.
    /// </summary>
    internal sealed class OrchestratorTestBuilder
    {
        private Mock<IChatStreamService> _chatService;
        private Mock<IToolExecutionManager> _toolManager;
        private Mock<IHistoryCompactor> _compactor;
        private Mock<IChatHistoryManager> _history;
        private Mock<ISnapshotManager> _snapshotManager;
        private Mock<ISubAgentsParallelPolicy> _parallelPolicy;
        private Mock<IActiveModelContext> _activeModelContext;
        private Mock<ISettingsManager> _settingsManager;
        private Mock<IUsageRecorder> _usageRecorder;

        /// <summary>Generation service of the rounds.</summary>
        public Mock<IChatStreamService> ChatService
        {
            get { return _chatService ?? (_chatService = new Mock<IChatStreamService>()); }
            set { _chatService = value; }
        }

        /// <summary>Execution manager of the tool calls of a round.</summary>
        public Mock<IToolExecutionManager> ToolManager
        {
            get { return _toolManager ?? (_toolManager = new Mock<IToolExecutionManager>()); }
            set { _toolManager = value; }
        }

        /// <summary>History compactor; by default it never compacts.</summary>
        public Mock<IHistoryCompactor> Compactor
        {
            get { return _compactor ?? (_compactor = new Mock<IHistoryCompactor>()); }
            set { _compactor = value; }
        }

        /// <summary>Chat history of the main chat.</summary>
        public Mock<IChatHistoryManager> History
        {
            get { return _history ?? (_history = new Mock<IChatHistoryManager>()); }
            set { _history = value; }
        }

        /// <summary>Snapshot manager of the file changes of a tool round.</summary>
        public Mock<ISnapshotManager> SnapshotManager
        {
            get { return _snapshotManager ?? (_snapshotManager = new Mock<ISnapshotManager>()); }
            set { _snapshotManager = value; }
        }

        /// <summary>Policy that says which tool calls may run in parallel; by default none.</summary>
        public Mock<ISubAgentsParallelPolicy> ParallelPolicy
        {
            get { return _parallelPolicy ?? (_parallelPolicy = new Mock<ISubAgentsParallelPolicy>()); }
            set { _parallelPolicy = value; }
        }

        /// <summary>Runner of the parallel tool group; a scenario about the fan-out runs the real one.</summary>
        public ISubAgentsParallelRunner ParallelRunner { get; set; }

        /// <summary>Model context of the active model; a mock with MaxContextLength 0 tells the chat the window is unknown.</summary>
        public Mock<IActiveModelContext> ActiveModelContext
        {
            get { return _activeModelContext ?? (_activeModelContext = new Mock<IActiveModelContext>()); }
            set { _activeModelContext = value; }
        }

        /// <summary>Settings read into the usage record of the request.</summary>
        public Mock<ISettingsManager> SettingsManager
        {
            get { return _settingsManager ?? (_settingsManager = new Mock<ISettingsManager>()); }
            set { _settingsManager = value; }
        }

        /// <summary>Recorder the usage record of the request is written to.</summary>
        public Mock<IUsageRecorder> UsageRecorder
        {
            get { return _usageRecorder ?? (_usageRecorder = new Mock<IUsageRecorder>()); }
            set { _usageRecorder = value; }
        }

        /// <summary>
        /// The orchestrator, built the way the DI container builds it.
        /// </summary>
        public ChatSessionOrchestrator Build()
        {
            var parallelRunner = ParallelRunner ?? new Mock<ISubAgentsParallelRunner>().Object;

            return new ChatSessionOrchestrator(
                ChatService.Object,
                Compactor.Object,
                History.Object,
                new ChatToolRoundRunner(ToolManager.Object, SnapshotManager.Object, ParallelPolicy.Object, parallelRunner),
                ActiveModelContext.Object,
                new ChatUsageWriter(UsageRecorder.Object, SettingsManager.Object, ActiveModelContext.Object));
        }
    }
}
