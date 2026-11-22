import { createCallback } from '@app/lib/callback.js';
import { AsyncGuard } from '@app/lib/async.guard.js';
import { escapeHtml } from '@app/lib/escape.js';
import { formatTokens as formatTokensShort } from '@app/lib/formatting.js';

const SOURCE_LABELS = {
    main: 'Main',
    subagent: 'Subagents',
    compaction: 'Compaction'
};

const PERIODS = ['current', 'today', 'yesterday', '3d', '7d', '30d'];

/**
 * Periods that are exactly one local day: their sessions are listed without a day header.
 */
const SINGLE_DAY_PERIODS = ['today', 'yesterday'];

/**
 * Formats a duration in milliseconds the way the summary line shows it: 45s, 18m 40s, 3h 12m.
 */
function formatDuration(ms) {
    if (!ms || ms < 0) return '0s';

    const totalSeconds = Math.round(ms / 1000);
    const hours = Math.floor(totalSeconds / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);
    const seconds = totalSeconds % 60;

    if (hours > 0) return minutes > 0 ? `${hours}h ${minutes}m` : `${hours}h`;
    if (minutes > 0) return seconds > 0 && minutes < 10 ? `${minutes}m ${seconds}s` : `${minutes}m`;
    return `${seconds}s`;
}

/**
 * Token count with two decimals in millions (16.19m), so the figures of neighbouring rows still add up.
 */
function formatTokens(n) {
    const num = Number(n);
    if (Number.isFinite(num) && Math.abs(num) >= 1_000_000)
        return (num / 1_000_000).toFixed(2) + "m";
    return formatTokensShort(n);
}

/**
 * Exact count with thousands separators, for the tooltip of a rounded figure.
 */
function exactTokens(n) {
    return (Number(n) || 0).toLocaleString("en-US");
}

/**
 * Speed of a group, or an em dash when nothing was measured.
 */
function formatSpeed(tokensPerSecond) {
    return tokensPerSecond ? `${Math.round(tokensPerSecond)} t/s` : '—';
}

/**
 * "in · out" pair used by every block row.
 */
function formatPair(prompt, completion) {
    return `${formatTokens(prompt || 0)} · ${formatTokens(completion || 0)}`;
}

/**
 * Grid header of a block: the block name, then the column captions.
 */
function renderGridHead(title, columns) {
    return `
        <div class="usage-block-title usage-grid">
            <span>${title}</span>
            ${columns.map(column => `<span class="usage-col">${column}</span>`).join('')}
        </div>
    `;
}

/**
 * UsageStatsDialog - token usage of the current session or of a period: totals, BY SOURCE, BY MODEL and the
 * sessions grouped by day.
 */
export class UsageStatsDialog {
    constructor() {
        this.period = 'current';
        this.data = null;
        this.filterText = '';
        this.expandedDays = new Set();
        this.expandedSessions = new Set();
        this._guard = new AsyncGuard();
        this.el = null;

        this.onLoadStats = createCallback();

        this._onPeriodClick = null;
        this._onRefreshClick = null;
        this._onCloseClick = null;
        this._onContainerClick = null;
        this._onFilterInput = null;
    }

    _getElements() {
        const dialog = document.getElementById('usage-stats-dialog');
        if (!dialog) return {};

        return {
            dialog,
            nav: dialog.querySelector('#usage-period-nav'),
            container: dialog.querySelector('#usage-stats-container'),
            refreshBtn: dialog.querySelector('#usage-refresh-btn'),
            closeBtn: dialog.querySelector('#usage-stats-close')
        };
    }

