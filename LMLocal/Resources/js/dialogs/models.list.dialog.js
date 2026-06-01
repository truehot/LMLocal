import { createCallback } from '@app/lib/callback.js';

export class ModelSelectorDialog {
    constructor(models = [], activeModel = null) {
        this.modelsList = models;
        this.filterText = '';
        this.sortAsc = true;
        this.showOnlyActive = false;
        this.isLoading = false;
        this.selectedModel = activeModel || null;
        this.el = null;

        this.onRefresh = createCallback();
        this.onSelect = createCallback();

        this._onRefreshClick = null;
        this._onCloseClick = null;
        this._onFilterInput = null;
        this._onSortClick = null;
        this._onToggle = null;
    }

    _getElements() {
        return {
            dialog: document.getElementById('model-selector-dialog'),
            container: document.getElementById('models-list-container'),
            refreshBtn: document.getElementById('model-refresh-btn'),
            filterInput: document.getElementById('model-filter-input'),
            sortBtn: document.getElementById('model-sort-btn'),
            closeBtn: document.getElementById('model-selector-close'),
            activeToggle: document.getElementById('model-active-only-toggle'),
        };
    }

    async _loadModels(showLoadingState = true) {
        if (this.isLoading) return;
        this.isLoading = true;
        try {
            if (showLoadingState) this._showLoadingState();
            const result = await this.onRefresh.emitResult();
            if (!result?.success) {
                this._showErrorState(result?.error?.message || 'Failed to load models');
                return;
            }
            const response = result.data || {};
            this.selectedModel = response.hasActiveModel && response.activeModel ? response.activeModel : this.selectedModel;
            const models = Array.isArray(response.models) ? response.models : [];
            this.modelsList = models;
            this._renderModels();
        } catch (error) {
            console.error('Failed to load models:', error);
            this._showErrorState(`Failed to load models: ${error.message}`);
        } finally {
            this.isLoading = false;
        }
    }

    _showLoadingState() {
        if (this.el?.container) {
            this.el.container.innerHTML = `
                <div class="loading-placeholder">
                    <div class="spinner"></div>
                    <span>Fetching models from endpoint...</span>
                </div>
            `;
        }
    }

    _showErrorState(errorMessage) {
        if (this.el?.container) {
            this.el.container.innerHTML = `
                <div class="error-placeholder">
                    <span style="color: var(--danger-color); padding: 20px;">Error: ${this._escapeHtml(errorMessage)}</span>
                </div>
            `;
        }
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

    _renderModels() {
        if (!this.el?.container) return;

        let displayList = this.modelsList.filter(model => {
            const nameMatch = (model.name || model.id).toLowerCase().includes(this.filterText);
            const activeMatch = this.showOnlyActive ? model.isLoaded === true : true;
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


            const statusClass = model.isLoaded ? 'status-loaded' : 'status-unloaded';
            const statusText = model.isLoaded ? 'Loaded' : 'Not loaded';

            return `
        <div class="model-card ${isSelected ? 'active' : ''}" data-model-id="${this._escapeHtml(modelId)}">
            <div class="model-card-header">
                <div class="model-name">${this._escapeHtml(modelName)}</div>
                <div class="model-status-badge ${statusClass}">${statusText}</div>
            </div>
            <div class="model-id">${this._escapeHtml(modelId)}</div>
            <div class="model-metadata">
                ${metaItems.join('')} 
            </div>
        </div>`;
        }).join('');

        this.el.container.innerHTML = `<div class="models-grid">${modelsHtml}</div>`;
        this._attachModelCardHandlers();
    }

    _attachModelCardHandlers() {
        const cards = this.el.container.querySelectorAll('.model-card');
        cards.forEach(card => {
            card.addEventListener('click', async (e) => {
                e.stopPropagation();
                const modelId = card.dataset.modelId;
                const model = this.modelsList.find(m => m.id === modelId);
                if (model) await this._selectModel(model);
            });
        });
    }

    async _selectModel(model) {
        try {
            const result = await this.onSelect.emitResult(model);
            if (result?.success !== false) {
                this.selectedModel = model;
                if (this.el?.dialog) this.el.dialog.close();
            } else {
                this._showErrorState('Failed to set active model');
            }
        } catch (error) {
            console.error('Model selection failed:', error);
            this._showErrorState(`Model selection failed: ${error.message}`);
        }
    }

    _escapeHtml(text) {
        if (!text) return '';
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    _attachEvents() {
        this._onRefreshClick = (e) => {
            e.stopPropagation();
            this.el.refreshBtn.classList.add('spinning');
            this._loadModels(false).finally(() => {
                this.el?.refreshBtn?.classList.remove('spinning');
            });
        };
        this._onCloseClick = () => {
            if (this.el?.dialog) this.el.dialog.close();
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

        this.el.activeToggle?.addEventListener('change', this._onToggle);
        this.el.filterInput?.addEventListener('input', this._onFilterInput);
        this.el.sortBtn?.addEventListener('click', this._onSortClick);
        this.el.refreshBtn?.addEventListener('click', this._onRefreshClick);
        this.el.closeBtn?.addEventListener('click', this._onCloseClick);
    }

    _detachEvents() {
        if (this.el.filterInput && this._onFilterInput) {
            this.el.filterInput.removeEventListener('input', this._onFilterInput);
        }
        if (this.el.sortBtn && this._onSortClick) {
            this.el.sortBtn.removeEventListener('click', this._onSortClick);
        }
        if (this.el.refreshBtn && this._onRefreshClick) {
            this.el.refreshBtn.removeEventListener('click', this._onRefreshClick);
        }
        if (this.el.closeBtn && this._onCloseClick) {
            this.el.closeBtn.removeEventListener('click', this._onCloseClick);
        }
        if (this.el.activeToggle && this._onToggle) {
            this.el.activeToggle.removeEventListener('change', this._onToggle);
        }
        this._onRefreshClick = null;
        this._onCloseClick = null;
        this._onFilterInput = null;
        this._onSortClick = null;
        this._onToggle = null;
    }

    async show() {
        this.el = this._getElements();
        if (!this.el.dialog) throw new Error('Dialog #model-selector-dialog not found');

        if (this.el.activeToggle) {
            this.showOnlyActive = !!this.el.activeToggle.checked;
        }

        this._attachEvents();

        if (this.modelsList.length) {

            this._renderModels();
        } else {
            await this._loadModels();
        }

        this.el.dialog.showModal();

        return new Promise((resolve) => {
            const onClose = () => {
                this._detachEvents();
                this.el.dialog.removeEventListener('close', onClose);
                resolve(this.selectedModel || null);
                this.el = null;
            };
            this.el.dialog.addEventListener('close', onClose);
        });
    }
}