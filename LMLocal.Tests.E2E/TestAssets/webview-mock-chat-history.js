const __mockBridge = {
    __webview: {
        addEventListener: () => {},
        removeEventListener: () => {}
    },
    ExecutePromptAsync: async (requestJson) => {},
    StopExecutionAsync: async () => {},
    ResetHistoryWithActionAsync: async () => true,
    SummarizeAndCompactAsync: async () => true,
    CopyToClipboardAsync: async (text) => true,
    GetSnapshotAsync: async () => true,
    DiscardChangesAsync: async () => true,
    AcceptChangesAsync: async () => true,
    ReviewFileAsync: async () => true,
    ReviewAllFilesAsync: async () => true,
    OpenAllFilesAsync: async () => true,
    DiscardFileAsync: async () => true,
    AcceptFileAsync: async () => true,
    GetInstructionsAsync: async () => {
        console.log('[mock] GetInstructionsAsync called');
        return JSON.stringify({ tabs: [] });
    },
    UpdateInstructionsAsync: async (json) => {
        console.log('[mock] UpdateInstructionsAsync called');
        return true;
    },
    GetProvidersAsync: async () => {
        console.log('[mock] GetProvidersAsync called');
        return JSON.stringify({
            success: true,
            data: {
                providers: [],
                providerTypes: [
                    { key: 'openai', displayName: 'OpenAI' },
                    { key: 'ollama', displayName: 'Ollama' }
                ]
            }
        });
    },
    UpdateProvidersAsync: async (json) => {
        console.log('[mock] UpdateProvidersAsync called with:', json);
        return true;
    },
    GetToolsAsync: async () => {
        console.log('[mock] GetToolsAsync called');
        return JSON.stringify({
            success: true,
            data: {
                tools: [
                    { id: 'tool-1', name: 'read_file', description: 'Read file contents', enabled: true },
                    { id: 'tool-2', name: 'write_file', description: 'Write file contents', enabled: false },
                    { id: 'tool-3', name: 'search_files', description: 'Search for files', enabled: true }
                ]
            }
        });
    },
    UpdateToolsAsync: async (json) => {
        console.log('[mock] UpdateToolsAsync called with:', json);
        return true;
    }
};

// Pre-built session data for E2E tests
const __MOCK_SESSIONS = [
    {
        sessionId: "session-aaa-111",
        prompt: "How do I refactor this class to use dependency injection?",
        timestamp: "2025-01-15T10:30:00.000Z",
        messageCount: 12
    },
    {
        sessionId: "session-bbb-222",
        prompt: "What does the error CS1061 mean and how to fix it?",
        timestamp: "2025-01-14T08:15:00.000Z",
        messageCount: 5
    },
    {
        sessionId: "session-ccc-333",
        prompt: "Generate unit tests for the OrderService class",
        timestamp: "2025-01-13T16:45:00.000Z",
        messageCount: 24
    }
];

const __MOCK_SESSION_MESSAGES = {
    "session-aaa-111": [
        { role: "user", content: "How do I refactor this class to use dependency injection?" },
        { role: "assistant", content: "Here is how you refactor..." }
    ],
    "session-bbb-222": [
        { role: "user", content: "What does the error CS1061 mean and how to fix it?" },
        { role: "assistant", content: "CS1061 means..." }
    ],
    "session-ccc-333": [
        { role: "user", content: "Generate unit tests for the OrderService class" },
        { role: "assistant", content: "Here are the tests..." }
    ]
};

function __startMock() {
    if (typeof window.lmInit === 'function') {
        console.log('[mock] calling window.lmInit');
        window.__instructionsOverride = {
            GetInstructionsAsync: async () => '{}',
            UpdateInstructionsAsync: async (json) => true,
            UpdateInstructionsSelectedTabAsync: async (id) => true,
        };
        window.__providersOverride = {
            GetProvidersAsync: async () => JSON.stringify({
                success: true,
                data: {
                    providers: [],
                    providerTypes: [
                        { key: 'openai', displayName: 'OpenAI' },
                        { key: 'ollama', displayName: 'Ollama' }
                    ]
                }
            }),
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
            GetSettingsAsync: async () => JSON.stringify({ AutoLoadOnStartup: true }),
            UpdateSettingsAsync: async (json) => true,
            TestConnectionAsync: async (json) => JSON.stringify({ success: true }),
            SetAiToolsAsync: async (json) => {
                console.log('[mock] SetAiToolsAsync called:', json);
                return true;
            },
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
                supportsIsLoaded: true,
                error: null
            }),
            SetActiveModelAsync: async (modelId, contextLength) => true,
        };
        window.__chatSessionOverride = {
            GetLastChatSessionAsync: async () => JSON.stringify({ hasSession: false, messages: [] }),
            GetChatSessionsAsync: async () => JSON.stringify({ sessions: __MOCK_SESSIONS }),
            GetChatSessionByIdAsync: async (sessionId) => {
                const msgs = __MOCK_SESSION_MESSAGES[sessionId] || [];
                return JSON.stringify({ hasSession: msgs.length > 0, messages: msgs });
            },
        };
        window.__bridgeOverride = __mockBridge;
        window.lmInit(__mockBridge);
    } else {
        console.log('[mock] lmInit not ready, retrying...');
        setTimeout(__startMock, 10);
    }
}

if (document.readyState === 'complete') {
    __startMock();
} else {
    window.addEventListener('load', __startMock);
}
