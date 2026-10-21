import { UIText } from '@app/constants/app.globals.js';
import { AppStatus } from '@app/store/app.status.js';
import { appSelectors } from '@app/store/app.selectors.js';
import { createCallback } from '@app/lib/callback.js';
import { formatSpeed } from '@app/lib/formatting.js';

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
        this._settingsState = null;
        this._currentInstructionsState = null;
        this._lastSignature = null;
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
            clearChatBtn: document.getElementById('clear-chat-btn'),
            toolsModeStatus: document.getElementById('tools-mode-status'),
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
            case AppStatus.STEPPING:
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
            appState.roundTokens === prevAppState.roundTokens &&
            appState.tokenSpeed === prevAppState.tokenSpeed) return;

        if (this.elements.liveTokenCount) {
            const isWorking = appSelectors.isBusy(appState.status);
            this.elements.liveTokenCount.style.display = isWorking ? "inline" : "none";
        }

        if (this.elements.tokenCountText && appState.roundTokens !== prevAppState?.roundTokens) {
            this.elements.tokenCountText.textContent = appState.roundTokens > 0
                ? `${appState.roundTokens} ${UIText.TEXT_TOKENS}`
                : "";
        }

        if (this.elements.tokensSpeed && appState.tokenSpeed !== prevAppState?.tokenSpeed) {
            this.elements.tokensSpeed.textContent = appState.tokenSpeed > 0
                ? `(${formatSpeed(appState.tokenSpeed)} ${UIText.TEXT_TOKENS_PER_SECOND})`
                : "";
        }
    }

    _updateStatusText(appState, prevAppState) {
        if (prevAppState && appState.status === prevAppState.status && appState.error === prevAppState.error) return;
        if (!this.elements.statusText) return;

        const status = appState.status;
        this.elements.statusText.classList.remove('error', 'offline');
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
            default: {
                const label = UIText[`STATUS_${status}`] || UIText.STATUS_UNKNOWN;
                if (label !== this.elements.statusText.textContent) {
                    this.elements.statusText.textContent = label;
                }
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
        this._settingsState = null;
        this._currentInstructionsState = null;
        this._lastSignature = null;
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

    _getSelectedInstruction(instructionsState) {
        const instructions = instructionsState?.instructions;
        const selectedTabId = instructionsState?.selectedTabId;
        if (!Array.isArray(instructions) || !selectedTabId) return null;
        return instructions.find((tab) => tab && tab.enabled && tab.id == selectedTabId) || null;
    }

    _instructionSegment() {
        const tab = this._getSelectedInstruction(this._currentInstructionsState);
        return tab ? tab.displayName : null;
    }

    _toolsSegment() {
        const settings = this._settingsState;
        if (!settings || !settings.EnableAiTools) return null;
        return settings.EnableAiWriteTools ? UIText.STATUS_TOOLS_READ_WRITE : UIText.STATUS_TOOLS_READ;
    }

    _composeTooltip() {
        const lines = [];

        const tab = this._getSelectedInstruction(this._currentInstructionsState);
        lines.push(tab
            ? UIText.STATUS_TOOLTIP_INSTRUCTIONS
                .replace('{name}', tab.displayName)
                .replace('{temperature}', tab.temperature)
            : UIText.STATUS_TOOLTIP_INSTRUCTIONS_DISABLED);

        const tools = this._toolsSegment();
        if (tools === UIText.STATUS_TOOLS_READ_WRITE) {
            lines.push(UIText.STATUS_TOOLTIP_TOOLS_READ_WRITE);
        } else if (tools === UIText.STATUS_TOOLS_READ) {
            lines.push(UIText.STATUS_TOOLTIP_TOOLS_READ);
        } else {
            lines.push(UIText.STATUS_TOOLTIP_TOOLS_DISABLED);
        }

        lines.push(this._settingsState?.EnableSubAgents
            ? UIText.STATUS_TOOLTIP_SUBAGENTS_ENABLED
            : UIText.STATUS_TOOLTIP_SUBAGENTS_DISABLED);

        return lines.join('\n');
    }

    _renderToolsMode() {
        if (!this.elements.toolsModeStatus) return;

        const parts = [];
        const instruction = this._instructionSegment();
        if (instruction) parts.push(instruction);

        const tools = this._toolsSegment();
        if (tools) {
            parts.push(tools);
            if (this._settingsState && this._settingsState.EnableSubAgents) {
                parts.push(UIText.STATUS_SUBAGENTS);
            }
        }

        const signature = parts.join('\u0000');
        if (signature === this._lastSignature) return;
        this._lastSignature = signature;

        const container = this.elements.toolsModeStatus;
        container.textContent = '';

        parts.forEach((segment, index) => {
            if (index > 0) {
                const separator = document.createElement('span');
                separator.className = 'status-segment-separator';
                separator.setAttribute('aria-hidden', 'true');
                separator.textContent = UIText.STATUS_SEGMENT_SEPARATOR;
                container.appendChild(separator);
            }

            const segmentElement = document.createElement('span');
            segmentElement.className = 'status-segment';
            segmentElement.textContent = segment;
            container.appendChild(segmentElement);
        });

        container.title = this._composeTooltip();
    }

    updateSettingsState(settingsState, prevSettingsState) {
        this._settingsState = settingsState;
        this._renderToolsMode();
        if (settingsState.Provider && settingsState.Provider !== prevSettingsState?.Provider && this.elements.connStatus) {
            this.elements.connStatus.title = `Provider type: ${settingsState.Provider}\nBase url: ${settingsState.LmStudioBaseUrl}`;
        }
    }

    updateInstructionsState(instructionsState, prevInstructionsState) {
        this._currentInstructionsState = instructionsState;
        this._renderToolsMode();
    }
}

const statusComponent = new StatusComponent();
export { statusComponent };