import { Config } from '@app/constants/app.globals.js';
import { openImageInNewTab } from '@app/lib/image.utils.js';

/**
 * Create and append a user message element into the provided container.
 * Supports:
 *  - plain text content (includes the "[images: N]" placeholder from loaded history)
 *  - multimodal content arrays [{type:'text'|'image_url', ...}] (defensive)
 *  - liveDataUrls: pasted images of the current message, rendered before persistence
 **/

export function createUserMessage(content, container, scrollManager, liveDataUrls = null) {
    const div = document.createElement('div');
    div.className = 'message user-message expandable';
    const contentDiv = document.createElement('div');
    contentDiv.className = 'message-content';

    const textParts = [];
    const imageUrls = [];

    if (liveDataUrls && liveDataUrls.length > 0) {
        // Live render: images come as data URLs, text as a string / text parts
        if (typeof content === 'string' && content) {
            textParts.push(content);
        } else if (Array.isArray(content)) {
            for (const part of content) {
                if (part && part.type === 'text' && part.text) textParts.push(part.text);
            }
        }
        for (const dataUrl of liveDataUrls) imageUrls.push(dataUrl);
    } else if (Array.isArray(content)) {
        // Multimodal content from history
        for (const part of content) {
            if (!part) continue;
            if (part.type === 'text' && part.text) {
                textParts.push(part.text);
            } else if (part.type === 'image_url' && part.image_url?.url) {
                imageUrls.push(part.image_url.url);
            }
        }
    } else if (typeof content === 'string') {
        textParts.push(content);
    }

    if (textParts.length > 0) {
        const span = document.createElement('span');
        span.textContent = textParts.join('');
        contentDiv.appendChild(span);
    }

    for (const url of imageUrls) {
        const img = document.createElement('img');
        img.src = url;
        img.className = 'message-image-thumb';
        img.style.maxWidth = '200px';
        img.style.maxHeight = '200px';
        img.style.display = 'block';
        img.style.marginTop = '6px';
        img.style.borderRadius = '4px';
        img.style.cursor = 'pointer';
        img.addEventListener('click', () => openImageInNewTab(url));
        contentDiv.appendChild(img);
    }

    div.appendChild(contentDiv);

    const textContent = textParts.join('');
    if (textContent.length > Config.USER_MESSAGE_COLLAPSE_CHAR_LIMIT || textContent.split('\n').length > Config.USER_MESSAGE_COLLAPSE_LINES_LIMIT) {
        const btn = document.createElement('button');
        btn.className = 'show-more-btn';
        div.appendChild(btn);
    }

    container.appendChild(div);
}
