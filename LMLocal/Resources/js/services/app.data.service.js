import bridgeClient from '@app/api/bridge.client.js';
import modelStore from '@app/store/model.store.js';
import settingsStore from '@app/store/settings.store.js';
import instructionsStore from '@app/store/instructions.store.js';
import providersStore from '@app/store/providers.store.js';
import modelsConfigStore from '@app/store/models.config.store.js';
import appStore from '@app/store/app.store.js';
import sessionStore from '@app/store/session.store.js';

import { AppStatus } from '@app/store/app.status.js';

/**
 * Normalizes the raw instructions document returned by the host into a stable shape.
 */
function normalizeInstructionsDoc(raw) {
    if (!raw || typeof raw !== 'object' || Array.isArray(raw)) {
        return { selectedTabId: null, tabs: [] };
    }

    const tabs = Array.isArray(raw.tabs) ? raw.tabs : [];
    let selectedTabId = raw.selectedTabId !== undefined && raw.selectedTabId !== null
        ? raw.selectedTabId
        : null;

    const selected = tabs.find(t => t && t.id == selectedTabId);
    if (!selected || !selected.enabled) {
        const firstEnabled = tabs.find(t => t && t.enabled);
        if (firstEnabled) selectedTabId = firstEnabled.id;
    }

    return { selectedTabId, tabs };
}

/**
 * Identity of the endpoint that serves the active model.
 */
function endpointIdentity(settings) {
    if (!settings) return null;

    const providerId = settings.ProviderId === undefined || settings.ProviderId === null
        ? null
        : String(settings.ProviderId);

    return JSON.stringify([
        settings.Provider ?? null,
        providerId,
        settings.LmStudioBaseUrl ?? ''
    ]);
}

class AppDataService {
    async loadModels() {
        return await bridgeClient.listModelsAsync();
    }

    async getLastChatSessionAsync() {
        return await bridgeClient.getLastChatSessionAsync();
    }

    async getCurrentChatHistoryAsync() {
        return await bridgeClient.getCurrentChatHistoryAsync();
    }

    async getChatSessionsAsync() {
        return await bridgeClient.getChatSessionsAsync();
    }

    async getChatSessionByIdAsync(sessionId) {
        return await bridgeClient.getChatSessionByIdAsync(sessionId);
    }

    async setActiveModel(modelId, modelName, supportsMaxTokens, tokenMax) {
        const result = await bridgeClient.setActiveModelAsync(modelId, tokenMax || 0);

        if (result) {
            const previousModelId = modelStore.getState().modelId;
            if (previousModelId !== modelId) {
                sessionStore.resetTraffic();
            }

            modelStore.setState({
                modelId: modelId,
                modelName: modelName || modelId,
                tokenMax: tokenMax || 0,
                supportsMaxTokens: supportsMaxTokens
            });
            const appState = appStore.getState();
            if (appState.status == AppStatus.OFFLINE || appState.status == AppStatus.CONNECTING || appState.status == AppStatus.ERROR) {
                appStore.setState({
                    status: AppStatus.IDLE,
                    roundTokens: 0,
                    lastAnswerTokens: 0,
                    tokenSpeed: 0,
                    error: null
                });
            }

            this.recordModelUsage(modelId, modelName || modelId);
        }

        return result;
    }

    async getRecentModelsAsync() {
        try {
            return await bridgeClient.getRecentModelsAsync();
        } catch (e) {
            console.warn('Recent models unavailable:', e);
            return { entries: [] };
        }
    }

    async recordModelUsage(modelId, modelName) {
        try {
            const settings = settingsStore.getState();
            await bridgeClient.recordModelUsageAsync({
                providerType: settings.Provider,
                providerId: settings.ProviderId ?? null,
                modelId,
                modelName: modelName || modelId,
            });
        } catch (e) {
            console.warn('Failed to record model usage:', e);
        }
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
            const previousEndpoint = endpointIdentity(settingsState);
            const nextEndpoint = endpointIdentity(newSettings);
            const endpointChanged = previousEndpoint !== nextEndpoint;

            settingsStore.setState(newSettings);

            if (endpointChanged) {
                sessionStore.resetTraffic();

                modelStore.setState({
                    modelId: null,
                    modelName: null,
                    tokenMax: 0,
                    supportsMaxTokens: false
                });
            }
        }

