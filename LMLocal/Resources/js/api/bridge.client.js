/**
 * Thin wrapper around host bridge API.
 * Supports __bridgeOverride for tests.
 */
class BridgeClient {

    _getHost() {
        return window.__bridgeOverride ?? window.chrome?.webview?.hostObjects?.bridge;
    }

    getWebview() {
        return window.__bridgeOverride?.__webview ?? window.chrome?.webview;
    }

    async _callHost(method, ...args) {
        const host = this._getHost();
        if (!host || typeof host[method] !== 'function') {
            throw new Error(`Bridge host method ${method} is unavailable`);
        }
        return host[method](...args);
    }

    _getModelsHost() {
        return window.__modelsOverride ?? window.chrome?.webview?.hostObjects?.models;
    }

    async _callModels(method, ...args) {
        const host = this._getModelsHost();
        if (!host || typeof host[method] !== 'function') {
            throw new Error(`Models method ${method} is unavailable`);
        }
        return host[method](...args);
    }

    async setActiveModelAsync(modelId, contextLength) {
        return await this._callModels("SetActiveModelAsync", modelId, contextLength);
    }

    async listModelsAsync() {
        return JSON.parse(await this._callModels("ListModelsAsync"));
    }

    async executePromptAsync(request) {
        const requestJson = JSON.stringify(request);
        return await this._callHost("ExecutePromptAsync", requestJson);
    }

    async stopExecutionAsync() {
        return await this._callHost("StopExecutionAsync");
    }

    async resetHistoryWithActionAsync(action) {
        return await this._callHost("ResetHistoryWithActionAsync", action);
    }

    async summarizeAndCompactAsync(modelId) {
        return await this._callHost("SummarizeAndCompactAsync", modelId);
    }


    async getLastChatSessionAsync() {
        const res = await this._callHost("GetLastChatSessionAsync");
        return JSON.parse(res);
    }

    async copyToClipboardAsync(text) {
        return await this._callHost("CopyToClipboardAsync", text);
    }

    _getSettingsHost() {
        return window.__settingsOverride ?? window.chrome?.webview?.hostObjects?.settings;
    }

    async _callSettings(method, ...args) {
        const host = this._getSettingsHost();
        if (!host || typeof host[method] !== 'function') {
            throw new Error(`Settings method ${method} is unavailable`);
        }
        return host[method](...args);
    }

    async getSettingsAsync() {
        const res = await this._callSettings("GetSettingsAsync");
        return JSON.parse(res);
    }

    async updateSettingsAsync(settings) {
        const payload = JSON.stringify(settings);
        return await this._callSettings("UpdateSettingsAsync", payload);
    }

    async testConnection(details) {
        const payload = JSON.stringify(details);
        var result = await this._callSettings("TestConnectionAsync", payload);
        return JSON.parse(result);
    }

    _getMcpHost() {
        return window.__mcpOverride ?? window.chrome?.webview?.hostObjects?.mcp;
    }

    async _callMcp(method, ...args) {
        const host = this._getMcpHost();
        if (!host || typeof host[method] !== 'function') {
            throw new Error(`Mcp method ${method} is unavailable`);
        }
        return host[method](...args);
    }

    async getMcpConfigAsync() {
        const res = await this._callMcp("GetMcpConfigAsync");
        return JSON.parse(res);
    }

    async updateMcpConfigAsync(mcpConfig) {
        const payload = JSON.stringify(mcpConfig);
        return await this._callMcp("UpdateMcpConfigAsync", payload);
    }

    async testMcpConnectionAsync(payload) {
        const payloadJson = JSON.stringify(payload);
        const result = await this._callMcp("TestMcpConnectionAsync", payloadJson);
        return JSON.parse(result);
    }

    _getProvidersHost() {
        return window.__providersOverride ?? window.chrome?.webview?.hostObjects?.providers;
    }

    async _callProviders(method, ...args) {
        const host = this._getProvidersHost();
        if (!host || typeof host[method] !== 'function') {
            throw new Error(`Providers method ${method} is unavailable`);
        }
        return host[method](...args);
    }

    async getProvidersAsync() {
        const res = await this._callProviders("GetProvidersAsync");
        return JSON.parse(res);
    }

    async updateProvidersAsync(providersConfig) {
        const payload = JSON.stringify(providersConfig);
        return await this._callProviders("UpdateProvidersAsync", payload);
    }

    _getToolsHost() {
        return window.__toolsOverride ?? window.chrome?.webview?.hostObjects?.tools;
    }

    async _callTools(method, ...args) {
        const host = this._getToolsHost();
        if (!host || typeof host[method] !== 'function') {
            throw new Error(`Tools method ${method} is unavailable`);
        }
        return host[method](...args);
    }

    async getToolsAsync() {
        const res = await this._callTools("GetToolsAsync");
        return JSON.parse(res);
    }

    async updateToolsAsync(toolsConfig) {
        const payload = JSON.stringify(toolsConfig);
        return await this._callTools("UpdateToolsAsync", payload);
    }

    async getSnapshotAsync() {
        return await this._callHost("GetSnapshotAsync");
    }

    async discardChangesAsync() {
        return await this._callHost("DiscardChangesAsync");
    }

    async acceptChangesAsync() {
        return await this._callHost("AcceptChangesAsync");
    }

    async reviewFileAsync(filePath) {
        return await this._callHost("ReviewFileAsync", filePath);
    }

    async reviewAllFilesAsync(filePaths) {
        return await this._callHost("ReviewAllFilesAsync", JSON.stringify(filePaths));
    }

    async openAllFilesAsync(filePaths) {
        return await this._callHost("OpenAllFilesAsync", JSON.stringify(filePaths));
    }

    async discardFileAsync(filePath) {
        return await this._callHost("DiscardFileAsync", filePath);
    }

    async acceptFileAsync(filePath) {
        return await this._callHost("AcceptFileAsync", filePath);
    }

    _getInstructionsHost() {
        return window.__instructionsOverride ?? window.chrome?.webview?.hostObjects?.instructions;
    }

    async _callInstructions(method, ...args) {
        const host = this._getInstructionsHost();
        if (!host || typeof host[method] !== 'function') {
            throw new Error(`Instructions method ${method} is unavailable`);
        }
        return host[method](...args);
    }

    async getInstructionsAsync() {
        const res = await this._callInstructions("GetInstructionsAsync");
        return JSON.parse(res);
    }

    async updateInstructionsAsync(instructions) {
        const payload = JSON.stringify(instructions);
        return await this._callInstructions("UpdateInstructionsAsync", payload);
    }

    async updateInstructionsSelectedTabAsync(selectedTabId) {
        return await this._callInstructions("UpdateInstructionsSelectedTabAsync", selectedTabId);
    }

}

const bridgeClient = new BridgeClient();
export default bridgeClient;