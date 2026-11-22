import { UIText } from '@app/constants/app.globals.js';
export function formatBytes(bytes) {
    const num = Number(bytes);
    if (!Number.isFinite(num) || num < 0) return "";
    if (num < 1024) return `${num} B`;

    let value, unit;

    if (num < 1_048_576) {
        value = num / 1024;
        unit = 'KB';
    } else if (num < 1_073_741_824) {
        value = num / 1_048_576;
        unit = 'MB';
    } else if (num < 1_099_511_627_776) {
        value = num / 1_073_741_824;
        unit = 'GB';
    } else {
        value = num / 1_099_511_627_776;
        unit = 'TB';
    }

    const decimals = value < 10
        ? 2
        : value < 100 ? 1 : 0;
    return `${value.toFixed(decimals)} ${unit}`;
}

export function formatTokens(n) {
    const num = Number(n);
    if (!Number.isFinite(num)) return "";
    const abs = Math.abs(num);
    if (abs < 1000) return String(num);
    if (abs < 1_000_000) return (num / 1000).toFixed(num % 1000 === 0 ? 0 : 1) + 'k';
    return (num / 1_000_000).toFixed(1) + 'm';
}

/**
 * Rounds a value expressed in percent to whole percent.
 */
export function roundPercent(value) {
    const num = Number(value);
    if (!Number.isFinite(num)) return 0;
    return Math.round(num);
}

/**
 * Formats a value expressed in percent, e.g. `73%`.
 */
export function formatPercent(value) {
    return `${roundPercent(value)}%`;
}

/**
 * Formats a token rate (tokens per second) with one decimal place, e.g. `42.0`.
 */
export function formatSpeed(value) {
    const num = Number(value);
    if (!Number.isFinite(num)) return "";
    return num.toFixed(1);
}

export function formatPrice(value, curr = '$') {
    const num = Number(value);
    if (!Number.isFinite(num)) return "";
    return curr + num.toFixed(2);
}

/**
* Formats a duration in milliseconds as a compact human-readable string.
*/
export function formatDuration(ms) {
    const totalSeconds = Math.max(0, ms) / 1000;

    if (totalSeconds < 60) {
        return `${totalSeconds.toFixed(1)}${UIText.TEXT_TIME_SECONDS}`;
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

const relativeTime = new Intl.RelativeTimeFormat('en', { numeric: 'auto', style: 'narrow' });

const MINUTE_MS = 60 * 1000;
const HOUR_MS = 60 * MINUTE_MS;
const DAY_MS = 24 * HOUR_MS;

/**
 * Difference in local calendar days, so `yesterday` means yesterday on the wall clock and not 24 hours back.
 */
function calendarDaysBetween(from, to) {
    const startOfDay = day => new Date(day.getFullYear(), day.getMonth(), day.getDate()).getTime();
    return Math.round((startOfDay(to) - startOfDay(from)) / DAY_MS);
}

/**
 * Short label of a message time: `now`, `5m ago`, `3h ago`, `yesterday`, `4d ago` for the last week, then the date
 * (`18 Nov`, with the year when it is not the current one).
 */
export function formatMessageTime(date, now = new Date()) {
    const value = date instanceof Date ? date : new Date(date);
    if (Number.isNaN(value.getTime())) return '';

    const elapsed = Math.max(0, now.getTime() - value.getTime());

    if (elapsed < MINUTE_MS) return relativeTime.format(0, 'second');
    if (elapsed < HOUR_MS) return relativeTime.format(-Math.floor(elapsed / MINUTE_MS), 'minute');
    if (elapsed < DAY_MS) return relativeTime.format(-Math.floor(elapsed / HOUR_MS), 'hour');

    const days = calendarDaysBetween(value, now);

    if (days <= 1) return relativeTime.format(-1, 'day');
    if (days < 7) return relativeTime.format(-days, 'day');

    return value.getFullYear() === now.getFullYear()
        ? value.toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })
        : value.toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });
}
