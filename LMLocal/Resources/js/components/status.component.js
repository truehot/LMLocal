import { UIText } from '@app/constants/app.globals.js';
import { AppStatus } from '@app/store/app.status.js';
import { appSelectors } from '@app/store/app.selectors.js';
import { createCallback } from '@app/lib/callback.js';

/**
 * UI component that displays application connection and token usage status. The component wires DOM elements, updates visual state from the application store, and exposes an `onRetry` callback for retrying connections.
 **/
class StatusComponent {
    constructor() {
        this.elements = {};
        this.retryTimeout = null;
        this.connectTimeout = null;
        this.onRetry = createCallback();
        this.onConnect = createCallback();
        this.onClearChat = createCallback();
        this._providerName = null;
        this._currentAppState = null;
    }

    _getElements() {
        return {
            connStatus: document.getElementById('conn-status'),
            retryBtn: document.getElementById('retry-btn'),
            connectBtn: document.getElementById('connect-btn'),
            statusText: document.getElementById('status-text'),
            statusBar: document.getElementById('status-bar'),
            liveTokenCount: document.getElementById('live-token-count'),
            tokenCountText: document.getElementById('token-number'),
            tokensSpeed: document.getElementById('tokens-speed'),
            clearChatBtn: document.getElementById('clear-chat-btn')
        };
    }

    _updateConnectionStatus(appState, prevAppState) {
        if (prevAppState && appState.status === prevAppState.status) return;

        const status = appState.status;
        const error = appState.error;
        let connText = '', connClass = '';

        switch (status) {
            case AppStatus.INITIALIZING:
            case AppStatus.CONNECTING:
                connText = UIText.STATUS_CONNECTING;
                connClass = 'status-label waiting';
                break;
            case AppStatus.OFFLINE:
                connText = UIText.STATUS_OFFLINE;
                if (error) {
                    connClass = 'status-label offline';
                } else {
                    connClass = 'status-label waiting';
                }
                break;
            case AppStatus.IDLE:
            case AppStatus.PROCESSING:
            case AppStatus.STREAMING:
            case AppStatus.THINKING:
            case AppStatus.FINISHING:
            case AppStatus.EXECUTING:
            case AppStatus.RESPONDING:
            case AppStatus.COMPACTING:
            case AppStatus.STOPPING:
            case AppStatus.ERROR:
                connText = this._providerName || UIText.STATUS_ONLINE;
                connClass = 'status-label online';
                break;
            default:
                connText = UIText.STATUS_UNKNOWN;
                connClass = 'status-label';
        }

        if (this.elements.connStatus) {
            this.elements.connStatus.textContent = connText;
            this.elements.connStatus.className = connClass;
        }

        if (this.elements.retryBtn && this.elements.connectBtn) {
            if (status === AppStatus.OFFLINE) {
                if (error) {
                    this.elements.retryBtn.style.display = 'inline-block';
                    this.elements.connectBtn.style.display = 'none';
                } else {
                    this.elements.retryBtn.style.display = 'none';
                    this.elements.connectBtn.style.display = 'inline-block';
                }
            } else {
                this.elements.retryBtn.style.display = 'none';
                this.elements.connectBtn.style.display = 'none';
            }
        } else if (this.elements.retryBtn) {
            this.elements.retryBtn.style.display = status === AppStatus.OFFLINE ? 'inline-block' : 'none';
        }
    }

    _updateTokenCounter(appState, prevAppState) {
        if (prevAppState &&
            appState.tokenUsed === prevAppState.tokenUsed &&
            appState.tokenSpeed === prevAppState.tokenSpeed) return;

        if (this.elements.liveTokenCount) {
            const isWorking = appSelectors.isBusy(appState.status);
            this.elements.liveTokenCount.style.display = isWorking ? "inline" : "none";
        }

        if (this.elements.tokenCountText && appState.tokenUsed !== prevAppState?.tokenUsed) {
            this.elements.tokenCountText.textContent = appState.tokenUsed > 0
                ? `${appState.tokenUsed} ${UIText.TEXT_TOKENS}`
                : "";
        }

        if (this.elements.tokensSpeed && appState.tokenSpeed !== prevAppState?.tokenSpeed) {
            this.elements.tokensSpeed.textContent = appState.tokenSpeed > 0
                ? `(${appState.tokenSpeed.toFixed(1)} ${UIText.TEXT_TOKENS_PER_SECOND})`
                : "";
        }
    }

