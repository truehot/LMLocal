import { createCallback } from '@app/lib/callback.js';
import { formatTokens, formatPercent, roundPercent } from '@app/lib/formatting.js';
import { AppStatus } from '@app/store/app.status.js';

/**
 * ToolbarComponent - manages the toolbar UI: displays model name, token usage and connection status.
 */
class ToolbarComponent {
    constructor() {
        this.elements = {};
        this.onModelNameClick = createCallback();
        this.isConnecting = true;
        this.timeoutId = null;
        this._hasShownModelInfo = false;
        this._modelState = null;
        this._appState = null;
        this._sessionState = null;
        this._sessionStreamed = 0;
        this._roundTokens = 0;
        this._lastRenderedTitle = null;
    }

    _getElements() {
        return {
            modelName: document.getElementById('model-name'),
            separator: document.getElementById('status-separator'),
            tokenBarFill: document.getElementById('token-bar-fill'),
            barInfoTooltip: document.getElementById('info-tooltip'),
            modelInfo: document.getElementById('model-info')
        };
    }

    _updateModelName(modelState, prevModelState) {
        if (modelState.modelName === prevModelState?.modelName) return;

        if (this.elements.modelName && modelState.modelName) {
            this.elements.modelName.textContent = modelState.modelName;
        } else {
            this.elements.modelName.textContent = 'Select model...';
        }
    }

    /**
     * True while the model is actively producing tokens for the current round.
     */
    _isStreamingStatus(status) {
        return status === AppStatus.STREAMING || status === AppStatus.THINKING;
    }

    _resetStreamCounters() {
        this._sessionStreamed = 0;
        this._roundTokens = 0;
    }

    /**
     * Renders the context usage bar and its tooltip.
     */
    _renderTokenBar() {
        const modelState = this._modelState;
        if (!modelState || !this.elements.tokenBarFill) return;

        const baseTokens = this._sessionState?.contextTokens || 0;
        const occupiedTokens = baseTokens + this._sessionStreamed + this._roundTokens;
        const tokenMax = modelState.tokenMax || 0;
        const supportsMax = !!modelState.supportsMaxTokens;
        const percent = supportsMax && tokenMax > 0
            ? Math.min((occupiedTokens / tokenMax) * 100, 100)
            : 0;

        const percentRounded = roundPercent(percent);

        const title = this._composeTooltipTitle(occupiedTokens, tokenMax, supportsMax, percentRounded);

        if (title === this._lastRenderedTitle) return;
        this._lastRenderedTitle = title;

        this.elements.tokenBarFill.style.display = supportsMax ? 'block' : 'none';
        if (supportsMax) {
            this.elements.tokenBarFill.style.transform = `scaleY(${percentRounded / 100})`;
        }

        if (this.elements.barInfoTooltip) this.elements.barInfoTooltip.title = title;
        if (this.elements.modelName) this.elements.modelName.title = title;
    }

    /**
     * Builds the tooltip text.
     */
    _composeTooltipTitle(occupiedTokens, tokenMax, supportsMax, percentRounded) {
        let text = supportsMax
            ? `Context usage: ${formatPercent(percentRounded)} \n${occupiedTokens.toLocaleString('en-US')} / ${tokenMax.toLocaleString('en-US')} tokens`
            : `Context usage: ${occupiedTokens.toLocaleString('en-US')}`;

        const promptTokens = this._sessionState?.sessionPromptTokens || 0;
        const completionTokens = this._sessionState?.sessionCompletionTokens || 0;
        const cachedTokens = this._sessionState?.sessionCachedTokens || 0;

        if (promptTokens > 0 || completionTokens > 0) {
            text += `\nSession: ${formatTokens(promptTokens)} in${this._cachedShare(promptTokens, cachedTokens)} · ${formatTokens(completionTokens)} out`;
        }

        return text;
    }

    /**
     * Cached input tokens are a SUBSET of the prompt, so they are rendered inside the `in` term
     * and never as an extra one.
     */
    _cachedShare(promptTokens, cachedTokens) {
        if (cachedTokens <= 0 || promptTokens <= 0) return '';
        return ` (${formatTokens(Math.min(cachedTokens, promptTokens))} cached)`;
    }

    _onModelNameClick = async (e) => {
        e.stopPropagation();
        if (this.isConnecting === true) return;
        this.elements.modelName.classList.add('generating');
        clearTimeout(this.timeoutId);
        this.timeoutId = setTimeout(() => {
            this.elements?.modelName?.classList.remove('generating');
        }, 500);
        await this.onModelNameClick.emitResult();
    };

    _attachEvents() {
        if (this.elements.modelName) {
            this.elements.modelName.addEventListener('click', this._onModelNameClick);
            this.elements.modelName.style.cursor = 'pointer';
        }
    }

    _detachEvents() {
        if (this.elements.modelName) {
            this.elements.modelName.removeEventListener('click', this._onModelNameClick);
        }
        clearTimeout(this.timeoutId);
    }

    setup() {
        this.elements = this._getElements();
        this._attachEvents();

        return this;
    }

    _showModelInfo() {
        if (!this.elements) return;
        if (this.elements.modelInfo) this.elements.modelInfo.style.display = "block";
        if (this.elements.separator) this.elements.separator.style.display = "block";
    }

    reset() {
        this._detachEvents();
        this.elements = {};
        this.isConnecting = true;
        clearTimeout(this.timeoutId);
        this._hasShownModelInfo = false;
        this._modelState = null;
        this._appState = null;
        this._sessionState = null;
        this._resetStreamCounters();
        this._lastRenderedTitle = null;
    }

    updateAppState(state, prevState) {
        this.isConnecting = state.status == AppStatus.CONNECTING;
        this._appState = state;

        if (prevState?.status == AppStatus.CONNECTING && state.status != AppStatus.CONNECTING && !this._hasShownModelInfo) {
            this._showModelInfo();
            this._hasShownModelInfo = true;
        }

        if (state.status === AppStatus.PROCESSING && (prevState?.status === AppStatus.IDLE || prevState?.status === AppStatus.ERROR)) {
            this._resetStreamCounters();
        }

        if (state.status === AppStatus.IDLE &&
            prevState &&
            prevState.status !== AppStatus.IDLE &&
            prevState.status !== AppStatus.ERROR) {
            this._resetStreamCounters();
        }

        if (state.status === AppStatus.CLEARING ||
            (state.status === AppStatus.ERROR && prevState?.status === AppStatus.CLEARING)) {
            this._resetStreamCounters();
        }

        if (this._isStreamingStatus(state.status)) {
            const currentRoundTokens = state.roundTokens || 0;

            if (currentRoundTokens < this._roundTokens) {
                this._sessionStreamed += this._roundTokens;
            }

            this._roundTokens = currentRoundTokens;
        }

        this._renderTokenBar();
    }

    updateModelState(modelState, prevModelState) {
        this._modelState = modelState;
        this._updateModelName(modelState, prevModelState);
        this._renderTokenBar();
    }

    /**
     * Conversation metrics (context base + cumulative traffic) live in sessionStore.
     */
    updateSessionState(sessionState, prevSessionState) {
        this._sessionState = sessionState;

        if (sessionState.contextTokens !== prevSessionState?.contextTokens) {
            this._resetStreamCounters();
        }

        this._renderTokenBar();
    }
}

const toolbarComponent = new ToolbarComponent();
export { toolbarComponent };
