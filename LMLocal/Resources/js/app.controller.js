import { statusComponent } from '@app/components/status.component.js';
import { menuComponent } from '@app/components/menu.component.js';
import { inputComponent } from '@app/components/input.component.js';
import { themeComponent } from '@app/components/theme.component.js';
import { toolbarComponent } from '@app/components/toolbar.component.js';
import { chatComponent } from '@app/components/chat.component.js';
import { changesPanelComponent } from '@app/components/changes.panel.component.js';

import chatController from '@app/chat/chat.controller.js';
import appManager from '@app/services/app.manager.js';

import { bridgeMessageHandler } from '@app/api/bridge.message.handler.js';
import { appSelectors } from '@app/store/app.selectors.js';
import appStore from '@app/store/app.store.js';
import modelStore from '@app/store/model.store.js';
import instructionsStore from '@app/store/instructions.store.js';
import settingsStore from '@app/store/settings.store.js';
import providersStore from '@app/store/providers.store.js';
import changesStore from '@app/store/changes.store.js';
import bridgeMessageDispatcher from '@app/api/bridge.message.dispatcher.js';
import appDataService from '@app/services/app.data.service.js';
import { createModelSelectorDialog } from '@app/lib/model-selector.factory.js';
import { ConfirmDialog } from '@app/dialogs/confirm.dialog.js';
import { SettingsDialog } from '@app/dialogs/settings.dialog.js';
import { InstructionsDialog } from '@app/dialogs/instructions.dialog.js';
import { McpSettingsDialog } from '@app/dialogs/mcp.settings.dialog.js';
import { ProvidersDialog } from '@app/dialogs/providers.dialog.js';
import { ModelsConfigDialog } from '@app/dialogs/models.config.dialog.js';
import { ToolsDialog } from '@app/dialogs/tools.dialog.js';
import { SubAgentsDialog } from '@app/dialogs/subagents.dialog.js';
import { AutocompletionsDialog } from '@app/dialogs/autocompletions.dialog.js';
import { ChatHistoryDialog } from '@app/dialogs/chat.history.dialog.js';

import { providerResolver } from '@app/lib/provider.resolver.js';

/**
 * AppController - central initializer and event router.
 * Waits for required DOM elements, initializes UI components (Status, Input, Chat, Menu),
 * wires AppStore subscriptions and component event handlers, and starts the BridgeMessageDispatcher.
 * AppController only bootstraps and routes events, it does not handle streaming itself.
 */
class AppController {
    constructor() {
        this._initialized = false;
        this._appStoreListener = null;
        this._modelStoreListener = null;
        this._instructionsStoreListener = null;
        this._settingsStoreListener = null;
        this._changesStoreListener = null;
        this._providerResolverUnsubscribe = null;
        this._globalClickHandler = null;
    }

    setup() {
        if (this._initialized) return;
        this.reset();

        statusComponent.setup();
        toolbarComponent.setup();
        inputComponent.setup();
        chatController.setup();
        menuComponent.setup();
        chatComponent.setup();
        changesPanelComponent.setup();

        this._attachEvents();

        bridgeMessageDispatcher.start(bridgeMessageHandler);
        this._initialized = true;
    }

    reset() {
        if (!this._initialized) return;

        this._detachEvents();

        bridgeMessageDispatcher.stop();

        statusComponent.reset();
        toolbarComponent.reset();
        inputComponent.reset();
        chatController.reset();
        menuComponent.reset();
        chatComponent.reset();
        changesPanelComponent.reset();

        this._initialized = false;
    }

