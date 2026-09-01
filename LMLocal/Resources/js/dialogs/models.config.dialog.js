import { createCallback } from '@app/lib/callback.js';
import { populateProviderSelect } from '@app/lib/populate-provider.select.js';
import { formatTokens } from '@app/lib/formatting.js';
import toast from '@app/lib/toast.js';
import modelStore from '@app/store/model.store.js';
import settingsStore from '@app/store/settings.store.js';

const REASONING_EFFORTS = ['none', 'low', 'medium', 'high', 'max'];

export class ModelsConfigDialog {
    constructor() {
        this.onLoad = createCallback();
        this.onLoadProviders = createCallback();
        this.onSave = createCallback();
        this._models = [];
        this._providers = [];
        this._selectedProvider = null;
        this._editingModel = null;
        this.el = null;
        this._addBtnClickHandler = null;
        this._adjustCurrentBtnClickHandler = null;
        this._filterInputHandler = null;
        this._sortClickHandler = null;
        this._providerChangeHandler = null;
        this._confirmHandler = null;
        this._cancelHandler = null;
        this._formSubmitHandler = null;
        this._formCancelHandler = null;
        this._dialogCloseHandler = null;
        this._filterText = '';
        this._sortAsc = null;
    }

    _getDialog() {
        return document.getElementById('models-config-dialog');
    }

    _getElements() {
        const dialog = this._getDialog();
        if (!dialog) return {};

        return {
            dialog,
            form: dialog.querySelector('#model-crud-form'),
            confirmBtn: dialog.querySelector('#models-config-modal-confirm'),
            cancelBtn: dialog.querySelector('#models-config-modal-cancel'),
            listView: dialog.querySelector('#models-config-list-view'),
            formView: dialog.querySelector('#model-form-view'),
            listContainer: dialog.querySelector('#models-config-list-container'),
            addBtn: dialog.querySelector('#model-add-btn'),
            adjustCurrentBtn: dialog.querySelector('#model-adjust-current-btn'),
            idInput: dialog.querySelector('[data-setting="id"]'),
            modelIdInput: dialog.querySelector('[data-setting="modelId"]'),
            providerSelect: dialog.querySelector('[data-setting="provider"]'),
            displayNameInput: dialog.querySelector('[data-setting="displayName"]'),
            contextLengthInput: dialog.querySelector('[data-setting="contextLength"]'),
            maxTokensInput: dialog.querySelector('[data-setting="maxTokens"]'),
            reasoningSelect: dialog.querySelector('[data-setting="reasoningEffort"]'),
            isCustomCheckbox: dialog.querySelector('[data-setting="isCustom"]'),
            enabledCheckbox: dialog.querySelector('[data-setting="enabled"]'),
            formCancelBtn: dialog.querySelector('#model-form-cancel'),
            listActions: dialog.querySelector('#models-config-list-actions'),
            formActions: dialog.querySelector('#models-config-form-actions'),
            filterInput: dialog.querySelector('#models-config-filter-input'),
            sortBtn: dialog.querySelector('#models-config-sort-btn')
        };
    }

    _attachEvents() {
        const { addBtn, adjustCurrentBtn, filterInput, sortBtn, providerSelect } = this.el;

        if (addBtn && this._addBtnClickHandler) {
            addBtn.addEventListener('click', this._addBtnClickHandler);
        }
        if (adjustCurrentBtn && this._adjustCurrentBtnClickHandler) {
            adjustCurrentBtn.addEventListener('click', this._adjustCurrentBtnClickHandler);
        }
        if (filterInput && this._filterInputHandler) {
            filterInput.addEventListener('input', this._filterInputHandler);
        }
        if (sortBtn && this._sortClickHandler) {
            sortBtn.addEventListener('click', this._sortClickHandler);
        }
        if (providerSelect && this._providerChangeHandler) {
            providerSelect.addEventListener('change', this._providerChangeHandler);
        }
    }

