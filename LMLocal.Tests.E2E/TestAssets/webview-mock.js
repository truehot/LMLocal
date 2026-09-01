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
    GetInstructionsAsync: async () => {
        console.log('[mock] GetInstructionsAsync called');
        return JSON.stringify({ tabs: [] });
    },
    UpdateInstructionsAsync: async (json) => {
        console.log('[mock] UpdateInstructionsAsync called');
        return true;
    },
    GetProvidersAsync: async () => {
        console.log('[mock] GetProvidersAsync called');
        return JSON.stringify({
            success: true,
            data: {
                providers: [],
                providerTypes: [
                    { key: 'openai', displayName: 'OpenAI' },
                    { key: 'ollama', displayName: 'Ollama' }
                ]
            }
        });
    },
    UpdateProvidersAsync: async (json) => {
        console.log('[mock] UpdateProvidersAsync called with:', json);
        return true;
    },
    GetToolsAsync: async () => {
        console.log('[mock] GetToolsAsync called');
        return JSON.stringify({
            success: true,
            data: {
                tools: [
                    { id: 'tool-1', name: 'read_file', description: 'Read file contents', enabled: true },
                    { id: 'tool-2', name: 'write_file', description: 'Write file contents', enabled: false },
                    { id: 'tool-3', name: 'search_files', description: 'Search for files', enabled: true }
                ]
            }
        });
    },
    UpdateToolsAsync: async (json) => {
        console.log('[mock] UpdateToolsAsync called with:', json);
        return true;
    }
};

function __startMock() {
    if (typeof window.lmInit === 'function') {
        console.log('[mock] calling window.lmInit');
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
                        { key: 'openai', displayName: 'OpenAI' },
                        { key: 'ollama', displayName: 'Ollama' }
                    ]
                }
            }),
            UpdateProvidersAsync: async (json) => true,
        };
        window.__modelsConfigOverride = {
            GetModelsConfigAsync: async () => JSON.stringify({
                models: [
                    { id: 1, modelId: 'qwen2.5-coder-7b-instruct', providerType: 'lmstudio', providerId: 0, contextLength: 32768, isCustom: false, enabled: true },
                    { id: 2, modelId: 'manual/custom-model', providerType: 'openai', providerId: 3, displayName: 'My Custom', contextLength: 8192, isCustom: true, enabled: true }
                ]
            }),
            UpdateModelsConfigAsync: async (json) => {
                console.log('[mock] UpdateModelsConfigAsync called:', json);
                window.__lastSavedModelsConfig = json;
                return true;
            },
        };
        window.__toolsOverride = {
            GetToolsAsync: async () => JSON.stringify({
                success: true,
                data: {
                    tools: [
                        { id: 'tool-1', name: 'read_file', description: 'Read file contents', enabled: true },
                        { id: 'tool-2', name: 'write_file', description: 'Write file contents', enabled: false },
                        { id: 'tool-3', name: 'search_files', description: 'Search for files', enabled: true }
                    ]
                }
            }),
            UpdateToolsAsync: async (json) => true,
        };
        window.__subAgentsOverride = {
            GetSubAgentsAsync: async () => JSON.stringify({
                success: true,
                data: {
                    agents: [
                        { id: 'researcher', displayName: 'Researcher', description: 'Research agent', providerType: 'deepseek', customBaseUrl: 'https://api.deepseek.com', model: 'deepseek-chat', temperature: 0.3, timeoutSeconds: 90, maxRounds: 5, maxTokens: 2048, enabled: true, allowedTools: ['get_solution_overview', 'find_files'] },
                        { id: 'coder', displayName: 'Coder', description: 'Coder agent', providerType: 'lmstudio', customBaseUrl: 'http://localhost:1234', model: 'qwen2.5-coder-7b-instruct', enabled: false, allowedTools: [] }
                    ]
                }
            }),
            UpdateSubAgentsAsync: async (json) => {
                console.log('[mock] UpdateSubAgentsAsync called:', json);
                return JSON.stringify({ success: true });
            },
        };
        window.__settingsOverride = {
            GetSettingsAsync: async () => JSON.stringify({ AutoLoadOnStartup: true }),
            UpdateSettingsAsync: async (json) => true,
            TestConnectionAsync: async (json) => JSON.stringify({ success: true }),
            SetAiToolsAsync: async (json) => {
                console.log('[mock] SetAiToolsAsync called:', json);
                return true;
            },
            SetSubAgentsAsync: async (json) => {
                console.log('[mock] SetSubAgentsAsync called:', json);
                return true;
            },
        };
        window.__mcpOverride = {
            GetMcpConfigAsync: async () => JSON.stringify({
                success: true,
                data: { EnableMcp: false, McpServersJson: '{}' }
            }),
            UpdateMcpConfigAsync: async (json) => true,
            TestMcpConnectionAsync: async (json) => JSON.stringify({
                success: true,
                data: { servers: [], hasErrors: false, hasSuccesses: false }
            }),
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
