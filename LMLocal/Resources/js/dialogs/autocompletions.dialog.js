import { createCallback } from '@app/lib/callback.js';

export class AutocompletionsDialog {
    constructor() {
        this.onLoad = createCallback();
        this.onSave = createCallback();
        this.onTest = createCallback();
        this.onListModels = createCallback();

        this._config = null;
        this._providers = [];
        this._models = [];
        this._selectedProvider = null;
        this._searchFilter = '';
        this._loadedOnly = false;
        this._sortAsc = true;
        this._testBtnTimeout = null;
        this._searchDebounce = null;
        this.el = null;
    }

    _getElements() {
        const dialog = document.getElementById('autocompletions-selector-dialog');
        if (!dialog) return {};
        return {
            dialog,
            enableCheckbox: dialog.querySelector('#autocompletions-dialog-enable-checkbox'),
            providerName: dialog.querySelector('#autocompletions-provider-name'),
            modelName: dialog.querySelector('#autocompletions-model-name'),
            changeBtn: dialog.querySelector('#autocompletions-change-btn'),
            infoView: dialog.querySelector('#autocompletions-info-view'),
            selectionView: dialog.querySelector('#autocompletions-selection-view'),
            providerSelect: dialog.querySelector('#autocompletions-provider-select'),
            modelsContainer: dialog.querySelector('#autocompletions-models-container'),
            backBtn: dialog.querySelector('#autocompletions-back-btn'),
            testBtn: dialog.querySelector('#autocompletions-test-btn'),
            cancelBtn: dialog.querySelector('#autocompletions-cancel-btn'),
            saveBtn: dialog.querySelector('#autocompletions-save-btn'),
            modelSearch: dialog.querySelector('#autocompletions-model-search'),
            loadedOnly: dialog.querySelector('#autocompletions-loaded-only'),
            refreshBtn: dialog.querySelector('#autocompletions-refresh-models'),
            sortBtn: dialog.querySelector('#autocompletions-sort-btn'),
        };
    }

    _showInfoView() {
        if (!this.el) return;
        this.el.infoView.classList.remove('hidden');
        this.el.selectionView.classList.add('hidden');
        this._updateInfoDisplay();
    }

    _showSelectionView() {
        if (!this.el) return;
        this.el.infoView.classList.add('hidden');
        this.el.selectionView.classList.remove('hidden');
        this._populateProviderSelect();
    }

    _updateInfoDisplay() {
        if (!this._config) return;
        this.el.enableCheckbox.checked = this._config.enabled || false;
        if (this._selectedProvider) {
            this.el.providerName.textContent = this._selectedProvider.name || this._selectedProvider.providerType || '—';
        } else {
            this.el.providerName.textContent = '—';
        }
        this.el.modelName.textContent = this._config.modelId || '—';
    }

    _populateProviderSelect() {
        const select = this.el.providerSelect;
        select.innerHTML = '';
        const changeHandler = select._acChangeHandler;
        if (changeHandler) select.removeEventListener('change', changeHandler);
        this._providers.forEach(p => {
            const opt = document.createElement('option');
            opt.value = p.id;
            opt.textContent = p.name || p.providerType || 'Unknown';
            opt._providerData = p;
            if (this._config && p.providerType === this._config.providerType &&
                p.id === this._config.providerId) {
                opt.selected = true;
                this._selectedProvider = p;
            }
            select.appendChild(opt);
        });
        if (changeHandler) select.addEventListener('change', changeHandler);
    }

    async _loadModels() {
        if (!this.el) return;
        this._searchFilter = '';
        this._loadedOnly = false;
        this._sortAsc = true;
        if (this.el.modelSearch) this.el.modelSearch.value = '';
        if (this.el.loadedOnly) this.el.loadedOnly.checked = false;
        if (this._searchDebounce) { clearTimeout(this._searchDebounce); this._searchDebounce = null; }
        const container = this.el.modelsContainer;
        if (!container) return;
        container.innerHTML = '<div class="loading-placeholder"><div class="spinner"></div><span>Loading models...</span></div>';

        const select = this.el.providerSelect;
        const selectedOpt = select.options[select.selectedIndex];
        if (!selectedOpt?._providerData) return;

        const provider = selectedOpt._providerData;
        this._selectedProvider = provider;

        try {
            const result = await this.onListModels.emitResult(
                provider.providerType || 'openai',
                provider.customBaseUrl || '',
                provider.customApiKey || ''
            );
            if (!this.el) return;
            let raw = result;
            if (result?.success && result.data) raw = result.data;
            this._models = Array.isArray(raw?.models) ? raw.models : [];
            this._renderModels();
        } catch (err) {
            console.error('Failed to load models:', err);
            if (!this.el) return;
            const c = this.el.modelsContainer;
            if (c) c.innerHTML = `<div class="empty-placeholder"><span style="color:var(--danger-color);padding:20px;">Error: ${this._escapeHtml(err.message)}</span></div>`;
            this._models = [];
        }
    }

