"use strict";

/**
 * Minimal transient toast notification.
 */

class Toast {
    constructor() {
        this._el = null;
        this._timer = null;
        this._supportsPopover = typeof HTMLElement.prototype.showPopover === 'function';
    }

    _ensureElement() {
        if (this._el) return this._el;
        this._el = document.createElement('div');
        this._el.id = 'app-toast';
        this._el.className = 'app-toast';
        this._el.setAttribute('popover', 'manual');
        document.body.appendChild(this._el);
        return this._el;
    }

    /**
     * Shows a transient toast message. Replaces any currently-visible toast.
     */
    show(message, type = 'error', durationMs = 3000, anchor = null) {
        const el = this._ensureElement();
        el.textContent = message;
        clearTimeout(this._timer);

        if (el.parentElement !== document.body) {
            document.body.appendChild(el);
        }

        if (anchor && typeof anchor.getBoundingClientRect === 'function') {
            this._positionAnchored(el, type, anchor);
        } else {
            el.className = `app-toast ${type} show`;
            el.style.top = '';
            el.style.left = '';
            el.style.width = '';
        }

        if (this._supportsPopover && !el.matches(':popover-open')) {
            el.showPopover();
        }

        this._timer = setTimeout(() => {
            el.classList.remove('show');
            if (this._supportsPopover && el.matches(':popover-open')) {
                el.hidePopover();
            }
        }, durationMs);
    }

    _positionAnchored(el, type, anchor) {
        const rect = anchor.getBoundingClientRect();
        const viewportW = window.innerWidth;
        const viewportH = window.innerHeight;
        const gap = 8;

        const estHeight = 40;
        const estWidth = 320;

        let top = rect.top - estHeight - gap;
        if (top < gap) {
            top = rect.bottom + gap;
        }
        top = Math.max(gap, Math.min(top, viewportH - estHeight - gap));

        let left = rect.left;
        if (left + estWidth > viewportW - gap) {
            left = viewportW - estWidth - gap;
        }
        left = Math.max(gap, left);

        el.className = `app-toast ${type} show anchored`;
        el.style.setProperty('top', `${top}px`, 'important');
        el.style.setProperty('left', `${left}px`, 'important');
        el.style.setProperty('width', 'auto', 'important');
    }
}

const toast = new Toast();
export default toast;