    _detachEvents() {
        if (!this.el) return;
        const { addBtn, adjustCurrentBtn, filterInput, sortBtn, providerSelect } = this.el;

        if (addBtn && this._addBtnClickHandler) {
            addBtn.removeEventListener('click', this._addBtnClickHandler);
        }
        if (adjustCurrentBtn && this._adjustCurrentBtnClickHandler) {
            adjustCurrentBtn.removeEventListener('click', this._adjustCurrentBtnClickHandler);
        }
        if (filterInput && this._filterInputHandler) {
            filterInput.removeEventListener('input', this._filterInputHandler);
        }
        if (sortBtn && this._sortClickHandler) {
            sortBtn.removeEventListener('click', this._sortClickHandler);
        }
        if (providerSelect && this._providerChangeHandler) {
            providerSelect.removeEventListener('change', this._providerChangeHandler);
        }
    }

    _cleanup() {
        this.onLoad.off();
        this.onLoadProviders.off();
        this.onSave.off();
        this._detachEvents();

        const { dialog, confirmBtn, cancelBtn, form, formCancelBtn } = this.el || {};
        if (confirmBtn) {
            confirmBtn.onclick = null;
            this._confirmHandler = null;
        }
        if (cancelBtn) {
            cancelBtn.onclick = null;
            this._cancelHandler = null;
        }
        if (form) {
            form.onsubmit = null;
            this._formSubmitHandler = null;
        }
        if (formCancelBtn) {
            formCancelBtn.onclick = null;
            this._formCancelHandler = null;
        }
        if (dialog) {
            dialog.onclose = null;
            this._dialogCloseHandler = null;
        }

        this._models = [];
        this._providers = [];
        this._selectedProvider = null;
        this._editingModel = null;
        this._addBtnClickHandler = null;
        this._adjustCurrentBtnClickHandler = null;
        this._filterInputHandler = null;
        this._sortClickHandler = null;
        this._providerChangeHandler = null;
        this.el = null;
        this._filterText = '';
        this._sortAsc = null;
    }

    _getNextId() {
        const maxId = this._models.reduce((max, m) => Math.max(max, m.id || m.Id || 0), 0);
        return maxId + 1;
    }

    _matchesFilter(model) {
        if (this._filterText.trim() === '') return true;
        const search = this._filterText.trim().toLowerCase();
        const modelId = (model.modelId || '').toLowerCase();
        const name = (model.displayName || '').toLowerCase();
        const type = (model.providerType || '').toLowerCase();
        const provider = this._getProviderLabel(model).toLowerCase();
        return modelId.includes(search) || name.includes(search)
            || type.includes(search) || provider.includes(search);
    }

    _getLabel(model) {
        return model.displayName || model.modelId || 'Unnamed';
    }

    /**
     * Resolves the provider profile name for a model entry.
     */
    _getProviderLabel(model) {
        const type = model.providerType || 'openai';
        const providerId = model.providerId ?? model.ProviderId;
        const list = Array.isArray(this._providers) ? this._providers : [];

        const exact = providerId !== undefined && providerId !== null
            ? list.find(p => p && p.providerType === type && p.id === providerId)
            : null;
        const byType = exact || list.find(p => p && p.providerType === type);

        return byType?.name || type;
    }

    _buildMeta(model) {
        const parts = [this._getProviderLabel(model)];
        if (model.contextLength) parts.push(`${formatTokens(model.contextLength)} context`);
        if (model.maxTokens) parts.push(`Max ${formatTokens(model.maxTokens)}`);
        if (model.reasoningEffort) parts.push(`Reasoning: ${model.reasoningEffort}`);
        if (model.isCustom) parts.push('Custom');
        if (model.enabled === false) parts.push('Disabled');
        return parts.join(' · ');
    }

