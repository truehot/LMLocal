import { createCallback } from '@app/lib/callback.js';

class InstructionsModeManager {
    constructor() {
        this.modes = [];
    }

    async load() {
        const response = await fetch('https://app.local/json/instruction-tabs.json');
        const data = await response.json();
        this.modes = data.tabs || [];
    }

    getAllModes() {
        return this.modes;
    }

    getModeConfig(id) {
        return this.modes.find(m => m.id === id);
    }
}

class InstructionsDataManager {
    constructor() {
        this.tabStates = {};
        this.modeManager = null;
    }

    async loadDefaults() {
        if (!this.modeManager) {
            this.modeManager = new InstructionsModeManager();
            await this.modeManager.load();
        }

        const allModes = this.modeManager.getAllModes();
        allModes.forEach(modeConfig => {
            this.tabStates[modeConfig.id] = {
                enabled: modeConfig.enabled !== false,
                prompt: modeConfig.prompt || '',
                temperature: modeConfig.temperature !== undefined ? modeConfig.temperature : 0.5
            };
        });
    }

    initializeFromSaved(saved) {
        if (!saved) return;

        const arr = Array.isArray(saved) ? saved : (Array.isArray(saved.tabs) ? saved.tabs : null);
        if (!arr) return;

        arr.forEach(tabObj => {
            const tabId = tabObj && (tabObj.id || tabObj.name);
            if (!tabId) return;

            this.tabStates[tabId] = {
                enabled: tabObj.enabled !== false,
                prompt: tabObj.prompt !== undefined ? tabObj.prompt : (this.tabStates[tabId]?.prompt || ''),
                temperature: tabObj.temperature !== undefined ? tabObj.temperature : (this.tabStates[tabId]?.temperature || 0.5)
            };
        });
    }

    getTabState(tabId) {
        return this.tabStates[tabId] || { enabled: true, prompt: '', temperature: 0.5 };
    }

    setTabState(tabId, state) {
        this.tabStates[tabId] = state;
    }

    mergeAllTabStates() {
        const allModes = this.modeManager.getAllModes();
        const result = allModes.map(modeConfig => {
            const state = this.tabStates[modeConfig.id] || {
                enabled: modeConfig.enabled !== false,
                prompt: modeConfig.prompt || '',
                temperature: modeConfig.temperature !== undefined ? modeConfig.temperature : 0.5
            };

            return {
                id: modeConfig.id,
                displayName: modeConfig.displayName,
                enabled: !!state.enabled,
                prompt: state.prompt,
                temperature: state.temperature
            };
        });

        return { tabs: result };
    }
}

export class InstructionsDialog {
    constructor() {
        this.dataManager = new InstructionsDataManager();
        this._abortController = null;
        this.onLoad = createCallback();
        this.onSave = createCallback();
    }

    async show() {
        const dialog = document.getElementById('instructions-dialog');
        if (!dialog) throw new Error('Dialog #instructions-dialog not found');

        const body = dialog.querySelector('.modal-body');
        const confirmBtn = dialog.querySelector('#instructions-dialog-confirm');
        const cancelBtn = dialog.querySelector('#instructions-dialog-cancel');

        if (!body || !confirmBtn || !cancelBtn) {
            throw new Error('Missing elements in dialog');
        }

        try {
            this._abortController?.abort();
            this._abortController = new AbortController();

            await this.dataManager.loadDefaults();

            const result = await this.onLoad.emitResult();
            const savedJson = result.success ? result.data : null;
            this.dataManager.initializeFromSaved(savedJson);

            await this._setupTabs(body);
        } catch (error) {
            console.error('Failed to populate instructions dialog:', error);
        }

        return new Promise((resolve) => {
            dialog.showModal();

            const onConfirm = async () => {
                try {
                    const currentTab = dialog.querySelector('.tab-btn.active').getAttribute('data-target');
                    this._saveTabState(currentTab, body);

                    const merged = this.dataManager.mergeAllTabStates();
                    merged.selectedTabId = currentTab;

                    const result = await this.onSave.emitResult(merged);

                    if (result.success) {
                        this._abortController?.abort();
                        this._abortController = null;
                        this.onLoad.off();
                        this.onSave.off();
                        dialog.close();
                        resolve(true);

                    } else {
                        console.error('Failed to save instructions', result.error);
                        this._abortController?.abort();
                        this._abortController = null;
                        this.onLoad.off();
                        this.onSave.off();
                        dialog.close();
                        resolve(false);
                        return;
                    }

                } catch (error) {
                    console.error('Error saving instructions:', error);
                    this._abortController?.abort();
                    this._abortController = null;
                    this.onLoad.off();
                    this.onSave.off();
                    dialog.close();
                    resolve(false);
                    return;
                }
            };

            const onCancel = () => {
                this._abortController?.abort();
                this._abortController = null;
                this.onLoad.off();
                this.onSave.off();
                dialog.close();
                resolve(false);
            };

            confirmBtn.addEventListener('click', onConfirm, { signal: this._abortController?.signal });
            cancelBtn.addEventListener('click', onCancel, { signal: this._abortController?.signal });
            dialog.addEventListener('close', onCancel, { signal: this._abortController?.signal });

        });
    }

