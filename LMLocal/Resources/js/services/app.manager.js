import { AppStatus } from '@app/store/app.status.js';
import { appSelectors } from '@app/store/app.selectors.js';
import appStore from '@app/store/app.store.js';
import modelStore from '@app/store/model.store.js';
import instructionsStore from '@app/store/instructions.store.js';
import appDataService from '@app/services/app.data.service.js';
import { startupManager } from '@app/services/startup.manager.js';
import chatController from '@app/chat/chat.controller.js';
import bridgeClient from '@app/api/bridge.client.js';

/**
 * AppManager - central controller for streaming and UI state.
 */
class AppManager {

    async onAppInit() {
        appStore.setState({ status: AppStatus.CONNECTING, accumulatedText: "", accumulatedThoughtText: "", error: null });

        try {
            const settings = await this.getSettings();

            if (settings.EnableChatLogging === true && settings.AutoLoadLastHistory === true) {
                const session = await appDataService.getLastChatSessionAsync();
                if (session && session.hasSession) {
                    chatController.renderHistory(session.messages);
                }
            }

            if (settings.AutoLoadOnStartup === false) {
                appStore.setState({
                    status: AppStatus.OFFLINE,
                    error: null,
                    tokenSpeed: 0
                });
                return;
            }

            await startupManager.initialize();

        } catch (e) {
            console.error("onAppInit load failed:", e);
        } finally {
            await appDataService.getInstructionsAsync();
            await appDataService.getSnapshotAsync();
        }
    }

    onFatalError(message) {
        appStore.setState({
            status: AppStatus.OFFLINE,
            error: message,
            tokenSpeed: 0
        });
    }

    async reloadActiveModel() {
        appStore.setState({ status: AppStatus.CONNECTING, accumulatedText: "", accumulatedThoughtText: "", error: null });
        await startupManager.initialize();
    }

    async performSendMessage(text, hasContent) {
        const cleanText = (text || '').trim();
        if (!cleanText || !appSelectors.canSend(appStore.getState().status)) return;

        appStore.setState({ status: AppStatus.PROCESSING, accumulatedText: "", accumulatedThoughtText: "", error: null, userMessage: cleanText });

        const instructionsState = instructionsStore.getState();
        const selectedTabId = instructionsState.selectedTabId;
        const instructions = instructionsState.instructions || [];

        const request = {
            prompt: cleanText,
            includeContent: hasContent,
            modelId: modelStore.getState().modelId || ""
        };

        if (Array.isArray(instructions) && selectedTabId) {
            const selectedTab = instructions.find(tab => tab.id == selectedTabId);
            if (selectedTab && selectedTab.enabled) {
                if (selectedTab.prompt !== undefined && selectedTab.prompt !== null) {
                    request.additionalPrompt = selectedTab.prompt;
                }
                if (selectedTab.temperature !== undefined && selectedTab.temperature !== null) {
                    request.temperature = selectedTab.temperature;
                }
            }
        }

        bridgeClient.executePromptAsync(request).catch(e => {
            console.error("Async Bridge Error:", e);
            this.onFatalError("Critical bridge communication failure.");
        });

        return true;
    }

    async performStop(text) {
        if (!appSelectors.isGenerating(appStore.getState().status)) return;

        appStore.setState({ status: AppStatus.STOPPING, userMessage: text });

        try {
            await bridgeClient.stopExecutionAsync();
        } catch (e) {
            console.error("Stop signal failed", e);
            this.onFatalError("Failed to send stop signal.");
        }
    }

    async performCopyCode(text) {
        try {
            return await bridgeClient.copyToClipboardAsync(text);
        } catch (e) {
            console.error("Copy failed", e);
            return false;
        }
    }

    async performSummarizeAndClear() {
        if (appSelectors.isBusy(appStore.getState().status)) return;

        const modelId = modelStore.getState().modelId;
        if (!modelId) {
            return await this.performClearChat('none');
        }

        appStore.setState({ status: AppStatus.COMPACTING, error: null });

        try {
            const ok = await bridgeClient.summarizeAndCompactAsync(modelId);
            if (!ok) {
                appStore.setState({
                    status: AppStatus.IDLE,
                    tokenUsed: 0, tokenSpeed: 0,
                    accumulatedText: "", accumulatedThoughtText: "",
                    userMessage: "", error: null
                });
                return;
            }
        } catch (error) {
            appStore.setState({
                status: AppStatus.IDLE,
                tokenUsed: 0, tokenSpeed: 0,
                accumulatedText: "", accumulatedThoughtText: "",
                userMessage: "", error: null
            });
            return;
        }

        appStore.setState({ status: AppStatus.IDLE });
        return await this.performClearChat('last-prompt');
    }

    async performClearChat(action = 'none') {
        if (appSelectors.isBusy(appStore.getState().status)) return;

        appStore.setState({
            status: AppStatus.CLEARING,
            error: null
        });

        try {
            await bridgeClient.resetHistoryWithActionAsync(action);

            appStore.setState({
                status: AppStatus.IDLE,
                tokenUsed: 0, tokenSpeed: 0,
                accumulatedText: "", accumulatedThoughtText: "",
                userMessage: "", error: null
            });

            if (action !== 'none') {
                const session = await appDataService.getLastChatSessionAsync();
                if (session && session.hasSession) {
                    chatController.renderHistory(session.messages);
                }
            }
        } catch (error) {
            appStore.setState({
                status: AppStatus.ERROR,
                error: "Failed to clear chat history",
                accumulatedText: "", accumulatedThoughtText: "",
                userMessage: "", tokenUsed: 0, tokenSpeed: 0
            });
        }
    }

    async getInstructions() {
        return await appDataService.getInstructionsAsync();
    }

    async getSettings() {
        return await appDataService.getSettingsAsync();
    }
}

const appManager = new AppManager();
export default appManager;
