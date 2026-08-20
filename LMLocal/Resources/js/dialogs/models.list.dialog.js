import { createCallback } from '@app/lib/callback.js';
import { populateProviderSelect } from '@app/lib/populate-provider.select.js';
import { AsyncGuard } from '@app/lib/async.guard.js';

export class ModelSelectorDialog {
    constructor(models = [], activeModel = null, supportsIsLoaded = true) {
        this.modelsList = models;
        this.filterText = '';
        this.sortAsc = true;
        this.showOnlyActive = false;
        this.isLoading = false;
        this.selectedModel = activeModel || null;
        this.el = null;

        this.onRefresh = createCallback();
        this.onSelect = createCallback();
        this.onLoadProviders = createCallback();
        this.onSaveProvider = createCallback();

        this.previousProviderValue = null;
        this._supportsIsLoaded = supportsIsLoaded;
        this._guard = new AsyncGuard();

        this._onRefreshClick = null;
        this._onCloseClick = null;
        this._onFilterInput = null;
        this._onSortClick = null;
        this._onToggle = null;
        this._onProviderChange = null;
        this._onModelCardClick = null;
    }

    _getElements() {
        const dialog = document.getElementById('model-selector-dialog');
        if (!dialog) return {};

        return {
            dialog,
            container: dialog.querySelector('#models-list-container'),
            refreshBtn: dialog.querySelector('#model-refresh-btn'),
            filterInput: dialog.querySelector('#model-filter-input'),
            sortBtn: dialog.querySelector('#model-sort-btn'),
            closeBtn: dialog.querySelector('#model-selector-close'),
            activeToggle: dialog.querySelector('#model-active-only-toggle'),
            providerSelect: dialog.querySelector('#model-provider-select'),
        };
    }

    async _loadModels(showLoadingState = true) {
        if (!this.el) return 0;
        const generation = this._guard.start();
        this.isLoading = true;
        try {
            if (showLoadingState) this._showLoadingState();
            const result = await this.onRefresh.emitResult();
            if (!this.el || !this._guard.isCurrent(generation)) return generation;
            if (!result?.success) {
                this._showErrorState(result?.error?.message || 'Failed to load models');
                return generation;
            }
            const response = result.data || {};
            this.selectedModel = response.hasActiveModel && response.activeModel ? response.activeModel : this.selectedModel;
            const models = Array.isArray(response.models) ? response.models : [];
            this.modelsList = models;
            this._supportsIsLoaded = response.supportsIsLoaded !== false;
            this._updateToggleVisibility();
            this._renderModels();
            return generation;
        } catch (error) {
            console.error('Failed to load models:', error);
            if (this._guard.isCurrent(generation)) {
                this._showErrorState(`Failed to load models: ${error.message}`);
            }
            return generation;
        } finally {
            if (this.el) {
                this.isLoading = false;
            }
        }
    }

    _showLoadingState() {
        if (!this.el?.container) return;
        this.el.container.innerHTML = `
                <div class="loading-placeholder">
                    <div class="spinner"></div>
                    <span>Fetching models from endpoint...</span>
                </div>
            `;
    }

    _showErrorState(errorMessage) {
        if (!this.el?.container) return;
        this.el.container.innerHTML = `
                <div class="error-placeholder">
                    <span style="color: var(--danger-color); padding: 20px;">Error: ${this._escapeHtml(errorMessage)}</span>
                </div>
            `;
    }

    _showEmptyState() {
        if (!this.el?.container) return;
        const isFiltering = this.filterText.length > 0;
        this.el.container.innerHTML = `
            <div class="empty-placeholder">
                <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1" stroke-linecap="round" stroke-linejoin="round">
                    <circle cx="11" cy="11" r="8"></circle>
                    <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
                </svg>
                <span>
                    ${isFiltering
                ? `No models match "<strong>${this._escapeHtml(this.filterText)}</strong>"`
                : 'No models available at the moment.'}
                </span>
            </div>
        `;
    }

    _updateToggleVisibility() {
        if (!this.el?.activeToggle) return;
        const toggleContainer = this.el.activeToggle.closest('.model-filter-toggle') ||
            this.el.activeToggle.closest('.toggle-container') ||
            this.el.activeToggle;
        if (this._supportsIsLoaded === false) {
            toggleContainer.classList.add('hidden');
            this.showOnlyActive = false;
        } else {
            toggleContainer.classList.remove('hidden');
        }
    }

