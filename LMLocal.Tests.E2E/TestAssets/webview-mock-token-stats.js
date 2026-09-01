// Mock that simulates streaming with token stats enabled.
// Same flow as webview-mock-streaming.js but:
//  - GetSettingsAsync returns ShowTokenStats: true
//  - ChatSessionComplete carries token metadata on the message root (real backend shape)
const _listeners = [];
const _timers = [];
window._listeners = _listeners;
window._mock_timers = _timers;
window.__emitBridgeMessage = (msg) => {
    try {
        console.log('[mock] __emitBridgeMessage called', msg);
        if (msg && msg.Type === 'StreamError') {
            console.log('[mock] StreamError emitted, clearing timers:', _timers.length);
            while (_timers.length) {
                const id = _timers.shift();
                try { clearTimeout(id); } catch (e) { }
            }
        }
    } catch (e) { }
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
    ExecutePromptAsync: async (requestJson) => {
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: { Type: 'StreamContent', Payload: '```javascript\nconsole.log("hello");\n```', Count: 10, TokensPerSecond: 15.5 }
            }));
        }, 50));
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: { Type: 'StreamEnd' }
            }));
        }, 150));
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: {
                    Type: 'ChatSessionComplete',
                    TotalTokens: 800,
                    PromptTokens: 500,
                    CompletionTokens: 300,
                    ReasoningTokens: 8,
                    CachedTokens: 100,
                    TokensPerSecond: 42.0
                }
            }));
        }, 200));
    },
    StopExecutionAsync: async () => {
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: { Type: 'StreamEnd' }
            }));
        }, 50));
    },
    ResetHistoryWithActionAsync: async () => true,
    SummarizeAndCompactAsync: async () => true,
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
        return JSON.stringify({ AutoLoadOnStartup: true, ShowTokenStats: true });
    },
    UpdateSettingsAsync: async (json) => {
        console.log('[mock] UpdateSettingsAsync called');
        return true;
    }
};

function __startMock() {
    if (typeof window.lmInit === 'function') {
        window.__instructionsOverride = {
            GetInstructionsAsync: async () => '{}',
            UpdateInstructionsAsync: async (json) => true,
            UpdateInstructionsSelectedTabAsync: async (id) => true,
        };
        window.__providersOverride = {
            GetProvidersAsync: async () => '{}',
            UpdateProvidersAsync: async (json) => true,
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
        window.__modelsOverride = {
            ListModelsAsync: async () => JSON.stringify({
                models: [{ id: "test-model-1", name: "Test Model", maxTokens: 16384, supportsMaxTokens: true, isLoaded: false, supportsToolUse: null }],
                hasActiveModel: true,
                activeModel: { id: "test-model-instance", name: "Test Model", maxTokens: 16384, supportsMaxTokens: true, isLoaded: true, supportsToolUse: null },
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
        window.__settingsOverride = {
            GetSettingsAsync: async () => JSON.stringify({ AutoLoadOnStartup: true, ShowTokenStats: true }),
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
