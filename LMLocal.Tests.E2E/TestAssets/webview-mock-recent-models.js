// Recent-models aware mock for startup priority tests.
// Configure via init scripts BEFORE the page loads (AddInitScript runs before lmInit):
//   window.__mockRecentEntries = [ { providerType, providerId, modelId, modelName, lastUsedUtc }, ... ]
//   window.__mockModels        = [ { id, name, isLoaded, supportsMaxTokens, maxTokens, supportsToolUse }, ... ]
//   window.__mockActiveModel   = null | { id, name, ... }
//   window.__mockSupportsIsLoaded = true | false
//   window.__mockSettings      = { AutoLoadOnStartup: true, Provider, ProviderId, ... } (optional)
const __mockBridge = {
    __webview: {
        addEventListener: () => {},
        removeEventListener: () => {}
    },
    ExecutePromptAsync: async (requestJson) => {},
    StopExecutionAsync: async () => {},
    ResetHistoryWithActionAsync: async () => true,
    SummarizeAndCompactAsync: async () => true,
    CopyToClipboardAsync: async (text) => true,
    GetSnapshotAsync: async () => true,
    DiscardChangesAsync: async () => true,
    AcceptChangesAsync: async () => true,
    ReviewFileAsync: async () => true,
    ReviewAllFilesAsync: async () => true,
    OpenAllFilesAsync: async () => true,
    DiscardFileAsync: async () => true,
    AcceptFileAsync: async () => true,
    FocusAsync: async () => {},
    GetInstructionsAsync: async () => '{}',
    UpdateInstructionsAsync: async (json) => true,
    GetProvidersAsync: async () => JSON.stringify({ success: true, data: { providers: [], providerTypes: [{ key: 'openai', displayName: 'OpenAI' }] } }),
    UpdateProvidersAsync: async (json) => true,
    GetToolsAsync: async () => JSON.stringify({ success: true, data: { tools: [] } }),
    UpdateToolsAsync: async (json) => true
};

function __startMock() {
    if (typeof window.lmInit === 'function') {
        window.__instructionsOverride = {
            GetInstructionsAsync: async () => '{}',
            UpdateInstructionsAsync: async (json) => true,
            UpdateInstructionsSelectedTabAsync: async (id) => true,
        };
        window.__providersOverride = {
            GetProvidersAsync: async () => JSON.stringify({
                success: true,
                data: {
                    providers: [],
                    providerTypes: [
                        { key: 'lmstudio', displayName: 'LM Studio' },
                        { key: 'openai', displayName: 'OpenAI' }
                    ]
                }
            }),
            UpdateProvidersAsync: async (json) => true,
        };
        window.__toolsOverride = {
            GetToolsAsync: async () => JSON.stringify({ success: true, data: { tools: [] } }),
            UpdateToolsAsync: async (json) => true,
        };
        window.__subAgentsOverride = {
            GetSubAgentsAsync: async () => JSON.stringify({ success: true, data: { agents: [] } }),
            UpdateSubAgentsAsync: async (json) => JSON.stringify({ success: true }),
        };
        window.__settingsOverride = {
            GetSettingsAsync: async () => JSON.stringify(window.__mockSettings || { AutoLoadOnStartup: true }),
            UpdateSettingsAsync: async (json) => true,
            TestConnectionAsync: async (json) => JSON.stringify({ success: true }),
            SetAiToolsAsync: async (json) => true,
            SetSubAgentsAsync: async (json) => true,
        };
        window.__mcpOverride = {
            GetMcpConfigAsync: async () => JSON.stringify({ success: true, data: { EnableMcp: false, McpServersJson: '{}' } }),
            UpdateMcpConfigAsync: async (json) => true,
            TestMcpConnectionAsync: async (json) => JSON.stringify({ success: true, data: { servers: [], hasErrors: false, hasSuccesses: false } }),
        };
        window.__modelsOverride = {
            ListModelsAsync: async () => JSON.stringify({
                models: window.__mockModels || [],
                hasActiveModel: !!window.__mockActiveModel,
                activeModel: window.__mockActiveModel || null,
                supportsIsLoaded: window.__mockSupportsIsLoaded !== false,
                error: null
            }),
            SetActiveModelAsync: async (modelId, contextLength) => {
                window.__capturedActiveModelId = modelId;
                return true;
            },
        };
        window.__recentModelsOverride = {
            GetRecentModelsAsync: async () => JSON.stringify({ entries: window.__mockRecentEntries || [] }),
            RecordModelUsageAsync: async () => true,
        };
        window.__chatSessionOverride = {
            GetLastChatSessionAsync: async () => JSON.stringify({ hasSession: false, messages: [] }),
            GetChatSessionsAsync: async () => JSON.stringify({ sessions: [] }),
            GetChatSessionByIdAsync: async (sessionId) => JSON.stringify({ hasSession: false, messages: [] }),
        };
        window.__bridgeOverride = __mockBridge;
        window.__hostOverride = {
            CopyToClipboardAsync: async (text) => true,
            FocusAsync: async () => {},
        };
        window.lmInit(__mockBridge);
    } else {
        console.log('[mock] lmInit not ready, retrying...');
        setTimeout(__startMock, 10);
    }
}

if (document.readyState === 'complete') {
    __startMock();
} else {
    window.addEventListener('load', __startMock);
}
