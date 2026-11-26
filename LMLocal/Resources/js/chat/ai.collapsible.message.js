import { formatTokenStats } from '@app/lib/token.stats.js';
import { startTooling as startToolStatus, stepTooling as stepToolStatus, finishTooling as finishToolStatus, updateToolGroup as updateToolGroupStatus, stopRunningTools } from '@app/chat/tool.status.js';
import { describeToolCall, stepActivity, formatAgentNames } from '@app/lib/tool.activity.js';
import { createGenerationStopped } from '@app/chat/generation.stopped.js';

/** How long one activity stays in the header: a single tool lasts milliseconds and several run per round. */
const HEADER_LABEL_MIN_MS = 1500;

/** Time without a running tool before the header says that the model is thinking. */
const HEADER_THINKING_DELAY_MS = 1000;

const THINKING_LABEL = 'Thinking…';

/**
 * Factory that creates a collapsible AI message DOM element with multi-step support.
 */
export function createAiCollapsibleMessage(container, highlightWorkerClient, currentPipeline, config) {
    const { roundNum = 0, behavior } = config || {};
    const keepClosed = behavior?.keepClosed === true;
    const onStopped = behavior?.onStopped;

    const thoughtHtml = `<div class="thought-container" style="display: none;" data-element="thought-container">
        <div class="reasoning-header">
            <div class="reasoning-title">Thoughts
                <div class="loading-indicator" data-element="thought-loader">
                    <div class="dot"></div><div class="dot"></div><div class="dot"></div>
                </div>
            </div>
            <button class="toggle-thought-btn"></button>
        </div>
        <div class="thought-content" data-element="thought-content"></div>
    </div>`;
    const toolHtml = `<div class="ai-tool-container" data-element="ai-tool-container"></div>`;
    const respHtml = `<div class="ai-response-container" style="display: none;" data-element="response-container"></div>`;

    const stepHtml = `<div class="step" data-step="${roundNum}">
        ${thoughtHtml}
        ${respHtml}
        ${toolHtml}
    </div>`;

    const html = `<div>
        <div class="loading-indicator" data-element="loading-indicator"><div class="dot"></div><div class="dot"></div><div class="dot"></div></div>
        <div class="collapsible-block" data-element="collapsible-block" style="display: none;">
            <div class="collapsible-header">
                <div class="collapsible-title">In Progress...</div>
                <button class="toggle-collapsible-btn"></button>
            </div>
            <div class="collapsible-content" data-element="collapsible-content">
                ${stepHtml}
            </div>
        </div>
        <div class="ai-final-response" data-element="final-response" style="display: none;"></div>
        </div>`;

    let element = document.createElement('div');
    element.className = 'message ai-message';
    element.innerHTML = html;
    container.appendChild(element);

    let wrapped = false;
    let isStreaming = false;
    let _isFinalRound = false;

    let currentStepElement = element.querySelector('.step');
    let collapsibleBlock = element.querySelector('[data-element="collapsible-block"]');
    let collapsibleContent = element.querySelector('[data-element="collapsible-content"]');
    let finalResponseEl = element.querySelector('[data-element="final-response"]');
    let finishPromise = null;

    let headerLabel = null;
    let headerLabelShownAt = 0;
    let headerStep = roundNum + 1;
    let nextHeaderStep = headerStep;
    let headerHoldTimer = null;
    let headerThinkingTimer = null;
    let headerThinking = false;
    let headerDone = false;
    const runningTools = new Map();
    const runningAgents = new Map();

    renderHeader();

    let elements = {
        loadingIndicator: element.querySelector('[data-element="loading-indicator"]'),
        thoughtContainer: currentStepElement.querySelector('[data-element="thought-container"]'),
        thoughtContent: currentStepElement.querySelector('[data-element="thought-content"]'),
        responseContainer: currentStepElement.querySelector('[data-element="response-container"]'),
        toolContainer: currentStepElement.querySelector('[data-element="ai-tool-container"]'),
        thoughtLoader: currentStepElement.querySelector('[data-element="thought-loader"]'),
    };

    function stopLoadingIndicator() {
        if (elements.loadingIndicator) {
            elements.loadingIndicator.remove();
            elements.loadingIndicator = null;
        }
    }

    function stopThoughts() {
        if (elements.thoughtContainer) elements.thoughtContainer.classList.remove('is-thinking');
        if (elements.thoughtLoader) elements.thoughtLoader.style.display = 'none';
    }

    function setTitleText(title, text) {
        if (title.textContent !== text) title.textContent = text;
    }

    /** Header of the group: "In Progress..." (hidden in expanded mode) until the first tool, then "Step N: <activity>". */
    function renderHeader() {
        if (headerDone) return;

        const title = collapsibleBlock?.querySelector('.collapsible-title');
        const header = collapsibleBlock?.querySelector('.collapsible-header');
        if (!title || !header) return;

        if (headerLabel === null) {
            if (keepClosed) {
                header.style.display = '';
                collapsibleBlock?.classList.remove('no-header');
                setTitleText(title, 'In Progress...');
                return;
            }

            header.style.display = 'none';
            collapsibleBlock?.classList.add('expanded', 'no-header');
            collapsibleContent?.classList.add('expanded');
            return;
        }

        header.style.display = '';
        collapsibleBlock?.classList.remove('no-header');
        setTitleText(title, `Step ${headerStep}: ${headerLabel}`);
    }

    /** Activity to show now: running SubAgents, else the last started tool, else "Thinking…"; null keeps the shown one. */
    function desiredHeaderLabel() {
        if (runningAgents.size > 0) {
            return formatAgentNames(Array.from(runningAgents.values()));
        }

        if (runningTools.size > 0) {
            const labels = Array.from(runningTools.values());
            return labels[labels.length - 1];
        }

        return headerThinking ? THINKING_LABEL : null;
    }

    /** Shows a label with the step it belongs to: the number of a new round waits for that round's first label. */
    function applyHeaderLabel(label) {
        if (label === headerLabel && nextHeaderStep === headerStep) return;
        headerLabel = label;
        headerStep = nextHeaderStep;
        headerLabelShownAt = Date.now();
        renderHeader();
    }

    function clearHeaderHoldTimer() {
        if (!headerHoldTimer) return;
        clearTimeout(headerHoldTimer);
        headerHoldTimer = null;
    }

    function clearThinkingTimer() {
        if (!headerThinkingTimer) return;
        clearTimeout(headerThinkingTimer);
        headerThinkingTimer = null;
    }

    /** Switches to the current activity once the shown one had HEADER_LABEL_MIN_MS; re-derived when the timer fires. */
    function refreshHeaderLabel() {
        if (headerDone) return;

        const desired = desiredHeaderLabel();
        if (!desired || (desired === headerLabel && nextHeaderStep === headerStep)) {
            clearHeaderHoldTimer();
            return;
        }

        const elapsed = headerLabel === null ? Number.POSITIVE_INFINITY : Date.now() - headerLabelShownAt;
        const remaining = HEADER_LABEL_MIN_MS - elapsed;

        if (remaining <= 0) {
            clearHeaderHoldTimer();
            applyHeaderLabel(desired);
            return;
        }

        if (headerHoldTimer) return;

        headerHoldTimer = setTimeout(() => {
            headerHoldTimer = null;
            refreshHeaderLabel();
        }, remaining);
    }

    /** "Thinking…" between tools only after a pause, so a fast tool → model → tool cycle does not blink through it. */
    function scheduleThinkingLabel() {
        if (headerDone || headerLabel === null) return;
        if (runningTools.size > 0 || runningAgents.size > 0) return;
        if (headerThinking || headerThinkingTimer) return;

        headerThinkingTimer = setTimeout(() => {
            headerThinkingTimer = null;
            if (runningTools.size > 0 || runningAgents.size > 0) return;
            headerThinking = true;
            refreshHeaderLabel();
        }, HEADER_THINKING_DELAY_MS);
    }

    /** Cancels every pending header update: a late label must not replace "Completed"/"Stopped". */
    function stopHeaderActivity() {
        clearHeaderHoldTimer();
        clearThinkingTimer();
        headerThinking = false;
        runningTools.clear();
        runningAgents.clear();
    }

    /** Records a started call: a SubAgent keeps its name for its whole run, a built-in tool shows its category. */
    function trackRunningCall(callId, message, toolName) {
        const activity = describeToolCall(toolName, message);

        clearThinkingTimer();
        headerThinking = false;

        if (activity.kind === 'agent') {
            runningAgents.set(callId, activity.label);
        } else {
            runningTools.set(callId, activity.label);
        }

        refreshHeaderLabel();
    }

    function untrackRunningCall(callId) {
        runningTools.delete(callId);
        runningAgents.delete(callId);

        if (runningTools.size > 0 || runningAgents.size > 0) {
            refreshHeaderLabel();
        } else {
            scheduleThinkingLabel();
        }
    }

    function wrapInBlock() {
        if (wrapped) return;
        wrapped = true;
        if (collapsibleBlock) {
            collapsibleBlock.style.display = 'block';
        }
    }

    function resetState() {
        stopHeaderActivity();
        elements = null;
        collapsibleBlock = null;
        collapsibleContent = null;
        currentStepElement = null;
        finalResponseEl = null;
        finishPromise = null;
        wrapped = false;
        _isFinalRound = false;
        isStreaming = false;
    }

    const api = {
        isCollapsible: true,
        root: element,

        markAsFinalRound: () => {
            _isFinalRound = true;
        },

        stopLoadingIndicator: () => {
            stopLoadingIndicator();
        },

        updateThought: (text) => {
            stopLoadingIndicator();
            wrapInBlock();
            if (elements.thoughtContent) elements.thoughtContent.textContent = text;
            if (elements.thoughtContainer) {
                elements.thoughtContainer.style.display = 'block';
                elements.thoughtContainer.classList.add('is-thinking');
            }
            scheduleThinkingLabel();
        },

        stopThoughts: () => {
            stopThoughts();
        },

        startStreaming: (text) => {
            stopLoadingIndicator();
            wrapInBlock();
            if (elements.responseContainer) {
                elements.responseContainer.classList.add('is-generating');
                elements.responseContainer.style.display = 'block';
                if (!isStreaming) {
                    currentPipeline.attach(elements.responseContainer);
                }
            }
            isStreaming = true;
            scheduleThinkingLabel();
        },

        updateStreaming: (text) => {
            if (!isStreaming) api.startStreaming(text);
            currentPipeline.write(text);
            scheduleThinkingLabel();
        },

        finishStreaming: () => {
            const responseContainer = elements?.responseContainer;
            const pipeline = currentPipeline;
            finishPromise = new Promise((resolve) => {
                pipeline.onEnd(async () => {
                    element?.classList.add('completed');
                    try {
                        await highlightWorkerClient.highlightContainer(responseContainer);
                    } catch (err) {
                        console.error('Highlighting failed', err);
                    }

                    if (responseContainer?.isConnected) {
                        responseContainer.classList.remove('is-generating');
                    }

                    resolve();
                });

                const wasActive = pipeline.end();
                if (!wasActive) {
                    resolve();
                }
            });
            return finishPromise;
        },

        startTooling: (callId, message, toolName) => {
            stopLoadingIndicator();
            wrapInBlock();
            startToolStatus(elements.toolContainer, callId, message);
            trackRunningCall(callId, message, toolName);
        },

        stepTooling: (callId, step, message, toolName) => {
            stepToolStatus(elements.toolContainer, callId, step, message, {
                category: stepActivity(toolName, message)
            });
        },

        finishTooling: (callId, withError, message) => {
            finishToolStatus(elements.toolContainer, callId, withError, message);
            untrackRunningCall(callId);
        },

        updateToolGroup: (groupId, completed, total) => {
            updateToolGroupStatus(elements.toolContainer, groupId, completed, total);
        },

        stopStreaming: (message, options = {}) => {
            if (isStreaming) {
                currentPipeline.abort();
                isStreaming = false;
            }
            stopLoadingIndicator();
            stopThoughts();
            stopHeaderActivity();
            headerDone = true;
            stopRunningTools(elements.toolContainer);

            if (finalResponseEl) {
                finalResponseEl.style.display = 'block';
                finalResponseEl.appendChild(createGenerationStopped(message, options));
            }
            element.classList.add('stopped');

            if (onStopped) onStopped(api);
        },

        nextRound: (roundNum) => {
            stopLoadingIndicator();
            stopThoughts();
            if (isStreaming) {
                isStreaming = false;
                currentPipeline?.abort();
            }

            currentPipeline?.reset();

            const stepDiv = document.createElement('div');
            stepDiv.className = 'step';
            stepDiv.setAttribute('data-step', String(roundNum));
            stepDiv.innerHTML = thoughtHtml + respHtml + toolHtml;
            collapsibleContent.appendChild(stepDiv);

            currentStepElement = stepDiv;
            elements.thoughtContainer = stepDiv.querySelector('[data-element="thought-container"]');
            elements.thoughtContent = stepDiv.querySelector('[data-element="thought-content"]');
            elements.responseContainer = stepDiv.querySelector('[data-element="response-container"]');
            elements.toolContainer = stepDiv.querySelector('[data-element="ai-tool-container"]');
            elements.thoughtLoader = stepDiv.querySelector('[data-element="thought-loader"]');

            nextHeaderStep = roundNum + 1;
            if (headerLabel === THINKING_LABEL) {
                headerStep = nextHeaderStep;
                renderHeader();
            }
            scheduleThinkingLabel();

            if (collapsibleBlock) {
                collapsibleBlock.style.display = 'block';
            }
        },

        finalizeResult: async () => {
            stopHeaderActivity();
            headerDone = true;

            if (finishPromise) {
                try { await finishPromise; } catch (e) { console.error('finalizeResult:', e); }
            }

            const steps = element?.querySelectorAll('.step');
            const lastStep = steps?.[steps.length - 1];

            if (_isFinalRound && lastStep && finalResponseEl) {

                const lastResponse = lastStep.querySelector('[data-element="response-container"]');
                if (lastResponse) {
                    while (lastResponse.firstChild) {
                        finalResponseEl.appendChild(lastResponse.firstChild);
                    }
                    finalResponseEl.style.display = 'block';
                    lastResponse.style.display = 'none';
                }

                if (steps.length <= 1) {
                    const hasThought = lastStep
                        ?.querySelector('[data-element="thought-content"]')
                        ?.textContent.trim();

                    if (hasThought) {
                        if (collapsibleBlock) {
                            collapsibleBlock.classList.remove('expanded', 'no-header');
                        }
                        collapsibleContent?.classList.remove('expanded');
                        const title = collapsibleBlock?.querySelector('.collapsible-title');
                        const header = collapsibleBlock?.querySelector('.collapsible-header');
                        if (title && header) {
                            header.style.display = '';
                            title.textContent = 'Completed: 1 step';
                        }
                    } else if (collapsibleBlock) {
                        collapsibleBlock.style.display = 'none';
                    }
                } else {

                    const title = collapsibleBlock?.querySelector('.collapsible-title');
                    const header = collapsibleBlock?.querySelector('.collapsible-header');
                    if (title && header) {
                        const stepCount = steps.length;
                        header.style.display = '';
                        title.textContent = `Completed: ${stepCount} step${stepCount !== 1 ? 's' : ''}`;
                        collapsibleBlock?.classList.remove('no-header');
                    }
                    if (collapsibleBlock) {
                        collapsibleBlock.classList.remove('expanded');
                        collapsibleContent?.classList.remove('expanded');
                    }
                }
            } else if (lastStep && finalResponseEl) {
                const lastResponse = lastStep.querySelector('[data-element="response-container"]');
                if (lastResponse) {
                    finalResponseEl.innerHTML = lastResponse.innerHTML;
                    finalResponseEl.style.display = 'block';
                }
            }

            if (!_isFinalRound) {
                const title = collapsibleBlock?.querySelector('.collapsible-title');
                const header = collapsibleBlock?.querySelector('.collapsible-header');
                if (title && header) {
                    const stepCount = steps?.length || 0;
                    header.style.display = '';
                    title.textContent = `Completed: ${stepCount} step${stepCount !== 1 ? 's' : ''}`;
                    collapsibleBlock?.classList.remove('no-header');
                }

                if (collapsibleBlock) {
                    collapsibleBlock.classList.remove('expanded');
                    collapsibleContent?.classList.remove('expanded');
                }
            }

            _isFinalRound = false;
        },

        showTokenStats: (stats) => {
            if (!finalResponseEl) return;
            if (finalResponseEl.querySelector('.token-stats')) return;
            const div = document.createElement('div');
            div.className = 'token-stats';
            div.innerHTML = formatTokenStats(stats);
            finalResponseEl.appendChild(div);
        },

        finalize: () => {
            stopLoadingIndicator();
            stopThoughts();
            stopHeaderActivity();
            if (isStreaming) {
                isStreaming = false;
                currentPipeline?.abort();
            }
        },

        clear: () => {
            stopLoadingIndicator();
            stopThoughts();
            if (isStreaming) {
                isStreaming = false;
                currentPipeline?.abort();
            }
            if (element) {
                element.replaceChildren();
                element.remove();
                element = null;
            }
            resetState();
            currentPipeline = null;
            container = null;
            highlightWorkerClient = null;
        },
    };

    return api;
}
