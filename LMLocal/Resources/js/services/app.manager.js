import { AppStatus } from '@app/store/app.status.js';
import { appSelectors } from '@app/store/app.selectors.js';
import appStore from '@app/store/app.store.js';
import modelStore from '@app/store/model.store.js';
import sessionStore from '@app/store/session.store.js';
import instructionsStore from '@app/store/instructions.store.js';
import appDataService from '@app/services/app.data.service.js';
import { startupManager } from '@app/services/startup.manager.js';
import { createCallback } from '@app/lib/callback.js';
import { ReplaceLastMode } from '@app/constants/app.globals.js';
import bridgeClient from '@app/api/bridge.client.js';

/**
 * AppManager — manage user-initiated chat actions (send, stop, clear, load session, summarize) against the bridge and drives store status transitions.
 */
class AppManager {

    constructor() {
        this.onUserMessagePending = createCallback();
        this.onHistoryLoaded = createCallback();
        this.onRemoveLastExchange = createCallback();
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
            await appDataService.applyActiveModelInstruction();
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
        await appDataService.applyActiveModelInstruction();
    }

    /**
     * Sends a prompt. With replaceLastPrompt (the prompt the chat shows for its last exchange) the prompt replaces
     * that exchange instead of following it: this is Send while the last prompt is being edited.
     */
    async performSendMessage(text, hasContent, images = [], replaceLastPrompt = null) {
        const cleanText = (text || '').trim();
        const hasImages = images && images.length > 0;
        if ((!cleanText && !hasImages) || !appSelectors.canSend(appStore.getState().status)) return false;

        const replacesExchange = typeof replaceLastPrompt === 'string';

        if (replacesExchange) {
            this.onRemoveLastExchange.emit(false);
            this._resetAnswerMetrics();
        }

        await this.onUserMessagePending.emit(cleanText, hasImages ? images.slice() : null);

        const request = this._buildPromptRequest(cleanText, hasContent, hasImages ? images : null);
        if (replacesExchange) {
            request.replaceLast = ReplaceLastMode.Exchange;
            request.replaceLastPrompt = replaceLastPrompt;
        }

        this._startPromptRound(request);
        return true;
    }

    /**
     * Asks the last prompt again with the currently selected model, instructions tab and temperature.
     * expectedPrompt is the prompt the chat shows, so the backend can refuse when its history ends differently.
     */
    performRegenerate(expectedPrompt) {
        if (!appSelectors.canSend(appStore.getState().status)) return false;

        this.onRemoveLastExchange.emit(true);
        this._resetAnswerMetrics();

        const request = this._buildPromptRequest('', false, null);
        request.replaceLast = ReplaceLastMode.Answer;
        request.replaceLastPrompt = typeof expectedPrompt === 'string' ? expectedPrompt : '';

        this._startPromptRound(request);
        return true;
    }

    /**
     * Reads the history the backend holds now. Used after a refused replacement: the chat already removed messages
     * the backend kept, so it is drawn again from the history instead of being patched by hand.
     */
    async loadCurrentHistory() {
        try {
            const session = await appDataService.getCurrentChatHistoryAsync();
            return session && session.hasSession && Array.isArray(session.messages) ? session.messages : [];
        } catch (error) {
            console.error("Failed to reload the chat history:", error);
            return null;
        }
    }

    /**
     * Resumes generation after a failed round without adding a new user message.
     */
    async performContinue() {
        if (!appSelectors.canSend(appStore.getState().status)) return false;

        try {
            this._startPromptRound(this._buildPromptRequest('', false, null));
            return true;
        } catch (error) {
            console.error("Continue failed:", error);
            return false;
        }
    }

    /**
     * Builds the bridge request payload shared by a new prompt and a continuation.
     */
    _buildPromptRequest(cleanText, includeContent, images) {
        const request = {
            prompt: cleanText,
            includeContent: includeContent,
            modelId: modelStore.getState().modelId || ""
        };

        if (images && images.length > 0) request.images = images;

        const instructionsState = instructionsStore.getState();
        const selectedTabId = instructionsState.selectedTabId;
        const instructions = instructionsState.instructions || [];

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

        return request;
    }

    /**
     * Switches the app to a fresh generation round and hands the request to the bridge.
     */
    _startPromptRound(request) {
        appStore.setState({
            status: AppStatus.PROCESSING,
            accumulatedText: "",
            accumulatedThoughtText: "",
            error: null,
            replacePending: request.replaceLast ? { mode: request.replaceLast, prompt: request.prompt || '' } : null,
            stoppedByUser: false,
            roundNumber: 0
        });

        bridgeClient.executePromptAsync(request).catch(e => {
            console.error("Async Bridge Error:", e);
            this.onFatalError("Critical bridge communication failure.");
        });
    }

    /**
     * Marks the start of a tool round.
     */
    onToolRoundStart(roundNumber) {
        appStore.setState({
            status: AppStatus.PROCESSING,
            roundNumber: roundNumber || 0,
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
                    roundTokens: 0, lastAnswerTokens: 0, tokenSpeed: 0,
                    accumulatedText: "", accumulatedThoughtText: "",
                    error: null
                });
                return;
            }
        } catch (error) {
            appStore.setState({
                status: AppStatus.IDLE,
                roundTokens: 0, lastAnswerTokens: 0, tokenSpeed: 0,
                accumulatedText: "", accumulatedThoughtText: "",
                error: null
            });
            return;
        }

        appStore.setState({ status: AppStatus.IDLE });
        return await this.performClearChat('last-prompt');
    }

    /**
     * Drops the numbers of the answer that is being replaced, so they cannot outlive it on the new one.
     */
    _resetAnswerMetrics() {
        appStore.setState({
            roundTokens: 0,
            lastAnswerTokens: 0,
            tokenSpeed: 0,
            cachedTokens: 0
        });
    }

    /**
     * Drops every conversation metric after the conversation was replaced (clear-chat with any carry-over action, or loading another session).
     */
    _resetContextMetrics() {
        sessionStore.setState({
            contextTokens: 0,
            sessionPromptTokens: 0,
            sessionCompletionTokens: 0,
            sessionCachedTokens: 0,
            toolResultTokens: 0
        });
    }

    async performClearChat(action = 'none') {
        if (appSelectors.isBusy(appStore.getState().status)) return;

        appStore.setState({
            status: AppStatus.CLEARING,
            error: null
        });

        try {
            await bridgeClient.resetHistoryWithActionAsync(action);

            this._resetContextMetrics();

            appStore.setState({
                status: AppStatus.IDLE,
                roundTokens: 0, lastAnswerTokens: 0, tokenSpeed: 0,
                accumulatedText: "", accumulatedThoughtText: "",
                error: null
            });

            if (action !== 'none') {
                const session = await appDataService.getCurrentChatHistoryAsync();
                if (session && session.hasSession) {
                    await this.onHistoryLoaded.emit(session.messages);
                }
            }
        } catch (error) {
            appStore.setState({
                status: AppStatus.ERROR,
                error: "Failed to clear chat history",
                stoppedByUser: false,
                accumulatedText: "", accumulatedThoughtText: "",
                roundTokens: 0, lastAnswerTokens: 0, tokenSpeed: 0
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

            this._resetContextMetrics();

            appStore.setState({
                status: AppStatus.IDLE,
                roundTokens: 0, lastAnswerTokens: 0, tokenSpeed: 0,
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
                stoppedByUser: false,
                accumulatedText: "", accumulatedThoughtText: "",
                roundTokens: 0, lastAnswerTokens: 0, tokenSpeed: 0
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