    _attachEvents() {
        this._appStoreListener = (state, prev) => {
            statusComponent.updateAppState(state, prev);
            inputComponent.updateAppState(state, prev);
            chatController.updateAppState(state, prev);
            toolbarComponent.updateAppState(state, prev);
            changesPanelComponent.updateAppState(state, prev);
        };
        appStore.subscribe(this._appStoreListener);

        this._modelStoreListener = (state, prev) => {
            toolbarComponent.updateModelState(state, prev);
        };
        modelStore.subscribe(this._modelStoreListener);

        this._instructionsStoreListener = (state, prev) => {
            inputComponent.updateInstructionsState(state, prev);
        };
        instructionsStore.subscribe(this._instructionsStoreListener);

        this._settingsStoreListener = (state, prev) => {
            themeComponent.updateSettingsState(state, prev);
            chatComponent.updateSettingsState(state, prev);
            statusComponent.updateSettingsState(state, prev);
            chatController.updateSettingsState(state, prev);
            inputComponent.updateSettingsState(state, prev);
        };
        settingsStore.subscribe(this._settingsStoreListener);

        this._changesStoreListener = (state, prev) => {
            changesPanelComponent.updateChangesState(state, prev);
        };
        changesStore.subscribe(this._changesStoreListener);

        this._providerResolverUnsubscribe = providerResolver.subscribe((name) => {
            statusComponent.updateProviderName(name);
        });

        themeComponent.setup();

        inputComponent.onClick.on(async (text, hasActiveContent, images) => {
            const isGenerating = appSelectors.isBusy(appStore.getState().status);
            if (isGenerating) {
                await appManager.performStop(text);
                return false;
            }

            return await appManager.performSendMessage(text, hasActiveContent, images);
        });

        inputComponent.onEnter.on(async (text, hasActiveContent, images) => {
            return await appManager.performSendMessage(text, hasActiveContent, images);
        });

        appManager.onUserMessagePending.on((text, images) => {
            chatController.renderPendingUserMessage(text, images);
        });

        appManager.onHistoryLoaded.on((messages) => {
            chatController.renderHistory(messages);
        });

        bridgeMessageHandler.onToolRoundStart.on((roundNumber, toolCount) => {
            appManager.onToolRoundStart(roundNumber, toolCount);
        });

        bridgeMessageHandler.onFinalRound.on(() => {
            chatController.markAsFinalRound();
        });

        inputComponent.onTabChanged.on(async (tabId) => {
            return await appDataService.updateInstructionsSelectedTabAsync(tabId);
        });

        inputComponent.onAiToolsChanged.on(async (mode) => {
            return await appDataService.setAiToolsModeAsync(mode);
        });

        inputComponent.onSubAgentsToggled.on(async () => {
            const enabled = !settingsStore.getState().EnableSubAgents;
            return await appDataService.setSubAgentsEnabledAsync(enabled);
        });

        chatController.onCopyCode.on(async (text) => {
            return await appManager.performCopyCode(text);
        });

        menuComponent.onClick.on(async (action) => {
            switch (action) {
                case 'open-settings':
                    const settingsDialog = new SettingsDialog();
                    settingsDialog.onLoad.on(async () => {
                        const settings = await appDataService.getSettingsAsync();

                        let providers;
                        const storeState = providersStore.getState();
                        if (storeState.loaded) {
                            providers = {
                                defaultProviders: storeState.defaultProviders,
                                providers: storeState.providers
                            };
                        } else {
                            providers = await appDataService.getProvidersAsync();
                        }

                        return {
                            ...settings,
                            defaultProviders: providers.defaultProviders,
                            providers: providers.providers
                        };
                    });
                    settingsDialog.onTestConnection.on(async (settings) => {
                        return await appDataService.testConnectionAsync(settings);
                    });
                    settingsDialog.onTestCertificate.on(async (payload) => {
                        return await appDataService.testCertificateAsync(payload);
                    });
                    settingsDialog.onSave.on(async (settings) => {
                        return await appDataService.updateSettingsAsync(settings);
                    });
                    menuComponent.hideMenu();
                    await settingsDialog.show();
                    return true;
                case 'open-instructions':
                    const instructionsDialog = new InstructionsDialog();
                    instructionsDialog.onLoad.on(async () => {
                        return await appDataService.getInstructionsAsync();
                    });
                    instructionsDialog.onSave.on(async (json) => {
                        return await appDataService.updateInstructionsAsync(json);
                    });
                    menuComponent.hideMenu();
                    await instructionsDialog.show();
                    return true;
                case 'mcp-settings':
                    const mcpDialog = new McpSettingsDialog();
                    mcpDialog.onLoad.on(async () => {
                        return await appDataService.getMcpConfigAsync();
                    });
                    mcpDialog.onTestConnection.on(async (payload) => {
                        return await appDataService.testMcpConnectionAsync(payload);
                    });
                    mcpDialog.onSave.on(async (config) => {
                        return await appDataService.updateMcpConfigAsync(config);
                    });
                    menuComponent.hideMenu();
                    await mcpDialog.show();
                    return true;
                case 'open-providers':
                    const providersDialog = new ProvidersDialog();
                    providersDialog.onLoad.on(async () => {
                        return await appDataService.getProvidersAsync();
                    });
                    providersDialog.onTestConnection.on(async (provider) => {
                        return await appDataService.testConnectionAsync(provider);
                    });
                    providersDialog.onSave.on(async (config) => {
                        return await appDataService.updateProvidersAsync(config);
                    });
                    menuComponent.hideMenu();
                    await providersDialog.show();
                    return true;
                case 'open-chat-history':
                    const chatHistoryDialog = new ChatHistoryDialog();
                    chatHistoryDialog.onLoadSessions.on(async () => {
                        return await appDataService.getChatSessionsAsync();
                    });
                    chatHistoryDialog.onLoadSession.on(async (sessionId) => {
                        return await appManager.performLoadSession(sessionId);
                    });
                    menuComponent.hideMenu();
                    await chatHistoryDialog.show();
                    return true;
                case 'open-tools':
                    const toolsDialog = new ToolsDialog();
                    toolsDialog.onLoad.on(async () => {
                        return await appDataService.getToolsAsync();
                    });
                    toolsDialog.onSave.on(async (config) => {
                        return await appDataService.updateToolsAsync(config);
                    });
                    menuComponent.hideMenu();
                    await toolsDialog.show();
                    return true;
                case 'open-subagents':
                    const subAgentsDialog = new SubAgentsDialog();
                    subAgentsDialog.onLoad.on(async () => {
                        return await appDataService.getSubAgentsConfigAsync();
                    });
                    subAgentsDialog.onSave.on(async (config) => {
                        return await appDataService.updateSubAgentsConfigAsync(config);
                    });
                    menuComponent.hideMenu();
                    await subAgentsDialog.show();
                    return true;
                case 'open-fim':
                    const autocompletionsDialog = new AutocompletionsDialog();
                    autocompletionsDialog.onLoad.on(async () => {
                        const config = await appDataService.getAutocompletionsConfigAsync();
                        const providers = await appDataService.getProvidersForAutocompletionsAsync();
                        return { success: true, data: config, providers: providers };
                    });
                    autocompletionsDialog.onSave.on(async (config) => {
                        return await appDataService.updateAutocompletionsConfigAsync(config);
                    });
                    autocompletionsDialog.onTest.on(async (payload) => {
                        return await appDataService.testAutocompletionsCompletionAsync(
                            payload.providerType, payload.baseUrl, payload.apiKey, payload.modelId
                        );
                    });
                    autocompletionsDialog.onListModels.on(async (providerType, baseUrl, apiKey) => {
                        return await appDataService.listAutocompletionsModelsAsync(providerType, baseUrl, apiKey);
                    });
                    menuComponent.hideMenu();
                    await autocompletionsDialog.show();
                    return true;
                case 'open-models':
                    const modelsConfigDialog = new ModelsConfigDialog();
                    modelsConfigDialog.onLoad.on(async () => {
                        return await appDataService.getModelsConfigAsync();
                    });
                    modelsConfigDialog.onLoadProviders.on(async () => {
                        return await appDataService.getProvidersForModelsAsync();
                    });
                    modelsConfigDialog.onSave.on(async (config) => {
                        return await appDataService.updateModelsConfigAsync(config);
                    });
                    menuComponent.hideMenu();
                    await modelsConfigDialog.show();
                    return true;
                default:
                    return false;
            }
        });

        toolbarComponent.onModelNameClick.on(async () => {
            const dialog = createModelSelectorDialog([], null, true);
            await dialog.show();
        });

        statusComponent.onClearChat.on(async () => {
            const confirmDialog = new ConfirmDialog();
            const hasModel = !!modelStore.getState().modelId;
            const result = await confirmDialog.confirm(hasModel);
            if (!result.confirmed) return false;
            if (result.action === 'summarize') {
                await appManager.performSummarizeAndClear();
            } else {
                await appManager.performClearChat(result.action);
            }
            return true;
        });

        statusComponent.onRetry.on(async () => {
            await appManager.reloadActiveModel();
        });

        changesPanelComponent.onDiscardAll.on(async () => {
            return await appDataService.discardAllAsync();
        });

        changesPanelComponent.onAcceptAll.on(async () => {
            return await appDataService.acceptAllAsync();
        });

        changesPanelComponent.onReviewFile.on(async (filePath) => {
            return await appDataService.reviewFileAsync(filePath);
        });

        changesPanelComponent.onReviewAll.on(async (filePaths) => {
            return await appDataService.reviewAllFilesAsync(filePaths);
        });

        changesPanelComponent.onOpenAll.on(async (filePaths) => {
            return await appDataService.openAllFilesAsync(filePaths);
        });

        changesPanelComponent.onOpenFile.on(async (filePath) => {
            return await appDataService.openFileAsync(filePath);
        });

        changesPanelComponent.onDiscardSingleFile.on(async (filePath) => {
            return await appDataService.discardFileAsync(filePath);
        });

        changesPanelComponent.onAcceptSingleFile.on(async (filePath) => {
            return await appDataService.acceptFileAsync(filePath);
        });

        this._globalClickHandler = () => {
            menuComponent.hideMenu();
            inputComponent.hideDropdown();
        };
        window.addEventListener('click', this._globalClickHandler);
    }

