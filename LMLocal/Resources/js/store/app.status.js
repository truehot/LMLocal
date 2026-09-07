/**
 * INITIALIZING → CONNECTING → IDLE
 * INITIALIZING → CONNECTING → OFFLINE
 * 
 * OFFLINE → CONNECTING → IDLE
 * 
 * IDLE  → [PROCESSING → THINKING → STREAMING → FINISHING → EXECUTING → RESPONDING]* → IDLE
 * IDLE → COMPACTING → IDLE
 * IDLE → CLEARING → IDLE
 */

export const AppStatus = {
    INITIALIZING: 'INITIALIZING',   // app starting up: loading settings, building DOM, preparing services

    // Connection states
    CONNECTING: 'CONNECTING',       // establishing or re-establishing connection
    OFFLINE: 'OFFLINE',             // terminal, disconnected, show reload button

    // Operational states (only possible when ONLINE)
    IDLE: 'IDLE',                   // waiting for user input. Can send new message
    PROCESSING: 'PROCESSING',       // pre-streaming (model thinking)
    THINKING: 'THINKING',           // actively receiving thinking tokens
    STREAMING: 'STREAMING',         // actively receiving tokens
    COMPACTING: 'COMPACTING',       // background history optimization
    FINISHING: 'FINISHING',         // post-processing (highlighting, copy buttons)

    // Tool execution states
    EXECUTING: 'EXECUTING',         // tool is being executed
    STEPPING: 'STEPPING',           // progress step received while a tool is still running
    RESPONDING: 'RESPONDING',       // tool execution finished

    // Interruption / error states
    STOPPING: 'STOPPING',           // user requested stop, cleaning up
    CLEARING: 'CLEARING',           // clearing conversation, resetting state

    // Error
    ERROR: 'ERROR'                 // terminal,online but an error occurred, can send message to try to recover
};