    async _setupTabs(body) {
        const sidebar = body.parentElement.querySelector('.settings-sidebar');
        if (!sidebar) {
            console.error('Settings sidebar not found');
            return;
        }

        sidebar.innerHTML = '';

        const allModes = this.dataManager.modeManager.getAllModes();
        allModes.forEach((modeConfig, index) => {
            const button = document.createElement('button');
            button.className = 'tab-btn' + (index === 0 ? ' active' : '');
            button.setAttribute('data-target', modeConfig.id);
            button.textContent = modeConfig.displayName;

            button.addEventListener('click', () => {
                const currentActiveTab = sidebar.querySelector('.tab-btn.active');
                const currentTarget = currentActiveTab?.getAttribute('data-target');

                if (currentTarget) {
                    this._saveTabState(currentTarget, body);
                }

                sidebar.querySelectorAll('.tab-btn').forEach(t => t.classList.remove('active'));
                button.classList.add('active');

                this._renderTab(body, modeConfig.id);
            }, { signal: this._abortController?.signal });

            sidebar.appendChild(button);
        });

        if (allModes.length > 0) {
            this._renderTab(body, allModes[0].id);
        }
    }

    _renderTab(container, tabId) {
        container.innerHTML = '';

        const modeConfig = this.dataManager.modeManager.getModeConfig(tabId);
        const tabState = this.dataManager.getTabState(tabId);

        const section = document.createElement('section');
        section.className = 'tab-content';

        const headerHtml = `<label class="group-header-row" for="enabled-${tabId}">
            <span class="settings-label">${escapeHtml(modeConfig.displayName)} Instructions</span>
            <input type="checkbox" name="enabled-${tabId}" id="enabled-${tabId}" data-field="enabled" ${tabState.enabled ? 'checked' : ''}>
           </label>
           <div class="checkbox-description header-desc">Enable this mode to make it available for quick selection via the mode dropdown in the chat bar.</div>`;

        section.innerHTML = headerHtml;

        const fieldsWrapper = document.createElement('div');
        fieldsWrapper.className = 'tab-fields-body';
        fieldsWrapper.style.transition = 'opacity 0.2s ease';
        section.appendChild(fieldsWrapper);

        const promptGroup = document.createElement('div');
        promptGroup.className = 'settings-group';
        promptGroup.innerHTML = `
        <label class="settings-label" for="prompt-${tabId}">System prompt</label>
        <div class="checkbox-description">Defines the AI's core persona, behavior, processing rules, and operational constraints.</div>
        <textarea data-field="prompt" name="prompt-${tabId}" id="prompt-${tabId}" class="prompt-textarea"></textarea>`;

        promptGroup.querySelector('textarea').value = tabState.prompt;
        fieldsWrapper.appendChild(promptGroup);

        const tempGroup = document.createElement('div');
        tempGroup.className = 'settings-group';
        tempGroup.innerHTML = `
        <label class="settings-label" for="temperature-${tabId}">Temperature</label>
        <div class="checkbox-description">Controls response variability: 0 is completely deterministic and focused, while 1 introduces maximum randomness and creativity.</div>
        <input type="number" data-field="temperature" name="temperature-${tabId}" id="temperature-${tabId}" class="temperature-input" min="0" max="1" step="0.05" value="${tabState.temperature}">`;

        fieldsWrapper.appendChild(tempGroup);

        const enableCheckbox = section.querySelector('input[type="checkbox"][data-field="enabled"]');
        const applyState = (isEnabled) => {
            fieldsWrapper.style.opacity = isEnabled ? '1' : '0.5';
            fieldsWrapper.style.pointerEvents = isEnabled ? 'auto' : 'none';
        };
        applyState(tabState.enabled);
        enableCheckbox.addEventListener('change', () => {
            applyState(enableCheckbox.checked);
        }, { signal: this._abortController?.signal });

        container.appendChild(section);
    }

    _saveTabState(tabId, container) {
        const state = {};

        const enabledCheckbox = container.querySelector('input[type="checkbox"][data-field="enabled"]');
        const promptTextarea = container.querySelector('textarea[data-field="prompt"]');
        const temperatureInput = container.querySelector('input[data-field="temperature"]');

        state.enabled = enabledCheckbox ? enabledCheckbox.checked : true;
        state.prompt = promptTextarea ? promptTextarea.value : '';
        state.temperature = temperatureInput ? (parseFloat(temperatureInput.value) ?? 0.5) : 0.5;

        this.dataManager.setTabState(tabId, state);
    }
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}
