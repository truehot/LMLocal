const __mockBridge = {
    __webview: {
        addEventListener: () => {},
        removeEventListener: () => {}
    },
    ListModelsAsync: async () => JSON.stringify({
        models: [],
        hasActiveModel: false,
        activeModel: null,
        error: "LM Studio unreachable"
    }),
    SetActiveModelAsync: async (modelId, contextLength) => {
        console.log('[mock] SetActiveModelAsync called with:', modelId, contextLength);
        return false;
    },
    ExecutePromptAsync: async (requestJson) => {},
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
        // Expose bridge so BridgeClient.getHost() can find it
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
