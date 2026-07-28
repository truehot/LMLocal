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
    DRAG_DROP_ALLOWED_EXTENSIONS: /\.(cs|json|js|ts|html|css|md|xml|txt|yaml|yml|py|java|cpp|c|h|sql|env|config|ini|log|sh|bat|ps1|rb|go|rs|php|vue|svelte|scss|less)$/i,
};


