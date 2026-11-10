import { createCallback } from '@app/lib/callback.js';
import { Icons } from '@app/constants/app.globals.js';

/**
 * URL of the built-in default tabs.
 */
const DEFAULTS_URL = 'https://app.local/json/instruction-tabs.json';

const USER_TAB_ID_START = 50;
const MAX_USER_TABS = 50;
const NEW_TAB_NAME = 'New Tab';
const DEFAULT_TEMPERATURE = 0.5;

/**
 * Returns the value when it is present, otherwise `undefined`, so a draft falls back to the built-in default for a field the saved document does not carry.
 */
function optionalValue(value) {
    return value === undefined || value === null ? undefined : value;
}

/**
 * Loads the built-in instruction tabs.
 */
async function loadDefaultTabs() {
    const result = [];
    try {
        const response = await fetch(DEFAULTS_URL);
        if (!response || !response.ok) {
            return result;
        }

        const data = await response.json();
        const tabs = Array.isArray(data?.tabs) ? data.tabs : [];

        for (const tab of tabs) {
            if (!tab || tab.id === undefined || tab.id === null || !tab.displayName) continue;
            result.push({
                id: String(tab.id),
                displayName: String(tab.displayName),
                enabled: tab.enabled !== false,
                temperature: tab.temperature !== undefined ? tab.temperature : DEFAULT_TEMPERATURE,
                prompt: tab.prompt || ''
            });
        }
    } catch (error) {
        console.warn('Failed to load instruction defaults:', error);
    }

    return result;
}

export class InstructionsDialog {
    constructor() {
        this.onLoad = createCallback();
        this.onSave = createCallback();

        this._tabs = [];                 // draft model: full list of tabs
        this._defaultIds = new Set();    // ids of built-in tabs (read-only names, not deletable)
        this._defaults = new Map();      // shipped values of built-in tabs, used by "Reset to default"
        this._activeId = null;

        this._abortController = null;
        this._dialog = null;
        this._sidebar = null;
        this._body = null;
        this._errorEl = null;
    }

    async show() {
        const dialog = document.getElementById('instructions-dialog');
        if (!dialog) throw new Error('Dialog #instructions-dialog not found');

        this._dialog = dialog;
        this._sidebar = dialog.querySelector('.settings-sidebar');
        this._body = dialog.querySelector('.modal-body');
        const confirmBtn = dialog.querySelector('#instructions-dialog-confirm');
        const cancelBtn = dialog.querySelector('#instructions-dialog-cancel');

        if (!this._sidebar || !this._body || !confirmBtn || !cancelBtn) {
            throw new Error('Missing elements in instructions dialog');
        }

        this._abortController?.abort();
        this._abortController = new AbortController();
        this._ensureErrorElement();

        await this._loadDraft();
        this._render();

        return new Promise((resolve) => {
            dialog.showModal();

            const cleanup = () => {
                this._abortController?.abort();
                this._abortController = null;
                this.onLoad.off();
                this.onSave.off();
            };

            const onConfirm = async () => {
                const document = this._collect();
                const result = await this.onSave.emitResult(document);

                if (result && result.success) {
                    cleanup();
                    dialog.close();
                    resolve(true);
                    return;
                }

                const message = result?.error?.message || result?.error || 'Failed to save instructions.';
                this._showError(message);
            };

            const onCancel = () => {
                cleanup();
                dialog.close();
                resolve(false);
            };

            confirmBtn.addEventListener('click', onConfirm, { signal: this._abortController?.signal });
            cancelBtn.addEventListener('click', onCancel, { signal: this._abortController?.signal });
            dialog.addEventListener('close', onCancel, { signal: this._abortController?.signal });
        });
    }

