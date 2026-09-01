import { createCallback } from '@app/lib/callback.js';
import { AsyncGuard } from '@app/lib/async.guard.js';
import { escapeHtml } from '@app/lib/escape.js';

export class ChatHistoryDialog {
    constructor() {
        this.sessions = [];
        this.filterText = '';
        this.sortAsc = false;
        this.isLoading = false;
        this._guard = new AsyncGuard();
        this.el = null;

        this.onLoadSessions = createCallback();
        this.onLoadSession = createCallback();

        this._onRefreshClick = null;
        this._onCloseClick = null;
        this._onFilterInput = null;
        this._onSortClick = null;
        this._onSessionClick = null;
    }

    _getElements() {
        const dialog = document.getElementById('chat-history-dialog');
        if (!dialog) return {};

        return {
            dialog,
            container: dialog.querySelector('#chat-history-container'),
            refreshBtn: dialog.querySelector('#chat-history-refresh-btn'),
            filterInput: dialog.querySelector('#chat-history-filter-input'),
            sortBtn: dialog.querySelector('#chat-history-sort-btn'),
            closeBtn: dialog.querySelector('#chat-history-close'),
        };
    }

    async _loadSessions(showLoadingState = true) {
        const generation = this._guard.start();
        this.isLoading = true;
        try {
            if (showLoadingState) this._showLoadingState();
            const result = await this.onLoadSessions.emitResult();
            if (!this._guard.isCurrent(generation)) return;
            if (!result?.success) {
                this._showErrorState(result?.error?.message || 'Failed to load chat history');
                return;
            }

            const data = result.data ?? result;
            this.sessions = Array.isArray(data?.sessions) ? data.sessions : [];
            this._renderSessions();
        } catch (error) {
            console.error('Failed to load chat history:', error);
            if (this._guard.isCurrent(generation)) {
                this._showErrorState(`Failed to load chat history: ${error.message}`);
            }
        } finally {
            if (this.el) {
                this.isLoading = false;
            }
        }
    }

    _showLoadingState() {
        if (this.el.container) {
            this.el.container.innerHTML = `
                <div class="loading-placeholder">
                    <div class="spinner"></div>
                    <span>Loading chat history...</span>
                </div>
            `;
        }
    }

    _showErrorState(errorMessage) {
        if (this.el.container) {
            this.el.container.innerHTML = `
                <div class="error-placeholder">
                    <span style="color: var(--danger-color); padding: 20px;">Error: ${escapeHtml(errorMessage)}</span>
                </div>
            `;
        }
    }

    _showEmptyState() {
        if (!this.el.container) return;
        const isFiltering = this.filterText.length > 0;
        this.el.container.innerHTML = `
            <div class="empty-placeholder">
                <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1" stroke-linecap="round" stroke-linejoin="round">
                    <circle cx="11" cy="11" r="8"></circle>
                    <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
                </svg>
                <span>
                    ${isFiltering
                ? `No sessions match "<strong>${escapeHtml(this.filterText)}</strong>"`
                : 'No chat history available at the moment.'}
                </span>
            </div>
        `;
    }

    _renderSessions() {
        if (!this.el.container) return;

        let displayList = this.sessions.filter(session => {
            const promptMatch = (session.prompt || '').toLowerCase().includes(this.filterText);
            return promptMatch;
        });

        if (displayList.length === 0) {
            this._showEmptyState();
            return;
        }

        displayList.sort((a, b) => {
            const timeA = a.timestamp || '';
            const timeB = b.timestamp || '';
            return this.sortAsc ? timeA.localeCompare(timeB) : timeB.localeCompare(timeA);
        });

        const sessionsHtml = displayList.map(session => {
            const prompt = escapeHtml(session.prompt || '(empty prompt)');
            const timestamp = this._formatTimestamp(session.timestamp);
            const count = session.messageCount || 0;
            const stepLabel = count === 1 ? 'step' : 'steps';
            const sessionId = escapeHtml(session.sessionId);

            return `
                <div class="chat-history-card" data-session-id="${sessionId}">
                    <div class="chat-history-card-body">
                        <div class="chat-history-prompt">${prompt}</div>
                        <div class="chat-history-meta">
                            <span class="chat-history-time">${timestamp}</span>
                            <span class="chat-history-count">${count} ${stepLabel}</span>
                        </div>
                    </div>
                    <button class="btn-small btn-secondary chat-history-load" type="button">Load</button>
                </div>
            `;
        }).join('');

        this.el.container.innerHTML = sessionsHtml;
    }

