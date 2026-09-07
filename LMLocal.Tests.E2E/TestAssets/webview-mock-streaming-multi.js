// Collapsible multi-round mock that mirrors the real orchestrator message order:
//   round 1 (tool call) -> nextRound -> round 2 (long final answer, multiple chunks) -> StreamEnd.
// Used to reproduce the bug where the last-but-one rendered block is overwritten by the
// final flush in collapsible mode.
const _listeners = [];
const _timers = [];

window._listeners = _listeners;
window._mock_timers = _timers;
window.__emitBridgeMessage = (msg) => {
    try {
        console.log('[mock] __emitBridgeMessage called', msg);
        if (msg && (msg.Type === 'StreamError' || msg.Type === 'ChatSessionError')) {
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
        // Round 1: short content then a tool call (streams into the initial step).
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: { Type: 'StreamContent', Payload: 'Searching for files in the project...', Count: 5, TokensPerSecond: 10.0 }
            }));
        }, 10));

        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: {
                    Type: 'StreamToolCall',
                    FunctionName: 'SearchFiles',
                    CallId: 'call_search_001',
                    ArgumentsJson: '{"query":"IChatHistoryManager"}',
                    Message: 'Searching for IChatHistoryManager...'
                }
            }));
        }, 50));

        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: {
                    Type: 'StreamToolEnd',
                    FunctionName: 'SearchFiles',
                    CallId: 'call_search_001',
                    Message: 'Found 1 file',
                    IsError: false
                }
            }));
        }, 100));

        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: { Type: 'StreamEnd' }
            }));
        }, 150));

        // Transition to round 2 (not final yet): nextRound creates a new step.
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: {
                    Type: 'ChatSessionIterating',
                    RoundNumber: 1,
                    ToolCount: 1,
                    IsFinalRound: false
                }
            }));
        }, 200));

        // Round 2 (final answer) streams into the new step in several chunks.
        const chunks = [
            'Open the solution or project in Visual Studio 2022.\n\n',
            'In the Solution Explorer window (Solution Explorer panel) find and select the source code file where the IChatHistoryManager class is defined.\n\n',
            'Then scroll through the file contents to find mentions of the IChatHistoryManager class. It can be used in various places, for example:\n\n    When creating an instance: myInstance = new ChatHistoryManager();\n\n    As a method parameter: myMethod(IChatHistoryManager historyManager)\n\n',
            '    As a variable type: IChatHistoryManager historyManager;\n\nWhen you find all usage locations of IChatHistoryManager, you will be able to better understand its role and purpose in your project.\n\n',
            'If the IChatHistoryManager class is not found in the current project or solution, its definition may be located in another assembly or project.\n\nHope this helps you find all usage locations of the IChatHistoryManager class in your project!'
        ];

        let delay = 250;
        for (const chunk of chunks) {
            const payload = chunk;
            _timers.push(setTimeout(() => {
                _listeners.forEach(fn => fn({
                    data: { Type: 'StreamContent', Payload: payload, Count: 10, TokensPerSecond: 15.5 }
                }));
            }, delay));
            delay += 30;
        }

        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: { Type: 'StreamEnd' }
            }));
        }, delay + 5));

        // Mark final round (real orchestrator sends this AFTER generation when no more tools).
        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: {
                    Type: 'ChatSessionIterating',
                    RoundNumber: 2,
                    ToolCount: 0,
                    IsFinalRound: true
                }
            }));
        }, delay + 20));

        _timers.push(setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: {
                    Type: 'ChatSessionComplete',
                    TotalTokens: 100,
                    ReasoningTokens: 0,
                    CachedTokens: 0,
                    TokensPerSecond: 15.5
                }
            }));
        }, delay + 60));
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