    _getFilteredModels() {
        let list = this._models.slice();
        if (this._searchFilter) {
            const q = this._searchFilter.toLowerCase();
            list = list.filter(m => {
                const id = (m.id || '').toLowerCase();
                const name = (m.name || '').toLowerCase();
                return id.includes(q) || name.includes(q);
            });
        }
        if (this._loadedOnly) {
            list = list.filter(m => m.isLoaded === true);
        }
        list.sort((a, b) => {
            const aName = (a.name || a.id || '').toLowerCase();
            const bName = (b.name || b.id || '').toLowerCase();
            return aName.localeCompare(bName);
        });
        if (!this._sortAsc) list.reverse();
        return list;
    }

    _renderModels() {
        if (!this.el) return;
        const container = this.el.modelsContainer;
        if (!container) return;
        const filtered = this._getFilteredModels();
        if (!filtered.length) {
            container.innerHTML = '<div class="empty-placeholder"><span>No models available.</span></div>';
            return;
        }

        container.innerHTML = filtered.map(m => {
            const modelId = m.id || 'unknown';
            const modelName = m.name || modelId;
            const isSelected = this._config && modelId === this._config.modelId;
            let badgeHtml = '';
            if (m.isLoaded === true) {
                badgeHtml = '<span class="model-status-badge loaded">LOADED</span>';
            } else if (m.isLoaded === false) {
                badgeHtml = '<span class="model-status-badge">NOT LOADED</span>';
            }
            return `<div class="model-card ${isSelected ? 'active' : ''}" data-model-id="${this._escapeHtml(modelId)}">
                <div class="model-card-header">
                    <div class="model-name">${this._escapeHtml(modelName)}</div>
                    ${badgeHtml}
                </div>
                <div class="model-id">${this._escapeHtml(modelId)}</div>
            </div>`;
        }).join('');

        container.querySelectorAll('.model-card').forEach(card => {
            card.addEventListener('click', () => {
                const modelId = card.dataset.modelId;
                this._config.modelId = modelId;
                this._config.providerType = this._selectedProvider.providerType;
                this._config.providerId = this._selectedProvider.id;
                this._showInfoView();
            });
        });
    }

