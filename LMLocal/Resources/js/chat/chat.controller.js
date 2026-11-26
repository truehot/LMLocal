import { Config, Icons } from '@app/constants/app.globals.js';
import { AppStatus } from '@app/store/app.status.js';
import { appSelectors } from '@app/store/app.selectors.js';
import { createCallback } from '@app/lib/callback.js';
import { createScrollManager } from '@app/lib/scroll.manager.js';
import { createUserMessage } from '@app/chat/user.message.js';
import { createAiMessage } from '@app/chat/ai.message.js';
import { createAiCollapsibleMessage } from '@app/chat/ai.collapsible.message.js';
import { createAiCollapsedMessage } from '@app/chat/ai.collapsed.message.js';
import { ToolCallsDisplay, resolveToolCallsDisplay } from '@app/lib/tool.calls.display.js';
import { buildHistoryCotNote, collectCotModel } from '@app/chat/history.note.js';
import { createHighlightParser } from '@app/workers/highlight.parser.js';
import { createMarkDownParser, ParserType } from '@app/workers/markdown.parser.js';
import { PipelineBuilder } from '@app/chat/chat.pipeline.builder.js';
import {
    attachCopyAction,
    attachExchangeActions,
    attachMessageTime,
    detachExchangeActions,
    getCopySource,
    getMessageSource,
    isServicePromptText,
    refreshMessageTime,
    setExchangeActionsDisabled,
    COPY_ACTION_SELECTOR,
    EDIT_ACTION_SELECTOR,
    MESSAGE_TIME_SELECTOR,
    REGENERATE_ACTION_SELECTOR
} from '@app/chat/message.actions.js';

/**
 * ChatController — manages chat UI and message lifecycle.
 */
const LOCATION_SUFFIX = /(?:::\s*[A-Za-z_$][\w$.]*|:\d+(?:[-–]\d+)?)+$/;

class ChatController {
    constructor() {
        this.container = null;
        this.currentAi = null;
        this.scrollManager = null;
        this.markdownParser = null;
        this.highlightParser = null;
        this._activeTimeouts = [];
        this.onCopyCode = createCallback();
        this.onOpenFile = createCallback();
        this.onContinue = createCallback();
        this.onRegenerate = createCallback();
        this.onEditLastPrompt = createCallback();
        this.toolCallsDisplay = ToolCallsDisplay.FLAT;
        this.showTokenStats = false;
        this._sessionStartedAt = 0;
        this.pipelineBuilder = null;
        this._pendingFinishPromise = null;
        this._editDimmed = false;
    }

    _getContainer() {
        return document.getElementById('chat-container');
    }

    _enforceMessageLimit() {
        const messages = Array.from(this.container.getElementsByClassName('message'));
        if (messages.length <= Config.MAX_DISPLAYED_MESSAGES) return;
        const toRemove = messages.length - Config.MAX_DISPLAYED_MESSAGES;
        messages.slice(0, toRemove).forEach(el => {
            el.remove();
        });
    }

