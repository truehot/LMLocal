import { UIText } from '@app/constants/app.globals.js';
import { formatTokens, formatDuration } from '@app/lib/formatting.js';
const TREND_UP_ICON =
    '<svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="20" x2="18" y2="10"></line><line x1="12" y1="20" x2="12" y2="4"></line><line x1="6" y1="20" x2="6" y2="14"></line></svg>';

/**
 * Formats token statistics into a compact human-readable HTML string.
 */
export function formatTokenStats(stats) {
    const total = stats?.tokenUsed || 0;
    const parts = [`${formatTokens(total)} ${UIText.TEXT_TOKENS}`];
    if (stats?.cachedTokens) {
        parts.push(`${UIText.TEXT_TOKENS_CACHED} ${formatTokens(stats.cachedTokens)}`);
    }
    if (stats?.tokenSpeed > 0) {
        parts.push(`${stats.tokenSpeed.toFixed(1)} ${UIText.TEXT_TOKENS_PER_SECOND}`);
    }
    if (stats?.elapsedMs > 0) {
        parts.push(formatDuration(stats.elapsedMs));
    }
    return `${TREND_UP_ICON}${parts.join(UIText.TOKEN_STATS_SEPARATOR)}`;
}