    async _load(showLoadingState = true) {
        const generation = this._guard.start();

        try {
            if (showLoadingState) this._showLoadingState();

            const result = await this.onLoadStats.emitResult(this.period);
            if (!this._guard.isCurrent(generation)) return;

            if (!result || result.success === false) {
                const message = result?.error?.message || result?.error || 'Failed to load usage statistics';
                this._showErrorState(message);
                return;
            }

            this.data = result.data;
            this._resetExpansion();
            this._render();
        } catch (error) {
            console.error('Failed to load usage statistics:', error);
            if (this._guard.isCurrent(generation)) {
                this._showErrorState(`Failed to load usage statistics: ${error.message}`);
            }
        }
    }

    /**
     * The newest day starts expanded, the rest stay collapsed until they are opened.
     */
    _resetExpansion() {
        this.expandedDays = new Set();

        const days = Array.isArray(this.data?.days) ? this.data.days : [];
        if (days.length > 0) this.expandedDays.add(days[0].date);
    }

    _showLoadingState() {
        if (!this.el.container) return;

        this.el.container.innerHTML = `
            <div class="loading-placeholder">
                <div class="spinner"></div>
                <span>Loading usage...</span>
            </div>
        `;
    }

    _showErrorState(message) {
        if (!this.el.container) return;

        this.el.container.innerHTML = `
            <div class="error-placeholder">
                <span style="color: var(--danger-color); padding: 20px;">Error: ${escapeHtml(message)}</span>
            </div>
        `;
    }

    /**
     * Empty state: either nothing was recorded for the selection, or logging is off and nothing ever will be.
     */
    _showEmptyState() {
        if (!this.el.container) return;

        const loggingEnabled = this.data?.loggingEnabled !== false;
        const message = loggingEnabled
            ? 'No usage recorded for this period.'
            : 'Usage is recorded only when "Save chat history & stats" is enabled in Settings.';

        this.el.container.innerHTML = `
            <div class="empty-placeholder">
                <span>${escapeHtml(message)}</span>
            </div>
        `;
    }

    _render() {
        if (!this.el.container) return;

        if (!this.data || !this.data.hasData) {
            this._showEmptyState();
            return;
        }

        const blocks = [];

        blocks.push(this._renderTotals(this.data.totals || {}));
        blocks.push(this._renderSources(this.data.sources));
        blocks.push(this._renderModels(this.data.models));

        if (this.period !== 'current') {
            blocks.push(this._renderSessions(this.data.days));
        }

        this.el.container.innerHTML = blocks.join('');

        // Re-render replaces the markup, so the filter box gets its text back before the cards are filtered.
        const filterInput = this.el.container.querySelector('#usage-session-filter');
        if (filterInput) filterInput.value = this.filterText;

        this._applyFilter();
    }

    /**
     * The card says how much work was done, not how many tokens: a token sum would add free local tokens to paid,
     * mostly cached cloud ones. Tokens, cache and speed live in BY MODEL, where each number belongs to one model.
     */
    _renderTotals(totals) {
        // Prompts are the user's messages to the main chat. Their time is on the Main row of BY SOURCE, not here.
        const prompts = totals.requests || 0;
        const parts = [`${prompts} prompt${prompts === 1 ? '' : 's'}`];

        const days = Array.isArray(this.data?.days) ? this.data.days : [];
        if (this.period !== 'current' && days.length > 0) {
            const sessions = days.reduce((total, day) => total + (day.sessions?.length || 0), 0);
            parts.push(`${sessions} session${sessions === 1 ? '' : 's'}`);
        }

        const sources = Array.isArray(this.data?.sources) ? this.data.sources : [];
        const subagentRuns = sources.filter(row => row.source === 'subagent').reduce((total, row) => total + (row.runs || 0), 0);
        if (subagentRuns > 0) {
            parts.push(`${subagentRuns} subagent${subagentRuns === 1 ? '' : 's'}`);
        }

        if (totals.failed > 0) {
            parts.push(`<span class="usage-failed">${totals.failed} failed</span>`);
        }

        // Only the current session: over a period it would mix sessions and models with different context windows.
        const peak = this.period === 'current' && totals.peakContextPercent !== null && totals.peakContextPercent !== undefined
            ? `<div class="usage-summary-peak">peak ctx ${Math.round(totals.peakContextPercent * 100)}%</div>`
            : '';

        return `
            <div class="usage-summary usage-card">
                <div class="usage-summary-meta">${parts.join(' · ')}</div>
                ${peak}
            </div>
        `;
    }

