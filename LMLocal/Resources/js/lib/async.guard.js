/*
 * AsyncGuard - protects async flows from stale responses.
 */
export class AsyncGuard {
    constructor() {
        this._generation = 0;
    }

    start() {
        return ++this._generation;
    }

    isCurrent(gen) {
        return gen === this._generation;
    }

    invalidate() {
        this._generation += 1;
    }
}