        return result;
    }

    async setAiToolsModeAsync(mode) {
        const enableAiTools = mode !== 'none';
        const enableAiWriteTools = mode === 'readwrite';

        const result = await bridgeClient.setAiToolsAsync(mode);

        if (result) {
            settingsStore.setState({
                EnableAiTools: enableAiTools,
                EnableAiWriteTools: enableAiWriteTools
            });
        }

        return result;
    }

    async setSubAgentsEnabledAsync(enabled) {
        const result = await bridgeClient.setSubAgentsAsync(!!enabled);

        if (result) {
            settingsStore.setState({ EnableSubAgents: !!enabled });
        }

        return result;
    }

    async getInstructionsAsync() {
        instructionsStore.setState({
            loading: true,
            error: null
        });

        try {
            const raw = await bridgeClient.getInstructionsAsync();
            const doc = normalizeInstructionsDoc(raw);

            instructionsStore.setState({
                instructions: doc.tabs,
                selectedTabId: doc.selectedTabId,
                loading: false,
                error: null
            });
            return doc;
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

            if (result && result.success) {

                try {
                    await this.getInstructionsAsync();
                } catch (reloadError) {
                    console.warn('Failed to reload instructions after save:', reloadError);
                }
                instructionsStore.setState({ loading: false, error: null });
                return { success: true };
            }

            const error = (result && result.error) || "Failed to update instructions";
            instructionsStore.setState({
                loading: false,
                error: error
            });
            return { success: false, error: error };
        } catch (error) {
            console.error('Failed to update instructions:', error);
            instructionsStore.setState({
                loading: false,
                error: "Failed to update instructions"
            });
            return { success: false, error: error?.message || "Failed to update instructions" };
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

    async testCertificateAsync(payload) {
        return await bridgeClient.testCertificate(payload);
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

            const defaultProviders = config.defaultProviders || [];
            const providers = config.providers || [];
            providersStore.setState({
                defaultProviders,
                providers,
                loading: false,
                loaded: defaultProviders.length > 0 || providers.length > 0,
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

    async getModelsConfigAsync() {
        modelsConfigStore.setState({
            loading: true,
            error: null
        });

        try {
            const result = await bridgeClient.getModelsConfigAsync();
            const config = result && result.models ? result : { models: [] };

            const models = config.models || [];
            modelsConfigStore.setState({
                models,
                loading: false,
                loaded: true,
                error: null
            });
            return config;
        } catch (error) {
            console.error('Failed to load models config:', error);
            modelsConfigStore.setState({
                loading: false,
                loaded: true,
                error: 'Failed to load models'
            });
            throw error;
        }
    }

    async updateModelsConfigAsync(config) {
        modelsConfigStore.setState({
            loading: true,
            error: null
        });

        try {
            const result = await bridgeClient.updateModelsConfigAsync(config);
            if (result) {
                modelsConfigStore.setState({
                    models: (config && config.models) || [],
                    loading: false,
                    loaded: true,
                    error: null
                });
            }
            return result;
        } catch (error) {
            console.error('Failed to update models config:', error);
            modelsConfigStore.setState({
                loading: false,
                error: 'Failed to update models'
            });
            throw error;
        }
    }

    /**
     * Applies the instruction bound to the currently active model.
     */
    async applyActiveModelInstruction() {
        try {
            const modelId = modelStore.getState().modelId;
            if (!modelId) return;

            if (!modelsConfigStore.getState().loaded) {
                try {
                    await this.getModelsConfigAsync();
                } catch (e) {
                    return;
                }
            }

            const models = modelsConfigStore.getState().models || [];
            const settings = settingsStore.getState();
            const providerType = settings.Provider;
            const providerId = settings.ProviderId ?? null;

            const model = models.find(m => m
                && m.enabled !== false
                && m.modelId === modelId
                && m.providerType === providerType
                && ((m.providerId ?? null) === providerId)
            );

            const boundId = model && model.instructionTabId;
            if (boundId === undefined || boundId === null) return;

            const tabs = instructionsStore.getState().instructions || [];
            const tab = tabs.find(t => t && t.enabled && Number(t.id) === Number(boundId));
            if (!tab) return;

            const current = instructionsStore.getState().selectedTabId;
            if (current === null || current === undefined || Number(current) !== Number(tab.id)) {
                instructionsStore.setState({ selectedTabId: tab.id });
            }
        } catch (e) {
            console.warn('Failed to apply active model instruction:', e);
        }
    }

    async getProvidersForModelsAsync() {
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

        return [
            ...(result.defaultProviders || []),
            ...(result.providers || [])
        ];
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

    async getSubAgentsConfigAsync() {
        try {
            const result = await bridgeClient.getSubAgentsAsync();
            return result;
        } catch (error) {
            console.error('Failed to load subagents:', error);
            throw error;
        }
    }

    async updateSubAgentsConfigAsync(config) {
        try {
            const result = await bridgeClient.updateSubAgentsAsync(config);
            return result;
        } catch (error) {
            console.error('Failed to update subagents:', error);
            throw error;
        }
    }

    async replaceSubAgentsConfigAsync(config) {
        try {
            const result = await bridgeClient.replaceSubAgentsConfigAsync(config);
            return result;
        } catch (error) {
            console.error('Failed to replace subagents config:', error);
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

    async openFileAsync(filePath) {
        return await bridgeClient.openAllFilesAsync([filePath]);
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

        return [
            ...(result.defaultProviders || []),
            ...(result.providers || [])
        ].filter(p => p && (p.customBaseUrl || '').trim() !== '');
    }

    async acceptFileAsync(filePath) {
        return await bridgeClient.acceptFileAsync(filePath);
    }
}

const appDataService = new AppDataService();
export default appDataService;