    _renderSources(sources) {
        const rows = Array.isArray(sources) ? sources : [];
        if (rows.length === 0) return '';

        const body = rows.map(row => {
            const label = SOURCE_LABELS[row.source] || row.source;
            const runs = row.source === 'subagent'
                ? `<span class="usage-badge">×${row.runs}</span>`
                : '';

            const failed = row.failed > 0
                ? `<div class="usage-row-caption usage-failed">${row.failed} failed</div>`
                : '';

            const duration = row.durationMs > 0 ? formatDuration(row.durationMs) : '—';
            const durationTitle = row.source === 'main'
                ? 'From sending a prompt to its answer, subagent runs included'
                : 'Time while at least one run was working; part of the Main time';

            return `
                <div class="usage-row usage-grid">
                    <span class="usage-row-name"><span class="usage-row-label">${escapeHtml(label)}</span>${runs}</span>
                    <span class="usage-num usage-in" title="${exactTokens(row.prompt)}">${formatTokens(row.prompt || 0)}</span>
                    <span class="usage-num usage-out" title="${exactTokens(row.completion)}">${formatTokens(row.completion || 0)}</span>
                    <span class="usage-num usage-extra" title="${durationTitle}">${duration}</span>
                </div>
                ${failed}
            `;
        }).join('');

        return `
            <div class="usage-block usage-card">
                ${renderGridHead('BY SOURCE', ['In', 'Out', 'Time'])}
                ${body}
            </div>
        `;
    }

    _renderModels(models) {
        const rows = Array.isArray(models) ? models : [];
        if (rows.length === 0) return '';

        const body = rows.map(row => {
            const approximate = row.estimated ? '≈ ' : '';
            const provider = row.showProviderLabel && row.providerLabel
                ? `<span class="usage-badge">${escapeHtml(row.providerLabel)}</span>`
                : '';

            const tooltip = [row.provider, row.baseUrl].filter(Boolean).join(' · ');

            // Only for providers that report the cache; for the rest (LM Studio) the share is unknown, not 0%.
            const cache = row.cachedPercent !== null && row.cachedPercent !== undefined
                ? `<div class="usage-row-caption usage-cache" title="${exactTokens(row.cached)}">${formatTokens(row.cached || 0)} cached · ${row.cachedPercent}%</div>`
                : '';

            return `
                <div class="usage-row usage-grid" title="${escapeHtml(tooltip)}">
                    <span class="usage-row-name"><span class="usage-row-label">${escapeHtml(row.model || 'unknown')}</span>${provider}</span>
                    <span class="usage-num usage-in" title="${exactTokens(row.prompt)}">${approximate}${formatTokens(row.prompt || 0)}</span>
                    <span class="usage-num usage-out" title="${exactTokens(row.completion)}">${formatTokens(row.completion || 0)}</span>
                    <span class="usage-num usage-extra">${formatSpeed(row.tokensPerSecond)}</span>
                </div>
                ${cache}
            `;
        }).join('');

        return `
            <div class="usage-block usage-card">
                ${renderGridHead('BY MODEL', ['In', 'Out', 'Speed'])}
                ${body}
            </div>
        `;
    }

    _renderSessions(days) {
        const groups = Array.isArray(days) ? days : [];
        if (groups.length === 0) return '';

        const body = groups.map(day => this._renderDay(day)).join('');

        return `
            <div class="usage-block usage-sessions">
                <div class="usage-block-title usage-sessions-title">
                    <span>SESSIONS (${groups.reduce((total, day) => total + (day.sessions?.length || 0), 0)})</span>
                    <input type="text" id="usage-session-filter" placeholder="Filter..." autocomplete="off" />
                </div>
                ${body}
            </div>
        `;
    }

