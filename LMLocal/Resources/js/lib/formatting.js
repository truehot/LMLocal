import { UIText } from '@app/constants/app.globals.js';
export function formatBytes(bytes) {
    const num = Number(bytes);
    if (!isFinite(num) || num < 0) return "";
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

    const decimals = value < 10 ? 2 : value < 100 ? 1 : 0;
    return `${value.toFixed(decimals)} ${unit}`;
}

export function formatTokens(n) {
    const num = Number(n);
    if (!isFinite(num)) return "";
    const abs = Math.abs(num);
    if (abs < 1000) return String(num);
    if (abs < 1_000_000) return (num / 1000).toFixed(num % 1000 === 0 ? 0 : 1) + 'K';
    return (num / 1_000_000).toFixed(1) + 'M';
}

export function formatPrice(value, curr = '$') {
    const num = Number(value);
    if (!isFinite(num)) return "";
    return curr + num.toFixed(2);
}

/**
* Formats a duration in milliseconds as a compact human-readable string.
*/
export function formatDuration(ms) {
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