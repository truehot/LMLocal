const __mockBridge = {
    __webview: {
        addEventListener: () => {},
        removeEventListener: () => {}
    },
    ListModelsAsync: async () => {
        console.log('[mock] ListModelsAsync called');
        const result = JSON.stringify({
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
            error: null
        });
        console.log('[mock] ListModelsAsync returning:', result);
        return result;
    },
    SetActiveModelAsync: async (modelId, contextLength) => {
        console.log('[mock] SetActiveModelAsync called with:', modelId, contextLength);
        return true;
    },
    ExecutePromptAsync: async (requestJson) => {},
    StopExecutionAsync: async () => {},
    ResetHistoryAsync: async () => {},
    CopyToClipboardAsync: async (text) => true,
    GetInstructionsAsync: async () => {
        console.log('[mock] GetInstructionsAsync called');
        return JSON.stringify({ tabs: [] });
    },
    UpdateInstructionsAsync: async (json) => {
        console.log('[mock] UpdateInstructionsAsync called');
        return true;
    },
    GetSettingsAsync: async () => {
        console.log('[mock] GetSettingsAsync called');
        return JSON.stringify({ AutoLoadOnStartup: true });
    },
    UpdateSettingsAsync: async (json) => {
        console.log('[mock] UpdateSettingsAsync called');
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
    TestConnectionAsync: async (json) => {
        console.log('[mock] TestConnectionAsync called with:', json);
        return JSON.stringify({ success: true });
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
    },
    GetMcpConfigAsync: async () => {
        console.log('[mock] GetMcpConfigAsync called');
        return JSON.stringify({
            success: true,
            data: {
                EnableMcp: false,
                McpServersJson: '{}'
            }
        });
    },
    UpdateMcpConfigAsync: async (json) => {
        console.log('[mock] UpdateMcpConfigAsync called with:', json);
        return true;
    },
    TestMcpConnectionAsync: async (json) => {
        console.log('[mock] TestMcpConnectionAsync called with:', json);
        return JSON.stringify({
            success: true,
            data: { servers: [], hasErrors: false, hasSuccesses: false }
        });
    }
};

function __startMock() {
    if (typeof window.lmInit === 'function') {
        console.log('[mock] calling window.lmInit');
        window.__bridgeOverride = __mockBridge;
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