    _onContainerClick = (e) => {
        const continueBtn = e.target.closest('.generation-stopped .continue-btn');
        if (continueBtn) {
            if (continueBtn.disabled) return;
            continueBtn.disabled = true;
            this.onContinue.emit().then(accepted => {
                if (!accepted) continueBtn.disabled = false;
            }).catch(err => {
                console.error('Continue failed:', err);
                continueBtn.disabled = false;
            });
            return;
        }

        const fileLink = e.target.closest('.file-link');
        if (fileLink) {
            e.preventDefault();
            const raw = (fileLink.textContent || '').trim();
            const path = raw.replace(LOCATION_SUFFIX, '');
            if (path) {
                this.onOpenFile.emit(path)
                    .catch(err => console.error(`Failed to open file link "${path}":`, err));
            } else {
                console.warn('file-link click without a resolvable path:', raw);
            }
            return;
        }

        const messageCopyBtn = e.target.closest(COPY_ACTION_SELECTOR);
        if (messageCopyBtn) {
            this._handleCopy(getCopySource(messageCopyBtn), messageCopyBtn);
            return;
        }

        const messageRegenerateBtn = e.target.closest(REGENERATE_ACTION_SELECTOR);
        if (messageRegenerateBtn) {
            if (messageRegenerateBtn.disabled) return;

            const expectedPrompt = this.getLastPromptText();

            messageRegenerateBtn.disabled = true;
            this.onRegenerate.emit(expectedPrompt).then(accepted => {
                if (!accepted) messageRegenerateBtn.disabled = false;
            }).catch(err => {
                console.error('Regenerate failed:', err);
                messageRegenerateBtn.disabled = false;
            });
            return;
        }

        const messageEditBtn = e.target.closest(EDIT_ACTION_SELECTOR);
        if (messageEditBtn) {
            if (messageEditBtn.disabled) return;
            this.onEditLastPrompt.emit(getCopySource(messageEditBtn));
            return;
        }

        const copyBtn = e.target.closest('.header-copy-btn');
        if (copyBtn) {
            const wrapper = copyBtn.closest('.code-block-container');
            if (!wrapper) return;
            const codeElement = wrapper.querySelector('pre code') || wrapper.querySelector('pre');
            if (!codeElement) return;
            this._handleCopy(codeElement.textContent, copyBtn);
            return;
        }

        const thoughtBlock = e.target.closest('.thought-container');
        if (thoughtBlock) {
            const isToggleBtn = e.target.classList.contains('toggle-thought-btn');
            const isHeader = e.target.closest('.reasoning-header');
            if (isToggleBtn || isHeader) {
                const content = thoughtBlock.querySelector('.thought-content');
                if (content) {
                    content.classList.toggle('expanded');
                }
                e.stopPropagation();
            }
        }

        const showMoreBtn = e.target.closest('.show-more-btn');
        if (showMoreBtn) {
            const userMessageDiv = showMoreBtn.closest('.message.user-message');
            if (userMessageDiv) {
                userMessageDiv.classList.toggle('expanded');
            }
            e.stopPropagation();
            return;
        }

        const codeToggleButton = e.target.closest('.code-toggle-btn');
        if (codeToggleButton) {
            const wrapper = codeToggleButton.closest('.code-block-container');
            if (!wrapper) return;
            const codeToggleIcon = wrapper.querySelector('.toggle-icon');
            if (!codeToggleIcon) return;
            const codeToggleText = wrapper.querySelector('.toggle-text');
            if (!codeToggleText) return;

            const isExpanded = wrapper.classList.toggle('is-expanded');
            codeToggleText.textContent = isExpanded ? 'Collapse' : 'Expand';
            codeToggleIcon.style.transform = isExpanded ? 'rotate(180deg)' : 'rotate(0deg)';
            e.stopPropagation();
        }

        const collapsibleBlock = e.target.closest('.collapsible-block');
        if (collapsibleBlock) {
            const isToggleBtn = e.target.classList.contains('toggle-collapsible-btn');
            const isHeader = e.target.closest('.collapsible-header');
            if (isToggleBtn || isHeader) {
                const content = collapsibleBlock.querySelector('.collapsible-content');
                if (content) {
                    content.classList.toggle('expanded');
                    collapsibleBlock.classList.toggle('expanded');
                }
                e.stopPropagation();
                return;
            }
        }
    };

