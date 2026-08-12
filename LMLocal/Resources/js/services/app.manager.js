import { AppStatus } from '@app/store/app.status.js';
import { appSelectors } from '@app/store/app.selectors.js';
import appStore from '@app/store/app.store.js';
import modelStore from '@app/store/model.store.js';
import instructionsStore from '@app/store/instructions.store.js';
import appDataService from '@app/services/app.data.service.js';
import { startupManager } from '@app/services/startup.manager.js';
import { createCallback } from '@app/lib/callback.js';
import bridgeClient from '@app/api/bridge.client.js';

/**
 * AppManager — manage user-initiated chat actions (send, stop, clear, load session, summarize) against the bridge and drives store status transitions.
 */
class AppManager {

    constructor() {
        this.onUserMessagePending = createCallback();
        this.onHistoryLoaded = createCallback();
    }

    async onAppInit() {
        appStore.setState({ status: AppStatus.CONNECTING, accumulatedText: "", accumulatedThoughtText: "", error: null });

        try {
            const settings = await this.getSettings();

            if (settings.EnableChatLogging === true && settings.AutoLoadLastHistory === true) {
                const session = await appDataService.getLastChatSessionAsync();
                if (session && session.hasSession) {
                    await this.onHistoryLoaded.emit(session.messages);
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

    async performSendMessage(text, hasContent, images = []) {
        const cleanText = (text || '').trim();
        const hasImages = images && images.length > 0;
        if ((!cleanText && !hasImages) || !appSelectors.canSend(appStore.getState().status)) return false;

        await this.onUserMessagePending.emit(cleanText, hasImages ? images.slice() : null);

        appStore.setState({
            status: AppStatus.PROCESSING,
            accumulatedText: "",
            accumulatedThoughtText: "",
            error: null,
            roundNumber: 0,
            toolCount: 0
        });

        const instructionsState = instructionsStore.getState();
        const selectedTabId = instructionsState.selectedTabId;
        const instructions = instructionsState.instructions || [];

        const request = {
            prompt: cleanText,
            includeContent: hasContent,
            modelId: modelStore.getState().modelId || ""
        };
        if (hasImages) request.images = images;

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

    /**
     * Marks the start of a tool round.
     */
    onToolRoundStart(roundNumber, toolCount) {
        appStore.setState({
            status: AppStatus.PROCESSING,
            roundNumber: roundNumber || 0,
            toolCount: toolCount || 0,
            accumulatedText: "",
            accumulatedThoughtText: ""
        });
    }

    async performStop(text) {
        if (!appSelectors.isGenerating(appStore.getState().status)) return;

        appStore.setState({ status: AppStatus.STOPPING });

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
                    error: null
                });
                return;
            }
        } catch (error) {
            appStore.setState({
                status: AppStatus.IDLE,
                tokenUsed: 0, tokenSpeed: 0,
                accumulatedText: "", accumulatedThoughtText: "",
                error: null
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
                error: null
            });

            if (action !== 'none') {
                const session = await appDataService.getLastChatSessionAsync();
                if (session && session.hasSession) {
                    await this.onHistoryLoaded.emit(session.messages);
                }
            }
        } catch (error) {
            appStore.setState({
                status: AppStatus.ERROR,
                error: "Failed to clear chat history",
                accumulatedText: "", accumulatedThoughtText: "",
                tokenUsed: 0, tokenSpeed: 0
            });
        }
    }

    /**
     * Loads an old chat session from the history log and renders it. Mirrors performClearChat.
     */
    async performLoadSession(sessionId) {
        if (appSelectors.isBusy(appStore.getState().status)) {
            return { success: false, error: new Error('Cannot load session while busy') };
        }

        appStore.setState({
            status: AppStatus.CLEARING,
            error: null
        });

        try {
            const session = await appDataService.getChatSessionByIdAsync(sessionId);

            appStore.setState({
                status: AppStatus.IDLE,
                tokenUsed: 0, tokenSpeed: 0,
                accumulatedText: "", accumulatedThoughtText: "",
                error: null
            });

            if (session && session.hasSession && Array.isArray(session.messages) && session.messages.length > 0) {
                await this.onHistoryLoaded.emit(session.messages);
                return { success: true };
            }

            return { success: false, error: new Error('Session has no messages') };
        } catch (error) {
            console.error("Load session failed:", error);
            appStore.setState({
                status: AppStatus.ERROR,
                error: "Failed to load chat session",
                accumulatedText: "", accumulatedThoughtText: "",
                tokenUsed: 0, tokenSpeed: 0
            });
            return { success: false, error };
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
