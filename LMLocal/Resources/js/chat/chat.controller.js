import { UIText, Config } from '@app/constants/app.globals.js';
import { AppStatus } from '@app/store/app.status.js';
import { createCallback } from '@app/lib/callback.js';
import { createScrollManager } from '@app/lib/scroll.manager.js';
import { createUserMessage } from '@app/chat/user.message.js';
import { createAiMessage } from '@app/chat/ai.message.js';
import { createAiCollapsibleMessage } from '@app/chat/ai.collapsible.message.js';
import { createHighlightParser } from '@app/workers/highlight.parser.js';
import { createMarkDownParser, ParserType } from '@app/workers/markdown.parser.js';
import { PipelineBuilder } from '@app/chat/chat.pipeline.builder.js';

/**
 * ChatController — manages chat UI and message lifecycle.
 */
class ChatController {
    constructor() {
        this.container = null;
        this.currentAi = null;
        this.scrollManager = null;
        this.markdownParser = null;
        this.highlightParser = null;
        this._activeTimeouts = [];
        this.onCopyCode = createCallback();
        this.collapseToolCalls = false;
        this.showTokenStats = false;
        this._sessionStartedAt = 0;
        this.pipelineBuilder = null;
        this._pendingFinishPromise = null;
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
        const copyBtn = e.target.closest('.header-copy-btn');
        if (copyBtn) {
            const wrapper = copyBtn.closest('.code-block-container');
            if (!wrapper) return;
            const codeElement = wrapper.querySelector('pre code') || wrapper.querySelector('pre');
            if (!codeElement) return;
            const textToCopy = codeElement.textContent;

            const statusSpan = copyBtn.querySelector('span');
            if (!statusSpan) return;

            this.onCopyCode.emit(textToCopy).then(success => {
                if (success) {
                    statusSpan.textContent = UIText.COPY_SUCCESS;
                    copyBtn.classList.add('success');
                    const timeoutId = setTimeout(() => {
                        statusSpan.textContent = UIText.COPY_LABEL;
                        copyBtn.classList.remove('success');
                        this._activeTimeouts = this._activeTimeouts.filter(id => id !== timeoutId);
                    }, Config.COPY_STATUS_RESET_MS);
                    this._activeTimeouts.push(timeoutId);
                } else {
                    statusSpan.textContent = UIText.COPY_ERROR;
                    const timeoutId = setTimeout(() => {
                        statusSpan.textContent = UIText.COPY_LABEL;
                        this._activeTimeouts = this._activeTimeouts.filter(id => id !== timeoutId);
                    }, Config.COPY_STATUS_RESET_MS);
                    this._activeTimeouts.push(timeoutId);
                }
            }).catch(err => {
                console.error('Copy failed', err);
            });
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
        if (state.status === prev.status &&
            state.accumulatedText === prev.accumulatedText &&
            state.accumulatedThoughtText === prev.accumulatedThoughtText &&
            state.roundNumber === prev.roundNumber) {

            return;
        }

        switch (state.status) {
            case AppStatus.PROCESSING:

                this._pendingFinishPromise = null;

                if (state.roundNumber > 0) {
                    // Tool round iteration — no new user message
                    if (this.collapseToolCalls && this.currentAi?.isCollapsible) {
                        this.currentAi.nextRound(
                            state.roundNumber,
                            state.toolCount
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

                    if (this.collapseToolCalls) {
                        this.currentAi = createAiCollapsibleMessage(
                            this.container,
                            this.highlightParser,
                            this.pipelineBuilder.createStreaming(this.markdownParser),
                            {
                                roundNum: 0,
                                toolCount: 0
                            }
                        );
                    } else {
                        this.currentAi = createAiMessage(
                            this.container,
                            this.highlightParser,
                            this.pipelineBuilder.createStreaming(this.markdownParser),
                            false
                        );
                    }
                }

                this.scrollManager.scrollToBottom(true);
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
                this.currentAi.startTooling(state.toolCallId, state.toolMessage);
                break;

            case AppStatus.RESPONDING:
                this.currentAi.finishTooling(state.toolCallId, state.toolWithError, state.toolMessage);
                this.scrollManager.scrollToBottom();
                break;

            case AppStatus.ERROR:
                if (this.currentAi) {
                    const errorMsg = `${state.error || 'Unknown error'}`;
                    this.currentAi.stopStreaming(errorMsg);
                    this.currentAi.finalize();
                    this.currentAi = null;
                }
                this._pendingFinishPromise = null;
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
                if (this.currentAi && prev.status !== AppStatus.IDLE) {
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
                break;

        }
    }

    _attachEvents() {
        if (this.container) {
            this.container.addEventListener('click', this._onContainerClick);
        }
    }

    _detachEvents() {
        if (this.container) {
            this.container.removeEventListener('click', this._onContainerClick);
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
            state.CollapseToolCalls === prev.CollapseToolCalls &&
            state.ShowTokenStats === prev.ShowTokenStats) {
            return;
        }
        this.collapseToolCalls = state.CollapseToolCalls;
        this.showTokenStats = !!state.ShowTokenStats;
    }

    _showTokenStats(state) {
        if (!this.currentAi || !this.showTokenStats) return;
        if (!state?.tokenUsed) return;

        const elapsedMs = this._sessionStartedAt
            ? Math.max(0, Date.now() - this._sessionStartedAt)
            : 0;

        this.currentAi.showTokenStats({
            tokenUsed: state.tokenUsed,
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

    renderHistory(messages) {
        if (!this.container || !messages || messages.length === 0) return;

        var stepCount = 0;
        var pending = [];

        for (var i = 0; i < messages.length; i++) {
            var msg = messages[i];
            if (!msg || !msg.role) continue;

            if (msg.role === 'user') {
                stepCount = 0;
                createUserMessage(msg.content || '', this.container, this.scrollManager);
            } else if (msg.role === 'assistant') {
                var hasTools = msg.toolCalls && Array.isArray(msg.toolCalls) && msg.toolCalls.length > 0;

                if (hasTools) {
                    stepCount++;
                } else {
                    var content = msg.content || '';
                    if (stepCount > 0) {
                        const stepsText = stepCount + ' step' + (stepCount !== 1 ? 's' : '') + ' taken (history)';
                        content = '`' + stepsText + '`\n\n' + content;
                    }
                    pending.push(this._renderHistoryFinal(content));
                    stepCount = 0;
                }
            }
        }

        var self = this;
        Promise.all(pending).then(function () { self.scrollManager?.scrollToBottom(true); });
    }

    markAsFinalRound() {
        if (this.currentAi?.isCollapsible) {
            this.currentAi.markAsFinalRound();
        }
    }

    _renderHistoryFinal(content) {
        var localAi = createAiMessage(
            this.container,
            this.highlightParser,
            this.pipelineBuilder.createImmediate(this.markdownParser),
            false
        );
        localAi.updateStreaming(content);
        return localAi.finishStreaming();
    }
}

const chatController = new ChatController();
export default chatController;