    async _loadDraft() {
        const [defaults, saved] = await Promise.all([
            loadDefaultTabs(),
            this._loadSaved()
        ]);

        this._defaultIds = new Set(defaults.map(t => t.id));

        this._defaults = new Map();
        for (const tab of defaults) {
            this._defaults.set(tab.id, { prompt: tab.prompt, temperature: tab.temperature });
        }

        const byId = new Map();
        for (const tab of defaults) {
            byId.set(tab.id, { ...tab });
        }
        for (const tab of saved.tabs) {
            if (byId.has(tab.id)) {
                const base = byId.get(tab.id);
                byId.set(tab.id, {
                    ...base,
                    enabled: tab.enabled !== undefined ? !!tab.enabled : base.enabled,
                    temperature: tab.temperature !== undefined ? tab.temperature : base.temperature,
                    prompt: tab.prompt !== undefined ? tab.prompt : base.prompt
                });
            } else {
                byId.set(tab.id, { ...tab });
            }
        }

        const userTabs = [...byId.values()]
            .filter(t => !this._defaultIds.has(t.id))
            .sort((a, b) => Number(a.id) - Number(b.id));

        this._tabs = [
            ...defaults.map(d => byId.get(d.id)),
            ...userTabs
        ];

        const requestedActive = saved.selectedTabId !== undefined && saved.selectedTabId !== null
            ? String(saved.selectedTabId)
            : null;
        const activeExists = requestedActive && this._tabs.some(t => t.id === requestedActive);

        this._activeId = activeExists
            ? requestedActive
            : (this._tabs[0]?.id ?? null);
    }

    async _loadSaved() {
        try {
            const result = await this.onLoad.emitResult();
            if (!result || !result.success || !result.data) {
                return { tabs: [], selectedTabId: null };
            }

            const document = result.data;
            const tabs = Array.isArray(document.tabs) ? document.tabs : [];

            return {
                tabs: tabs.map(t => ({
                    id: String(t.id),
                    displayName: t.displayName || ('Custom ' + t.id),
                    enabled: t.enabled !== false,
                    temperature: optionalValue(t.temperature),
                    prompt: optionalValue(t.prompt)
                })),
                selectedTabId: document.selectedTabId
            };
        } catch (error) {
            console.warn('Failed to load saved instructions:', error);
            return { tabs: [], selectedTabId: null };
        }
    }

    _render() {
        this._renderSidebar();
        this._renderActiveTab();
        this._clearError();
    }

    _renderSidebar() {
        const sidebar = this._sidebar;
        sidebar.innerHTML = '';

        for (const tab of this._tabs) {
            const button = document.createElement('button');
            button.className = 'tab-btn' + (tab.id === this._activeId ? ' active' : '') + (tab.enabled ? '' : ' inactive');
            button.setAttribute('data-target', tab.id);
            button.textContent = tab.displayName;
            button.addEventListener('click', () => this._selectTab(tab.id), { signal: this._abortController?.signal });
            sidebar.appendChild(button);
        }

        const userTabCount = this._tabs.filter(t => !this._defaultIds.has(t.id)).length;
        const addBtn = document.createElement('button');
        addBtn.type = 'button';
        addBtn.className = 'tab-btn';
        addBtn.textContent = '+ Add';
        addBtn.disabled = userTabCount >= MAX_USER_TABS;
        addBtn.title = addBtn.disabled
            ? `Maximum of ${MAX_USER_TABS} custom instructions reached`
            : 'Add instruction';
        if (addBtn.disabled) {
            addBtn.style.opacity = '0.5';
            addBtn.style.cursor = 'default';
        }
        addBtn.addEventListener('click', () => {
            if (!addBtn.disabled) this._addTab();
        }, { signal: this._abortController?.signal });
        sidebar.appendChild(addBtn);
    }