    _renderList() {
        const { listContainer } = this.el || this._getElements();
        if (!listContainer) return;

        let filteredModels = [...this._models];
        if (this._filterText.trim() !== '') {
            filteredModels = filteredModels.filter(model => this._matchesFilter(model));
        }

        if (this._sortAsc !== null) {
            filteredModels.sort((a, b) => {
                const labelA = this._getLabel(a).toLowerCase();
                const labelB = this._getLabel(b).toLowerCase();
                const cmp = labelA.localeCompare(labelB);
                return this._sortAsc ? cmp : -cmp;
            });
        }

        listContainer.innerHTML = '';

        if (filteredModels.length === 0) {
            const emptyMsg = document.createElement('div');
            emptyMsg.className = 'loading-placeholder';
            emptyMsg.id = 'models-config-empty-state';
            const filterActive = this._filterText.trim() !== '';
            emptyMsg.innerHTML = filterActive
                ? '<span>No models match the current filter.</span>'
                : '<span>No models added yet. Click "+ Add Model" to create one.</span>';
            listContainer.appendChild(emptyMsg);
            return;
        }

        filteredModels.forEach((model) => {
            const card = document.createElement('div');
            card.className = 'provider-card';

            const infoStack = document.createElement('div');
            infoStack.className = 'provider-info-stack';

            const nameEl = document.createElement('div');
            nameEl.className = 'provider-card-name';
            nameEl.textContent = this._getLabel(model);

            const typeEl = document.createElement('div');
            typeEl.className = 'provider-card-type';
            typeEl.textContent = model.modelId || '';

            const metaEl = document.createElement('div');
            metaEl.className = 'provider-card-meta';
            metaEl.textContent = this._buildMeta(model);

            infoStack.appendChild(nameEl);
            infoStack.appendChild(typeEl);
            infoStack.appendChild(metaEl);

            const actions = document.createElement('div');
            actions.className = 'provider-card-actions';

            const editBtn = document.createElement('button');
            editBtn.type = 'button';
            editBtn.className = 'btn-secondary provider-card-btn';
            editBtn.textContent = 'Edit';
            editBtn.onclick = (e) => { e.preventDefault(); this._showForm(model); };

            const deleteBtn = document.createElement('button');
            deleteBtn.type = 'button';
            deleteBtn.className = 'btn-secondary provider-card-btn btn-danger-text';
            deleteBtn.textContent = 'Remove';
            deleteBtn.onclick = (e) => { e.preventDefault(); this._handleDeleteModel(model); };

            actions.appendChild(editBtn);
            actions.appendChild(deleteBtn);

            card.appendChild(infoStack);
            card.appendChild(actions);
            listContainer.appendChild(card);
        });
    }

    _fillReasoningSelect() {
        const { reasoningSelect } = this.el;
        if (!reasoningSelect) return;
        reasoningSelect.innerHTML = '';

        const notSet = document.createElement('option');
        notSet.value = '';
        notSet.textContent = 'Not set';
        reasoningSelect.appendChild(notSet);

        REASONING_EFFORTS.forEach(effort => {
            const option = document.createElement('option');
            option.value = effort;
            option.textContent = effort.charAt(0).toUpperCase() + effort.slice(1);
            reasoningSelect.appendChild(option);
        });
    }

    _populateProvider() {
        const { providerSelect } = this.el;
        if (!providerSelect) return;

        try {
            populateProviderSelect(
                providerSelect,
                this._providers,
                {
                    providerType: this._editingModel?.providerType,
                    providerId: this._editingModel?.providerId
                }
            );
        } catch (e) {
            console.error('Failed to populate provider profiles', e);
        }

        this._syncSelectedProviderFromSelect();
    }

    /**
     * Reads the provider currently selected in the dropdown into `_selectedProvider`.
     */
    _syncSelectedProviderFromSelect() {
        const { providerSelect } = this.el || this._getElements();
        if (!providerSelect) {
            this._selectedProvider = null;
            return;
        }

        const selectedOpt = providerSelect.selectedIndex >= 0
            ? providerSelect.options[providerSelect.selectedIndex]
            : null;
        this._selectedProvider = selectedOpt?._providerData || null;
    }