    _updateStatusText(appState, prevAppState) {
        if (prevAppState && appState.status === prevAppState.status && appState.error === prevAppState.error) return;
        if (!this.elements.statusText) return;

        const status = appState.status;
        this.elements.statusText.classList.remove('error');
        this.elements.statusText.classList.remove('offline');
        switch (status) {
            case AppStatus.ERROR:
                this.elements.statusText.textContent = UIText.STATUS_ERROR;
                this.elements.statusText.classList.add('error');
                break;
            case AppStatus.OFFLINE:
                {
                    const label = UIText[`TEXT_NOT_READY`] || UIText.STATUS_UNKNOWN;
                    this.elements.statusText.textContent = label;
                    if (appState.error) {
                        this.elements.statusText.textContent += " | " + appState.error;
                        this.elements.statusText.classList.add('error');
                    } else {
                        this.elements.statusText.classList.add('offline');
                    }

                }
                break;
            case AppStatus.INITIALIZING:
            case AppStatus.CONNECTING:
                this.elements.statusText.classList.add('offline');
                this.elements.statusText.textContent = UIText[`TEXT_NOT_READY`] || UIText.STATUS_UNKNOWN;
                break;
            case AppStatus.CLEARING:
                this.elements.statusText.textContent = UIText.STATUS_CLEARING;
                break;
            default:
                const label = UIText[`STATUS_${status}`] || UIText.STATUS_UNKNOWN;
                if (label !== this.elements.statusText.textContent) {
                    this.elements.statusText.textContent = label;
                }
        }
    }

    _updateGeneratingAnimation(appState, prevAppState) {
        const isTerminalState = appSelectors.isTerminal(appState.status);
        if (prevAppState && isTerminalState === appSelectors.isTerminal(prevAppState.status)) return;

        if (this.elements.statusBar) {
            this.elements.statusBar.classList.toggle('generating', !isTerminalState);
        }
    }

    _onRetryClick = async (e) => {
        if (this.retryTimeout) clearTimeout(this.retryTimeout);
        const btn = e.currentTarget;
        btn.classList.add('retry-animate');
        this.retryTimeout = setTimeout(() => {
            if (btn) btn.classList.remove('retry-animate');
            this.retryTimeout = null;
        }, 500);
        await this.onRetry.emit();
    };

    _onConnectClick = async (e) => {
        if (this.connectTimeout) clearTimeout(this.connectTimeout);
        const btn = e.currentTarget;
        btn.classList.add('retry-animate');
        this.connectTimeout = setTimeout(() => {
            if (btn) btn.classList.remove('retry-animate');
            this.connectTimeout = null;
        }, 500);
        await this.onConnect.emit();
    };

    _onClearChatClick = async () => {
        await this.onClearChat.emit();
    }

    _attachEvents() {
        if (this.elements.retryBtn) {
            this.elements.retryBtn.addEventListener('click', this._onRetryClick);
        }
        if (this.elements.connectBtn) {
            this.elements.connectBtn.addEventListener('click', this._onConnectClick);
        }
        if (this.elements.clearChatBtn) {
            this.elements.clearChatBtn.addEventListener('click', this._onClearChatClick);
        }
    }

    _detachEvents() {
        if (this.elements.retryBtn) {
            this.elements.retryBtn.removeEventListener('click', this._onRetryClick);
        }
        if (this.elements.connectBtn) {
            this.elements.connectBtn.removeEventListener('click', this._onConnectClick);
        }
        if (this.elements.clearChatBtn) {
            this.elements.clearChatBtn.removeEventListener('click', this._onClearChatClick);
        }
    }

    setup() {
        this.reset();
        this.elements = this._getElements();
        this._attachEvents();
        return this;
    }

    reset() {
        this._detachEvents();
        if (this.retryTimeout) {
            clearTimeout(this.retryTimeout);
            this.retryTimeout = null;
        }
        if (this.connectTimeout) {
            clearTimeout(this.connectTimeout);
            this.connectTimeout = null;
        }
        this.elements = {};
        this._providerName = null;
        this._currentAppState = null;
    }

    updateAppState(appState, prevAppState) {
        this._currentAppState = appState;
        this._updateConnectionStatus(appState, prevAppState);
        this._updateTokenCounter(appState, prevAppState);
        this._updateStatusText(appState, prevAppState);
        this._updateGeneratingAnimation(appState, prevAppState);
    }

    updateProviderName(name) {
        if (this._providerName === name) return;
        this._providerName = name;

        if (this._currentAppState) {
            this._updateConnectionStatus(this._currentAppState, null);
        }
    }

    updateSettingsState(settingsState, prevSettingsState) {
        if (settingsState.Provider && settingsState.Provider !== prevSettingsState?.Provider && this.elements.connStatus) {
            this.elements.connStatus.title = `Provider type: ${settingsState.Provider}\nBase url: ${settingsState.LmStudioBaseUrl}`;
        }
    }
}

const statusComponent = new StatusComponent();
export { statusComponent };