    _renderModels() {
        if (!this.el?.container) return;

        let displayList = this.modelsList.filter(model => {
            const nameMatch = (model.name || model.id).toLowerCase().includes(this.filterText);
            const activeMatch = this._supportsIsLoaded !== false && this.showOnlyActive ? model.isLoaded === true : true;
            return nameMatch && activeMatch;
        });

        if (displayList.length === 0) {
            this._showEmptyState();
            return;
        }

        displayList.sort((a, b) => {
            const aIsSelected = a.id === this.selectedModel?.id ? 1 : 0;
            const bIsSelected = b.id === this.selectedModel?.id ? 1 : 0;
            if (aIsSelected !== bIsSelected) return bIsSelected - aIsSelected;

            const nameA = (a.name || a.id).toLowerCase();
            const nameB = (b.name || b.id).toLowerCase();
            return this.sortAsc ? nameA.localeCompare(nameB) : nameB.localeCompare(nameA);
        });

        const modelsHtml = displayList.map(model => {
            const modelId = model.id || 'unknown';
            const modelName = model.name || modelId;
            const isSelected = model.id === this.selectedModel?.id;

            const metaItems = [];

            if (model.sizeInBytes) {
                metaItems.push(`<div class="model-size">${(model.sizeInBytes / (1024 * 1024)).toFixed(2)} MB</div>`);
            }

            if (model.maxTokens) {
                metaItems.push(`<div class="model-tokens">${this._escapeHtml(model.maxTokens)} context</div>`);
            }

            if (model.supportsToolUse != null) {
                const toolClass = model.supportsToolUse ? 'model-tooluse-active' : 'model-tooluse-none';
                const toolText = model.supportsToolUse ? 'Tool Use: Yes' : 'Tool Use: No';
                metaItems.push(`<div class="model-tooluse ${toolClass}">${toolText}</div>`);
            }

            if (model.supportsVision != null) {
                const visionClass = model.supportsVision ? 'model-tooluse-active' : 'model-tooluse-none';
                const visionText = model.supportsVision ? 'Vision: Yes' : 'Vision: No';
                metaItems.push(`<div class="model-tooluse ${visionClass}">${visionText}</div>`);
            }


            const badgeHtml = this._supportsIsLoaded !== false
                ? `<div class="model-status-badge ${model.isLoaded ? 'status-loaded' : 'status-unloaded'}">${model.isLoaded ? 'Loaded' : 'Not loaded'}</div>`
                : '';

            return `
        <div class="model-card ${isSelected ? 'active' : ''}" data-model-id="${this._escapeHtml(modelId)}">
            <div class="model-card-header">
                <div class="model-name">${this._escapeHtml(modelName)}</div>
                ${badgeHtml}
            </div>
            <div class="model-id">${this._escapeHtml(modelId)}</div>
            <div class="model-metadata">
                ${metaItems.join('')} 
            </div>
        </div>`;
        }).join('');

        this.el.container.innerHTML = `<div class="models-grid">${modelsHtml}</div>`;
    }

    async _selectModel(model) {
        try {
            const result = await this.onSelect.emitResult(model);
            if (result?.success !== false) {
                this.selectedModel = model;
                if (this.el.dialog) this.el.dialog.close();
            } else {
                this._showErrorState('Failed to set active model');
            }
        } catch (error) {
            console.error('Model selection failed:', error);

            if (this.el?.container) {
                this._showErrorState(`Model selection failed: ${error.message}`);
            }
        }
    }

