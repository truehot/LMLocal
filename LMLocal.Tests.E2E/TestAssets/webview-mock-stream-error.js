// Mock that simulates a server-side stream error via PostWebMessage
const _listeners = [];

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
        setTimeout(() => {
            _listeners.forEach(fn => fn({
                data: { Type: 'ChatSessionError', Payload: 'model crashed' }
            }));
        }, 50);
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