    _showForm(model = null) {
        const { listView, listActions, formView, formActions, idInput, modelIdInput, displayNameInput,
            contextLengthInput, maxTokensInput, isCustomCheckbox, enabledCheckbox } = this.el;

        if (!listView || !formView || !idInput) {
            console.error('Required elements not found for form view');
            return;
        }

        this._editingModel = model;

        listView.classList.add('hidden');
        if (listActions) listActions.classList.add('hidden');

        formView.classList.remove('hidden');
        if (formActions) formActions.classList.remove('hidden');

        this._fillReasoningSelect();
        this._populateProvider();

        if (model) {
            idInput.value = model.id || '';
            modelIdInput.value = model.modelId || '';
            displayNameInput.value = model.displayName || '';
            contextLengthInput.value = model.contextLength ?? '';
            maxTokensInput.value = model.maxTokens ?? '';
            this.el.reasoningSelect.value = model.reasoningEffort || '';
            isCustomCheckbox.checked = !!model.isCustom;
            enabledCheckbox.checked = model.enabled !== false;
        } else {
            idInput.value = this._getNextId();
            modelIdInput.value = '';
            displayNameInput.value = '';
            contextLengthInput.value = '';
            maxTokensInput.value = '';
            this.el.reasoningSelect.value = '';
            isCustomCheckbox.checked = true;
            enabledCheckbox.checked = true;
        }

        modelIdInput.focus();
    }

    _showList(resetView = false) {
        const { listView, listActions, formView, formActions, filterInput } = this.el;

        if (!listView || !formView) return;

        if (resetView) {
            this._filterText = '';
            this._sortAsc = null;
            if (filterInput) filterInput.value = '';
        }

        this._editingModel = null;

        listView.classList.remove('hidden');
        if (listActions) listActions.classList.remove('hidden');

        formView.classList.add('hidden');
        if (formActions) formActions.classList.add('hidden');

        this._renderList();
    }

    _handleDeleteModel(model) {
        if (!model) return;
        this._models = this._models.filter(m => (m.id || m.Id) !== (model.id || model.Id));
        this._renderList();
    }

    _showSaveError(message) {
        toast.show(message, 'error', 4000, this.el?.confirmBtn);
    }

    /**
     * Optional numeric field: an empty input must stay absent from the payload, otherwise "not set" would be persisted as 0.
     */
    _parseOptionalInt(value) {
        const trimmed = (value === null || value === undefined) ? '' : value.toString().trim();
        if (trimmed === '') return undefined;
        const parsed = parseInt(trimmed, 10);
        return Number.isFinite(parsed) && parsed > 0 ? parsed : undefined;
    }

    _buildModelPayload() {
        const { idInput, modelIdInput, displayNameInput, contextLengthInput, maxTokensInput, reasoningSelect, isCustomCheckbox, enabledCheckbox } = this.el;

        this._syncSelectedProviderFromSelect();

        const provider = this._selectedProvider || {};

        const payload = {
            id: parseInt(idInput.value, 10) || this._getNextId(),
            modelId: modelIdInput.value.trim(),
            providerType: provider.providerType || 'openai',
            isCustom: !!isCustomCheckbox.checked,
            enabled: !!enabledCheckbox.checked
        };

        if (provider.id !== undefined && provider.id !== null) {
            payload.providerId = provider.id;
        }

        const displayName = displayNameInput.value.trim();
        if (displayName) payload.displayName = displayName;

        const contextLength = this._parseOptionalInt(contextLengthInput.value);
        if (contextLength !== undefined) payload.contextLength = contextLength;

        const maxTokens = this._parseOptionalInt(maxTokensInput.value);
        if (maxTokens !== undefined) payload.maxTokens = maxTokens;

        if (reasoningSelect.value) payload.reasoningEffort = reasoningSelect.value;

        return payload;
    }

