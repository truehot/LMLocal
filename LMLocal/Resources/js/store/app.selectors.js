import { AppStatus } from '@app/store/app.status.js';

export const appSelectors = {
    isTerminal: (status) => [AppStatus.OFFLINE, AppStatus.IDLE, AppStatus.ERROR].includes(status),
    // Busy if not IDLE (includes STOPPING, CONNECTING, OFFLINE, etc.)
    isBusy: (status) => ![AppStatus.IDLE, AppStatus.ERROR, AppStatus.OFFLINE].includes(status),
    // Generating during active token flow or tool activity (incl. progress steps)
    isGenerating: (status) => [AppStatus.PROCESSING, AppStatus.THINKING, AppStatus.STREAMING, AppStatus.EXECUTING, AppStatus.STEPPING, AppStatus.RESPONDING, AppStatus.FINISHING].includes(status),
    // Can send requests when online
    canSend: (status) => [AppStatus.IDLE, AppStatus.ERROR].includes(status),
};
