import bridgeClient from '@app/api/bridge.client.js';
import modelStore from '@app/store/model.store.js';
import settingsStore from '@app/store/settings.store.js';
import instructionsStore from '@app/store/instructions.store.js';
import providersStore from '@app/store/providers.store.js';
import appStore from '@app/store/app.store.js';


import { AppStatus } from '@app/store/app.status.js';

class AppDataService {
    async loadModels() {
        return await bridgeClient.listModelsAsync();
    }

    async getLastChatSessionAsync() {
        return await bridgeClient.getLastChatSessionAsync();
    }

    async setActiveModel(modelId, modelName, supportsMaxTokens, tokenMax) {
        const result = await bridgeClient.setActiveModelAsync(modelId, tokenMax || 0);

        if (result) {
            modelStore.setState({
                modelId: modelId,
                modelName: modelName || modelId,
                tokenMax: tokenMax || 0,
                tokenUsed: 0,
                supportsMaxTokens: supportsMaxTokens
            });
            const appState = appStore.getState();
            if (appState.status == AppStatus.OFFLINE || appState.status == AppStatus.CONNECTING || appState.status == AppStatus.ERROR) {
                appStore.setState({
                    status: AppStatus.IDLE,
                    tokenUsed: 0,
                    tokenSpeed: 0,
                    error: null
                });
            }
        }

        return result;
    }

    async getSettingsAsync() {
        const settings = await bridgeClient.getSettingsAsync();
        settingsStore.setState(settings);
        return settings;
    }

    async updateSettingsAsync(newSettings) {

        const settingsState = settingsStore.getState();
        const result = await bridgeClient.updateSettingsAsync(newSettings);

        if (result) {
            settingsStore.setState(newSettings);

            if (settingsState?.Provider !== newSettings.Provider) {
                modelStore.setState({
                    modelId: null,
                    modelName: null,
                    tokenMax: 0,
                    tokenUsed: 0,
                    supportsMaxTokens: false
                });
            }
        }

        return result;
    }

    async getInstructionsAsync() {
        instructionsStore.setState({
            loading: true,
            error: null
        });

        try {
            const result = await bridgeClient.getInstructionsAsync();
            const tabs = result.tabs || result;
            const selectedTabId = result.selectedTabId || null;

            instructionsStore.setState({
                instructions: tabs,
                selectedTabId: selectedTabId,
                loading: false,
                error: null
            });
            return result;
        } catch (error) {
            console.error('Failed to load instructions:', error);
            instructionsStore.setState({
                loading: false,
                error: "Failed to load instructions"
            });
            throw error;
        }
    }

    async updateInstructionsAsync(json) {
        instructionsStore.setState({
            loading: true,
            error: null
        });

        try {
            const result = await bridgeClient.updateInstructionsAsync(json);
            instructionsStore.setState({
                instructions: json.tabs,
                selectedTabId: json.selectedTabId || null,
                loading: false,
                error: null
            });
            return result;
        } catch (error) {
            console.error('Failed to update instructions:', error);
            instructionsStore.setState({
                loading: false,
                error: "Failed to update instructions"
            });
            throw error;
        }
    }

    async updateInstructionsSelectedTabAsync(selectedTabId) {
        try {
            const result = await bridgeClient.updateInstructionsSelectedTabAsync(selectedTabId);
            if (result) {
                instructionsStore.setState({
                    selectedTabId: selectedTabId
                });
            }
            return result;
        } catch (error) {
            console.error('Failed to update selected instructions tab:', error);
            throw error;
        }
    }

    async testConnectionAsync(details) {
        return await bridgeClient.testConnection(details);
    }

    async getMcpConfigAsync() {
        return await bridgeClient.getMcpConfigAsync();
    }

    async updateMcpConfigAsync(config) {
        return await bridgeClient.updateMcpConfigAsync(config);
    }

    async testMcpConnectionAsync(payload) {
        return await bridgeClient.testMcpConnectionAsync(payload);
    }

