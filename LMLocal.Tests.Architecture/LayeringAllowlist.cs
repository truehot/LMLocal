namespace LMLocal.Tests.Architecture
{
    /// <summary>
    /// Known layering violations.
    /// </summary>
    internal static class LayeringAllowlist
    {
        /// <summary>Application → Infrastructure. Closed by item 9 (ports moved to Application/Abstractions).</summary>
        public static readonly string[] ApplicationToInfrastructure =
        {
            "LMLocal.Application.Autocompletions.AutocompletionsService",
            "LMLocal.Application.Chat.HistoryCompactor",
            "LMLocal.Application.ChatSession.ChatSessionOrchestrator",
            "LMLocal.Application.ChatSession.ChatToolRoundRunner",
            "LMLocal.Application.ChatSession.IChatSessionOrchestrator",
            "LMLocal.Application.ChatSession.ISessionManager",
            "LMLocal.Application.ChatSession.SessionManager",
            "LMLocal.Application.ChatSessionStream.StreamingRoundService",
            "LMLocal.Application.ChatSessionStream.StreamRoundRequest",
            "LMLocal.Application.ModelsList.IModelsListService",
            "LMLocal.Application.ModelsList.ModelsListService",
            "LMLocal.Application.ModelsList.TestConnectionService",
            "LMLocal.Application.SubAgents.ReadRefs.ReadRefExpander",
            "LMLocal.Application.SubAgents.ReadRefs.ReadResultRecorder",
            "LMLocal.Application.SubAgents.ReadRefs.SearchResultRecorder",
            "LMLocal.Application.SubAgents.ReadRefs.SubAgentRunEvidence",
            "LMLocal.Application.SubAgents.ReadRefs.SymbolReferenceRecorder",
            "LMLocal.Application.SubAgents.SubAgentsService",
        };

        /// <summary>
        /// Commands → Infrastructure.DependencyInjection.
        /// </summary>
        public static readonly string[] CommandsToDependencyInjection = new string[0];
    }
}
