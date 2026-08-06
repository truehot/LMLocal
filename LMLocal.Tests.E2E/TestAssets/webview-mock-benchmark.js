// Mock that simulates rendering an avalanche of large markdown chunks
const _listeners = [];

// A large, complex markdown text to stress test the rendering
const baseText = `
### Large Header
Here is some text with **bold** and *italic*.
- List item 1
- List item 2

\`\`\`csharp
public class PerfTest {
    public void Run() {
        Console.WriteLine("Stress testing highlight.js and marked.js");
    }
}
\`\`\`
`.repeat(100); // Repeat to make it roughly ~1 page

const totalChunks = 500;
const chunkSize = Math.ceil(baseText.length / totalChunks);
const chunks = [];
for(let i=0; i<baseText.length; i+=chunkSize) {
    chunks.push(baseText.substring(i, i + chunkSize));
}

// Global state to track streaming progress
window.__streamingState = {
    isStreaming: false,
    totalChunksToSend: totalChunks,
    chunksSent: 0,
    isComplete: false
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
        let i = 0;
        window.__streamingState.isStreaming = true;
        window.__streamingState.chunksSent = 0;
        window.__streamingState.isComplete = false;

        console.log(`Starting stream with ${chunks.length} chunks`);
        performance.mark('stream-start');

        function sendNext() {
            if (i < chunks.length) {
                _listeners.forEach(fn => fn({
                    data: { Type: 'StreamContent', Payload: chunks[i], Count: i, TokensPerSecond: 100 }
                }));
                window.__streamingState.chunksSent = i + 1;
                i++;
                setTimeout(sendNext, 1);
            } else {
                _listeners.forEach(fn => fn({ data: { Type: 'StreamEnd' } }));
                window.__streamingState.isStreaming = false;
                window.__streamingState.isComplete = true;
                console.log(`Stream complete: sent ${window.__streamingState.chunksSent} chunks`);
                performance.mark('stream-end');
                performance.measure('stream-duration', 'stream-start', 'stream-end');
            }
        }
        sendNext();
    },
    StopExecutionAsync: async () => {},
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
        window.__modelsOverride = {
            ListModelsAsync: async () => JSON.stringify({
                models: [{ id: "test-model-1", name: "Benchmark Model", maxTokens: 16384, supportsMaxTokens: true, isLoaded: false, supportsToolUse: null }],
                hasActiveModel: true,
                activeModel: { id: "test-model-instance", name: "Benchmark Model", maxTokens: 16384, supportsMaxTokens: true, isLoaded: true, supportsToolUse: null },
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

if (document.readyState === 'complete') __startMock();
else window.addEventListener('load', __startMock);
