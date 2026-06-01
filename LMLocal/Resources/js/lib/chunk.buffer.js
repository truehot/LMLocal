/**
 * Accumulates incoming stream chunks and provides
 * time-based flushing. Stateless except for internal buffer and lastFlush.
 */
export class ChunkBuffer {
    constructor(flushIntervalMs = 60) {
        this._buffer = [];
        this._flushIntervalMs = flushIntervalMs;
        this._lastFlush = 0;
    }
    append(chunk) {
        if (!chunk) return;
        this._buffer.push(chunk);
    }
    flush() {
        const out = this._buffer.join('');
        this._buffer = [];
        this._lastFlush = Date.now();
        return out;
    }
    isEmpty() {
        return this._buffer.length === 0;
    }
    reset() {
        this._buffer = [];
        this._lastFlush = Date.now();
    }
    shouldFlush(now = Date.now()) {
        if (this._flushIntervalMs === 0 && this._buffer.length > 0) return true;
        return (now - this._lastFlush) > this._flushIntervalMs && this._buffer.length > 0;
    }
    flushIfNeeded(now = Date.now()) {
        if (this.shouldFlush(now)) {
            return this.flush();
        }
        return '';
    }
}
