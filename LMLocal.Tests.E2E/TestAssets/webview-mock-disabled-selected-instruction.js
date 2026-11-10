const __mockBridge = {
    __webview: {
        addEventListener: () => {},
        removeEventListener: () => {}
    },
    ExecutePromptAsync: async (requestJson) => { window.__lastPromptRequest = typeof requestJson === "string" ? requestJson : JSON.stringify(requestJson); },
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
};

// Instruction document: the persisted selection (id 4, "Review") points at a DISABLED tab,
// e.g. after the user unchecked the open tab in "AI Instructions..." and clicked Save.
const __MOCK_INSTRUCTIONS = {
    selectedTabId: 4,
    tabs: [
        { id: 1, displayName: 'Default', enabled: true, temperature: 0.2, prompt: 'DEFAULT PROMPT' },
        { id: 4, displayName: 'Review', enabled: false, temperature: 0.5, prompt: 'REVIEW PROMPT' }
    ]
};

// Models config: no instruction binding, so the persisted selection decides.
const __MOCK_MODELS_CONFIG = {
    models: [
        {
            id: 1,
            modelId: 'test-model-instance',
            providerType: 'openai',
            providerId: 0,
            displayName: 'Test Model',
            isCustom: false,
            enabled: true
        },
        {
            id: 2,
            modelId: 'other-model',
            providerType: 'openai',
            providerId: 0,
            displayName: 'Other Model',
            isCustom: true,
            enabled: true
        }
    ]
};

function __startMock() {
    if (typeof window.lmInit === 'function') {
        console.log('[mock] calling window.lmInit');
        window.__instructionsOverride = {
            GetInstructionsAsync: async () => JSON.stringify(__MOCK_INSTRUCTIONS),
            UpdateInstructionsAsync: async (json) => JSON.stringify({ success: true }),
            UpdateInstructionsSelectedTabAsync: async (id) => true,
        };
        window.__providersOverride = {
            GetProvidersAsync: async () => JSON.stringify({
                success: true,
                data: {
                    providers: [],
                    providerTypes: [
                        { key: 'openai', displayName: 'OpenAI' },
                        { key: 'ollama', displayName: 'Ollama' }
                    ]
                }
            }),
            UpdateProvidersAsync: async (json) => true,
        };
        window.__modelsConfigOverride = {
            GetModelsConfigAsync: async () => JSON.stringify(__MOCK_MODELS_CONFIG),
            UpdateModelsConfigAsync: async (json) => {
                console.log('[mock] UpdateModelsConfigAsync called:', json);
                window.__lastSavedModelsConfig = json;
                return true;
            },
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
            GetSettingsAsync: async () => JSON.stringify({ AutoLoadOnStartup: true, Provider: 'openai', ProviderId: 0 }),
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
                models: [
                    {
                        id: "test-model-1",
                        name: "Test Model",
                        maxTokens: 16384,
                        supportsMaxTokens: true,
                        isLoaded: false,
                        supportsToolUse: null
                    }
                ],
                hasActiveModel: true,
                activeModel: {
                    id: "test-model-instance",
                    name: "Test Model",
                    maxTokens: 16384,
                    supportsMaxTokens: true,
                    isLoaded: true,
                    supportsToolUse: null
                },
                supportsIsLoaded: true,
                error: null
            }),
            SetActiveModelAsync: async (modelId, contextLength) => true,
        };
        window.__recentModelsOverride = {
            GetRecentModelsAsync: async () => JSON.stringify({ entries: [] }),
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
