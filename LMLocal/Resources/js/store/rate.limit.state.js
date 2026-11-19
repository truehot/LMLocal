import { appSelectors } from '@app/store/app.selectors.js';

/**
 * Pure state transition for the rate-limit banner.
 *
 * Returns the slice to merge into the app state, or null when the message changes nothing. Kept out of the bridge
 * message handler so that handler stays a plain wire-to-store translator.
 */
export function applyRateLimitWait(appState, data) {
    if (!data) return null;

    if (data.Phase === 'End') {
        const current = appState?.rateLimitWait;
        if (!current || current.waitId !== data.WaitId) return null;

        return { rateLimitWait: null };
    }

    if (data.Phase !== 'Begin') return null;

    if (appSelectors.isTerminal(appState?.status)) return null;

    return {
        rateLimitWait: {
            waitId: data.WaitId || '',
            attempt: Number(data.Attempt) || 0,
            maxAttempts: Number(data.MaxAttempts) || 0,
            delaySeconds: Number(data.DelaySeconds) || 0,
            source: data.Source || ''
        }
    };
}
