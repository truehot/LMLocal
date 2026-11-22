import { Icons, UIText } from '@app/constants/app.globals.js';
import { formatMessageTime } from '@app/lib/formatting.js';

/**
 * Per-message actions: copy on every message, plus regenerate (answer) and edit (prompt) on the last exchange.
 */
const copySources = new WeakMap();

/**
 * Selectors of the message action buttons, used by the chat controller's click delegation.
 */
export const COPY_ACTION_SELECTOR = '.message-copy-btn';
export const REGENERATE_ACTION_SELECTOR = '.message-regenerate-btn';
export const EDIT_ACTION_SELECTOR = '.message-edit-btn';
export const MESSAGE_TIME_SELECTOR = '.message-time';

const ACTIONS_CLASS = 'message-actions';

/**
 * Placeholder the log keeps instead of the images of a user message.
 */
const IMAGE_PLACEHOLDER = /^\[[1-9]\d* images? attached - not available in this session\]$/;

/**
 * User messages the backend writes itself.
 */
const SUMMARY_REQUEST = 'Provide a brief summary of our previous session to continue.';
const FORCED_SUMMARY_PLACEHOLDER = 'Summary of the earlier part of this session:';

/**
 * Returns the panel that holds the actions of a message, creating it on demand.
 */
function ensureActions(messageEl) {
    let actions = messageEl.querySelector('.' + ACTIONS_CLASS);
    if (actions) return actions;

    actions = document.createElement('div');
    actions.className = ACTIONS_CLASS;
    messageEl.appendChild(actions);
    return actions;
}

/**
 * Adds one action button to a message, reusing the button that may already be there.
 */
function attachAction(messageEl, className, label, tooltip, icon) {
    const actions = ensureActions(messageEl);
    const selector = '.' + className;

    if (actions.querySelector(selector)) return;

    const button = document.createElement('button');
    button.type = 'button';
    button.className = className;
    button.title = tooltip;
    button.setAttribute('aria-label', label);
    button.innerHTML = icon;

    actions.appendChild(button);
}

/**
 * Attaches the copy button to a message element and remembers the text it copies.
 */
export function attachCopyAction(messageEl, text) {
    if (!messageEl) return;

    const value = typeof text === 'string' ? text : '';
    if (!value) return;

    copySources.set(messageEl, value);

    attachAction(messageEl, 'message-copy-btn', UIText.COPY_LABEL, UIText.COPY_LABEL, Icons.COPY);
}

/**
 * Recomputes the relative label of a time element from the moment it was created.
 */
export function refreshMessageTime(timeEl) {
    if (!timeEl || !timeEl.dataset?.time) return;

    const label = formatMessageTime(timeEl.dataset.time);
    if (label) timeEl.textContent = label;
}

/**
 * Attaches the time of an answer to its action panel: relative to now on the panel, the full date in its tooltip.
 */
export function attachMessageTime(messageEl, date) {
    if (!messageEl || !date) return;

    const value = date instanceof Date ? date : new Date(date);
    if (Number.isNaN(value.getTime())) return;

    const actions = ensureActions(messageEl);

    let time = actions.querySelector(MESSAGE_TIME_SELECTOR);
    if (!time) {
        time = document.createElement('span');
        time.className = 'message-time';
        actions.appendChild(time);
    }

    time.dataset.time = value.toISOString();
    time.title = value.toLocaleString([], { hourCycle: 'h23' });
    refreshMessageTime(time);
}

/**
 * Attaches the regenerate button to an answer and the edit button to its prompt. Either node may be missing.
 */
export function attachExchangeActions(aiEl, userEl) {
    if (aiEl) {
        attachAction(aiEl, 'message-regenerate-btn', UIText.REGENERATE_LABEL, UIText.REGENERATE_TOOLTIP, Icons.REGENERATE);
    }

    if (userEl) {
        attachAction(userEl, 'message-edit-btn', UIText.EDIT_LABEL, UIText.EDIT_TOOLTIP, Icons.EDIT);
    }
}

/**
 * Removes the exchange actions from every message of the container and drops the panels left empty.
 */
export function detachExchangeActions(container) {
    if (!container) return;

    const buttons = container.querySelectorAll(`${REGENERATE_ACTION_SELECTOR}, ${EDIT_ACTION_SELECTOR}`);
    for (const button of buttons) {
        const actions = button.parentElement;
        button.remove();

        if (actions && actions.classList.contains(ACTIONS_CLASS) && actions.children.length === 0) {
            actions.remove();
        }
    }
}

/**
 * Enables or disables the exchange actions of the container.
 */
export function setExchangeActionsDisabled(container, disabled) {
    if (!container) return;

    const idleTooltips = {
        'message-regenerate-btn': UIText.REGENERATE_TOOLTIP,
        'message-edit-btn': UIText.EDIT_TOOLTIP
    };

    const buttons = container.querySelectorAll(`${REGENERATE_ACTION_SELECTOR}, ${EDIT_ACTION_SELECTOR}`);
    for (const button of buttons) {
        button.disabled = !!disabled;
        button.title = disabled ? UIText.EDIT_BUSY_TOOLTIP : idleTooltips[button.className] || button.title;
    }
}

/**
 * Last non-empty line of a text, trimmed.
 */
function lastLine(text) {
    const value = (text || '').replace(/\s+$/, '');
    if (!value) return '';

    const newlineIndex = value.lastIndexOf('\n');
    return (newlineIndex >= 0 ? value.slice(newlineIndex + 1) : value).trim();
}

/**
 * True when the text ends with the image placeholder of a session loaded from the log.
 */
export function isImagePlaceholderText(text) {
    return IMAGE_PLACEHOLDER.test(lastLine(text));
}

/**
 * True when the text is a message the backend wrote itself (summary request, compaction placeholder or the image
 * placeholder of a loaded log).
 */
export function isServicePromptText(text) {
    const value = (text || '').trim();
    if (!value) return false;

    return isImagePlaceholderText(value)
        || value === SUMMARY_REQUEST
        || value === FORCED_SUMMARY_PLACEHOLDER;
}

/**
 * Raw text of a message element: what its copy button copies and what the edit button returns to the input.
 */
export function getMessageSource(messageEl) {
    return messageEl ? copySources.get(messageEl) || '' : '';
}

/**
 * Raw text to copy for the message that owns the given button.
 */
export function getCopySource(button) {
    return getMessageSource(button?.closest('.message'));
}
