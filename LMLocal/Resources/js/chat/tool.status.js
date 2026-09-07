/**
 * Shared tool-status DOM helpers for AI message views.
 */
export function startTooling(toolContainer, callId, message) {
    const toolDiv = document.createElement('div');
    toolDiv.className = 'tool-status';
    toolDiv.setAttribute('data-tool-call-id', callId);

    const header = document.createElement('span');
    header.className = 'tool-status-message';
    header.textContent = message || 'Tooling started.';
    toolDiv.appendChild(header);

    const stepSpan = document.createElement('span');
    stepSpan.className = 'tool-status-step';
    stepSpan.style.opacity = '0.85';
    stepSpan.textContent = '';
    header.appendChild(stepSpan);

    toolDiv._stepSpan = stepSpan;
    toolDiv.setAttribute('data-step', '');
    toolDiv.setAttribute('data-message', '');
    toolContainer.appendChild(toolDiv);
}

export function stepTooling(toolContainer, callId, step, message) {
    if (!callId || !toolContainer) return;
    const toolDiv = toolContainer.querySelector(`[data-tool-call-id="${callId}"]`);
    if (!toolDiv) return;
    if (toolDiv.classList.contains('tool-status-error') || toolDiv.classList.contains('tool-status-completed')) {
        return;
    }

    const stepSpan = toolDiv._stepSpan;
    if (!stepSpan) return;

    const text = message || '';

    const hasStep = typeof step === 'number' && !Number.isNaN(step);
    const sameStep = hasStep && toolDiv.getAttribute('data-step') === String(step);

    if (sameStep) {
        if (toolDiv.getAttribute('data-message') === text) return;
        stepSpan.textContent += ` + ${text}`;
    } else {
        stepSpan.textContent = ' ' + (hasStep ? `Step ${step}: ` : '') + text;
    }

    if (hasStep) toolDiv.setAttribute('data-step', String(step));
    toolDiv.setAttribute('data-message', text);
}

export function finishTooling(toolContainer, callId, withError, message) {
    if (!toolContainer) return;
    const toolDiv = toolContainer.querySelector(`[data-tool-call-id="${callId}"]`);
    if (!toolDiv) return;
    toolDiv.className = withError ? 'tool-status-error' : 'tool-status-completed';

    const stepSpan = toolDiv._stepSpan;
    if (stepSpan) {
        stepSpan.textContent = ' ' + (message || 'Tooling stopped.');
        stepSpan.style.opacity = '1';
    }
}
