// Progress header mock (`expanded`): a reading tool, the round transition right after it, then three parallel SubAgents.
const _listeners = [];
const _timers = [];
window._listeners = _listeners;
window._mock_timers = _timers;
window.__emitBridgeMessage = (msg) => {
    try {
        console.log('[mock-progress-header] __emitBridgeMessage', msg);
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
        const emit = (delay, data) => {
            _timers.push(setTimeout(() => {
                _listeners.forEach(fn => { try { fn({ data }); } catch (e) { } });
            }, delay));
        };

        // === Round 1: one reading tool; the round transition follows its end at once, as the backend sends it ===
        emit(250, {
            Type: 'StreamToolCall',
            FunctionName: 'search_file_content',
            CallId: 'call_search_001',
            ArgumentsJson: '{"pattern":"IChatHistoryManager"}',
            Message: 'Searching for "IChatHistoryManager"...'
        });
        emit(350, {
            Type: 'StreamToolEnd',
            FunctionName: 'search_file_content',
            CallId: 'call_search_001',
            Message: 'Found 3 matches',
            IsError: false
        });
        emit(380, { Type: 'ChatSessionIterating', RoundNumber: 1, ToolCount: 1, IsFinalRound: false });

        // === Round 2: the model streams, then three SubAgents, each starting while the previous one runs ===
        emit(900, { Type: 'StreamContent', Payload: 'Let me read the interface. ', Count: 5, TokensPerSecond: 10.0 });
        emit(1500, { Type: 'StreamEnd' });
        emit(3600, {
            Type: 'StreamToolCall',
            FunctionName: 'code_explorer_subagent',
            CallId: 'call_agent_001',
            ArgumentsJson: '{"task":"read the interface"}',
            Message: 'Running Code explorer...'
        });
        emit(3900, {
            Type: 'StreamToolCall',
            FunctionName: 'reviewer_subagent',
            CallId: 'call_agent_002',
            ArgumentsJson: '{"task":"review the plan"}',
            Message: 'Running Reviewer...'
        });
        emit(6000, {
            Type: 'StreamToolCall',
            FunctionName: 'test_runner_subagent',
            CallId: 'call_agent_003',
            ArgumentsJson: '{"task":"run the tests"}',
            Message: 'Running Test runner...'
        });

        // No StreamToolEnd: all three agents keep running, so their names stay in the header.
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
        // ToolCallsDisplay: 'expanded' enables the grouped message path with a visible header
        return JSON.stringify({ AutoLoadOnStartup: true, CollapseToolCalls: true, ToolCallsDisplay: 'expanded' });
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
            GetSettingsAsync: async () => JSON.stringify({ AutoLoadOnStartup: true, CollapseToolCalls: true, ToolCallsDisplay: 'expanded' }),
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
