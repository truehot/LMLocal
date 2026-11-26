/** Activity of a tool call shown after `Step N:`: built-in category, SubAgent display name, or `Using <tool>`. */

const ACTIVITY_READING = 'Reading code';
const ACTIVITY_EDITING = 'Editing';
const ACTIVITY_BUILDING = 'Building';
const ACTIVITY_TESTING = 'Testing';
const ACTIVITY_PLANNING = 'Planning';

/** Built-in tools and the activity they perform. SubAgents, pipelines and MCP tools are not listed: they are dynamic. */
const TOOL_ACTIVITIES = new Map([
    ['read_file_lines', ACTIVITY_READING],
    ['find_files', ACTIVITY_READING],
    ['list_directory', ACTIVITY_READING],
    ['search_file_content', ACTIVITY_READING],
    ['get_symbol_info', ACTIVITY_READING],
    ['get_symbol_info_js', ACTIVITY_READING],
    ['inspect_type', ACTIVITY_READING],
    ['get_solution_overview', ACTIVITY_READING],
    ['get_active_document', ACTIVITY_READING],
    ['search_solution_knowledge', ACTIVITY_READING],
    ['get_changes_diff', ACTIVITY_READING],
    ['search_chat_history', ACTIVITY_READING],

    ['replace_file_lines', ACTIVITY_EDITING],
    ['replace_file_content', ACTIVITY_EDITING],
    ['insert_file_lines', ACTIVITY_EDITING],
    ['create_file', ACTIVITY_EDITING],
    ['delete_file', ACTIVITY_EDITING],
    ['format_document', ACTIVITY_EDITING],
    ['set_file_project_status', ACTIVITY_EDITING],

    ['build_solution', ACTIVITY_BUILDING],
    ['run_tests', ACTIVITY_TESTING],
    ['todo_write', ACTIVITY_PLANNING]
]);

/** Processing text of a SubAgent: `ToolRouter.GetProcessingMessage` renders it as `Running {DisplayName}...`. */
const SUB_AGENT_MESSAGE = /^Running\s+(.+?)\.\.\.$/;

/** Separator of a pipeline progress message, e.g. `[1/2 collect] explorer · find_files`. */
const STEP_SEPARATOR = ' · ';

/** Activity of a built-in tool, or null when the name does not belong to one. */
export function activityForTool(toolName) {
    const name = typeof toolName === 'string' ? toolName.trim().toLowerCase() : '';
    return TOOL_ACTIVITIES.get(name) || null;
}

/** Display name of the SubAgent announced by a processing message, or null when the message is not one. */
export function subAgentNameFromMessage(message) {
    if (typeof message !== 'string') return null;
    const match = SUB_AGENT_MESSAGE.exec(message.trim());
    const name = match ? match[1].trim() : '';
    return name || null;
}

/** `{ kind: 'tool' | 'agent' | 'other', label }` of a tool call; label is null when nothing is known about it. */
export function describeToolCall(toolName, message) {
    const name = typeof toolName === 'string' ? toolName.trim() : '';

    const activity = activityForTool(name);
    if (activity) return { kind: 'tool', label: activity };

    const agent = subAgentNameFromMessage(message);
    if (agent) return { kind: 'agent', label: agent };

    return { kind: 'other', label: name ? `Using ${name}` : null };
}

/** Tool name of a SubAgent step: its `FunctionName`, else the last ` · ` segment of a pipeline message. */
export function toolNameFromStep(toolName, message) {
    const name = typeof toolName === 'string' ? toolName.trim() : '';
    if (name) return name;
    if (typeof message !== 'string') return '';

    const parts = message.split(STEP_SEPARATOR);
    return parts[parts.length - 1].trim();
}

/** Category of a SubAgent step line, or an empty string when the step runs no known tool. */
export function stepActivity(toolName, message) {
    return activityForTool(toolNameFromStep(toolName, message)) || '';
}

/** Header label of the running SubAgents: up to two names, then `+K` for the rest. */
export function formatAgentNames(names) {
    const list = (names || []).filter(name => !!name);

    if (list.length === 0) return null;
    if (list.length <= 2) return list.join(', ');

    return `${list[0]}, ${list[1]} +${list.length - 2}`;
}