    _renderMessageFlow(state, prev = {}) {
        const groupChanged =
            state.toolGroupId !== prev.toolGroupId ||
            state.toolGroupCompleted !== prev.toolGroupCompleted;

        const statusChanged = state.status !== prev.status;

        const hasRenderableChange =
            state.accumulatedText !== prev.accumulatedText ||
            state.accumulatedThoughtText !== prev.accumulatedThoughtText ||
            state.roundNumber !== prev.roundNumber ||
            state.toolCallId !== prev.toolCallId ||
            state.toolWithError !== prev.toolWithError ||
            state.toolMessage !== prev.toolMessage ||
            state.toolStep !== prev.toolStep;

        if (!groupChanged && !statusChanged && !hasRenderableChange) {
            return;
        }

        if (groupChanged && this.currentAi) {
            this.currentAi.updateToolGroup(state.toolGroupId, state.toolGroupCompleted, state.toolGroupTotal);
        }

        if (!statusChanged && !hasRenderableChange) {
            return;
        }

        switch (state.status) {
            case AppStatus.PROCESSING:

                this._pendingFinishPromise = null;

                if (state.roundNumber === 0) {
                    detachExchangeActions(this.container);
                }

                if (state.roundNumber > 0) {
                    // Tool round iteration — no new user message.
                    if (this.currentAi?.isCollapsible) {
                        this.currentAi.nextRound(
                            state.roundNumber
                        );
                    } else {
                        this.currentAi?.finalize();
                        this.currentAi = createAiMessage(
                            this.container,
                            this.highlightParser,
                            this.pipelineBuilder.createStreaming(this.markdownParser),
                            true
                        );
                    }
                } else {
                    // New user message. Only create the AI response bubble.
                    this.currentAi?.finalize();
                    this.currentAi = this._createAiAnswer();
                }

                this.scrollManager.scrollToBottom(false);
                break;

            case AppStatus.THINKING:
                this.currentAi?.updateThought(state.accumulatedThoughtText);
                this.scrollManager.scrollToBottom();
                break;

            case AppStatus.STREAMING:
                if (prev.status === AppStatus.THINKING) this.currentAi?.stopThoughts();
                this.currentAi?.updateStreaming(state.accumulatedText);
                break;

            case AppStatus.FINISHING: {
                if (this.currentAi) {
                    this.currentAi.stopThoughts();

                    this._pendingFinishPromise = this.currentAi.finishStreaming();
                    this._pendingFinishPromise.then(() => {
                        this.scrollManager.scrollToBottom();
                    });
                }
                break;
            }

            case AppStatus.EXECUTING:
                this.currentAi.startTooling(state.toolCallId, state.toolMessage, state.toolName);
                break;

            case AppStatus.STEPPING:
                this.currentAi?.stepTooling(state.toolCallId, state.toolStep, state.toolMessage, state.toolName);
                this.scrollManager.scrollToBottom();
                break;

            case AppStatus.RESPONDING:
                this.currentAi.finishTooling(state.toolCallId, state.toolWithError, state.toolMessage);
                this.scrollManager.scrollToBottom();
                break;

            case AppStatus.ERROR:
                if (this.currentAi) {
                    const errorMsg = `${state.error || 'Unknown error'}`;
                    this.currentAi.stopStreaming(errorMsg, { canContinue: true });
                    this.currentAi.finalize();
                    this.currentAi = null;
                }
                this._pendingFinishPromise = null;
                this._attachLastExchangeActions();
                break;

            case AppStatus.OFFLINE:
                if (this.currentAi) {
                    this.currentAi.stopStreaming("You are offline");
                    this.currentAi.finalize();
                    this.currentAi = null;
                }
                this._pendingFinishPromise = null;
                break;

            case AppStatus.CLEARING:
                if (this.currentAi) {
                    this.currentAi.clear();
                    this.currentAi = null;
                }
                this.resetChatUI();
                break;

            case AppStatus.IDLE:
                if (this.currentAi && appSelectors.isGenerating(prev.status)) {
                    attachCopyAction(this.currentAi.root, state.accumulatedText);
                    attachMessageTime(this.currentAi.root, new Date());
                    this.currentAi.stopLoadingIndicator();

                    const awaitFinalize = () =>
                        (this._pendingFinishPromise || Promise.resolve()).catch(() => { });

                    const showStatsAndScroll = () => {
                        this._showTokenStats(state);
                        this.scrollManager.scrollToBottom();
                    };

                    if (this.currentAi.isCollapsible) {
                        this.currentAi.finalizeResult().then(showStatsAndScroll);
                    } else {
                        awaitFinalize().then(showStatsAndScroll);
                    }
                }

                this._attachLastExchangeActions();
                break;

        }
    }

    /**
     * Creates the answer bubble of a new prompt with the display mode chosen when the answer starts.
     */
    _createAiAnswer() {
        const pipeline = this.pipelineBuilder.createStreaming(this.markdownParser);
        const config = { roundNum: 0 };

        if (this.toolCallsDisplay === ToolCallsDisplay.COLLAPSED) {
            return createAiCollapsedMessage(this.container, this.highlightParser, pipeline, config);
        }

        if (this.toolCallsDisplay === ToolCallsDisplay.EXPANDED) {
            return createAiCollapsibleMessage(this.container, this.highlightParser, pipeline, config);
        }

        return createAiMessage(this.container, this.highlightParser, pipeline, false);
    }

    /**
     * Copies text through the host clipboard and reflects the outcome on the button icon.
     */
    _handleCopy(text, btn) {
        if (!btn) return Promise.resolve();

        if (btn._copyBusy) return Promise.resolve();
        btn._copyBusy = true;

        const scheduleReset = () => {
            const timeoutId = setTimeout(() => {
                btn.innerHTML = Icons.COPY;
                btn.classList.remove('success', 'error');
                btn._copyBusy = false;
                this._activeTimeouts = this._activeTimeouts.filter(id => id !== timeoutId);
            }, Config.COPY_STATUS_RESET_MS);
            this._activeTimeouts.push(timeoutId);
        };

        const showResult = (success) => {
            btn.innerHTML = success ? Icons.SUCCESS : Icons.ERROR;
            btn.classList.toggle('success', success);
            btn.classList.toggle('error', !success);
            scheduleReset();
        };

        if (!text) {
            showResult(false);
            return Promise.resolve();
        }

        return this.onCopyCode.emit(text)
            .then(success => showResult(!!success))
            .catch(err => {
                console.error('Copy failed', err);
                showResult(false);
            });
    }

