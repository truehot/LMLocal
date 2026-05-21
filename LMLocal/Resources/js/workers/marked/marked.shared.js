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


const COPY_LABEL = 'Copy';
const COPY_BUTTON_SVG = `<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path></svg>`;
/**
 * Attaches a copy header to each code block.
 */
function renderCodeBlock(block) {
    let lang = block.lang || 'text';
    if (['undefined', 'null', 'unknown'].includes(lang)) lang = 'text';
    const code = block.text;
    const finalCode = escapeHtml(code);

    return `
        <div class="code-block-container">
            <div class="code-header">
                <span class="code-lang">${escapeHtml(lang)}</span>
                <button class="header-copy-btn">
                    ${COPY_BUTTON_SVG}
                    <span>${escapeHtml(COPY_LABEL)}</span>
                </button>
            </div>
            <pre><code class="language-${escapeHtml(lang)}">${finalCode}</code></pre>
            <button class="code-toggle-btn">
                <svg class="toggle-icon" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"></polyline></svg>
                <span class="toggle-text">Expand</span>
            </button>
        </div>
    `;
}