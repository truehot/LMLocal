import { formatTokenStats } from '@app/lib/token.stats.js';

/**
 * Factory that creates a collapsible AI message DOM element with multi-step support.
 */
export function createAiCollapsibleMessage(container, highlightWorkerClient, currentPipeline, config) {
    const { roundNum = 0, toolCount = 0 } = config || {};

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

    updateBlockTitle(roundNum, toolCount);

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

    function updateBlockTitle(roundNum, toolCount) {
        const title = collapsibleBlock?.querySelector('.collapsible-title');
        const header = collapsibleBlock?.querySelector('.collapsible-header');
        if (!title || !header) return;

        if (toolCount < 1) {
            header.style.display = 'none';
            collapsibleBlock?.classList.add('expanded', 'no-header');
            collapsibleContent?.classList.add('expanded');
            return;
        }

        header.style.display = '';
        collapsibleBlock?.classList.remove('no-header');
        title.textContent = toolCount > 1
            ? `Step ${roundNum}: Multi-Tool Execution (${toolCount} actions)`
            : `Step ${roundNum}: Tool Execution`;
    }

    function wrapInBlock() {
        if (wrapped) return;
        wrapped = true;
        if (collapsibleBlock) {
            collapsibleBlock.style.display = 'block';
        }
    }

    function resetState() {
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
        },

        updateStreaming: (text) => {
            if (!isStreaming) api.startStreaming(text);
            currentPipeline.write(text);
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

        startTooling: (callId, message) => {
            stopLoadingIndicator();
            wrapInBlock();

            const toolDiv = document.createElement('div');
            toolDiv.className = 'tool-status';
            toolDiv.textContent = message || 'Tooling started.';
            toolDiv.setAttribute('data-tool-call-id', callId);
            elements.toolContainer.appendChild(toolDiv);
        },

        finishTooling: (callId, withError, message) => {
            const toolDiv = elements.toolContainer.querySelector(`[data-tool-call-id="${callId}"]`);
            if (toolDiv) {
                if (withError) {
                    toolDiv.className = 'tool-status-error';
                } else {
                    toolDiv.className = 'tool-status-completed';
                }
                toolDiv.textContent += (message || 'Tooling stopped.');
            }
        },

        stopStreaming: (message) => {
            if (isStreaming) {
                currentPipeline.abort();
                isStreaming = false;
            }
            stopLoadingIndicator();
            stopThoughts();

            if (finalResponseEl) {
                finalResponseEl.style.display = 'block';

                const stopDiv = document.createElement('div');
                stopDiv.className = 'generation-stopped';
                stopDiv.textContent = message || 'Generation stopped';
                finalResponseEl.appendChild(stopDiv);
            }
            element.classList.add('stopped');
        },

        nextRound: (roundNum, toolCount) => {
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

            updateBlockTitle(roundNum, toolCount);

            if (collapsibleBlock) {
                collapsibleBlock.style.display = 'block';
            }
        },

        finalizeResult: async () => {
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
