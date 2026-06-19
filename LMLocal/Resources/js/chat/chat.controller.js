import { UIText, Config } from '@app/constants/app.globals.js';
import { AppStatus } from '@app/store/app.status.js';
import { createCallback } from '@app/lib/callback.js';
import { createScrollManager } from '@app/lib/scroll.manager.js';
import { createUserMessage } from '@app/chat/user.message.js';
import { createAiMessage } from '@app/chat/ai.message.js';

import { createHighlightParser } from '@app/workers/highlight.parser.js';
import { createMarkDownParser, ParserType } from '@app/workers/markdown.parser.js';

import { StreamingBuffer } from '@app/streaming/streaming.buffer.js';
import { createStreamingPipeline } from '@app/streaming/streaming.pipeline.js';
import { createStreamingRenderer, StreamingMode } from '@app/streaming/streaming.renderer.js';
import { createStreamingScheduler } from '@app/streaming/streaming.scheduler.js';
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
        this.activeTimeouts = [];
        this.onCopyCode = createCallback();
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

    // Event delegation for copy buttons
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
                        this.activeTimeouts = this.activeTimeouts.filter(id => id !== timeoutId);
                    }, Config.COPY_STATUS_RESET_MS);
                    this.activeTimeouts.push(timeoutId);
                } else {
                    statusSpan.textContent = UIText.COPY_ERROR;
                    const timeoutId = setTimeout(() => {
                        statusSpan.textContent = UIText.COPY_LABEL;
                        this.activeTimeouts = this.activeTimeouts.filter(id => id !== timeoutId);
                    }, Config.COPY_STATUS_RESET_MS);
                    this.activeTimeouts.push(timeoutId);
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
    };

    _renderMessageFlow(state, prev = {}) {
        if (state.status === prev.status &&
            state.accumulatedText === prev.accumulatedText &&
            state.accumulatedThoughtText === prev.accumulatedThoughtText) {
            return;
        }

        switch (state.status) {
            case AppStatus.PROCESSING:
                if (state.userMessage) {
                    this._enforceMessageLimit();
                    createUserMessage(state.userMessage, this.container, this.scrollManager);
                    if (this.currentAi) {
                        this.currentAi.finalize();
                    }

                    this.currentAi = createAiMessage(
                        this.container,
                        this.highlightParser,
                        this._createPipeline(this.markdownParser)
                    );
                } else {

                    //iterating
                    if (this.currentAi) {
                        this.currentAi.finalize();
                    }

                    this.currentAi = createAiMessage(
                        this.container,
                        this.highlightParser,
                        this._createPipeline(this.markdownParser),
                        true
                    );
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
                    this.currentAi.finishStreaming().then(async () => {
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
                break;

            case AppStatus.OFFLINE:
                if (this.currentAi) {
                    this.currentAi.stopStreaming("You are offline");
                    this.currentAi.finalize();
                    this.currentAi = null;
                }
                break;

            case AppStatus.CLEARING:
                if (this.currentAi) {
                    this.currentAi.clear();
                    this.currentAi = null;
                }
                this.reset();
                this.container?.replaceChildren();
                this.setup(); // Re-setup to reinitialize everything after clearing.

                break;

            case AppStatus.IDLE:
                if (this.currentAi && prev.status !== AppStatus.IDLE) {
                    this.currentAi.stopLoadingIndicator();
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

    _clearTimeouts() {
        for (const timeoutId of this.activeTimeouts) {
            clearTimeout(timeoutId);
        }
        this.activeTimeouts = [];
    }

    _createPipeline(markdownParser) {
        let streamingRenderer = createStreamingRenderer({
            mode: StreamingMode.BLOCK_TAIL,
            onUpdate: () => this.scrollManager.scrollToBottom(),
        });

        let streamBuffer = new StreamingBuffer(2);

        let scheduler = createStreamingScheduler(streamBuffer, {
            baseIntervalMs: 60,
            minIntervalMs: 30,
            maxIntervalMs: 300,
            targetQueueLength: 2
        });

        let streamingPipeline = createStreamingPipeline(
            streamBuffer,
            streamingRenderer,
            markdownParser,
            scheduler
        );
        return streamingPipeline;
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



        this._attachEvents();
        return this;
    }

    updateAppState(state, prev) {
        this._renderMessageFlow(state, prev);
    }

    reset() {
        this._clearTimeouts();
        this._detachEvents();

        this.scrollManager?.reset();

        this.currentAi?.clear();
        this.currentAi = null;
    }
}

const chatController = new ChatController();
export default chatController;