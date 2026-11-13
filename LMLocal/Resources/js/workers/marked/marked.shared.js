const escapeTable = (() => {
    const table = new Array(128);
    for (let i = 0; i < 128; i++) {
        const ch = String.fromCharCode(i);
        switch (ch) {
            case '&': table[i] = '&amp;'; break;
            case '<': table[i] = '&lt;'; break;
            case '>': table[i] = '&gt;'; break;
            case '"': table[i] = '&quot;'; break;
            case "'": table[i] = '&#39;'; break;
            default: table[i] = ch;
        }
    }
    return table;
})();

function escapeHtml(str) {
    let result = '';
    for (let i = 0; i < str.length; i++) {
        const code = str.charCodeAt(i);
        if (code < 128) {
            result += escapeTable[code];
        } else {
            result += str[i];
        }
    }
    return result;
}

function countLines(str) {
    let count = 1;
    for (let i = 0; i < str.length; i++) {
        if (str[i] === '\n') count++;
    }
    return count;
}

const COPY_BUTTON_SVG = `<svg class="btn-icon" width="14" height="14" viewBox="0 0 16 16" fill="currentColor"><path d="M4.5 3A1.5 1.5 0 0 1 6 1.5h5.5A1.5 1.5 0 0 1 13 3v7a1.5 1.5 0 0 1-1.5 1.5H6A1.5 1.5 0 0 1 4.5 10V3z" opacity=".55"/><path d="M2 5.5A1.5 1.5 0 0 1 3.5 4H4v1h-.5a.5.5 0 0 0-.5.5v7a.5.5 0 0 0 .5.5H9a.5.5 0 0 0 .5-.5V12h1v.5A1.5 1.5 0 0 1 9 14H3.5A1.5 1.5 0 0 1 2 12.5v-7z"/></svg>`;

/**
 * Attaches a copy header to each code block.
 */
function renderCodeBlock(block) {
    let lang = block.lang || 'text';
    if (['undefined', 'null', 'unknown'].includes(lang)) lang = 'text';
    const code = block.text;
    const hasOverflowClass = countLines(code) > 18 ? 'has-overflow' : '';
    const finalCode = escapeHtml(code);

    return `
        <div class="code-block-container ${hasOverflowClass}">
            <div class="code-header">
                <span class="code-lang">${escapeHtml(lang)}</span>
                <button type="button" class="header-copy-btn" title="Copy" aria-label="Copy">${COPY_BUTTON_SVG}</button>
            </div>
            <pre><code class="language-${escapeHtml(lang)}">${finalCode}</code></pre>
            <button class="code-toggle-btn">
                <svg class="toggle-icon" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"></polyline></svg>
                <span class="toggle-text">Expand</span>
            </button>
        </div>
    `;
}

const EXT_SET = new Set([
    'md', 'txt', 'json', 'yaml', 'yml', 'js', 'ts', 'tsx', 'jsx', 'cs', 'csproj', 'sln',
    'py', 'go', 'rs', 'java', 'kt', 'cpp', 'c', 'h', 'hpp', 'css', 'scss', 'html', 'xml',
    'sh', 'ps1', 'bat', 'sql', 'log', 'env'
]);

const LINE_SUFFIX = /:\d+(?:[-–]\d+)?$/;
const GLOB_CHARS = /[*?[\]{}]/;
const PATH_SEP = /[\\/]/;
function isFilePath(text) {
    if (!text || /\s/.test(text)) return false;
    if (text.includes('://')) return false;
    if (GLOB_CHARS.test(text)) return false;
    if (!PATH_SEP.test(text)) return false;

    const path = text.replace(LINE_SUFFIX, '');
    const segs = path.split(PATH_SEP);

    if (segs.some(s => s === '...' || s === '…')) return false;

    const base = segs.pop();
    const dot = base.lastIndexOf('.');
    if (dot < 0) return false;

    const ext = base.slice(dot + 1).toLowerCase();
    return EXT_SET.has(ext);
}

function renderCodeSpan(data) {
    if (isFilePath(data.text)) {
        return `<code class="file-link">${escapeHtml(data.text)}</code>`;
    }
    return `<code>${escapeHtml(data.text)}</code>`;
}