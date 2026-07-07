/**
 * Factory that creates a message DOM element, caches its internal blocks and returns an API to manipulate the message.
 */
export function createAiMessage(container, highlightWorkerClient, currentPipeline, iterating = false) {

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

    const html = `<div>
        <div class="loading-indicator" data-element="loading-indicator"><div class="dot"></div><div class="dot"></div><div class="dot"></div></div>
        ${thoughtHtml}
        ${respHtml}
        ${toolHtml}
        </div>`;

    let element;
    if (iterating) {
        element = container.lastElementChild;
        element.insertAdjacentHTML('beforeend', html);
        element = element.lastElementChild;
    } else {
        element = document.createElement('div');
        element.className = 'message ai-message';
        element.innerHTML = html;
        container.appendChild(element);
    }

    let elements = {
        loadingIndicator: element.querySelector('[data-element="loading-indicator"]'),
        thoughtContainer: element.querySelector('[data-element="thought-container"]'),
        thoughtContent: element.querySelector('[data-element="thought-content"]'),
        responseContainer: element.querySelector('[data-element="response-container"]'),
        toolContainer: element.querySelector('[data-element="ai-tool-container"]'),
        thoughtLoader: element.querySelector('[data-element="thought-loader"]'),
    };

    let isStreaming = false;

    function stopLoadingIndicator() {
        if (elements.loadingIndicator) elements.loadingIndicator.remove();
        elements.loadingIndicator = null;
    }

    function stopThoughts() {
        if (elements.thoughtContainer) elements.thoughtContainer.classList.remove('is-thinking');
        if (elements.thoughtLoader) elements.thoughtLoader.style.display = 'none';
    }

    function resetState() {
        elements = null;
        isStreaming = false;
    }

    const api = {
        isCollapsible: false,

        stopLoadingIndicator: () => {
            stopLoadingIndicator();
        },

        updateThought: (text) => {
            stopLoadingIndicator();
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
            return new Promise((resolve) => {
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
        },

        startTooling: (callId, message) => {
            stopLoadingIndicator();

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
            if (elements.responseContainer) {
                elements.responseContainer.classList.remove('is-generating');
                elements.responseContainer.style.display = 'block';

                const stopDiv = document.createElement('div');
                stopDiv.className = 'generation-stopped';
                stopDiv.textContent = message || 'Generation stopped';
                elements.responseContainer.appendChild(stopDiv);
            }
            element.classList.add('stopped');
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