    _renderActiveTab() {
        const body = this._body;
        body.innerHTML = '';

        const tab = this._tabs.find(t => t.id === this._activeId);
        if (!tab) return;

        const isUserTab = !this._defaultIds.has(tab.id);

        const section = document.createElement('section');
        section.className = 'tab-content active';

        const header = document.createElement('div');
        header.className = 'group-header-row';

        const title = document.createElement('label');
        title.className = 'header-title';
        title.setAttribute('for', `enabled-${tab.id}`);

        const label = document.createElement('span');
        label.className = 'settings-label';
        label.textContent = `${tab.displayName} Instructions`;
        title.appendChild(label);
        header.appendChild(title);

        const actions = document.createElement('span');
        actions.className = 'header-actions';

        const enabledCheckbox = document.createElement('input');
        enabledCheckbox.type = 'checkbox';
        enabledCheckbox.setAttribute('data-field', 'enabled');
        enabledCheckbox.id = `enabled-${tab.id}`;
        enabledCheckbox.checked = !!tab.enabled;
        actions.appendChild(enabledCheckbox);

        let resetBtn = null;
        if (!isUserTab) {
            resetBtn = document.createElement('button');
            resetBtn.type = 'button';
            resetBtn.className = 'tab-reset-btn';
            resetBtn.title = 'Reset to default';
            resetBtn.setAttribute('aria-label', 'Reset to default');
            resetBtn.innerHTML = Icons.RESTORE;
            resetBtn.disabled = !this._isModifiedFromDefault(tab);
            resetBtn.addEventListener('click', (e) => {
                e.preventDefault();
                e.stopPropagation();
                this._resetTabToDefault(tab.id);
            }, { signal: this._abortController?.signal });
            actions.appendChild(resetBtn);
        }

        if (isUserTab) {
            const deleteBtn = document.createElement('button');
            deleteBtn.type = 'button';
            deleteBtn.className = 'tab-delete-btn';
            deleteBtn.title = 'Remove instruction';
            deleteBtn.setAttribute('aria-label', 'Remove instruction');
            deleteBtn.innerHTML = Icons.REMOVE;
            deleteBtn.addEventListener('click', (e) => {
                e.preventDefault();
                e.stopPropagation();
                this._deleteTab(tab.id);
            }, { signal: this._abortController?.signal });
            actions.appendChild(deleteBtn);
        }

        header.appendChild(actions);
        section.appendChild(header);

        const headerDesc = document.createElement('div');
        headerDesc.className = 'checkbox-description header-desc';
        headerDesc.textContent = 'Enable this mode to make it available for quick selection via the mode dropdown in the chat bar.';
        section.appendChild(headerDesc);

        const fieldsWrapper = document.createElement('div');
        fieldsWrapper.className = 'tab-fields-body';
        fieldsWrapper.style.transition = 'opacity 0.2s ease';
        section.appendChild(fieldsWrapper);

        if (isUserTab) {
            const nameGroup = document.createElement('div');
            nameGroup.className = 'settings-group';

            const nameLabel = document.createElement('label');
            nameLabel.className = 'settings-label';
            nameLabel.setAttribute('for', `name-${tab.id}`);
            nameLabel.textContent = 'Name';

            const nameInput = document.createElement('input');
            nameInput.type = 'text';
            nameInput.className = 'temperature-input';
            nameInput.style.width = '100%';
            nameInput.setAttribute('data-field', 'displayName');
            nameInput.id = `name-${tab.id}`;
            nameInput.value = tab.displayName;
            nameInput.addEventListener('input', () => {
                tab.displayName = nameInput.value;
                const display = nameInput.value.trim() || NEW_TAB_NAME;
                label.textContent = `${display} Instructions`;
                const sideBtn = this._sidebar.querySelector(`.tab-btn[data-target="${tab.id}"]`);
                if (sideBtn) sideBtn.textContent = display;
            }, { signal: this._abortController?.signal });

            nameGroup.appendChild(nameLabel);
            nameGroup.appendChild(nameInput);
            fieldsWrapper.appendChild(nameGroup);
        }

        const promptGroup = document.createElement('div');
        promptGroup.className = 'settings-group';

        const promptLabel = document.createElement('label');
        promptLabel.className = 'settings-label';
        promptLabel.setAttribute('for', `prompt-${tab.id}`);
        promptLabel.textContent = 'System prompt';

        const promptDesc = document.createElement('div');
        promptDesc.className = 'checkbox-description';
        promptDesc.textContent = "Defines the AI's core persona, behavior, processing rules, and operational constraints.";

        const promptTextarea = document.createElement('textarea');
        promptTextarea.setAttribute('data-field', 'prompt');
        promptTextarea.id = `prompt-${tab.id}`;
        promptTextarea.className = 'prompt-textarea';
        promptTextarea.value = tab.prompt || '';

        promptGroup.appendChild(promptLabel);
        promptGroup.appendChild(promptDesc);
        promptGroup.appendChild(promptTextarea);
        fieldsWrapper.appendChild(promptGroup);

        const tempGroup = document.createElement('div');
        tempGroup.className = 'settings-group';

        const tempLabel = document.createElement('label');
        tempLabel.className = 'settings-label';
        tempLabel.setAttribute('for', `temperature-${tab.id}`);
        tempLabel.textContent = 'Temperature';

        const tempDesc = document.createElement('div');
        tempDesc.className = 'checkbox-description';
        tempDesc.textContent = 'Controls response variability: 0 is completely deterministic and focused, while 1 introduces maximum randomness and creativity.';

        const tempInput = document.createElement('input');
        tempInput.type = 'number';
        tempInput.setAttribute('data-field', 'temperature');
        tempInput.id = `temperature-${tab.id}`;
        tempInput.className = 'temperature-input';
        tempInput.min = '0';
        tempInput.max = '1';
        tempInput.step = '0.05';
        tempInput.value = tab.temperature !== undefined && tab.temperature !== null ? tab.temperature : DEFAULT_TEMPERATURE;

        tempGroup.appendChild(tempLabel);
        tempGroup.appendChild(tempDesc);
        tempGroup.appendChild(tempInput);
        fieldsWrapper.appendChild(tempGroup);

        const syncModifiedState = () => {
            if (resetBtn) resetBtn.disabled = !this._isModifiedFromDefault(tab);
        };

        promptTextarea.addEventListener('input', () => {
            tab.prompt = promptTextarea.value;
            syncModifiedState();
        }, { signal: this._abortController?.signal });

        tempInput.addEventListener('input', () => {
            const value = parseFloat(tempInput.value);
            if (!Number.isNaN(value)) tab.temperature = value;
            syncModifiedState();
        }, { signal: this._abortController?.signal });

        syncModifiedState();

        const applyEnabledState = (isEnabled) => {
            fieldsWrapper.style.opacity = isEnabled ? '1' : '0.5';
            fieldsWrapper.style.pointerEvents = isEnabled ? 'auto' : 'none';

            const sideBtn = this._sidebar.querySelector(`.tab-btn[data-target="${tab.id}"]`);
            if (sideBtn) sideBtn.classList.toggle('inactive', !isEnabled);
        };
        applyEnabledState(!!tab.enabled);
        enabledCheckbox.addEventListener('change', () => {
            applyEnabledState(enabledCheckbox.checked);
        }, { signal: this._abortController?.signal });

        body.appendChild(section);
    }