    _escapeHtml(text) {
        if (!text) return '';
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    _resetTestButton() {
        const testBtn = this.el?.testBtn;
        if (!testBtn) return;
        if (this._testBtnTimeout) { clearTimeout(this._testBtnTimeout); this._testBtnTimeout = null; }
        testBtn.disabled = false;
        testBtn.classList.remove('success', 'error');
        testBtn.innerHTML = `<svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor">
            <path d="M11.5 2a.5.5 0 0 1 .5-.5v4a.5.5 0 0 1-1 0V3H8.5v3.293l1.854 1.853a.5.5 0 0 1-.708.708L8.5 7.707V14H7.5V7.707L6.354 8.854a.5.5 0 1 1-.708-.708L7.5 6.293V3H4.5v3.5a.5.5 0 0 1-1 0v-4a.5.5 0 0 1 .5-.5h8z" />
        </svg>
        <span>Test</span>`;
    }

    async show() {
        this.el = this._getElements();
        const { dialog, enableCheckbox, changeBtn, backBtn, testBtn, cancelBtn, saveBtn, providerSelect, modelSearch, loadedOnly, refreshBtn, sortBtn } = this.el;
        if (!dialog) throw new Error('Dialog #autocompletions-selector-dialog not found');

        this._resetTestButton();

        // Load config and providers
        try {
            const result = await this.onLoad.emitResult();
            if (result?.success && result.data) {
                this._config = result.data;
                this._providers = result.providers || [];
                if (this._config) {
                    this._selectedProvider = this._providers.find(p =>
                        p.providerType === this._config.providerType &&
                        p.id === this._config.providerId
                    );
                }
            }
        } catch (e) {
            console.error('Failed to load autocompletions config', e);
            this._config = { enabled: false, providerId: 0, providerType: 'lmstudio', modelId: '' };
            this._providers = [];
        }

        this._showInfoView();

        const onEnableChange = () => {
            if (this._config) this._config.enabled = enableCheckbox.checked;
        };
        enableCheckbox.addEventListener('change', onEnableChange);

        const onChangeClick = () => { this._showSelectionView(); this._loadModels(); };
        changeBtn.addEventListener('click', onChangeClick);

        const onBackClick = () => { this._showInfoView(); };
        backBtn.addEventListener('click', onBackClick);

        const onProviderChange = () => { this._loadModels(); };
        providerSelect.addEventListener('change', onProviderChange);
        providerSelect._acChangeHandler = onProviderChange;

        const onModelSearchInput = () => {
            if (this._searchDebounce) clearTimeout(this._searchDebounce);
            this._searchDebounce = setTimeout(() => {
                this._searchFilter = modelSearch.value;
                this._renderModels();
            }, 200);
        };
        modelSearch.addEventListener('input', onModelSearchInput);

        const onLoadedOnlyChange = () => {
            this._loadedOnly = loadedOnly.checked;
            this._renderModels();
        };
        loadedOnly.addEventListener('change', onLoadedOnlyChange);

        const onSortClick = () => {
            this._sortAsc = !this._sortAsc;
            this._renderModels();
        };
        sortBtn.addEventListener('click', onSortClick);

        const onRefreshClick = () => {
            this._loadModels();
        };
        refreshBtn.addEventListener('click', onRefreshClick);

        const onTestClick = async (e) => {
            e.preventDefault();
            if (this._testBtnTimeout) { clearTimeout(this._testBtnTimeout); this._testBtnTimeout = null; }
            testBtn.disabled = true;
            testBtn.classList.remove('success', 'error');
            testBtn.innerHTML = '<span class="btn-spinner"></span>';
            try {
                const select = this.el.providerSelect;
                const selectedOpt = select.options[select.selectedIndex];
                const provider = selectedOpt?._providerData || {};
                const result = await this.onTest.emitResult({
                    providerType: provider.providerType || 'openai',
                    baseUrl: provider.customBaseUrl || '',
                    apiKey: provider.customApiKey || '',
                    modelId: this._config?.modelId || ''
                });
                const successIcon = `<svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor"><path d="M13.854 3.646a.5.5 0 0 1 0 .708l-7 7a.5.5 0 0 1-.708 0l-3.5-3.5a.5.5 0 1 1 .708-.708L6.5 10.293l6.646-6.647a.5.5 0 0 1 .708 0z"/></svg>`;
                const errorIcon = `<svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor"><path d="M4.646 4.646a.5.5 0 0 1 .708 0L8 7.293l2.646-2.647a.5.5 0 0 1 .708.708L8.707 8l2.647 2.646a.5.5 0 0 1-.708.708L8 8.707l-2.646 2.647a.5.5 0 0 1-.708-.708L7.293 8 4.646 5.354a.5.5 0 0 1 0-.708z"/></svg>`;
                if (result?.success && result?.data) {
                    testBtn.innerHTML = `${successIcon} <span>Test</span>`;
                    testBtn.classList.add('success');
                } else {
                    testBtn.innerHTML = `${errorIcon} <span>Test</span>`;
                    testBtn.classList.add('error');
                }
            } catch (err) {
                console.error('Test autocomplete error', err);
                const errorIcon = `<svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor"><path d="M4.646 4.646a.5.5 0 0 1 .708 0L8 7.293l2.646-2.647a.5.5 0 0 1 .708.708L8.707 8l2.647 2.646a.5.5 0 0 1-.708.708L8 8.707l-2.646 2.647a.5.5 0 0 1-.708-.708L7.293 8 4.646 5.354a.5.5 0 0 1 0-.708z"/></svg>`;
                testBtn.innerHTML = `${errorIcon} <span>Test</span>`;
                testBtn.classList.add('error');
            } finally {
                this._testBtnTimeout = setTimeout(() => { this._resetTestButton(); this._testBtnTimeout = null; }, 3000);
            }
        };
        testBtn.addEventListener('click', onTestClick);

        dialog.showModal();

        return new Promise((resolve) => {
            const onCancel = () => { cleanup(); resolve(null); };
            const onSave = async () => { await this.onSave.emit(this._config); cleanup(); resolve(this._config); };
            const cleanup = () => {
                enableCheckbox.removeEventListener('change', onEnableChange);
                changeBtn.removeEventListener('click', onChangeClick);
                backBtn.removeEventListener('click', onBackClick);
                providerSelect.removeEventListener('change', onProviderChange);
                testBtn.removeEventListener('click', onTestClick);
                cancelBtn.removeEventListener('click', onCancel);
                saveBtn.removeEventListener('click', onSave);
                dialog.removeEventListener('close', onClose);
                if (modelSearch) modelSearch.removeEventListener('input', onModelSearchInput);
                if (loadedOnly) loadedOnly.removeEventListener('change', onLoadedOnlyChange);
                if (sortBtn) sortBtn.removeEventListener('click', onSortClick);
                if (refreshBtn) refreshBtn.removeEventListener('click', onRefreshClick);
                if (this._searchDebounce) { clearTimeout(this._searchDebounce); this._searchDebounce = null; }
                if (this._testBtnTimeout) { clearTimeout(this._testBtnTimeout); this._testBtnTimeout = null; }
                delete providerSelect._acChangeHandler;
                this.el = null;
                dialog.close();
            };
            const onClose = () => { cleanup(); resolve(null); };
            cancelBtn.addEventListener('click', onCancel);
            saveBtn.addEventListener('click', onSave);
            dialog.addEventListener('close', onClose);
        });
    }
}
