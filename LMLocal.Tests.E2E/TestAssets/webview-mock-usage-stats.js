const __mockBridge = {
    __webview: {
        addEventListener: () => { },
        removeEventListener: () => { }
    },
    ExecutePromptAsync: async (requestJson) => { },
    StopExecutionAsync: async () => { },
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
// "models" mirrors ChatSessionSummary.Models: unique model ids in order of first use.
// The last session keeps no models on purpose — it stands for a log written before model ids were persisted.
const __MOCK_SESSIONS = [
    {
        sessionId: "session-aaa-111",
        prompt: "How do I refactor this class to use dependency injection?",
        timestamp: "2025-01-15T10:30:00.000Z",
        messageCount: 12,
        models: ["qwen3-coder"]
    },
    {
        sessionId: "session-bbb-222",
        prompt: "What does the error CS1061 mean and how to fix it?",
        timestamp: "2025-01-14T08:15:00.000Z",
        messageCount: 5,
        models: ["qwen3-coder", "deepseek-chat"]
    },
    {
        sessionId: "session-ccc-333",
        prompt: "Generate unit tests for the OrderService class",
        timestamp: "2025-01-13T16:45:00.000Z",
        messageCount: 24
    }
];

const __MOCK_SESSION_MESSAGES = {
    // Two assistant answers, the second one preceded by a tool round, so the chat/copy tests can
    // check that every answer gets its own copy button and that the "steps taken" note is not copied.
    // modelId mirrors the model_id persisted per message and drives the note of a restored CoT.
    "session-aaa-111": [
        { role: "user", content: "How do I refactor this class to use dependency injection?" },
        { role: "assistant", content: "**First answer** with `inline` code.", modelId: "qwen3-coder" },
        { role: "user", content: "Now add unit tests for it." },
        { role: "assistant", content: null, modelId: "qwen3-coder", toolCalls: [{ id: "call_1", type: "function", function: { name: "read_file", arguments: "{}" } }] },
        { role: "tool", content: "file contents", toolCallId: "call_1" },
        { role: "assistant", content: "**Second answer** with a list:\n\n- one\n- two", modelId: "qwen3-coder" }
    ],
    "session-bbb-222": [
        // The model was switched in the middle of the CoT: the tool round ran on qwen3-coder, the answer on
        // deepseek-chat, so a restored answer must list both.
        { role: "user", content: "What does the error CS1061 mean and how to fix it?" },
        { role: "assistant", content: null, modelId: "qwen3-coder", toolCalls: [{ id: "call_2", type: "function", function: { name: "search_files", arguments: "{}" } }] },
        { role: "tool", content: "search results", toolCallId: "call_2" },
        { role: "assistant", content: "CS1061 means...", modelId: "deepseek-chat" }
    ],
    "session-ccc-333": [
        { role: "user", content: "Generate unit tests for the OrderService class" },
        { role: "assistant", content: "Here are the tests..." }
    ]
};


// Usage & Stats responses, in the shape UsageController returns (UsageStatsResponse JSON).
function __isoDay(offsetDays) {
    const d = new Date();
    d.setDate(d.getDate() + offsetDays);
    const m = String(d.getMonth() + 1).padStart(2, "0");
    const day = String(d.getDate()).padStart(2, "0");
    return d.getFullYear() + "-" + m + "-" + day;
}

const __USAGE_SOURCES_FULL = [
    { source: "main", runs: 3, failed: 0, prompt: 122000, completion: 9000, cached: 100000, durationMs: 1120000, estimated: false },
    { source: "subagent", runs: 12, failed: 2, prompt: 71000, completion: 4000, cached: 0, durationMs: 600000, estimated: false },
    { source: "compaction", runs: 1, failed: 0, prompt: 6000, completion: 1000, cached: 0, durationMs: 0, estimated: false }
];

const __USAGE_MODELS_TWO_PROVIDERS = [
    { model: "qwen3-coder-30b", provider: "lmstudio", baseUrl: "http://localhost:1234", providerLabel: "LM Studio", showProviderLabel: true, prompt: 150000, completion: 10000, cached: 0, tokensPerSecond: 42.4, estimated: false },
    { model: "deepseek-chat", provider: "deepseek", baseUrl: "https://api.deepseek.com", providerLabel: "DeepSeek", showProviderLabel: true, prompt: 49000, completion: 4000, cached: 39000, cachedPercent: 80, tokensPerSecond: 0, estimated: false }
];

function __usageCurrent() {
    return {
        hasData: true,
        period: "current",
        loggingEnabled: true,
        totals: { prompt: 199000, completion: 14000, cached: 100000, reasoning: 0, requests: 3, failed: 2, durationMs: 1120000, peakContextPercent: 0.65, estimated: false },
        sources: __USAGE_SOURCES_FULL,
        models: __USAGE_MODELS_TWO_PROVIDERS,
        days: []
    };
}

function __usagePeriod(period) {
    return {
        hasData: true,
        period: period,
        loggingEnabled: true,
        totals: { prompt: 240000, completion: 16000, cached: 100000, reasoning: 0, requests: 4, failed: 0, durationMs: 1300000, peakContextPercent: 0.4, estimated: false },
        // Main only: a period without subagents or compaction must show no such rows.
        sources: [
            { source: "main", runs: 4, failed: 0, prompt: 240000, completion: 16000, cached: 100000, durationMs: 1300000, estimated: false }
        ],
        models: [
            { model: "qwen3-coder-30b", provider: "lmstudio", baseUrl: "http://localhost:1234", providerLabel: "LM Studio", showProviderLabel: false, prompt: 240000, completion: 16000, cached: 100000, tokensPerSecond: 42, estimated: false }
        ],
        days: [
            {
                date: __isoDay(0), prompt: 190000, completion: 12000,
                sessions: [
                    {
                        sessionId: "usage-session-1", title: "Refactor the stream processor", first: new Date().toISOString(),
                        model: "qwen3-coder-30b", provider: "lmstudio", baseUrl: "http://localhost:1234",
                        prompt: 184000, completion: 11000, cached: 100000, requests: 3, failed: 1,
                        sources: [{ source: "main", runs: 3, failed: 1, prompt: 184000, completion: 11000, cached: 100000, durationMs: 900000, estimated: false }],
                        models: [{ model: "qwen3-coder-30b", provider: "lmstudio", baseUrl: "http://localhost:1234", providerLabel: "LM Studio", showProviderLabel: false, prompt: 184000, completion: 11000, cached: 100000, tokensPerSecond: 42, estimated: false }]
                    },
                    {
                        // Written before the title was recorded: the card must fall back to "Untitled".
                        sessionId: "usage-session-2", title: null, first: new Date().toISOString(),
                        model: "gemma-3-12b", provider: "lmstudio", baseUrl: "http://localhost:1234",
                        prompt: 6000, completion: 1000, cached: 0, requests: 1, failed: 0,
                        sources: [{ source: "main", runs: 1, failed: 0, prompt: 6000, completion: 1000, cached: 0, durationMs: 400000, estimated: false }],
                        models: [{ model: "gemma-3-12b", provider: "lmstudio", baseUrl: "http://localhost:1234", providerLabel: "LM Studio", showProviderLabel: false, prompt: 6000, completion: 1000, cached: 0, tokensPerSecond: 71, estimated: false }]
                    }
                ]
            },
            {
                date: __isoDay(-1), prompt: 50000, completion: 4000,
                sessions: [
                    {
                        sessionId: "usage-session-3", title: "Fix the button color", first: new Date(Date.now() - 86400000).toISOString(),
                        model: "qwen3-coder-30b", provider: "lmstudio", baseUrl: "http://localhost:1234",
                        prompt: 50000, completion: 4000, cached: 0, requests: 1, failed: 0,
                        sources: [{ source: "main", runs: 1, failed: 0, prompt: 50000, completion: 4000, cached: 0, durationMs: 0, estimated: false }],
                        models: [{ model: "qwen3-coder-30b", provider: "lmstudio", baseUrl: "http://localhost:1234", providerLabel: "LM Studio", showProviderLabel: false, prompt: 50000, completion: 4000, cached: 0, tokensPerSecond: 42, estimated: false }]
                    }
                ]
            }
        ]
    };
}

function __usageResponse(period) {
    const mode = window.__usageMode || "normal";
    if (mode === "empty") return { hasData: false, period: period, loggingEnabled: true, sources: [], models: [], days: [] };
    if (mode === "loggingOff") return { hasData: false, period: period, loggingEnabled: false, sources: [], models: [], days: [] };
    if (period === "current") return __usageCurrent();

    const data = __usagePeriod(period);
    // Today and Yesterday are a single local day: one group, which the dialog renders without a day header.
    if (period === "today" || period === "yesterday") data.days = data.days.slice(0, 1);
    // 3d adds the day before yesterday, so the dialog has three day groups to fold.
    if (period === "3d") data.days = data.days.concat([__usageDayBeforeYesterday()]);
    return data;
}

/**
 * Third day group of the 3d period.
 */
function __usageDayBeforeYesterday() {
    return {
        date: __isoDay(-2), prompt: 80000, completion: 6000,
        sessions: [
            {
                sessionId: "usage-session-4", title: "Wire the usage store", first: new Date(Date.now() - 2 * 86400000).toISOString(),
                model: "qwen3-coder-30b", provider: "lmstudio", baseUrl: "http://localhost:1234",
                prompt: 80000, completion: 6000, cached: 0, requests: 2, failed: 0,
                sources: [{ source: "main", runs: 2, failed: 0, prompt: 80000, completion: 6000, cached: 0, durationMs: 500000, estimated: false }],
                models: [{ model: "qwen3-coder-30b", provider: "lmstudio", baseUrl: "http://localhost:1234", providerLabel: "LM Studio", showProviderLabel: false, prompt: 80000, completion: 6000, cached: 0, tokensPerSecond: 42, estimated: false }]
            }
        ]
    };
}

function __startMock() {
    if (typeof window.lmInit === 'function') {
        console.log('[mock] calling window.lmInit');
        window.__instructionsOverride = {
            GetInstructionsAsync: async () => '{}',
            UpdateInstructionsAsync: async (json) => JSON.stringify({ success: true }),
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
        window.__subAgentsOverride = {
            GetSubAgentsAsync: async () => JSON.stringify({
                success: true,
                data: {
                    agents: [
                        { id: 'researcher', displayName: 'Researcher', description: 'Research agent', providerType: 'deepseek', customBaseUrl: 'https://api.deepseek.com', model: 'deepseek-chat', temperature: 0.3, timeoutSeconds: 90, maxRounds: 5, maxTokens: 2048, enabled: true, allowedTools: ['get_solution_overview', 'find_files'] },
                        { id: 'coder', displayName: 'Coder', description: 'Coder agent', providerType: 'lmstudio', customBaseUrl: 'http://localhost:1234', model: 'qwen2.5-coder-7b-instruct', enabled: false, allowedTools: [] }
                    ]
                }
            }),
            UpdateSubAgentsAsync: async (json) => {
                console.log('[mock] UpdateSubAgentsAsync called:', json);
                return JSON.stringify({ success: true });
            },
        };
        window.__settingsOverride = {
            GetSettingsAsync: async () => JSON.stringify({ AutoLoadOnStartup: true }),
            UpdateSettingsAsync: async (json) => true,
            TestConnectionAsync: async (json) => JSON.stringify({ success: true }),
            SetAiToolsAsync: async (json) => {
                console.log('[mock] SetAiToolsAsync called:', json);
                return true;
            },
            SetSubAgentsAsync: async (json) => {
                console.log('[mock] SetSubAgentsAsync called:', json);
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
        window.__recentModelsOverride = {
            GetRecentModelsAsync: async () => JSON.stringify({ entries: [] }),
            RecordModelUsageAsync: async () => true,
        };
        window.__chatSessionOverride = {
            GetLastChatSessionAsync: async () => JSON.stringify({ hasSession: false, messages: [] }),
            GetChatSessionsAsync: async () => JSON.stringify({ sessions: __MOCK_SESSIONS }),
            GetChatSessionByIdAsync: async (sessionId) => {
                const msgs = __MOCK_SESSION_MESSAGES[sessionId] || [];
                return JSON.stringify({ hasSession: msgs.length > 0, messages: msgs });
            },
        };
        window.__usageCalls = [];
        window.__usageOverride = {
            // The real host returns a JSON string, exactly like the other controllers.
            GetUsageStatsAsync: async (period) => {
                window.__usageCalls.push(period);
                return JSON.stringify(__usageResponse(period));
            }
        };
        window.__bridgeOverride = __mockBridge;
        window.__hostOverride = {
            CopyToClipboardAsync: async (text) => true,
            FocusAsync: async () => { },
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
        console.log('[mock] lmInit not ready, retrying...');
        setTimeout(__startMock, 10);
    }
}

if (document.readyState === 'complete') {
    __startMock();
} else {
    window.addEventListener('load', __startMock);
}
