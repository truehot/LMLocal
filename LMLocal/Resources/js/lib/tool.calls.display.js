/**
 * Display modes of AI tool calls, mirrored in AppSettings/SettingsManager.
 */
export const ToolCallsDisplay = Object.freeze({
    FLAT: 'flat',
    EXPANDED: 'expanded',
    COLLAPSED: 'collapsed'
});

const MODES = [ToolCallsDisplay.FLAT, ToolCallsDisplay.EXPANDED, ToolCallsDisplay.COLLAPSED];

/**
 * Resolves the display mode a settings object stands for.
 */
export function resolveToolCallsDisplay(settings) {
    const raw = settings?.ToolCallsDisplay;
    const mode = typeof raw === 'string' ? raw.trim().toLowerCase() : '';

    if (MODES.includes(mode)) return mode;

    return settings?.CollapseToolCalls ? ToolCallsDisplay.EXPANDED : ToolCallsDisplay.FLAT;
}