    async show() {
        this.el = this._getElements();
        this._detachEvents();

        const { dialog, form, confirmBtn, cancelBtn, formCancelBtn } = this.el;

        if (!dialog || !form || !confirmBtn || !cancelBtn || !formCancelBtn) {
            throw new Error('Missing required dialog elements');
        }

        try {
            const result = await this.onLoad.emitResult();
            const data = (result && result.success) ? result.data : result;
            this._models = (data && (data.models || data.Models)) || [];
        } catch (e) {
            console.error('Failed to load models config', e);
            this._models = [];
        }

        try {
            const providersResult = await this.onLoadProviders.emitResult();
            this._providers = (providersResult && providersResult.success)
                ? (providersResult.data || [])
                : (providersResult || []);
        } catch (e) {
            console.error('Failed to load providers', e);
            this._providers = [];
        }

        this._addBtnClickHandler = () => { this._showForm(); };

        this._adjustCurrentBtnClickHandler = () => {
            const modelState = modelStore.getState();
            const settingsState = settingsStore.getState();
            const activeModelId = modelState.modelId;
            const activeProviderType = settingsState.Provider;
            const activeProviderId = settingsState.ProviderId ?? null;

            if (!activeModelId) {
                toast.show('No active model selected', 'error', 4000, this.el?.adjustCurrentBtn);
                return;
            }

            const existing = this._models.find(m => {
                const mId = m.modelId || m.ModelId;
                const mType = m.providerType || m.ProviderType;
                const mPid = (m.providerId ?? m.ProviderId ?? null);
                return mId === activeModelId
                    && mType === activeProviderType
                    && mPid === activeProviderId;
            });

            if (existing) {
                this._showForm(existing);
            } else {
                this._showForm({
                    modelId: activeModelId,
                    displayName: modelState.modelName || activeModelId,
                    providerType: activeProviderType,
                    providerId: activeProviderId,
                    enabled: true,
                    isCustom: false
                });
            }
        };

        this._providerChangeHandler = () => { this._syncSelectedProviderFromSelect(); };

        this._filterInputHandler = (e) => {
            this._filterText = e.target.value;
            this._renderList();
        };

        this._sortClickHandler = () => {
            if (this._sortAsc === null) {
                this._sortAsc = true;
            } else if (this._sortAsc === true) {
                this._sortAsc = false;
            } else {
                this._sortAsc = null;
            }
            this._renderList();
        };

        this._attachEvents();
        this._showList(true);

        return new Promise((resolve) => {
            dialog.showModal();

            this._confirmHandler = async () => {
                try {
                    const config = { models: this._models };
                    const result = await this.onSave.emitResult(config);
                    if (!this.el) return;
                    if (!(result && result.success)) {
                        console.error('Failed to save models', result?.error);
                        this._showSaveError(result?.error?.message || 'Failed to save models');
                        return;
                    }
                    this._cleanup();
                    dialog.close();
                    resolve(true);
                } catch (err) {
                    console.error('Failed to save models', err);
                    if (!this.el) return;
                    this._showSaveError(err?.message || 'Failed to save models');
                }
            };

            this._formSubmitHandler = (e) => {
                if (e) e.preventDefault();
                if (!this.el) return;

                const { modelIdInput, displayNameInput } = this.el;

                try {
                    if (!this.el.form.checkValidity()) {
                        if (typeof this.el.form.reportValidity === 'function') this.el.form.reportValidity();
                        const firstInvalid = this.el.form.querySelector(':invalid');
                        if (firstInvalid) firstInvalid.focus();
                        return;
                    }

                    const newModel = this._buildModelPayload();

                    if (newModel.isCustom && !newModel.displayName) {
                        displayNameInput.focus();
                        toast.show('Display name is required for a custom model', 'error', 4000, displayNameInput);
                        return;
                    }

                    const existingIndex = this._models.findIndex(m => (m.id === newModel.id || m.Id === newModel.id));
                    if (existingIndex >= 0) {
                        this._models[existingIndex] = newModel;
                    } else {
                        this._models.push(newModel);
                    }

                    if (!this._matchesFilter(newModel)) {
                        this._filterText = '';
                        this._sortAsc = null;
                        const flt = this.el?.filterInput;
                        if (flt) flt.value = '';
                    }

                    this._showList();
                } catch (err) {
                    console.error('Failed to save model', err);
                    if (modelIdInput) modelIdInput.focus();
                }
            };

            this._cancelHandler = () => {
                this._cleanup();
                dialog.close();
                resolve(false);
            };

            this._formCancelHandler = (e) => {
                if (e) e.preventDefault();
                this._showList();
            };

            this._dialogCloseHandler = () => {
                this._cleanup();
                resolve(false);
            };

            confirmBtn.onclick = this._confirmHandler;
            cancelBtn.onclick = this._cancelHandler;
            form.onsubmit = this._formSubmitHandler;
            formCancelBtn.onclick = this._formCancelHandler;
            dialog.onclose = this._dialogCloseHandler;
        });
    }
}