    async getProvidersAsync() {
        providersStore.setState({
            loading: true,
            error: null
        });

        try {
            const result = await bridgeClient.getProvidersAsync();
            const config = result.defaultProviders || result.providers ? result : { defaultProviders: [], providers: [] };
            providersStore.setState({
                defaultProviders: config.defaultProviders || [],
                providers: config.providers || [],
                loading: false,
                loaded: true,
                error: null
            });
            return result;
        } catch (error) {
            console.error('Failed to load providers:', error);
            providersStore.setState({
                loading: false,
                loaded: true,
                error: 'Failed to load providers'
            });
            throw error;
        }
    }

    async updateProvidersAsync(config) {
        providersStore.setState({
            loading: true,
            error: null
        });

        try {
            const result = await bridgeClient.updateProvidersAsync(config);
            if (result) {
                const currentState = providersStore.getState();
                providersStore.setState({
                    defaultProviders: currentState.defaultProviders,
                    providers: config.providers || [],
                    loading: false,
                    loaded: true,
                    error: null
                });
            }
            return result;
        } catch (error) {
            console.error('Failed to update providers:', error);
            providersStore.setState({
                loading: false,
                error: 'Failed to update providers'
            });
            throw error;
        }
    }

    async getToolsAsync() {
        try {
            const result = await bridgeClient.getToolsAsync();
            return result;
        } catch (error) {
            console.error('Failed to load tools:', error);
            throw error;
        }
    }

    async updateToolsAsync(config) {
        try {
            const result = await bridgeClient.updateToolsAsync(config);
            return result;
        } catch (error) {
            console.error('Failed to update tools:', error);
            throw error;
        }
    }

    async getSnapshotAsync() {
        try {
            const result = await bridgeClient.getSnapshotAsync();
            return result;
        } catch (error) {
            console.error('Failed to load snapshot:', error);
        }
    }

    async discardAllAsync() {
        return await bridgeClient.discardChangesAsync();
    }

    async acceptAllAsync() {
        return await bridgeClient.acceptChangesAsync();
    }

    async reviewFileAsync(filePath) {
        return await bridgeClient.reviewFileAsync(filePath);
    }

    async reviewAllFilesAsync(filePaths) {
        return await bridgeClient.reviewAllFilesAsync(filePaths);
    }

    async openAllFilesAsync(filePaths) {
        return await bridgeClient.openAllFilesAsync(filePaths);
    }

    async discardFileAsync(filePath) {
        return await bridgeClient.discardFileAsync(filePath);
    }


    async getAutocompletionsConfigAsync() {
        return await bridgeClient.getAutocompletionsConfigAsync();
    }

    async updateAutocompletionsConfigAsync(config) {
        const json = JSON.stringify(config);
        return await bridgeClient.updateAutocompletionsConfigAsync(json);
    }

    async listAutocompletionsModelsAsync(providerType, baseUrl, apiKey) {
        return await bridgeClient.listAutocompletionsModelsAsync(providerType, baseUrl, apiKey);
    }

    async testAutocompletionsCompletionAsync(providerType, baseUrl, apiKey, modelId) {
        const result = await bridgeClient.testAutocompletionsCompletionAsync(providerType, baseUrl, apiKey, modelId);
        return JSON.parse(result);
    }

    async getProvidersForAutocompletionsAsync() {
        const storeState = providersStore.getState();
        let result;
        if (storeState.loaded) {
            result = {
                defaultProviders: storeState.defaultProviders,
                providers: storeState.providers,
            };
        } else {
            result = await this.getProvidersAsync();
        }

        const allowedTypes = ['lmstudio', 'llamacpp'];
        return (result.defaultProviders || []).filter(
            p => p && p.providerType && allowedTypes.includes(p.providerType.toLowerCase())
        );
    }


    async acceptFileAsync(filePath) {
        return await bridgeClient.acceptFileAsync(filePath);
    }
}

const appDataService = new AppDataService();
export default appDataService;