    _formatTimestamp(timestamp) {
        if (!timestamp) return '';
        const date = new Date(timestamp);
        if (isNaN(date.getTime())) return escapeHtml(timestamp);
        return date.toLocaleString();
    }

    async _loadSession(sessionId) {
        try {
            const result = await this.onLoadSession.emitResult(sessionId);
            if (result?.success === false) {
                const errorMessage = result?.error?.message || result?.error || 'Failed to load session';
                this._showErrorState(errorMessage);
                return;
            }
            this.el.dialog.close();
        } catch (error) {
            console.error('Session load failed:', error);
            this._showErrorState(`Session load failed: ${error.message}`);
        }
    }

    _attachEvents() {
        this._onRefreshClick = (e) => {
            e.stopPropagation();
            this.el.refreshBtn.classList.add('spinning');
            this._loadSessions(false).finally(() => {
                this.el.refreshBtn.classList.remove('spinning');
            });
        };
        this._onCloseClick = () => {
            this.el.dialog.close();
        };
        this._onFilterInput = (e) => {
            this.filterText = e.target.value.toLowerCase();
            this._renderSessions();
        };
        this._onSortClick = () => {
            this.sortAsc = !this.sortAsc;
            this._renderSessions();
        };
        this._onSessionClick = async (e) => {
            const card = e.target.closest('.chat-history-card');
            if (!card) return;
            e.stopPropagation();

            if (!e.target.closest('.chat-history-load')) return;

            const sessionId = card.dataset.sessionId;
            if (sessionId) await this._loadSession(sessionId);
        };

        this.el.filterInput.addEventListener('input', this._onFilterInput);
        this.el.sortBtn.addEventListener('click', this._onSortClick);
        this.el.refreshBtn.addEventListener('click', this._onRefreshClick);
        this.el.closeBtn.addEventListener('click', this._onCloseClick);
        this.el.container.addEventListener('click', this._onSessionClick);
    }

    _detachEvents() {
        this.el.filterInput.removeEventListener('input', this._onFilterInput);
        this.el.sortBtn.removeEventListener('click', this._onSortClick);
        this.el.refreshBtn.removeEventListener('click', this._onRefreshClick);
        this.el.closeBtn.removeEventListener('click', this._onCloseClick);
        this.el.container.removeEventListener('click', this._onSessionClick);
        this._onRefreshClick = null;
        this._onCloseClick = null;
        this._onFilterInput = null;
        this._onSortClick = null;
        this._onSessionClick = null;
    }

    async show() {
        if (this.el?.dialog?.open) {
            this.el.dialog.close();
        }

        this.el = this._getElements();
        this.filterText = '';
        this.sortAsc = false;
        this._guard.invalidate();

        if (!this.el.dialog) throw new Error('Dialog #chat-history-dialog not found');

        this.el.filterInput.value = '';

        this._attachEvents();

        this.el.dialog.showModal();

        if (this.sessions.length) {
            this._renderSessions();
        } else {
            await this._loadSessions();
        }

        return new Promise((resolve) => {
            const onClose = () => {
                try {
                    this._detachEvents();
                    this.onLoadSessions.off();
                    this.onLoadSession.off();
                    this.el.dialog.removeEventListener('close', onClose);
                    resolve(null);
                } catch (err) {
                    console.error('Error during dialog close cleanup:', err);
                    resolve(null);
                } finally {
                    this._guard.invalidate();
                    this.el = null;
                }
            };
            this.el.dialog.addEventListener('close', onClose);
        });
    }
}