    _selectTab(tabId) {
        if (tabId === this._activeId) return;
        this._captureActiveFields();
        this._activeId = tabId;
        this._render();
    }

    _addTab() {
        this._captureActiveFields();

        const userTabCount = this._tabs.filter(t => !this._defaultIds.has(t.id)).length;
        if (userTabCount >= MAX_USER_TABS) return;

        const maxId = this._tabs.reduce((acc, t) => {
            const n = Number(t.id);
            return Number.isNaN(n) ? acc : Math.max(acc, n);
        }, 0);

        const tab = {
            id: String(Math.max(USER_TAB_ID_START, maxId + 1)),
            displayName: NEW_TAB_NAME,
            enabled: true,
            temperature: DEFAULT_TEMPERATURE,
            prompt: ''
        };

        this._tabs.push(tab);
        this._activeId = tab.id;
        this._render();
    }

    _deleteTab(tabId) {
        if (this._defaultIds.has(tabId)) return;
        const index = this._tabs.findIndex(t => t.id === tabId);
        if (index === -1) return;

        this._captureActiveFields();
        this._tabs = this._tabs.filter(t => t.id !== tabId);

        if (this._activeId === tabId) {
            const neighbor = this._tabs[index - 1] || this._tabs[0];
            this._activeId = neighbor ? neighbor.id : null;
        }

        this._render();
    }