    _escapeHtml(text) {
        if (!text) return '';
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    _setControlsEnabled(enabled) {
        if (!this.el) return;
        const { providerSelect, refreshBtn, filterInput, sortBtn, activeToggle, closeBtn } = this.el;
        if (providerSelect) providerSelect.disabled = !enabled;
        if (refreshBtn) refreshBtn.disabled = !enabled;
        if (filterInput) filterInput.disabled = !enabled;
        if (sortBtn) sortBtn.disabled = !enabled;
        if (activeToggle) activeToggle.disabled = !enabled;
        if (closeBtn) closeBtn.disabled = !enabled;
    }

    async _handleProviderChange() {
        if (!this.el) return;
        const select = this.el.providerSelect;
        const selectedOption = select.options[select.selectedIndex];
        if (!selectedOption?._providerData) return;

        const providerData = selectedOption._providerData;
        const previousValue = this.previousProviderValue;
        const generation = this._guard.start();
        this.previousProviderValue = select.value;

        this._setControlsEnabled(false);
        this._showLoadingState();

        try {
            const result = await this.onSaveProvider.emitResult({
                Provider: providerData.providerType || 'openai',
                ProviderId: providerData.id ?? null,
                LmStudioBaseUrl: providerData.customBaseUrl || '',
                ApiKey: providerData.customApiKey || '',
            });

            if (!this.el || !this._guard.isCurrent(generation)) return;

            if (result?.success !== false) {
                await this._loadModels(false);
            } else {
                select.value = previousValue;
                this.previousProviderValue = previousValue;
                this._showErrorState(result?.error?.message || 'Failed to save provider');
            }
        } catch (err) {
            console.error('Provider change failed:', err);
            if (!this.el || !this._guard.isCurrent(generation)) return;
            select.value = previousValue;
            this.previousProviderValue = previousValue;
            this._showErrorState(`Provider change failed: ${err.message}`);
        } finally {
            if (this.el) {
                this._setControlsEnabled(true);
            }
        }
    }

    _attachEvents() {
        this._onRefreshClick = (e) => {
            e.stopPropagation();
            if (!this.el?.refreshBtn) return;
            this.el.refreshBtn.classList.add('spinning');
            this._loadModels(false).finally(() => {
                if (this.el) {
                    this.el.refreshBtn.classList.remove('spinning');
                }
            });
        };
        this._onCloseClick = () => {
            this.el.dialog.close();
        };
        this._onFilterInput = (e) => {
            this.filterText = e.target.value.toLowerCase();
            this._renderModels();
        };
        this._onSortClick = () => {
            this.sortAsc = !this.sortAsc;
            this._renderModels();
        };
        this._onToggle = (e) => {
            this.showOnlyActive = e.target.checked;
            this._renderModels();
        };
        this._onProviderChange = () => {
            this._handleProviderChange();
        };
        this._onModelCardClick = async (e) => {
            const card = e.target.closest('.model-card');
            if (!card) return;
            e.stopPropagation();
            const modelId = card.dataset.modelId;
            const model = this.modelsList.find(m => m.id === modelId);
            if (model) await this._selectModel(model);
        };

        this.el.activeToggle.addEventListener('change', this._onToggle);
        this.el.filterInput.addEventListener('input', this._onFilterInput);
        this.el.sortBtn.addEventListener('click', this._onSortClick);
        this.el.refreshBtn.addEventListener('click', this._onRefreshClick);
        this.el.closeBtn.addEventListener('click', this._onCloseClick);
        this.el.providerSelect.addEventListener('change', this._onProviderChange);
        this.el.container.addEventListener('click', this._onModelCardClick);
    }

    _detachEvents() {
        this.el.filterInput.removeEventListener('input', this._onFilterInput);
        this.el.sortBtn.removeEventListener('click', this._onSortClick);
        this.el.refreshBtn.removeEventListener('click', this._onRefreshClick);
        this.el.closeBtn.removeEventListener('click', this._onCloseClick);
        this.el.activeToggle.removeEventListener('change', this._onToggle);
        this.el.providerSelect.removeEventListener('change', this._onProviderChange);
        this.el.container.removeEventListener('click', this._onModelCardClick);
        this._onRefreshClick = null;
        this._onCloseClick = null;
        this._onFilterInput = null;
        this._onSortClick = null;
        this._onToggle = null;
        this._onProviderChange = null;
        this._onModelCardClick = null;
    }

    async show() {
        if (this.el?.dialog?.open) {
            this.el.dialog.close();
        }

        this.el = this._getElements();
        this.filterText = '';
        this.sortAsc = true;
        this.showOnlyActive = false;
        this._guard.invalidate();

        if (!this.el.dialog) throw new Error('Dialog #model-selector-dialog not found');

        this._setControlsEnabled(true);

        this.el.filterInput.value = '';
        this.el.activeToggle.checked = false;

        this._attachEvents();

        const resultPromise = new Promise((resolve) => {
            const onClose = () => {
                try {
                    this._detachEvents();
                    this.onLoadProviders.off();
                    this.onSaveProvider.off();
                    this.el.dialog.removeEventListener('close', onClose);
                    resolve(this.selectedModel || null);
                } catch (err) {
                    console.error('Error during dialog close cleanup:', err);
                    resolve(this.selectedModel || null);
                } finally {
                    this._guard.invalidate();
                    this.el = null;
                }
            };
            this.el.dialog.addEventListener('close', onClose);
        });

        this.el.dialog.showModal();

        try {
            const providersResult = await this.onLoadProviders.emitResult();
            if (!this.el) return resultPromise;
            if (providersResult?.success) {
                const data = providersResult.data || {};

                const isActiveProvider = (p) => {
                    if (!p) return false;
                    const type = (data.Provider || '').toLowerCase();
                    if ((p.providerType || '').toLowerCase() !== type) return false;
                    if (data.ProviderId != null) return String(p.id) === String(data.ProviderId);

                    return (p.customBaseUrl || '') === (data.LmStudioBaseUrl || '')
                        && (!p.customApiKey || p.customApiKey === (data.ApiKey || ''));
                };

                const allProviders = {
                    defaultProviders: (data.defaultProviders || []).filter(p => p.customBaseUrl || isActiveProvider(p)),
                    providers: (data.providers || []).filter(p => p.customBaseUrl || isActiveProvider(p)),
                };
                this.previousProviderValue = populateProviderSelect(
                    this.el.providerSelect,
                    allProviders,
                    data,
                );
            }
        } catch (err) {
            console.error('Failed to load providers for model dialog:', err);
        }

        if (!this.el) return resultPromise;

        if (this.modelsList.length) {
            this._updateToggleVisibility();
            this._renderModels();
        } else {
            await this._loadModels();
        }

        return resultPromise;
    }
}