    _renderDay(day) {
        const expanded = this.expandedDays.has(day.date);
        const count = Array.isArray(day.sessions) ? day.sessions.length : 0;
        const caret = expanded ? '▾' : '▸';
        const countLabel = expanded ? '' : ` (${count})`;
        const label = this._formatDayLabel(day.date);

        const sessions = expanded
            ? (day.sessions || []).map(session => this._renderSession(session)).join('')
            : '';

        if (SINGLE_DAY_PERIODS.includes(this.period)) {
            return `
                <div class="usage-day" data-day="${escapeHtml(day.date)}">
                    <div class="usage-day-body">${(day.sessions || []).map(session => this._renderSession(session)).join('')}</div>
                </div>
            `;
        }

        return `
            <div class="usage-day" data-day="${escapeHtml(day.date)}">
                <div class="usage-day-header" data-day-toggle="${escapeHtml(day.date)}">
                    <span class="usage-day-caret">${caret}</span>
                    <span class="usage-day-label">${escapeHtml(label)}${countLabel}</span>
                    <span class="usage-day-values">${formatPair(day.prompt, day.completion)}</span>
                </div>
                <div class="usage-day-body">${sessions}</div>
            </div>
        `;
    }

    _renderSession(session) {
        const sessionId = session.sessionId || '';
        const expanded = this.expandedSessions.has(sessionId);
        const title = session.title || 'Untitled';
        const time = this._formatTime(session.first);
        const model = session.model ? `<span class="usage-badge">${escapeHtml(session.model)}</span>` : '';

        const failed = session.failed > 0
            ? ` <span class="usage-failed">⚠ ${session.failed} failed</span>`
            : '';

        const detail = expanded
            ? `<div class="usage-session-detail">
                    ${this._renderSources(session.sources)}
                    ${this._renderModels(session.models)}
               </div>`
            : '';

        return `
            <div class="usage-session-card" data-session="${escapeHtml(sessionId)}" data-model="${escapeHtml(session.model || '')}">
                <div class="usage-session-row">
                    <span class="usage-session-caret">${expanded ? '▾' : '▸'}</span>
                    <span class="usage-session-title">${escapeHtml(title)}</span>
                </div>
                <div class="usage-session-meta">
                    <span>${time}</span>${model}<span>${formatTokens(session.prompt || 0)} in · ${session.requests || 0} req</span>${failed}
                </div>
                ${detail}
            </div>
        `;
    }

    _formatDayLabel(date) {
        if (!date) return '';

        const today = new Date();
        const yesterday = new Date();
        yesterday.setDate(today.getDate() - 1);

        if (date === this._isoDate(today)) return 'TODAY';
        if (date === this._isoDate(yesterday)) return 'YESTERDAY';

        const parsed = new Date(`${date}T00:00:00`);
        return isNaN(parsed.getTime()) ? date : parsed.toLocaleDateString();
    }

    _isoDate(date) {
        const month = String(date.getMonth() + 1).padStart(2, '0');
        const day = String(date.getDate()).padStart(2, '0');

        return `${date.getFullYear()}-${month}-${day}`;
    }

    _formatTime(first) {
        if (!first) return '';

        const date = new Date(first);
        if (isNaN(date.getTime())) return '';

        return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    }

    /**
     * Hides the session cards that do not match the filter; a day with no visible card is hidden as well.
     */
    _applyFilter() {
        if (!this.el.container) return;

        const filter = this.filterText;

        this.el.container.querySelectorAll('.usage-session-card').forEach(card => {
            const title = (card.querySelector('.usage-session-title')?.textContent || '').toLowerCase();
            const model = (card.dataset.model || '').toLowerCase();
            const matches = filter.length === 0 || title.includes(filter) || model.includes(filter);

            card.classList.toggle('usage-hidden', !matches);
        });

        this.el.container.querySelectorAll('.usage-day').forEach(day => {
            const visible = Array.from(day.querySelectorAll('.usage-session-card'))
                .some(card => !card.classList.contains('usage-hidden'));

            day.classList.toggle('usage-hidden', !visible && filter.length > 0);
        });
    }

