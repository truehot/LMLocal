/**
 * Lightweight client that runs a `marked` Markdown parser inside a Web Worker.
 */
export class MarkedWorkerClient {
    constructor() {
        this.worker = null;
        this.callbacks = new Map();
        this.nextId = 0;
        this.started = false;
    }

    _getWorkerCode() {

        let workerCode = `
            importScripts('https://app.local/js/workers/marked/marked.udm.js', 'https://app.local/js/workers/marked/marked.shared.js');
            if (typeof marked === 'undefined') {
                throw new Error('marked library not loaded');
            }

            const renderer = new marked.Renderer();
            renderer.code = function(data) {
                return renderCodeBlock(data);
            };

            marked.setOptions({
                gfm: true,
                breaks: true,
                renderer: renderer
            });
            self.onmessage = function(e) {
                const { id, markdown, options } = e.data;
                try {
                    const html = marked.parse(markdown, options);
                    self.postMessage({ id, html, error: null });
                } catch (err) {
                    self.postMessage({ id, html: null, error: err.message });
                }
            };
        `;
        return workerCode;
    }
    start() {
        if (this.started && this.worker) return;
        if (typeof Worker === 'undefined') throw new Error('Web Workers not supported');

        const blob = new Blob([this._getWorkerCode()], { type: 'application/javascript' });
        const workerUrl = URL.createObjectURL(blob);
        this.worker = new Worker(workerUrl);
        URL.revokeObjectURL(workerUrl);

        this.worker.onmessage = (e) => this._handleMessage(e.data);
        this.worker.onerror = (err) => { console.error(err); this.stop(); };
        this.started = true;
    }
    stop() {
        if (!this.started) return;
        this.started = false;
        if (this.worker) {
            this.worker.terminate();
            this.worker = null;
        }
        for (const { reject } of this.callbacks.values()) {
            reject(new Error('Worker stopped'));
        }
        this.callbacks.clear();
        this.nextId = 0;
    }
    _handleMessage({ id, html, error }) {
        const cb = this.callbacks.get(id);
        if (!cb) return;
        if (error) {
            cb.reject(new Error(error));
        } else {
            cb.resolve(html);
        }
        this.callbacks.delete(id);
    }
    async parse(markdown, options = {}) {
        if (!this.started) return Promise.reject(new Error('Marked worker not started'));

        return new Promise((resolve, reject) => {
            const id = this.nextId++;
            this.callbacks.set(id, { resolve, reject });
            this.worker.postMessage({ id, markdown, options });
        });
    }
}


