// Mock that sends the todo_write list and then fails the request with ChatSessionError.
const _listeners = [];
const _timers = [];
window._listeners = _listeners;
window._mock_timers = _timers;

const FIRST_LIST = [
    { text: 'Read the file', status: 'done' },
    { text: 'Fix the parser', status: 'in_progress' },
    { text: 'Build', status: 'pending' },
];

const __emit = (msg) => {
    _listeners.forEach(fn => { try { fn({ data: msg }); } catch (e) { /* swallow */ } });
};

window.__emitBridgeMessage = __emit;

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
        _timers.push(setTimeout(() => __emit({
            Type: 'StreamToolCall',
            FunctionName: 'todo_write',
            CallId: 'call_todo_1',
            ArgumentsJson: '{"items":[]}',
            Message: 'Updating task list...'
        }), 50));

        _timers.push(setTimeout(() => __emit({
            Type: 'StreamToolEnd',
            FunctionName: 'todo_write',
            CallId: 'call_todo_1',
            Message: 'Tasks updated: 1/3 done; in progress: Fix the parser.',
            IsError: false,
            UiData: FIRST_LIST
        }), 150));

        _timers.push(setTimeout(() => __emit({
            Type: 'ChatSessionError', Payload: 'model crashed'
        }), 2500));
    },
    StopExecutionAsync: async () => {
        window.__stopCallCount = (window.__stopCallCount || 0) + 1;
        while (_timers.length) {
            const id = _timers.shift();
            try { clearTimeout(id); } catch (e) { }
        }
        _timers.push(setTimeout(() => __emit({ Type: 'ChatSessionCancelled', Payload: 'Stopped by user' }), 50));
    },
    ResetHistoryWithActionAsync: async () => true,
    SummarizeAndCompactAsync: async () => true,
    CopyToClipboardAsync: async (text) => true,
    GetInstructionsAsync: async () => JSON.stringify({ tabs: [] }),
    UpdateInstructionsAsync: async (json) => true,
    GetSettingsAsync: async () => JSON.stringify({ AutoLoadOnStartup: true }),
    UpdateSettingsAsync: async (json) => true,
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
                        { id: 'tool-2', name: 'todo_write', description: 'Stores the step list', enabled: true }
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
