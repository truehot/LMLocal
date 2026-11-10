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

/**
 * Closes every tool that is still running.
 */
export function stopRunningTools(toolContainer, message) {
    if (!toolContainer) return;

    toolContainer.querySelectorAll('.tool-status').forEach(toolDiv => {
        toolDiv.className = 'tool-status-stopped';
        const stepSpan = toolDiv._stepSpan;
        if (stepSpan) {
            stepSpan.textContent = ' ' + (message || 'Stopped.');
            stepSpan.style.opacity = '1';
        }
    });

    toolContainer.querySelectorAll('.tool-group-status:not(.tool-group-status-completed)').forEach(groupDiv => {
        groupDiv.classList.add('tool-group-status-stopped');
        const header = groupDiv.querySelector('.tool-group-status-message');
        if (header && !header.textContent.endsWith('(stopped)')) {
            header.textContent += ' (stopped)';
        }
    });
}

/**
 * Renders/updates the "N/M done" header for a parallel SubAgent fan-out group.
 */
export function updateToolGroup(toolContainer, groupId, completed, total) {
    if (!toolContainer || !groupId) return;

    let groupDiv = toolContainer.querySelector(`[data-tool-group-id="${groupId}"]`);
    if (!groupDiv) {
        groupDiv = document.createElement('div');
        groupDiv.className = 'tool-group-status';
        groupDiv.setAttribute('data-tool-group-id', groupId);

        const header = document.createElement('span');
        header.className = 'tool-group-status-message';
        groupDiv.appendChild(header);

        toolContainer.insertBefore(groupDiv, toolContainer.firstChild);
    }

    const totalCount = typeof total === 'number' ? total : 0;
    const doneCount = typeof completed === 'number' ? completed : 0;
    const isComplete = totalCount > 0 && doneCount >= totalCount;

    const header = groupDiv.querySelector('.tool-group-status-message');
    if (header) {
        header.textContent = totalCount > 0
            ? `Parallel agents: ${Math.min(doneCount, totalCount)}/${totalCount} done`
            : 'Parallel agents';
    }

    groupDiv.classList.toggle('tool-group-status-completed', isComplete);
}
