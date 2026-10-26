import { UIText } from '@app/constants/app.globals.js';

/**
 * Builds the "generation stopped" badge shared by the AI message factories.
 */
export function createGenerationStopped(message, options = {}) {
    const { canContinue = false } = options || {};

    const stopDiv = document.createElement('div');
    stopDiv.className = 'generation-stopped';

    const textSpan = document.createElement('span');
    textSpan.className = 'generation-stopped-text';
    textSpan.textContent = message || UIText.TEXT_GENERATION_STOPPED;
    stopDiv.appendChild(textSpan);

    if (canContinue) {
        const continueBtn = document.createElement('button');
        continueBtn.type = 'button';
        continueBtn.className = 'continue-btn';
        continueBtn.textContent = UIText.BUTTON_CONTINUE;
        stopDiv.appendChild(continueBtn);
    }

    return stopDiv;
}
