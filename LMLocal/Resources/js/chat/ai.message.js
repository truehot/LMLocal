/**
 * Factory that creates an message DOM element, caches its internal blocks,
 * and returns an API to manipulate the message.
 */
export function createAiMessage(container, highlightWorkerClient, streamingPipeline, iterating = false) {

    const html = `<div>
        <div class="loading-indicator" data-element="loading-indicator"><div class="dot"></div><div class="dot"></div><div class="dot"></div></div>
        <div class="thought-container" style="display: none;" data-element="thought-container">
            <div class="reasoning-header">
                <div class="reasoning-title">Thoughts
                    <div class="loading-indicator" data-element="thought-loader">
                        <div class="dot"></div><div class="dot"></div><div class="dot"></div>
                    </div>
                </div>
                <button class="toggle-thought-btn"></button>
            </div>
            <div class="thought-content" data-element="thought-content"></div>
        </div>
        <div data-element="ai-tool-container"></div>
        <div class="ai-response-container" style="display: none;" data-element="response-container"></div>
        </div>
    `;

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
        thoughtLoader: element.querySelector('[data-element="thought-loader"]')
    };

    let isStreaming = false;

    const api = {
        stopLoadingIndicator: () => {
            if (elements.loadingIndicator) elements.loadingIndicator.remove();
            elements.loadingIndicator = null;
        },

        updateThought: (text) => {
            api.stopLoadingIndicator();
            if (elements.thoughtContent) elements.thoughtContent.textContent = text;
            if (elements.thoughtContainer) {
                elements.thoughtContainer.style.display = 'block';
                elements.thoughtContainer.classList.add('is-thinking');
            }
        },

        stopThoughts: () => {
            if (elements.thoughtContainer) elements.thoughtContainer.classList.remove('is-thinking');
            if (elements.thoughtLoader) elements.thoughtLoader.style.display = 'none';
        },

        startStreaming: (text) => {
            api.stopLoadingIndicator();
            if (elements.responseContainer) {
                elements.responseContainer.classList.add('is-generating');
                elements.responseContainer.style.display = 'block';
                if (!isStreaming) {
                    streamingPipeline.attach(elements.responseContainer);
                }
            }
            isStreaming = true;
        },

        updateStreaming: (text) => {
            if (!isStreaming) api.startStreaming(text);
            streamingPipeline.write(text);

        },

        finishStreaming: () => {
            const responseContainer = elements?.responseContainer;
            return new Promise((resolve) => {
                streamingPipeline.onEnd(async () => {
                    element?.classList.add('completed');
                    try {
                        await highlightWorkerClient.highlightContainer(responseContainer);
                    } catch (err) {
                        console.error('Highlighting failed', err);
                    }

                    if (responseContainer?.isConnected) {
                        elements.responseContainer.classList.remove('is-generating');
                    }

                    resolve();
                });

                const wasActive = streamingPipeline.end();
                if (!wasActive) {
                    resolve();
                }
            });
        },

        startTooling: (callId, message) => {
            api.stopLoadingIndicator();

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
                streamingPipeline.abort();
                isStreaming = false;
            }
            api.stopLoadingIndicator();
            api.stopThoughts();
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
            if (isStreaming) {
                isStreaming = false;
                streamingPipeline?.abort();
                streamingPipeline = null;
                container = null;
                highlightWorkerClient = null;
                element = null;
            }
        },
        clear: () => {
            if (element) {
                element.replaceChildren();
                element.remove();
                element = null;
            }
            api.finalize();
        }
    };

    return api;
}
