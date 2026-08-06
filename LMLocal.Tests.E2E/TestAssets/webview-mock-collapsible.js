// Mock that simulates a multi-round collapsible chat:
// Round 1: 2 tools + streaming content
// Round 2 (final): 1 tool + final streaming content
const _listeners = [];
const _timers = [];
window._listeners = _listeners;
window._mock_timers = _timers;
window.__emitBridgeMessage = (msg) => {
    try {
        console.log('[mock-collapsible] __emitBridgeMessage', msg);
        if (msg && (msg.Type === 'StreamError' || msg.Type === 'ChatSessionError')) {
            while (_timers.length) {
                const id = _timers.shift();
                try { clearTimeout(id); } catch (e) { }
            }
        }
    } catch (e) { }
    _listeners.forEach(fn => { try { fn({ data: msg }); } catch (e) { } });
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
        // === Round 1: 2 tools ===
        // ChatSessionIterating informs controller about round 1 (not final)
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: {
                    Type: 'ChatSessionIterating',
                    RoundNumber: 1,
                    ToolCount: 2,
                    IsFinalRound: false
                }
            }));
        }, 10));

        // Tool 1: SearchFiles
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: {
                    Type: 'StreamToolCall',
                    FunctionName: 'SearchFiles',
                    CallId: 'call_search_001',
                    ArgumentsJson: '{"query":"test"}',
                    Message: 'Searching for files matching "test"...'
                }
            }));
        }, 50));

        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: {
                    Type: 'StreamToolEnd',
                    FunctionName: 'SearchFiles',
                    CallId: 'call_search_001',
                    Message: 'Found 5 files matching "test"',
                    IsError: false
                }
            }));
        }, 100));

        // Tool 2: ReadFile
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: {
                    Type: 'StreamToolCall',
                    FunctionName: 'ReadFile',
                    CallId: 'call_read_002',
                    ArgumentsJson: '{"path":"src/app.js"}',
                    Message: 'Reading file src/app.js...'
                }
            }));
        }, 150));

        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: {
                    Type: 'StreamToolEnd',
                    FunctionName: 'ReadFile',
                    CallId: 'call_read_002',
                    Message: 'File read successfully (120 lines)',
                    IsError: false
                }
            }));
        }, 200));

        // Round 1 streaming content
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: { Type: 'StreamContent', Payload: 'Based on the search, ', Count: 5, TokensPerSecond: 10.0 }
            }));
        }, 250));

        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: { Type: 'StreamContent', Payload: 'I found relevant files.', Count: 5, TokensPerSecond: 10.0 }
            }));
        }, 300));

        // StreamEnd for round 1
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: { Type: 'StreamEnd' }
            }));
        }, 350));

        // === Round 2 (final): 1 tool ===
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: {
                    Type: 'ChatSessionIterating',
                    RoundNumber: 2,
                    ToolCount: 1,
                    IsFinalRound: true
                }
            }));
        }, 400));

        // Tool for final round: WriteFile
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: {
                    Type: 'StreamToolCall',
                    FunctionName: 'WriteFile',
                    CallId: 'call_write_003',
                    ArgumentsJson: '{"path":"output.txt"}',
                    Message: 'Writing output file...'
                }
            }));
        }, 450));

        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: {
                    Type: 'StreamToolEnd',
                    FunctionName: 'WriteFile',
                    CallId: 'call_write_003',
                    Message: 'File written successfully',
                    IsError: false
                }
            }));
        }, 500));

        // Final round streaming content
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: { Type: 'StreamContent', Payload: 'Here is the final summary: ', Count: 5, TokensPerSecond: 12.0 }
            }));
        }, 550));

        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: { Type: 'StreamContent', Payload: 'everything looks good.', Count: 5, TokensPerSecond: 12.0 }
            }));
        }, 600));

        // StreamEnd for final round
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: { Type: 'StreamEnd' }
            }));
        }, 650));

        // Session complete
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: { Type: 'ChatSessionComplete', Payload: { TotalTokens: 800, ReasoningTokens: 0 } }
            }));
        }, 700));
    },
    StopExecutionAsync: async () => {
        while (_timers.length) {
            const id = _timers.shift();
            try { clearTimeout(id); } catch (e) { }
        }
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
        // CollapseToolCalls: true enables the collapsible message path
        return JSON.stringify({ AutoLoadOnStartup: true, CollapseToolCalls: true });
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
        window.__settingsOverride = {
            GetSettingsAsync: async () => JSON.stringify({ AutoLoadOnStartup: true, CollapseToolCalls: true }),
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
        window.__chatSessionOverride = {
            GetLastChatSessionAsync: async () => JSON.stringify({ hasSession: false, messages: [] }),
            GetChatSessionsAsync: async () => JSON.stringify({ sessions: [] }),
            GetChatSessionByIdAsync: async (sessionId) => JSON.stringify({ hasSession: false, messages: [] }),
        };
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