    /**
     * Refreshes the relative time of the hovered answer.
     */
    _onContainerHover = (e) => {
        const time = e.target.closest('.ai-message')?.querySelector(MESSAGE_TIME_SELECTOR);
        if (time) refreshMessageTime(time);
    };

    _attachEvents() {
        if (this.container) {
            this.container.addEventListener('click', this._onContainerClick);
            this.container.addEventListener('mouseover', this._onContainerHover);
            this.container.addEventListener('focusin', this._onContainerHover);
        }
    }

    _detachEvents() {
        if (this.container) {
            this.container.removeEventListener('click', this._onContainerClick);
            this.container.removeEventListener('mouseover', this._onContainerHover);
            this.container.removeEventListener('focusin', this._onContainerHover);
        }
    }

    /**
     * Resets the chat UI container to a clean state, ready for a new conversation or for rendering a loaded session.
     */
    resetChatUI() {
        const container = this.container;
        if (!container) return;
        this.reset();
        container.replaceChildren();
        this.setup();
    }

    _clearTimeouts() {
        for (const timeoutId of this._activeTimeouts) {
            clearTimeout(timeoutId);
        }
        this._activeTimeouts = [];
    }

    setup() {
        this.reset();
        this.container = this._getContainer();
        if (!this.container) return this;

        this.scrollManager = createScrollManager(this.container, Config.SCROLL_THRESHOLD_PX);

        this.markdownParser = createMarkDownParser(ParserType.MARKED_WORKER);
        this.markdownParser.start();

        this.highlightParser = createHighlightParser();
        this.highlightParser.start();

        this.pipelineBuilder = new PipelineBuilder({
            scrollManager: this.scrollManager,
        });

        this._attachEvents();
        return this;
    }

    updateAppState(state, prev) {
        this._renderMessageFlow(state, prev);
    }

    updateSettingsState(state, prev) {
        if (state.status === prev.status &&
            state.ToolCallsDisplay === prev.ToolCallsDisplay &&
            state.CollapseToolCalls === prev.CollapseToolCalls &&
            state.ShowTokenStats === prev.ShowTokenStats) {
            return;
        }
        this.toolCallsDisplay = resolveToolCallsDisplay(state);
        this.showTokenStats = !!state.ShowTokenStats;
    }

    _showTokenStats(state) {
        if (!this.currentAi || !this.showTokenStats) return;
        if (!state?.lastAnswerTokens) return;

        const elapsedMs = this._sessionStartedAt
            ? Math.max(0, Date.now() - this._sessionStartedAt)
            : 0;

        this.currentAi.showTokenStats({
            answerTokens: state.lastAnswerTokens,
            cachedTokens: state.cachedTokens,
            tokenSpeed: state.tokenSpeed,
            elapsedMs
        });
    }

    reset() {
        this._clearTimeouts();
        this._detachEvents();

        this.scrollManager?.reset();

        this.currentAi?.clear();
        this.currentAi = null;
        this.pipelineBuilder = null;
        this._sessionStartedAt = 0;
        this._pendingFinishPromise = null;
        this._editDimmed = false;
    }

    /**
     * Pre-render a user message (text + optional dataUrls) directly into the chat DOM
     */
    renderPendingUserMessage(text, images) {
        if (!this.container) return;
        this._sessionStartedAt = Date.now();
        this._enforceMessageLimit();
        createUserMessage(text || '', this.container, this.scrollManager, images);
    }

    /**
     * Message elements of the chat, in DOM order.
     */
    _getMessageElements() {
        if (!this.container) return [];
        return Array.from(this.container.children)
            .filter(el => el.classList && el.classList.contains('message'));
    }

