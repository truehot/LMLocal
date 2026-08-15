"use strict";

/**
 * lm-api.js — host bridge between the VS tool window (C#/WebView2) and the chat UI.
 */
const LmApi = {
    /**
     * Replaces the chat input value, resizes, dispatches the 'input' event, focuses and moves the caret to the end.
     */
    setInputText(text) {
        const el = document.getElementById('userInput');
        if (!el) return false;

        el.value = text;
        el.style.height = 'auto';
        el.style.height = `${el.scrollHeight}px`;
        el.dispatchEvent(new Event('input', { bubbles: true }));
        el.focus();
        el.setSelectionRange(el.value.length, el.value.length);

        const wrapper = el.closest('.input-wrapper');
        if (wrapper) wrapper.classList.add('expanded');
        return true;
    },

    /**
     * Clicks the instruction dropdown item with the given id (if present).
     */
    selectInstructionTab(tabId) {
        if (!tabId) return false;
        const item = document.querySelector(`.dropdown-item[data-value="${tabId}"]`);
        if (!item) return false;
        item.click();
        return true;
    },

    /**
     * Selects an instruction tab (optional), sets the input text and clicks Send.
     */
    injectAndSend(text, instructionTabId) {
        this.selectInstructionTab(instructionTabId);
        if (!this.setInputText(text)) return false;
        const btn = document.getElementById('mainBtn');
        if (btn) btn.click();
        return true;
    },

    /**
     * Focuses the chat input.
     */
    focusInput() {
        const el = document.getElementById('userInput');
        if (!el) return false;
        el.focus();
        return true;
    },

    /**
     * Moves the caret in the currently focused editable element.
     * Supports Home/End/ArrowLeft/ArrowRight; pass isShift to extend the selection.
     */
    moveCaret(keyName, isShift) {
        const el = document.activeElement;
        if (!el || !('selectionStart' in el)) return false;

        const caret = el.selectionDirection === 'backward'
            ? el.selectionStart
            : el.selectionEnd;

        const text = el.value;
        const textLength = text.length;

        let anchor = el.dataset.selAnchor
            ? parseInt(el.dataset.selAnchor, 10)
            : caret;

        const getLineStart = (pos) => {
            const idx = text.lastIndexOf('\n', pos - 1);
            return idx === -1 ? 0 : idx + 1;
        };

        const getLineEnd = (pos) => {
            const idx = text.indexOf('\n', pos);
            return idx === -1 ? textLength : idx;
        };

        let newPos = caret;

        if (keyName === 'Home') {
            newPos = getLineStart(caret);
        } else if (keyName === 'End') {
            newPos = getLineEnd(caret);
        } else if (keyName === 'ArrowLeft') {
            if (caret > 0) newPos = caret - 1;
        } else if (keyName === 'ArrowRight') {
            if (caret < textLength) newPos = caret + 1;
        } else {
            return false;
        }

        if (!isShift) {
            delete el.dataset.selAnchor;
            el.setSelectionRange(newPos, newPos, 'none');
        } else {
            if (!el.dataset.selAnchor) {
                el.dataset.selAnchor = anchor;
            }
            const start = Math.min(anchor, newPos);
            const end = Math.max(anchor, newPos);
            const direction = anchor <= newPos ? 'forward' : 'backward';
            el.setSelectionRange(start, end, direction);
        }
        return true;
    }
};

window.lmApi = LmApi;
