/**
 * The note that precedes a restored answer: which models produced its chain of thought and how many tool
 * round-trips it took.
 */

/**
 * Model ids come from a log file and the note is rendered as inline code; a backtick or a line break would break it.
 */
function sanitizeNotePart(text) {
    return String(text).replace(/[`\r\n]+/g, ' ').trim();
}

/**
 * Adds the model id of a CoT message to the running list, keeping first-use order and skipping messages without one.
 */
export function collectCotModel(models, modelId) {
    if (!modelId || typeof modelId !== 'string') return;
    if (models.indexOf(modelId) === -1) models.push(modelId);
}

/**
 * Builds the note for a finished CoT: `modelA, modelB · 3 steps taken (history)`, `model (history)` when the
 * answer needed no tool round, and an empty string for legacy logs that carry neither a model nor steps.
 */
export function buildHistoryCotNote(models, steps) {
    let parts = [];

    if (models && models.length > 0) {
        parts.push(models.map(sanitizeNotePart).join(', '));
    }

    if (steps > 0) {
        parts.push(steps + ' step' + (steps !== 1 ? 's' : '') + ' taken');
    }

    if (parts.length === 0) return '';

    return parts.join(' · ') + ' (history)';
}
