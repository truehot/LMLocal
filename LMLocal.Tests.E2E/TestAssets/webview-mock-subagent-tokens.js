// Mock that reports live SubAgent tokens (`StreamToolTokens`) in the status bar: a built-in tool (no numbers of its
// own), then one agent with tokens, then a second parallel agent, then the main model's stream again.
const _listeners = [];
const _timers = [];
window._listeners = _listeners;
window._mock_timers = _timers;
window.__emitBridgeMessage = (msg) => {
    try {
        console.log('[mock-subagent-tokens] __emitBridgeMessage', msg);
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
            _timers.push(setTimeout(() => window.__emitBridgeMessage(data), delay));
        };

        // Built-in tool: it reports no tokens of its own, so the status bar shows no numbers at all.
        emit(50, {
            Type: 'StreamToolCall',
            FunctionName: 'search_file_content',
            CallId: 'call_search_001',
            ArgumentsJson: '{"text":"IChatHistoryManager"}',
            Message: 'Searching for "IChatHistoryManager"...'
        });
        emit(700, {
            Type: 'StreamToolEnd',
            FunctionName: 'search_file_content',
            CallId: 'call_search_001',
            Message: 'Found 3 matches',
            IsError: false
        });

        // First SubAgent: its step line comes first, then its live tokens.
        emit(750, {
            Type: 'StreamToolCall',
            FunctionName: 'plan_critic_subagent',
            CallId: 'call_agent_001',
            ArgumentsJson: '{"task":"critique the plan"}',
            Message: 'Running Plan critic...'
        });
        emit(900, {
            Type: 'StreamToolStep',
            FunctionName: 'search_solution_knowledge',
            CallId: 'call_agent_001',
            Message: 'search_solution_knowledge',
            Step: 1
        });
        emit(1000, {
            Type: 'StreamToolTokens',
            CallId: 'call_agent_001',
            AgentName: '',
            Tokens: 1520,
            TokensPerSecond: 95.3
        });

        // Second SubAgent of the same round: both are running, so their numbers are summed.
        emit(2100, {
            Type: 'StreamToolCall',
            FunctionName: 'reviewer_subagent',
            CallId: 'call_agent_002',
            ArgumentsJson: '{"task":"review the plan"}',
            Message: 'Running Reviewer...'
        });
        emit(2200, {
            Type: 'StreamToolTokens',
            CallId: 'call_agent_002',
            AgentName: '',
            Tokens: 480,
            TokensPerSecond: 44.7
        });

        emit(3400, {
            Type: 'StreamToolEnd',
            FunctionName: 'plan_critic_subagent',
            CallId: 'call_agent_001',
            Message: 'Done',
            IsError: false
        });
        emit(3500, {
            Type: 'StreamToolEnd',
            FunctionName: 'reviewer_subagent',
            CallId: 'call_agent_002',
            Message: 'Done',
            IsError: false
        });

        // The main model streams again: its own numbers come back.
        emit(4600, {
            Type: 'StreamContent',
            Payload: 'Here is the answer. ',
            Count: 40,
            TokensPerSecond: 20.0
        });
        emit(5600, { Type: 'StreamEnd' });
        emit(5700, {
            Type: 'ChatSessionComplete',
            TotalTokens: 60,
            ReasoningTokens: 0,
            CachedTokens: 0,
            TokensPerSecond: 20.0
        });
    },
    StopExecutionAsync: async () => {
        window.__stopCallCount = (window.__stopCallCount || 0) + 1;
        while (_timers.length) {
            const id = _timers.shift();
            try { clearTimeout(id); } catch (e) { }
        }
        setTimeout(() => window.__emitBridgeMessage({ Type: 'StreamEnd' }), 50);
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
