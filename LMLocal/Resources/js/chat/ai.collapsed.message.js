import { createAiCollapsibleMessage } from '@app/chat/ai.collapsible.message.js';

/**
 * Factory for the `collapsed` display mode: the same multi-step block as the collapsible message, but the group is
 * closed from the start and the code never opens it.
 */
export function createAiCollapsedMessage(container, highlightWorkerClient, currentPipeline, config) {
    return createAiCollapsibleMessage(container, highlightWorkerClient, currentPipeline, {
        ...config,
        behavior: {
            keepClosed: true,
            onStopped: (api) => showStoppedTitle(api.root)
        }
    });
}

/**
 * Titles the group after a Stop, an error or going offline: without a final answer the title would still say
 * "In Progress..." or the last step number.
 */
function showStoppedTitle(root) {
    const block = root?.querySelector('[data-element="collapsible-block"]');
    const header = block?.querySelector('.collapsible-header');
    const title = block?.querySelector('.collapsible-title');

    if (!block || !header || !title) return;

    if (block.style.display === 'none') return;

    const steps = Array.from(root.querySelectorAll('.step'));
    const hasTools = steps.some(step => step.querySelector('[data-element="ai-tool-container"] > div'));

    block.classList.remove('no-header');
    header.style.display = '';

    title.textContent = steps.length > 1 || hasTools
        ? `Stopped after ${steps.length} step${steps.length === 1 ? '' : 's'}`
        : 'Stopped';
}
