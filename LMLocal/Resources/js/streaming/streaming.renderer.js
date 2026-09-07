/**
 * Mode "full" — complete overwrite with throttling.
 */
class FullRenderer {
    constructor(onUpdate) {
        this.onUpdate = onUpdate;
        this.element = null;
        this.lastRenderedText = '';
        this.lastRequestedText = '';
    }

    _runRender(fullText, html) {
        if (!this.element) return;
        if (html === undefined) return;
        if (fullText === this.lastRenderedText) return;
        this.lastRequestedText = fullText;

        if (this.element.innerHTML !== html) {
            this.element.innerHTML = html;
            this.onUpdate?.();
        }
        this.lastRenderedText = fullText;
    }

    start(targetElement) {
        if (!targetElement) {
            console.error('Target element is required to start renderer');
            return;
        }
        this.stop();
        this.element = targetElement;
    }

    write(text, html) {
        if (!this.element) return;
        this._runRender(text, html);
    }

    stop() {
        this.element = null;
        this.lastRenderedText = '';
        this.lastRequestedText = '';
    }

    isActive() { return this.element !== null; }
}


/**
 * Mode "block-tail" — stable committed blocks with a live tail element.
 *
 * Keeps completed blocks stable in the container and maintains a dedicated
 * tail element that updates with the currently streaming (unfinished) block.
 */
class ProgressiveBlockRenderer {
    static GROUPABLE_TAGS = new Set(['OL', 'UL', 'BLOCKQUOTE']);
    constructor(onUpdate) {
        this.onUpdate = onUpdate;
        this.container = null;
        this.activeTailDiv = null;
        this.lastCompletedChildrenCount = 0;
        this.lastRenderedText = '';
        this.tempDiv = null;
    }

    _moveChildrenToContainer(el) {
        if (!el || !this.container) return;
        const fragment = document.createDocumentFragment();
        while (el.firstChild) fragment.appendChild(el.firstChild);
        this.container.appendChild(fragment);
    }

    _commitableChildCount() {
        const children = this.tempDiv.children;
        let count = children.length;
        while (count >= 2) {
            const prev = children[count - 2];
            const curr = children[count - 1];
            if (prev.tagName === curr.tagName && ProgressiveBlockRenderer.GROUPABLE_TAGS.has(prev.tagName)) {
                count--;
            } else {
                break;
            }
        }
        return count;
    }

    _runRender(fullText, html) {
        if (!this.container || fullText === this.lastRenderedText || html === undefined) return;

        if (!this.tempDiv) this.tempDiv = document.createElement('div');
        this.tempDiv.innerHTML = html;
        const foundChildren = this._commitableChildCount();

        if (!this.activeTailDiv) {
            this.activeTailDiv = document.createElement('div');
            this.activeTailDiv.className = 'streaming-tail';
            this.activeTailDiv.style.display = 'contents';
            this.container.appendChild(this.activeTailDiv);
        }

        while (foundChildren > this.lastCompletedChildrenCount + 1) {
            const finishedNode = this.tempDiv.children[this.lastCompletedChildrenCount];
            const nodeToCommit = finishedNode.cloneNode(true);
            this.container.insertBefore(nodeToCommit, this.activeTailDiv);
            this.lastCompletedChildrenCount++;
        }

        const children = this.tempDiv.children;
        const tailStart = this.lastCompletedChildrenCount;
        const tailLength = children.length - tailStart;

        if (tailLength > 0) {
            let alreadyMatches = (this.activeTailDiv.children.length === tailLength);
            if (alreadyMatches) {
                for (let i = 0; i < tailLength; i++) {
                    const oldEl = this.activeTailDiv.children[i];
                    const newEl = children[tailStart + i];
                    if (!oldEl.isEqualNode(newEl)) {
                        alreadyMatches = false;
                        break;
                    }
                }
            }

            if (!alreadyMatches) {
                const fragment = document.createDocumentFragment();
                for (let i = tailStart; i < children.length; i++) {
                    fragment.appendChild(children[i].cloneNode(true));
                }
                this.activeTailDiv.replaceChildren(fragment);
                const lastActiveNode = children[children.length - 1];
                this.activeTailDiv.dataset.tag = lastActiveNode.tagName.toLowerCase();
            }
        }

        const tailChildren = Array.from(this.tempDiv.children).slice(this.lastCompletedChildrenCount);
        if (tailChildren.length > 0) {
            const cloned = tailChildren.map(n => n.cloneNode(true));
            const alreadyMatches = this.activeTailDiv.children.length === cloned.length && cloned.every((n, i) => this.activeTailDiv.children[i]?.isEqualNode(n));
            if (!alreadyMatches) {
                this.activeTailDiv.replaceChildren(...cloned);
                const lastActiveNode = tailChildren[tailChildren.length - 1];
                this.activeTailDiv.dataset.tag = lastActiveNode.tagName.toLowerCase();
            }
        }

        this.lastRenderedText = fullText;
        this.onUpdate?.();
    }

