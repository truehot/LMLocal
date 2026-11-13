import { Icons, UIText } from '@app/constants/app.globals.js';

/**
 * Per-message copy action.
 *
 * The raw markdown of a message is kept next to its DOM node, so the controller never has to
 * rebuild it from the rendered markup (markdown syntax is already lost there) and never has to
 * re-read it from the backend.
 */
const copySources = new WeakMap();

/**
 * Selector of the copy button, used by the chat controller's click delegation.
 */
export const COPY_ACTION_SELECTOR = '.message-copy-btn';

/**
 * Attaches the copy button to a message element and remembers the text it copies.
 */
export function attachCopyAction(messageEl, text) {
    if (!messageEl) return;

    const value = typeof text === 'string' ? text : '';
    const existingActions = messageEl.querySelector('.message-actions');

    if (!value) {
        return;
    }

    copySources.set(messageEl, value);

    if (existingActions) return;

    const actions = document.createElement('div');
    actions.className = 'message-actions';

    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'message-copy-btn';
    button.title = UIText.COPY_LABEL;
    button.setAttribute('aria-label', UIText.COPY_LABEL);
    button.innerHTML = Icons.COPY;

    actions.appendChild(button);
    messageEl.appendChild(actions);
}

/**
 * Raw text to copy for the message that owns the given button.
 */
export function getCopySource(button) {
    const messageEl = button?.closest('.message');
    return messageEl ? copySources.get(messageEl) || '' : '';
}