    _setPeriod(period) {
        if (!PERIODS.includes(period) || period === this.period) return;

        this.period = period;
        this.data = null;
        this.expandedSessions = new Set();
        this.filterText = '';

        this.el.dialog.querySelectorAll('.usage-period-btn').forEach(button => {
            button.classList.toggle('active', button.dataset.period === period);
        });

        this._load();
    }

    _attachEvents() {
        this._onPeriodClick = (e) => {
            const button = e.target.closest('.usage-period-btn');
            if (!button) return;

            e.stopPropagation();
            this._setPeriod(button.dataset.period);
        };

        this._onRefreshClick = (e) => {
            e.stopPropagation();
            this.el.refreshBtn.classList.add('spinning');
            this._load(false).finally(() => this.el?.refreshBtn?.classList.remove('spinning'));
        };

        this._onCloseClick = () => {
            this.el.dialog.close();
        };

        this._onFilterInput = (e) => {
            if (e.target?.id !== 'usage-session-filter') return;

            this.filterText = e.target.value.toLowerCase();
            this._applyFilter();
        };

        this._onContainerClick = (e) => {
            const dayHeader = e.target.closest('[data-day-toggle]');
            if (dayHeader) {
                e.stopPropagation();
                const date = dayHeader.dataset.dayToggle;
                if (this.expandedDays.has(date)) {
                    this.expandedDays.delete(date);
                } else {
                    this.expandedDays.add(date);
                }
                this._render();
                return;
            }

            const card = e.target.closest('.usage-session-card');
            if (!card) return;

            e.stopPropagation();
            const sessionId = card.dataset.session;
            if (!sessionId) return;

            if (this.expandedSessions.has(sessionId)) {
                this.expandedSessions.delete(sessionId);
            } else {
                this.expandedSessions.add(sessionId);
            }
            this._render();
        };

        this.el.nav.addEventListener('click', this._onPeriodClick);
        this.el.refreshBtn.addEventListener('click', this._onRefreshClick);
        this.el.closeBtn.addEventListener('click', this._onCloseClick);
        this.el.container.addEventListener('click', this._onContainerClick);
        this.el.container.addEventListener('input', this._onFilterInput);
    }

    _detachEvents() {
        this.el?.nav?.removeEventListener('click', this._onPeriodClick);
        this.el?.refreshBtn?.removeEventListener('click', this._onRefreshClick);
        this.el?.closeBtn?.removeEventListener('click', this._onCloseClick);
        this.el?.container?.removeEventListener('click', this._onContainerClick);
        this.el?.container?.removeEventListener('input', this._onFilterInput);

        this._onPeriodClick = null;
        this._onRefreshClick = null;
        this._onCloseClick = null;
        this._onContainerClick = null;
        this._onFilterInput = null;
    }

    async show() {
        if (this.el?.dialog?.open) {
            this.el.dialog.close();
        }

        this.el = this._getElements();
        this.period = 'current';
        this.data = null;
        this.filterText = '';
        this.expandedSessions = new Set();
        this._guard.invalidate();

        if (!this.el.dialog) throw new Error('Dialog #usage-stats-dialog not found');

        this.el.dialog.querySelectorAll('.usage-period-btn').forEach(button => {
            button.classList.toggle('active', button.dataset.period === 'current');
        });

        this._attachEvents();
        this.el.dialog.showModal();

        if (document.activeElement instanceof HTMLElement && this.el.dialog.contains(document.activeElement))
            document.activeElement.blur();

        await this._load();

        return new Promise((resolve) => {
            const onClose = () => {
                try {
                    this._detachEvents();
                    this.onLoadStats.off();
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
