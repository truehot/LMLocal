import { Config } from '@app/constants/app.globals.js';

/**
 * Create and append a user message element into the provided container.
 **/

export function createUserMessage(text, container, scrollManager) {
    const div = document.createElement('div');
    div.className = 'message user-message expandable';
    const content = document.createElement('div');
    content.className = 'message-content';
    content.textContent = text;
    div.appendChild(content);

    if (text.length > Config.USER_MESSAGE_COLLAPSE_CHAR_LIMIT || text.split('\n').length > Config.USER_MESSAGE_COLLAPSE_LINES_LIMIT) {
        const btn = document.createElement('button');
        btn.className = 'show-more-btn';
        div.appendChild(btn);
    }

    container.appendChild(div);
}
