import { statusComponent } from '@app/components/status.component.js';
import { menuComponent } from '@app/components/menu.component.js';
import { inputComponent } from '@app/components/input.component.js';
import { themeComponent } from '@app/components/theme.component.js';
import { toolbarComponent } from '@app/components/toolbar.component.js';
import { chatComponent } from '@app/components/chat.component.js';

import chatController from '@app/chat/chat.controller.js';
import appManager from '@app/services/app.manager.js';
import { bridgeMessageHandler } from '@app/api/bridge.message.handler.js';
import appStore from '@app/store/app.store.js';
import { appSelectors } from '@app/store/app.selectors.js';
import modelStore from '@app/store/model.store.js';
import instructionsStore from '@app/store/instructions.store.js';
import settingsStore from '@app/store/settings.store.js';
import bridgeMessageDispatcher from '@app/api/bridge.message.dispatcher.js';
import appDataService from '@app/services/app.data.service.js';

import { ConfirmDialog } from '@app/dialogs/confirm.dialog.js';
import { SettingsDialog } from '@app/dialogs/settings.dialog.js';
import { InstructionsDialog } from '@app/dialogs/instructions.dialog.js';
import { ModelSelectorDialog } from '@app/dialogs/models.list.dialog.js';
import { McpSettingsDialog } from '@app/dialogs/mcp.settings.dialog.js';
import { UIText } from '@app/constants/app.globals.js';

/**
 * AppController - central initializer and event router.
 * Waits for required DOM elements, initializes UI components (Status, Input, Chat, Menu),
 * wires AppStore subscriptions and component event handlers, and starts the BridgeMessageDispatcher.
 * AppController only bootstraps and routes events, it does not handle streaming itself.
 */
class AppController {
    constructor() {
        this._initialized = false;
        this._storeListener = null;
        this._modelListener = null;
        this._instructionsListener = null;
        this._settingsListener = null;
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

        this._initialized = false;
    }

    _attachEvents() {
        this._storeListener = (appState, prevAppState) => {
            statusComponent.update(appState, prevAppState);
            inputComponent.update(appState, prevAppState);
            chatController.update(appState, prevAppState);
            toolbarComponent.update(appState, prevAppState);
        };
        appStore.subscribe(this._storeListener);

        this._modelListener = (state, prev) => {
            toolbarComponent.updateModelState(state, prev);
        };
        modelStore.subscribe(this._modelListener);

        this._instructionsListener = (state, prev) => {
            inputComponent.updateInstructionsState(state, prev);
        };
        instructionsStore.subscribe(this._instructionsListener);

        this._settingsListener = (state, prev) => {
            themeComponent.updateTheme(state, prev);
            chatComponent.update(state, prev);
        };
        settingsStore.subscribe(this._settingsListener);

        themeComponent.setup();

        inputComponent.onClick.on(async (text, hasActiveContent) => {
            const isGenerating = appSelectors.isBusy(appStore.getState().status);
            if (isGenerating) {
                await appManager.performStop(text);
                return false;
            } else if (text && text.trim()) {
                await appManager.performSendMessage(text, hasActiveContent);
                return true;
            }
            return false;
        });

        inputComponent.onEnter.on(async (text, hasActiveContent) => {
            if (text && text.trim()) {
                await appManager.performSendMessage(text, hasActiveContent);
                return true;
            }
            return false;
        });

        inputComponent.onTabChanged.on((tabId) => {
            instructionsStore.setState({
                selectedTabId: tabId
            });
        });

        chatController.onCopyCode.on(async (text) => {
            return await appManager.performCopyCode(text);
        });

        menuComponent.onClick.on(async (action) => {
            switch (action) {
                case 'clear-chat':
                    const confirmDialog = new ConfirmDialog();
                    const isConfirmed = await confirmDialog.confirm(UIText.CONFIRM_CLEAR_CONVERSATION);
                    if (!isConfirmed) return false;
                    await appManager.performClearChat();
                    return true;
                case 'open-settings':
                    const settingsDialog = new SettingsDialog();
                    settingsDialog.onLoad.on(async () => {
                        return await appDataService.getSettingsAsync();
                    });
                    settingsDialog.onTestConnection.on(async (settings) => {
                        return await appDataService.testConnectionAsync(settings);
                    });
                    settingsDialog.onSave.on(async (settings) => {
                        return await appDataService.updateSettingsAsync(settings);
                    });
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
                    await mcpDialog.show();
                    return true;
                default:
                    return false;
            }
        });

        toolbarComponent.onModelNameClick.on(async () => {
            const response = await appDataService.loadModels();
            if (response && response.models && response.models.length > 0) {
                const dialog = new ModelSelectorDialog(response.models, response.activeModel);

                dialog.onRefresh.on(async () => {
                    return await appDataService.loadModels();
                });

                dialog.onSelect.on(async (selectedModel) => {
                    if (selectedModel) {
                        await appDataService.setActiveModel(selectedModel.id, selectedModel.name, selectedModel.supportsMaxTokens, selectedModel.maxTokens || 0);
                        return true;
                    }
                    return false;
                });

                await dialog.show();
            }
        });

        statusComponent.onRetry.on(async () => {
            await appManager.reloadActiveModel();
        });

        this._globalClickHandler = () => {
            menuComponent.hideMenu();
            inputComponent.hideDropdown();
        };
        window.addEventListener('click', this._globalClickHandler);
    }

    _detachEvents() {
        if (this._storeListener) {
            appStore.unsubscribe(this._storeListener);
            this._storeListener = null;
        }

        if (this._modelListener) {
            modelStore.unsubscribe(this._modelListener);
            this._modelListener = null;
        }

        if (this._instructionsListener) {
            instructionsStore.unsubscribe(this._instructionsListener);
            this._instructionsListener = null;
        }

        if (this._settingsListener) {
            settingsStore.unsubscribe(this._settingsListener);
            this._settingsListener = null;
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
        statusComponent.onRetry.off();
        statusComponent.onModelNameClick.off();
    }

    get initialized() {
        return this._initialized;
    }
}

const appController = new AppController();
export default appController;