    _captureActiveFields() {
        const tab = this._tabs.find(t => t.id === this._activeId);
        if (!tab) return;

        const body = this._body;
        const enabledCheckbox = body.querySelector('input[type="checkbox"][data-field="enabled"]');
        const promptTextarea = body.querySelector('textarea[data-field="prompt"]');
        const tempInput = body.querySelector('input[data-field="temperature"]');
        const nameInput = body.querySelector('input[data-field="displayName"]');

        if (enabledCheckbox) tab.enabled = enabledCheckbox.checked;
        if (promptTextarea) tab.prompt = promptTextarea.value;
        if (nameInput && nameInput.value.trim()) tab.displayName = nameInput.value.trim();

        if (tempInput) {
            const value = parseFloat(tempInput.value);
            if (!Number.isNaN(value)) tab.temperature = value;
        }
    }

    _collect() {
        this._captureActiveFields();

        return {
            selectedTabId: this._activeId !== null ? Number(this._activeId) : undefined,
            tabs: this._tabs.map(t => ({
                id: Number(t.id),
                displayName: t.displayName,
                enabled: !!t.enabled,
                temperature: t.temperature,
                prompt: t.prompt
            }))
        };
    }

    _ensureErrorElement() {
        if (this._errorEl && this._dialog.contains(this._errorEl)) return;

        const el = document.createElement('div');
        el.className = 'instructions-error';
        el.style.cssText = 'display:none;padding:8px 12px;background:#c0392b;color:#fff;font-size:12px;line-height:1.4;';

        const header = this._dialog.querySelector('.modal-header');
        if (header && header.parentElement) {
            header.parentElement.insertBefore(el, header.nextSibling);
        } else {
            this._dialog.insertBefore(el, this._dialog.firstChild);
        }

        this._errorEl = el;
    }

    _showError(message) {
        this._ensureErrorElement();
        this._errorEl.textContent = message;
        this._errorEl.style.display = 'block';
    }

    _clearError() {
        if (this._errorEl) {
            this._errorEl.textContent = '';
            this._errorEl.style.display = 'none';
        }
    }

    /**
     * True when the tab carries values that differ from the ones shipped with the extension.
     */
    _isModifiedFromDefault(tab) {
        const defaults = this._defaults.get(tab.id);
        if (!defaults) return false;

        return this._normalizePrompt(tab.prompt) !== this._normalizePrompt(defaults.prompt) ||
            !this._isSameTemperature(tab.temperature, defaults.temperature);
    }

    /**
     * Normalizes line endings only: a textarea always re-emits newlines as "\n".
     */
    _normalizePrompt(value) {
        return String(value ?? '').replace(/\r\n?/g, '\n');
    }

    _isSameTemperature(left, right) {
        const first = Number(left);
        const second = Number(right);
        if (!Number.isFinite(first) || !Number.isFinite(second)) return String(left) === String(right);
        return Math.abs(first - second) < 1e-9;
    }

    /**
     * Restores the built-in text and temperature of a built-in tab.
     */
    _resetTabToDefault(tabId) {
        const tab = this._tabs.find(t => t.id === tabId);
        const defaults = this._defaults.get(tabId);
        if (!tab || !defaults) return;

        this._captureActiveFields();

        tab.prompt = defaults.prompt;
        tab.temperature = defaults.temperature;
        this._render();
    }
}