    get initialized() {
        return this._initialized;
    }

    _detachEvents() {
        if (this._appStoreListener) {
            appStore.unsubscribe(this._appStoreListener);
            this._appStoreListener = null;
        }

        if (this._modelStoreListener) {
            modelStore.unsubscribe(this._modelStoreListener);
            this._modelStoreListener = null;
        }

        if (this._instructionsStoreListener) {
            instructionsStore.unsubscribe(this._instructionsStoreListener);
            this._instructionsStoreListener = null;
        }

        if (this._settingsStoreListener) {
            settingsStore.unsubscribe(this._settingsStoreListener);
            this._settingsStoreListener = null;
        }

        if (this._changesStoreListener) {
            changesStore.unsubscribe(this._changesStoreListener);
            this._changesStoreListener = null;
        }

        if (this._providerResolverUnsubscribe) {
            this._providerResolverUnsubscribe();
            this._providerResolverUnsubscribe = null;
        }

        if (this._globalClickHandler) {
            window.removeEventListener('click', this._globalClickHandler);
            this._globalClickHandler = null;
        }

        inputComponent.onClick.off();
        inputComponent.onEnter.off();
        inputComponent.onTabChanged.off();
        chatController.onCopyCode.off();
        menuComponent.onClick.off();
        toolbarComponent.onModelNameClick.off();
        statusComponent.onRetry.off();
        statusComponent.onClearChat.off();
        changesPanelComponent.onDiscardAll.off();
        changesPanelComponent.onAcceptAll.off();
        changesPanelComponent.onReviewFile.off();
        changesPanelComponent.onReviewAll.off();
        changesPanelComponent.onOpenAll.off();
        changesPanelComponent.onOpenFile.off();
        changesPanelComponent.onDiscardSingleFile.off();
        changesPanelComponent.onAcceptSingleFile.off();
    }
}

const appController = new AppController();
export default appController;