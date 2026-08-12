"use strict";

/**
 * Shared  UI text.
 */

export const UIText = Object.freeze({
    BUTTON_SEND: 'Send',
    BUTTON_STOP: 'Stop',
    BUTTON_WAIT: '...',
    COPY_ERROR: 'Error!',
    COPY_LABEL: 'Copy',
    COPY_SUCCESS: 'Done!',
    SHOW_LESS: 'less',
    SHOW_MORE: 'more',
    STATUS_CLEARING: 'Clearing conversation...',
    STATUS_COMPACTING: 'Summarizing history...',
    STATUS_CONNECTING: 'Connecting...',
    STATUS_ERROR: 'Error',
    STATUS_EXECUTING: 'Running tool...',
    STATUS_FINISHING: 'Finishing...',
    STATUS_IDLE: 'Ready',
    STATUS_OFFLINE: 'Disconnected',
    STATUS_ONLINE: 'Connected',
    STATUS_PROCESSING: 'Thinking...',
    STATUS_RESPONDING: 'Tool result ready',
    STATUS_STOPPING: 'Stopping...',
    STATUS_STREAMING: 'Generating...',
    STATUS_THINKING: 'Reasoning...',
    STATUS_UNKNOWN: 'Wait...',
    TEXT_GENERATION_STOPPED: 'Generation stopped.',
    TEXT_NOT_READY: 'Not ready',
    TEXT_TOKENS: 'tokens',
    TEXT_TOKENS_PER_SECOND: 't/s',
    TEXT_TOKENS_CACHED: 'cached',
    TEXT_TIME_SECONDS: 's',
    TEXT_TIME_MINUTES: 'm',
    TEXT_TIME_HOURS: 'h',
    TOKEN_STATS_SEPARATOR: ' · ',
    IMAGE_TOO_MANY: 'Max 3 images allowed',
    IMAGE_TOO_LARGE: 'Image exceeds 4 MB limit',
    IMAGE_UNSUPPORTED: 'Unsupported image format',
    IMAGE_PROCESSING: 'Images are still processing — please wait',
    FILES_PROCESSING: 'Files are still loading — please wait',
    FILES_TOO_LARGE: 'File exceeds size limit',
    FILES_UNSUPPORTED: 'Unsupported file type',
});

export const Config = {
    MAX_DISPLAYED_MESSAGES: 200,
    RENDER_THROTTLE_MS: 90,
    RENDER_BATCH_SIZE_WORDS: 2,
    STREAM_BUFFER_INTERVAL_MS: 100,
    USER_MESSAGE_COLLAPSE_CHAR_LIMIT: 500,
    USER_MESSAGE_COLLAPSE_LINES_LIMIT: 8,
    MAX_TOKENS: 16384,
    COPY_STATUS_RESET_MS: 2000,
    SCROLL_THRESHOLD_PX: 150,
    STREAM_INACTIVITY_TIMEOUT_MS: 30000,

    // Drag-and-drop file upload
    DRAG_DROP_MAX_FILES: 10,
    DRAG_DROP_MAX_FILE_SIZE_BYTES: 200 * 1024,
    DRAG_DROP_ALLOWED_EXTENSIONS: /\.(cs|json|js|ts|html|css|md|xml|txt|yaml|yml|py|java|cpp|c|h|sql|env|config|ini|log|sh|bat|ps1|rb|go|rs|php|vue|svelte|scss|less|jpg|jpeg|png)$/i,

    // Pasted images (multimodal chat)
    IMAGE_MAX_COUNT: 3,
    IMAGE_MAX_FILE_SIZE_BYTES: 4 * 1024 * 1024,
    IMAGE_ALLOWED_TYPES: ['image/jpeg', 'image/png'],

    // Set IMAGE_COMPRESSION_ENABLED = true to re-encode pasted images as WebP (quality 0.8, max 1024 px long side) before sending. Disabled by default —original images (JPEG/PNG/WebP) are sent as-is.
    IMAGE_COMPRESSION_ENABLED: false,
    IMAGE_COMPRESSION_QUALITY: 0.8,
    IMAGE_COMPRESSION_MAX_DIMENSION: 1024,   // long side; 0 = no downscale
};

/**
 * Shared inline SVG icons for UI buttons.
 * Use these instead of inlining SVG markup in dialogs/components.
 */
export const Icons = Object.freeze({
    LINK: `<svg class="btn-icon" width="16" height="16" viewBox="0 0 16 16" fill="currentColor" xmlns="http://www.w3.org/2000/svg">
        <path d="M4 5.5a2.5 2.5 0 0 0 0 5h2.5a.5.5 0 0 0 0-1H4a1.5 1.5 0 0 1 0-3h2.5a.5.5 0 0 0 0-1H4zm8 0H9.5a.5.5 0 0 0 0 1H12a1.5 1.5 0 0 1 0 3H9.5a.5.5 0 0 0 0 1H12a2.5 2.5 0 0 0 0-5z" />
        <path d="M5.5 7.5h5a.5.5 0 0 1 0 1h-5a.5.5 0 0 1 0-1z" />
    </svg>`,
    SUCCESS: `<svg class="btn-icon" width="14" height="14" viewBox="0 0 16 16" fill="currentColor"><path d="M13.854 3.646a.5.5 0 0 1 0 .708l-7 7a.5.5 0 0 1-.708 0l-3.5-3.5a.5.5 0 1 1 .708-.708L6.5 10.293l6.646-6.647a.5.5 0 0 1 .708 0z"/></svg>`,
    ERROR: `<svg class="btn-icon" width="14" height="14" viewBox="0 0 16 16" fill="currentColor"><path d="M4.646 4.646a.5.5 0 0 1 .708 0L8 7.293l2.646-2.647a.5.5 0 0 1 .708.708L8.707 8l2.647 2.646a.5.5 0 0 1-.708.708L8 8.707l-2.646 2.647a.5.5 0 0 1-.708-.708L7.293 8 4.646 5.354a.5.5 0 0 1 0-.708z"/></svg>`,
});

