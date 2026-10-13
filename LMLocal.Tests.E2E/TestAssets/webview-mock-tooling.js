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
        }, 300));

        // Emit ToolEnd message (successful) — 550ms after second ToolCall for reliable test detection
        // 1000ms keeps the STEPPING window (progress steps) long enough for stop-button tests
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
        }, 1000));

        // Emit progress steps for the first tool: tool name, then repeated "thinking" status
        // (simulates SubAgent progress: ToolStep must not erase the previous line)
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: {
                    Type: 'StreamToolStep',
                    FunctionName: 'SearchInFiles',
                    CallId: 'call_search_001',
                    Message: 'searching files...',
                    Step: 1
                }
            }));
        }, 200));

        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: {
                    Type: 'StreamToolStep',
                    FunctionName: 'SearchInFiles',
                    CallId: 'call_search_001',
                    Message: 'thinking',
                    Step: 2
                }
            }));
        }, 350));

        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: {
                    Type: 'StreamToolStep',
                    FunctionName: 'SearchInFiles',
                    CallId: 'call_search_001',
                    Message: 'thinking',
                    Step: 3
                }
            }));
        }, 450));

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
        }, 650));

        // Emit ToolEnd message (error) — 550ms after second ToolCall
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: {
                    Type: 'StreamToolEnd',
                    FunctionName: 'FindSymbolReferences',
                    CallId: 'call_symbol_002',
                    Message: 'Symbol not found',
                    IsError: true
                }
            }));
        }, 1200));

        // Emit content
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: { Type: 'StreamContent', Payload: 'here is the summary.', Count: 5, TokensPerSecond: 12.0 }
            }));
        }, 1300));

        // End streaming
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: { Type: 'StreamEnd' }
            }));
        }, 1400));

        // Session complete
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: { Type: 'ChatSessionComplete', Payload: {} }
            }));
        }, 1500));
    },
    StopExecutionAsync: async () => {
        window.__stopCallCount = (window.__stopCallCount || 0) + 1;
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
