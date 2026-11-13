// Mock that does NOT drive the stream on its own: the test emits bridge messages
// explicitly through window.__emitBridgeMessage(...) so every step is deterministic.
const _listeners = [];
window._listeners = _listeners;

window.__emitBridgeMessage = (msg) => {
    _listeners.forEach(fn => { try { fn({ data: msg }); } catch (e) { /* swallow */ } });
};

const __mockBridge = {
    __webview: {
        addEventListener: (event, handler) => {
            if (event === 'message') _listeners.push(handler);
        },
        removeEventListener: (event, handler) => {
            const i = _listeners.indexOf(handler);
            if (i !== -1) _listeners.splice(i, 1);
        }
    },
    // The test drives the whole protocol, so sending a prompt does not schedule anything.
    ExecutePromptAsync: async (requestJson) => { },
    StopExecutionAsync: async () => { },
    ResetHistoryWithActionAsync: async () => true,
    SummarizeAndCompactAsync: async () => true,
    CopyToClipboardAsync: async (text) => true,
    GetInstructionsAsync: async () => JSON.stringify({ tabs: [] }),
    UpdateInstructionsAsync: async (json) => true,
    GetSettingsAsync: async () => JSON.stringify({ AutoLoadOnStartup: true, Provider: 'openai', LmStudioBaseUrl: 'http://localhost:1234' }),
    UpdateSettingsAsync: async (json) => true,
    GetSnapshotAsync: async () => '{}'
};

function __startMock() {
    if (typeof window.lmInit === 'function') {
        window.__instructionsOverride = {
            GetInstructionsAsync: async () => '{}',
            UpdateInstructionsAsync: async (json) => JSON.stringify({ success: true }),
            UpdateInstructionsSelectedTabAsync: async (id) => true,
        };
        window.__providersOverride = {
            GetProvidersAsync: async () => '{}',
            UpdateProvidersAsync: async (json) => true,
        };
        window.__toolsOverride = {
            GetToolsAsync: async () => JSON.stringify({
                success: true,
                data: { tools: [] }
            }),
            UpdateToolsAsync: async (json) => true,
        };
        // Two loaded models with different context windows, so a model switch can be verified:
        // the context value must survive and the percentage must recalculate for the new window.
        // test-model-1 = 16384 (default), test-model-2 = 8192 (half).
        window.__modelsOverride = {
            ListModelsAsync: async () => JSON.stringify({
                models: [
                    { id: "test-model-1", name: "Test Model", maxTokens: 16384, supportsMaxTokens: true, isLoaded: true, supportsToolUse: null },
                    { id: "test-model-2", name: "Small Model", maxTokens: 8192, supportsMaxTokens: true, isLoaded: true, supportsToolUse: null }
                ],
                hasActiveModel: false,
                activeModel: null,
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
            GetChatSessionsAsync: async () => JSON.stringify({
                sessions: [
                    {
                        sessionId: 'session-1',
                        timestamp: '2024-01-01T00:00:00Z',
                        prompt: 'Loaded session prompt',
                        modelName: 'Test Model',
                        messageCount: 2
                    }
                ]
            }),
            GetChatSessionByIdAsync: async (sessionId) => JSON.stringify({
                hasSession: true,
                messages: [
                    { role: 'user', content: 'Loaded question' },
                    { role: 'assistant', content: 'Loaded answer' }
                ]
            }),
        };
        window.__bridgeOverride = __mockBridge;
        window.__hostOverride = {
            CopyToClipboardAsync: async (text) => true,
            FocusAsync: async () => { },
        };
        window.__settingsOverride = {
            GetSettingsAsync: async () => JSON.stringify({ AutoLoadOnStartup: true, Provider: 'openai', LmStudioBaseUrl: 'http://localhost:1234' }),
            UpdateSettingsAsync: async (json) => true,
            TestConnectionAsync: async (json) => JSON.stringify({ success: true }),
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
        if (!window.__modelsConfigOverride) {
            window.__modelsConfigOverride = {
                GetModelsConfigAsync: async () => JSON.stringify({ models: [] }),
                UpdateModelsConfigAsync: async () => true
            };
        }
        window.lmInit(__mockBridge);
    } else {
        setTimeout(__startMock, 10);
    }
}

if (document.readyState === 'complete') {
    __startMock();
} else {
    window.addEventListener('load', __startMock);
}
