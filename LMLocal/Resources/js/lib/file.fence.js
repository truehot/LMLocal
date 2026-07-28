'use strict';

/**
 * Map of file extensions to markdown language identifiers for code fences.
 */
const EXTENSION_LANG_MAP = {
    cs: 'csharp',
    js: 'javascript',
    ts: 'typescript',
    html: 'html',
    css: 'css',
    md: 'markdown',
    json: 'json',
    xml: 'xml',
    yaml: 'yaml',
    yml: 'yaml',
    py: 'python',
    java: 'java',
    cpp: 'cpp',
    c: 'c',
    h: 'c',
    sql: 'sql',
    sh: 'bash',
    bat: 'batch',
    ps1: 'powershell',
    rb: 'ruby',
    go: 'go',
    rs: 'rust',
    php: 'php',
    vue: 'vue',
    svelte: 'svelte',
    scss: 'scss',
    less: 'less',
    env: 'env',
    ini: 'ini',
    config: 'ini',
    log: 'text',
    txt: 'text',
};

/**
 * Wraps file content in a markdown code fence with the appropriate language tag.
 */
export function wrapAsCodeFence(filename, content) {
    const ext = (filename.split('.').pop() || '').toLowerCase();
    const lang = EXTENSION_LANG_MAP[ext] || '';
    const commentChar = ['csharp', 'javascript', 'typescript', 'java', 'cpp', 'c', 'go', 'rs', 'php', 'vue', 'svelte'].includes(lang)
        ? '//'
        : lang === 'css' || lang === 'scss' || lang === 'less'
            ? '/*'
            : lang === 'html' || lang === 'xml'
                ? '<!--'
                : '#';

    const comment = lang === 'css' || lang === 'scss' || lang === 'less'
        ? `${commentChar} ${filename} */`
        : lang === 'html' || lang === 'xml'
            ? `${commentChar} ${filename} -->`
            : `${commentChar} ${filename}`;

    return `\n\`\`\`${lang}\n${comment}\n${content}\n\`\`\`\n`;
}
