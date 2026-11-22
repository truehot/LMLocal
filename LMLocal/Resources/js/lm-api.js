"use strict";

/**
 * lm-api.js — host bridge between the VS tool window (C#/WebView2) and the chat UI.
 */

import bridgeClient from '@app/api/bridge.client.js';

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
     * Shows or hides the debugger stop banner above the status bar.
     */
    setDebugStop(payload) {
        const banner = document.getElementById('debug-stop-banner');
        if (!banner) return false;

        if (!payload) {
            banner.classList.add('hidden');
            delete banner.dataset.stopKey;
            return true;
        }

        this._wireDebugStopBanner(banner);

        const caption = document.getElementById('debug-stop-caption');
        if (caption) caption.textContent = LmApi._formatDebugStopCaption(payload);

        banner.dataset.stopKey = LmApi._debugStopKey(payload);
        banner.classList.toggle('debug-stop-exception', payload.kind === 'Exception');
        banner.classList.remove('hidden');
        return true;
    },

    /**
     * Wires the banner buttons once; every push after the first one reuses the handlers.
     */
    _wireDebugStopBanner(banner) {
        if (banner.dataset.wired === 'true') return;
        banner.dataset.wired = 'true';

        const explainBtn = document.getElementById('debug-stop-explain');
        if (explainBtn) {
            explainBtn.addEventListener('click', () => LmApi._explainDebugStop());
        }

        const dismissBtn = document.getElementById('debug-stop-dismiss');
        if (dismissBtn) {
            dismissBtn.addEventListener('click', () => LmApi.setDebugStop(null));
        }
    },

    /**
     * Asks the host to read the full context of the stop and send it to the chat.
     */
    async _explainDebugStop() {
        const banner = document.getElementById('debug-stop-banner');
        const explainBtn = document.getElementById('debug-stop-explain');
        if (!banner || !explainBtn || explainBtn.disabled) return;

        const clickedKey = banner.dataset.stopKey;
        explainBtn.disabled = true;

        let sent = false;
        try {
            sent = await bridgeClient.explainDebugStopAsync();
        } catch (err) {
            console.error('Failed to request the debugger stop explanation', err);
        } finally {
            explainBtn.disabled = false;
            if (sent === true && banner.dataset.stopKey === clickedKey) LmApi.setDebugStop(null);
        }
    },

    /**
     * Identity of a stop, so that the answer of an older request cannot hide a newer stop.
     */
    _debugStopKey(payload) {
        return [payload.kind, payload.reason, payload.file, payload.line].join('|');
    },

    /**
     * "Breakpoint hit · SomeService.cs:42" — what the debugger stopped on, plus the file and the line.
     */
    _formatDebugStopCaption(payload) {
        let caption;
        if (payload.kind === 'Exception') {
            caption = payload.reason === 'ExceptionNotHandled' ? 'Unhandled exception' : 'Exception thrown';
        } else {
            caption = payload.reason === 'UserBreak' ? 'Break mode' : 'Breakpoint hit';
        }

        if (payload.file) {
            const segments = String(payload.file).split(/[\\/]/);
            const fileName = segments[segments.length - 1];
            caption += ` · ${fileName}${payload.line ? ':' + payload.line : ''}`;
        }

        return caption;
    },

    /**
     * Moves the caret in the currently focused editable element.
     * Supports Home/End/ArrowLeft/ArrowRight; pass isShift to extend the selection
     * and isCtrl for document/word-level movement:
     *   - Ctrl+Home / Ctrl+End  -> very start / very end of the whole text
     *   - Ctrl+ArrowLeft/Right  -> jump word by word (whitespace separated)
     */
    moveCaret(keyName, isShift, isCtrl) {
        const el = document.activeElement;
        if (!el || !('selectionStart' in el)) return false;

        const caret = el.selectionDirection === 'backward'
            ? el.selectionStart
            : el.selectionEnd;

        const text = el.value;
        const textLength = text.length;

        let anchor = el.dataset.selAnchor
            ? Number.parseInt(el.dataset.selAnchor, 10)
            : caret;

        const isWordChar = (ch) => !/\s/.test(ch);

        const getLineStart = (pos) => {
            const idx = text.lastIndexOf('\n', pos - 1);
            return idx === -1 ? 0 : idx + 1;
        };

        const getLineEnd = (pos) => {
            const idx = text.indexOf('\n', pos);
            return idx === -1 ? textLength : idx;
        };

        const getWordStart = (pos) => {
            let i = pos;
            while (i > 0 && !isWordChar(text[i - 1])) i--;
            while (i > 0 && isWordChar(text[i - 1])) i--;
            return i;
        };

        const getWordEnd = (pos) => {
            let i = pos;
            while (i < textLength && !isWordChar(text[i])) i++;
            while (i < textLength && isWordChar(text[i])) i++;
            return i;
        };

        let newPos = caret;

        if (keyName === 'Home') {
            newPos = isCtrl ? 0 : getLineStart(caret);
        } else if (keyName === 'End') {
            newPos = isCtrl ? textLength : getLineEnd(caret);
        } else if (keyName === 'ArrowLeft') {
            if (isCtrl) {
                newPos = getWordStart(caret);
            } else if (caret > 0) {
                newPos = caret - 1;
            }
        } else if (keyName === 'ArrowRight') {
            if (isCtrl) {
                newPos = getWordEnd(caret);
            } else if (caret < textLength) {
                newPos = caret + 1;
            }
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
