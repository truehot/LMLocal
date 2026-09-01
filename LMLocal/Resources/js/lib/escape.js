
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

/**
 * Escapes HTML special characters for safe insertion into text or attributes.
 */
export function escapeHtml(str) {
    if (str == null) return '';
    const s = String(str);
    let result = '';
    for (let i = 0; i < s.length; i++) {
        const code = s.charCodeAt(i);

        result += (code < 128) ? escapeTable[code] : s[i];
    }
    return result;
}