    _flush() {
        if (!this.activeTailDiv) return;

        const childCount = this.activeTailDiv.children.length;
        if (this.container && this.container.isConnected) {
            if (childCount > 0) {
                this._moveChildrenToContainer(this.activeTailDiv);
                this.lastCompletedChildrenCount += childCount;
            }
        }

        this.activeTailDiv.replaceChildren();
        this.activeTailDiv.remove();
        this.activeTailDiv = null;
    }

    start(targetElement) {
        if (!targetElement) {
            console.error('Target element is required to start renderer');
            return;
        }
        const savedOnUpdate = this.onUpdate;
        this.stop();
        this.onUpdate = savedOnUpdate;
        this.container = targetElement;
        if (!this.tempDiv) this.tempDiv = document.createElement('div');
    }

    write(text, html) {
        if (this.container) this._runRender(text, html);
    }

    stop() {
        this._flush();
        this.container = null;
        this.activeTailDiv = null;
        this.lastCompletedChildrenCount = 0;
        this.lastRenderedText = '';
        if (this.tempDiv) {
            this.tempDiv.replaceChildren();
            this.tempDiv = null;
        }
        this.onUpdate = null;
    }

    isActive() { return this.container !== null; }
}


/**
 * Mode "diff" — virtual DOM diffing for surgical updates.
 * 
 * Create a renderer that performs a DOM diff between the current container
 * and a newly rendered HTML tree. The diff attempts to patch text nodes and
 * attributes in-place, while preserving nodes like PRE/CODE by replacing them
 * entirely when their text changes.
 *
 * This is more surgical than full replacement and avoids re-creating nodes
 * with attached event listeners where possible.
 */
class CleanDiffRenderer {
    constructor(onUpdate) {
        this.onUpdate = onUpdate;
        this.container = null;
        this.hiddenTemplate = document.createElement('template');
        this.lastRenderedText = '';
    }