    /**
     * The last exchange of the chat: its prompt and every response node that follows it.
     */
    _getLastExchange() {
        const messages = this._getMessageElements();
        let userIndex = -1;

        for (let i = messages.length - 1; i >= 0; i--) {
            if (messages[i].classList.contains('user-message')) {
                userIndex = i;
                break;
            }
        }

        if (userIndex < 0) return null;

        const answers = messages.slice(userIndex + 1).filter(el => el.classList.contains('ai-message'));

        return {
            index: userIndex,
            messages,
            user: messages[userIndex],
            answer: answers.length > 0 ? answers[answers.length - 1] : null
        };
    }

    /**
     * Puts Regenerate on the last answer and Edit on the last prompt; removes both everywhere else.
     */
    _attachLastExchangeActions() {
        if (!this.container) return;

        detachExchangeActions(this.container);

        const exchange = this._getLastExchange();
        if (!exchange) return;

        const promptText = getMessageSource(exchange.user);

        const isServicePrompt = isServicePromptText(promptText);

        const hasImages = !!exchange.user.querySelector('.message-image-thumb');
        const hasPromptText = !!promptText;

        const canRegenerate = !!exchange.answer && !isServicePrompt;
        const canEdit = !isServicePrompt && hasPromptText && !hasImages;

        if (!canRegenerate && !canEdit) return;

        attachExchangeActions(canRegenerate ? exchange.answer : null, canEdit ? exchange.user : null);

        if (this._editDimmed) this.setLastExchangeDimmed(true);
    }

    /**
     * Text of the last prompt on screen, as its copy button would copy it; empty when there is none.
     */
    getLastPromptText() {
        return getMessageSource(this._getLastExchange()?.user);
    }

    /**
     * Drops the last exchange from the chat. The prompt is kept when asked (regenerate) and dropped otherwise (edit).
     */
    removeLastExchange(keepUserMessage) {
        const exchange = this._getLastExchange();
        if (!exchange) return;

        // If the backend refuses, the chat is drawn again from the history (AppController), so nothing is kept here.
        const removeFrom = exchange.index + (keepUserMessage ? 1 : 0);
        for (const node of exchange.messages.slice(removeFrom)) node.remove();

        this.currentAi = null;
        this._pendingFinishPromise = null;
    }

    /**
     * Greys out the exchange that is being edited and disables the actions that would overwrite the field: the prompt
     * is not part of the chat any more unless Send replaces it. Copy stays available - it changes nothing.
     */
    setLastExchangeDimmed(active) {
        this._editDimmed = !!active;

        const exchange = this._getLastExchange();

        if (exchange) {
            for (let i = exchange.index; i < exchange.messages.length; i++) {
                exchange.messages[i].classList.toggle('exchange-being-edited', this._editDimmed);
            }
        }

        setExchangeActionsDisabled(this.container, this._editDimmed);
    }

    renderHistory(messages) {
        if (!this.container || !messages || messages.length === 0) return;

        let stepCount = 0;
        let cotModels = [];
        let pending = [];

        for (let i = 0; i < messages.length; i++) {
            let msg = messages[i];
            if (!msg || !msg.role) continue;

            if (msg.role === 'user') {
                stepCount = 0;
                cotModels = [];
                createUserMessage(msg.content || '', this.container, this.scrollManager);
            } else if (msg.role === 'assistant') {
                let hasTools = msg.toolCalls && Array.isArray(msg.toolCalls) && msg.toolCalls.length > 0;

                collectCotModel(cotModels, msg.modelId);

                if (hasTools) {
                    stepCount++;
                } else {
                    let content = msg.content || '';
                    let note = buildHistoryCotNote(cotModels, stepCount);
                    if (note) {
                        content = '`' + note + '`\n\n' + content;
                    }
                    pending.push(this._renderHistoryFinal(content, msg.content || '', msg.timestamp));
                    stepCount = 0;
                    cotModels = [];
                }
            }
        }

        Promise.all(pending).then(() => {
            this.scrollManager?.scrollToBottom(true);
            this._attachLastExchangeActions();
        });
    }

    markAsFinalRound() {
        if (this.currentAi?.isCollapsible) {
            this.currentAi.markAsFinalRound();
        }
    }

    /**
     * Renders a historical answer.
     */
    _renderHistoryFinal(content, rawContent, timestamp) {
        let localAi = createAiMessage(
            this.container,
            this.highlightParser,
            this.pipelineBuilder.createImmediate(this.markdownParser),
            false
        );
        localAi.updateStreaming(content);
        attachCopyAction(localAi.root, rawContent);
        attachMessageTime(localAi.root, timestamp);
        return localAi.finishStreaming();
    }
}

const chatController = new ChatController();
export default chatController;
