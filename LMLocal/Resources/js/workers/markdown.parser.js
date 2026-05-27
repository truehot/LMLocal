import { MarkedWorkerClient } from '@app/workers/marked/marked.worker.client.js';

/**
 * SimpleParser
 *
 * Minimal markdown parser used as a lightweight fallback.
 * - Escapes HTML to prevent injection.
 * - Wraps the provided text in a single <p> element.
 * - Does not support lists, code blocks, or other complex markdown features.
 *
 */
export class SimpleParser {
    async parse(markdown) {
        const escaped = this.escapeHtml(markdown);
        return `<p>${escaped}</p>`;
    }
    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
}

/**
 * Available parser types for markdown parsing.
 * @readonly
 * @enum {string}
 */
export const ParserType = {
    /** Offloads markdown parsing to a Web Worker using the `marked` library.*/
    MARKED_WORKER: 'marked-worker',
    /** Simple parser that only handles basic paragraphs and line breaks, without any advanced markdown features. */
    SIMPLE: 'simple'
};

/**
 * Factory function that returns a parser instance for the requested `ParserType`.
 *
 */
export function createMarkDownParser(type) {
    switch (type) {
        case ParserType.SIMPLE:
            return new SimpleParser();
        case ParserType.MARKED_WORKER:
            return new MarkedWorkerClient();
        default:
            throw new Error(`Unknown parser type: ${type}`);
    }
}


