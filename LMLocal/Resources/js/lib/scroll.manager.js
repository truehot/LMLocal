/**
 * ScrollManager - manages auto-scrolling behavior for a scrollable container.
 */
class ScrollManager {
    static State = Object.freeze({
        Following: 'following',
        DetachedByUser: 'detached-by-user'
    });

    _container = null;
    _state = ScrollManager.State.Following;
    _lastSeenScrollTop = 0;
    _rafId = null;
    _stickThreshold = 50;
    _bottomEpsilon = 1;

    constructor(container, thresholdPx = 50) {
        this.setup(container, thresholdPx);
    }

    setup(container, thresholdPx = 50) {
        this.reset();
        this._container = container;
        this._stickThreshold = thresholdPx;
        if (this._container) {
            this._lastSeenScrollTop = this._container.scrollTop;
            this._container.addEventListener('scroll', this._onScroll, { passive: true });
        }
    }

    _onScroll = () => {
        const prev = this._lastSeenScrollTop;
        const curr = this._container.scrollTop;
        this._lastSeenScrollTop = curr;

        const delta = curr - prev;
        const distance = this._container.scrollHeight - curr - this._container.clientHeight;

        if (this._state === ScrollManager.State.Following && delta < -0.5) {
            if (distance > this._bottomEpsilon) {
                this._state = ScrollManager.State.DetachedByUser;
            }
            return;
        }

        if (this._state === ScrollManager.State.DetachedByUser
            && delta > 0.5
            && distance >= 0
            && distance <= this._stickThreshold) {

            this._state = ScrollManager.State.Following;
        }
    };

    scrollToBottom(force = false) {
        if (!this._container) return;

        if (force) {
            this._state = ScrollManager.State.Following;
        }
        if (this._state !== ScrollManager.State.Following || this._rafId !== null) return;

        this._rafId = requestAnimationFrame(() => {
            this._rafId = null;
            if (!this._container || this._state !== ScrollManager.State.Following) return;
            this._container.scrollTop = this._container.scrollHeight;
            this._lastSeenScrollTop = this._container.scrollTop;
        });
    }

    reset() {
        if (this._rafId !== null) {
            cancelAnimationFrame(this._rafId);
            this._rafId = null;
        }
        if (this._container) {
            this._container.removeEventListener('scroll', this._onScroll);
        }
        this._container = null;
        this._state = ScrollManager.State.Following;
        this._lastSeenScrollTop = 0;
        this._stickThreshold = 50;
    }
}

export const createScrollManager = (container, threshold = 50) => new ScrollManager(container, threshold);
