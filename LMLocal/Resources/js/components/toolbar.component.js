import { createCallback } from '@app/lib/callback.js';
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

    _updateTokenBar(modelState, prevModelState) {
        if (modelState.tokenMax === prevModelState?.tokenMax &&
            modelState.tokenUsed === prevModelState?.tokenUsed &&
            modelState.supportsMaxTokens === prevModelState?.supportsMaxTokens) return;

        if (!modelState.supportsMaxTokens) {
            this.elements.tokenBarFill.style.display = 'none';

            const text = `Token usage: ${modelState.tokenUsed.toLocaleString('en-US') }`;
            this.elements.barInfoTooltip.title = text;
            this.elements.modelName.title = text;

            return;
        }

        this.elements.tokenBarFill.style.display = 'block';
        const percent = modelState.tokenMax > 0
            ? Math.min((modelState.tokenUsed / modelState.tokenMax) * 100, 100)
            : 0;
        this.elements.tokenBarFill.style.transform = `scaleY(${percent / 100})`;

        const text = `Last context: ${Math.round(percent)}% \n${modelState.tokenUsed.toLocaleString('en-US')} / ${modelState.tokenMax.toLocaleString('en-US') } tokens`;
        this.elements.barInfoTooltip.title = text;
        this.elements.modelName.title = text;
    }

    _onModelNameClick = async (e) => {
        e.stopPropagation();
        if (this.isConnecting === true) return;
        this.elements.modelName.classList.add('generating');
        clearTimeout(this.timeoutId);
        this.timeoutId = setTimeout(() => {
            this.elements?.modelName?.classList.remove('generating');
        }, 1000);
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
    }

    updateAppState(state, prevState) {
        this.isConnecting = state.status == AppStatus.CONNECTING;

        if (prevState?.status == AppStatus.CONNECTING && state.status != AppStatus.CONNECTING && !this._hasShownModelInfo) {
            this._showModelInfo();
            this._hasShownModelInfo = true;
        }
    }

    updateModelState(modelState, prevModelState) {
        this._updateModelName(modelState, prevModelState);
        this._updateTokenBar(modelState, prevModelState);
    }
}

const toolbarComponent = new ToolbarComponent();
export { toolbarComponent };
