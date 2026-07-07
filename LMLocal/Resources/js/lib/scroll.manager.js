/**
 * ScrollManager - manages auto-scrolling behavior for a scrollable container.
 */
class ScrollManager {
    _container = null;
    _stickThreshold = 50;
    _detachThreshold = 100;
    _isStuckToBottom = true;
    _scrollScheduled = false;
    _rafId = null;

    constructor(container, thresholdPx = 50) {
        this.setup(container, thresholdPx);
    }

    setup(container, thresholdPx = 50) {
        this.reset();
        this._container = container;
        this._stickThreshold = thresholdPx;
        this._detachThreshold = thresholdPx * 2;
        if (this._container) {
            this._updateStuckState();
            this._attachEvents();
        }
    }

    reset() {
        if (this._rafId) {
            cancelAnimationFrame(this._rafId);
            this._rafId = null;
        }
        this._scrollScheduled = false;
        this._detachEvents();
        this._container = null;
        this._stickThreshold = 50;
        this._detachThreshold = 100;
        this._isStuckToBottom = true;
    }

    _attachEvents() {
        if (this._container) {
            this._container.addEventListener('scroll', this._handleManualScroll, { passive: true });
        }
    }

    _detachEvents() {
        if (this._container) {
            this._container.removeEventListener('scroll', this._handleManualScroll);
        }
    }

    _updateStuckState() {
        if (!this._container) return;
        const distance = this._container.scrollHeight - (this._container.scrollTop + this._container.clientHeight);

        if (this._isStuckToBottom) {
            this._isStuckToBottom = distance <= this._detachThreshold;
        } else {
            this._isStuckToBottom = distance <= this._stickThreshold;
        }
    }

    _handleManualScroll = () => {
        this._updateStuckState();
    };

    scrollToBottom(force = false) {
        if (!this._container) return;

        if (force) {
            if (this._rafId) cancelAnimationFrame(this._rafId);
            this._scrollScheduled = false;
            this._rafId = null;
            this._isStuckToBottom = true;
        }

        if (this._scrollScheduled) return;

        if (!force) {
            this._scrollScheduled = true;
            this._rafId = requestAnimationFrame(() => {
                this._updateStuckState();

                if (!this._isStuckToBottom) {
                    this._scrollScheduled = false;
                    this._rafId = null;
                    return;
                }

                requestAnimationFrame(() => {
                    if (!this._container) {
                        this._scrollScheduled = false;
                        this._rafId = null;
                        return;
                    }

                    try {
                        this._updateStuckState();
                        if (!this._isStuckToBottom) return;

                        this._container.scrollTop = this._container.scrollHeight;
                        this._isStuckToBottom = true;
                    } finally {
                        this._scrollScheduled = false;
                        this._rafId = null;
                    }
                });
            });
        } else {
            this._scrollScheduled = true;
            this._rafId = requestAnimationFrame(() => {
                requestAnimationFrame(() => {
                    if (!this._container) {
                        this._scrollScheduled = false;
                        this._rafId = null;
                        return;
                    }

                    try {
                        this._container.scrollTop = this._container.scrollHeight;
                        this._isStuckToBottom = true;
                    } finally {
                        this._scrollScheduled = false;
                        this._rafId = null;
                    }
                });
            });
        }
    }
}

export const createScrollManager = (container, threshold = 50) => new ScrollManager(container, threshold);
