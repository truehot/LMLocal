import { UIText } from '@app/constants/app.globals.js';

const TREND_UP_ICON =
    '<svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="20" x2="18" y2="10"></line><line x1="12" y1="20" x2="12" y2="4"></line><line x1="6" y1="20" x2="6" y2="14"></line></svg>';

/**
 * Formats token statistics into a compact human-readable HTML string.
 */
export function formatTokenStats(stats) {
    const total = stats?.tokenUsed || 0;
    const parts = [`${total} ${UIText.TEXT_TOKENS}`];
    if (stats?.cachedTokens) {
        parts.push(`${UIText.TEXT_TOKENS_CACHED} ${stats.cachedTokens}`);
    }
    if (stats?.tokenSpeed > 0) {
        parts.push(`${stats.tokenSpeed.toFixed(1)} ${UIText.TEXT_TOKENS_PER_SECOND}`);
    }
    if (stats?.elapsedMs > 0) {
        parts.push(formatDuration(stats.elapsedMs));
    }
    return `${TREND_UP_ICON}${parts.join(UIText.TOKEN_STATS_SEPARATOR)}`;
}

/**
 * Formats a duration in milliseconds as a compact human-readable string.
 */
function formatDuration(ms) {
    const totalSeconds = Math.max(0, ms) / 1000;

    if (totalSeconds < 60) {
        return `${totalSeconds.toFixed(1)} ${UIText.TEXT_TIME_SECONDS}`;
    }

    const roundedTotalSeconds = Math.round(totalSeconds);
    const minutes = Math.floor(roundedTotalSeconds / 60);
    const seconds = roundedTotalSeconds % 60;

    if (minutes < 60) {
        return `${minutes}${UIText.TEXT_TIME_MINUTES} ${seconds}${UIText.TEXT_TIME_SECONDS}`;
    }

    const hours = Math.floor(minutes / 60);
    const remMinutes = minutes % 60;
    return `${hours}${UIText.TEXT_TIME_HOURS} ${remMinutes}${UIText.TEXT_TIME_MINUTES}`;
}
