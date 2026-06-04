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

    async setActiveModelAsync(modelId, contextLength) {
        return await this._callHost("SetActiveModelAsync", modelId, contextLength);
    }

    async listModelsAsync() {
        return JSON.parse(await this._callHost("ListModelsAsync"));
    }

    async executePromptAsync(request) {
        const requestJson = JSON.stringify(request);
        return await this._callHost("ExecutePromptAsync", requestJson);
    }

    async stopExecutionAsync() {
        return await this._callHost("StopExecutionAsync");
    }

    async resetHistoryAsync() {
        return await this._callHost("ResetHistoryAsync");
    }

    async copyToClipboardAsync(text) {
        return await this._callHost("CopyToClipboardAsync", text);
    }

    async getSettingsAsync() {
        const res = await this._callHost("GetSettingsAsync");
        return JSON.parse(res);
    }

    async updateSettingsAsync(settings) {
        const payload = JSON.stringify(settings);
        return await this._callHost("UpdateSettingsAsync", payload);
    }

    async getInstructionsAsync() {
        const res = await this._callHost("GetInstructionsAsync");
        return JSON.parse(res);
    }

    async updateInstructionsAsync(instructions) {
        const payload = JSON.stringify(instructions);
        return await this._callHost("UpdateInstructionsAsync", payload);
    }

    async testConnection(details) {
        const payload = JSON.stringify(details);
        var result = await this._callHost("TestConnectionAsync", payload);
        return JSON.parse(result);
    }

    async getMcpConfigAsync() {
        const res = await this._callHost("GetMcpConfigAsync");
        return JSON.parse(res);
    }

    async updateMcpConfigAsync(mcpConfig) {
        const payload = JSON.stringify(mcpConfig);
        return await this._callHost("UpdateMcpConfigAsync", payload);
    }

    async testMcpConnectionAsync(payload) {
        const payloadJson = JSON.stringify(payload);
        const result = await this._callHost("TestMcpConnectionAsync", payloadJson);
        return JSON.parse(result);
    }

    async getProvidersAsync() {
        const res = await this._callHost("GetProvidersAsync");
        return JSON.parse(res);
    }

    async updateProvidersAsync(providersConfig) {
        const payload = JSON.stringify(providersConfig);
        return await this._callHost("UpdateProvidersAsync", payload);
    }
}

const bridgeClient = new BridgeClient();
export default bridgeClient;
