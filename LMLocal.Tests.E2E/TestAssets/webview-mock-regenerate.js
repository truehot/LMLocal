// Mock that simulates streaming: fires chunks then StreamEnd via postMessage
const _listeners = [];
// timers for scheduled mock events so tests can cancel them when simulating errors
const _timers = [];
// Expose listeners and helpers for tests so they can simulate bridge messages and cancel pending timers.
window._listeners = _listeners;
window._mock_timers = _timers;
window.__emitBridgeMessage = (msg) => {
    // if a StreamError/disconnect is emitted, cancel pending scheduled mock events to avoid race
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

// Records every prompt request the app sends, so a test can see what the bridge received.
window.__bridgeCalls = [];
// Records every history reset, which the regenerate/edit path must not use.
window.__bridgeResetCalls = [];
// When true, a request that asks to replace the last exchange is refused like an out-of-sync history.
window.__failReplacement = false;
// When true, the next request fails like a session-lock timeout: an error before ChatSessionStart.
window.__failSessionLock = false;
// When true, the next request starts and then fails, leaving an error bubble with Continue.
window.__failAfterStart = false;
// The backend history the mock keeps, so a refused request can be checked against it.
window.__history = [];

function __emit(data, delay) {
    _timers.push(setTimeout(() => _listeners.forEach(fn => fn({ data })), delay));
}

/**
 * Drops the last exchange like ChatHistoryManager.TruncateLastExchangeAsync.
 */
function __truncateLastExchange(keepUserMessage) {
    let lastUser = -1;
    for (let i = window.__history.length - 1; i >= 0; i--) {
        if (window.__history[i].role === 'user') { lastUser = i; break; }
    }
    if (lastUser < 0) return;
    window.__history = window.__history.slice(0, lastUser + (keepUserMessage ? 1 : 0));
}

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
        const request = requestJson ? JSON.parse(requestJson) : {};
        window.__bridgeCalls.push(request);

        // Both failures happen before the session starts, so the history stays as it was.
        if (window.__failSessionLock) {
            window.__failSessionLock = false;
            __emit({ Type: 'ChatSessionError', Payload: 'Previous session is still finishing. Try again in a moment.' }, 50);
            return;
        }

        if (request.replaceLast && window.__failReplacement) {
            __emit({ Type: 'ChatSessionError', Payload: 'The chat is out of sync with the history. Nothing was changed.' }, 50);
            return;
        }

        if (request.replaceLast) __truncateLastExchange(request.replaceLast === 'answer');
        if (request.prompt) window.__history.push({ role: 'user', content: request.prompt });

        __emit({ Type: 'ChatSessionStart' }, 20);

        if (window.__failAfterStart) {
            window.__failAfterStart = false;
            __emit({ Type: 'ChatSessionError', Payload: 'Model crashed' }, 60);
            return;
        }

        window.__history.push({ role: 'assistant', content: 'Answer #' + window.__bridgeCalls.length });

        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: { Type: 'StreamContent', Payload: 'Answer #' + window.__bridgeCalls.length, Count: 10, TokensPerSecond: 15.5 }
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
                    TotalTokens: 10,
                    ReasoningTokens: 0,
                    CachedTokens: 0,
                    TokensPerSecond: 15.5
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
    ResetHistoryWithActionAsync: async (action) => {
        window.__bridgeResetCalls.push(action);
        return true;
    },
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
        return JSON.stringify({ AutoLoadOnStartup: true });
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
            GetCurrentChatHistoryAsync: async () => JSON.stringify({
                hasSession: window.__history.length > 0,
                messages: window.__history
            }),
        };
        window.__bridgeOverride = __mockBridge;
        window.__hostOverride = {
            CopyToClipboardAsync: async (text) => true,
            FocusAsync: async () => {},
        };
        window.__settingsOverride = {
            GetSettingsAsync: async () => JSON.stringify({ AutoLoadOnStartup: true }),
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
        if (typeof __mockBridge.GetSnapshotAsync !== 'function') {
            __mockBridge.GetSnapshotAsync = async () => '{}';
        }
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
