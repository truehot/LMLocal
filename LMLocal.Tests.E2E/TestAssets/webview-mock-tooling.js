// Mock that simulates tool calls during streaming: fires ToolCall, ToolEnd messages
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
        error: null
    }),
    SetActiveModelAsync: async (modelId, contextLength) => {
        console.log('[mock] SetActiveModelAsync called with:', modelId, contextLength);
        return true;
    },
    ExecutePromptAsync: async (requestJson) => {
        // Emit ToolCall message
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: {
                    Type: 'StreamToolCall',
                    FunctionName: 'SearchInFiles',
                    CallId: 'call_search_001',
                    ArgumentsJson: '{"query": "test"}',
                    Message: 'Searching for "test"...'
                }
            }));
        }, 50));

        // Emit some content while tool is running
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: { Type: 'StreamContent', Payload: 'Based on the search results: ', Count: 5, TokensPerSecond: 10.0 }
            }));
        }, 100));

        // Emit ToolEnd message (successful)
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: {
                    Type: 'StreamToolEnd',
                    FunctionName: 'SearchInFiles',
                    CallId: 'call_search_001',
                    Message: 'Found 3 matches',
                    IsError: false
                }
            }));
        }, 150));

        // Emit second tool call (with error)
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: {
                    Type: 'StreamToolCall',
                    FunctionName: 'FindSymbolReferences',
                    CallId: 'call_symbol_002',
                    ArgumentsJson: '{"symbol": "MyClass"}',
                    Message: 'Finding symbol references...'
                }
            }));
        }, 200));

        // Emit ToolEnd message (error)
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: {
                    Type: 'StreamToolEnd',
                    FunctionName: 'FindSymbolReferences',
                    CallId: 'call_symbol_002',
                    Message: 'Symbol not found',
                    Error: 'Symbol "MyClass" not found in solution',
                    IsError: true
                }
            }));
        }, 250));

        // Emit content
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: { Type: 'StreamContent', Payload: 'here is the summary.', Count: 5, TokensPerSecond: 12.0 }
            }));
        }, 300));

        // End streaming
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: { Type: 'StreamEnd' }
            }));
        }, 350));

        // Session complete
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: { Type: 'ChatSessionComplete', Payload: {} }
            }));
        }, 400));
    },
    StopExecutionAsync: async () => {
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: { Type: 'StreamEnd' }
            }));
        }, 50));
    },
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
    }
};

function __startMock() {
    if (typeof window.lmInit === 'function') {
        window.__bridgeOverride = __mockBridge;
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