    _updateRecursive(oldNode, newNode) {
        if (oldNode.isEqualNode && newNode.isEqualNode && oldNode.isEqualNode(newNode)) return;
        if (oldNode.nodeType !== newNode.nodeType) {
            const cloned = newNode.cloneNode(true);
            oldNode.parentNode.replaceChild(cloned, oldNode);
            return;
        }
        if (oldNode.nodeType === Node.TEXT_NODE && newNode.nodeType === Node.TEXT_NODE) {
            if (oldNode.textContent !== newNode.textContent) {
                oldNode.textContent = newNode.textContent;
            }
            return;
        }
        if (oldNode.nodeType === Node.ELEMENT_NODE && newNode.nodeType === Node.ELEMENT_NODE) {
            if (oldNode.tagName === 'PRE' || oldNode.tagName === 'CODE') {
                if (oldNode.textContent !== newNode.textContent) {
                    const cloned = newNode.cloneNode(true);
                    oldNode.parentNode.replaceChild(cloned, oldNode);
                }
                return;
            }
            if (oldNode.tagName !== newNode.tagName) {
                const cloned = newNode.cloneNode(true);
                oldNode.parentNode.replaceChild(cloned, oldNode);
                return;
            }
            for (const attr of oldNode.attributes) {
                if (!newNode.hasAttribute(attr.name)) oldNode.removeAttribute(attr.name);
            }
            for (const attr of newNode.attributes) {
                if (oldNode.getAttribute(attr.name) !== attr.value) {
                    oldNode.setAttribute(attr.name, attr.value);
                }
            }
            let oldChild = oldNode.firstChild;
            let newChild = newNode.firstChild;
            while (oldChild || newChild) {
                if (!oldChild) {
                    const nextNew = newChild.nextSibling;
                    oldNode.appendChild(newChild.cloneNode(true));
                    newChild = nextNew;
                } else if (!newChild) {
                    const nextOld = oldChild.nextSibling;
                    oldChild.remove();
                    oldChild = nextOld;
                } else {
                    const nextOld = oldChild.nextSibling;
                    const nextNew = newChild.nextSibling;
                    this._updateRecursive(oldChild, newChild);
                    oldChild = nextOld;
                    newChild = nextNew;
                }
            }
        }
    }

    _runRender(fullText, html) {
        if (!this.container || fullText === this.lastRenderedText) return;
        if (html === undefined) return;

        this.hiddenTemplate.innerHTML = html;

        let oldNode = this.container.firstChild;
        let newNode = this.hiddenTemplate.content.firstChild;
        while (oldNode || newNode) {
            if (!oldNode) {
                const nextNew = newNode.nextSibling;
                this.container.appendChild(newNode.cloneNode(true));
                newNode = nextNew;
            } else if (!newNode) {
                const nextOld = oldNode.nextSibling;
                oldNode.remove();
                oldNode = nextOld;
            } else {
                const nextOld = oldNode.nextSibling;
                const nextNew = newNode.nextSibling;
                this._updateRecursive(oldNode, newNode);
                oldNode = nextOld;
                newNode = nextNew;
            }
        }
        this.lastRenderedText = fullText;
        this.onUpdate?.();
    }

    start(targetElement) {
        if (!targetElement) {
            console.error('Target element is required to start renderer');
            return;
        }
        this.stop();
        this.container = targetElement;
    }

    write(text, html) {
        if (this.container) this._runRender(text, html);
    }

    stop() {
        this.container = null;
        this.hiddenTemplate.replaceChildren();
        this.lastRenderedText = '';
    }

    isActive() { return this.container !== null; }
}


/**
 * Available streaming modes for streaming renderer.
 * @readonly
 * @enum {string}
 */
export const StreamingMode = {
    /** Full overwrite of container innerHTML with throttling. */
    FULL: 'full',
    /** Commit completed blocks, keep a live tail element for streaming content. */
    BLOCK_TAIL: 'block-tail',
    /** Virtual DOM diffing for surgical updates, preserving event listeners where possible. */
    DIFF: 'diff'
};

/**
 * Factory that returns a streaming renderer according to options or legacy args.
 *
 * Supported modes:
 *  - 'full'       : full overwrite  
 *  - 'block-tail' : commit completed blocks and keep a dedicated live tail element
 *  - 'diff'       : virtual DOM diff 
 */
export function createStreamingRenderer({ mode, onUpdate }) {
    switch (mode) {
        case StreamingMode.FULL:
            return new FullRenderer(onUpdate);
        case StreamingMode.BLOCK_TAIL:
            return new ProgressiveBlockRenderer(onUpdate);
        case StreamingMode.DIFF:
            return new CleanDiffRenderer(onUpdate);
        default:
            throw new Error(`Unknown mode: ${mode}`);
    }
}