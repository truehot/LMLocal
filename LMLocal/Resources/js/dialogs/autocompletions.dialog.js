import { Icons } from '@app/constants/app.globals.js';
import { createCallback } from '@app/lib/callback.js';
import { AsyncGuard } from '@app/lib/async.guard.js';
import { populateProviderSelect } from '@app/lib/populate-provider.select.js';
import toast from '@app/lib/toast.js';
import { escapeHtml } from '@app/lib/escape.js';

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
        this._testGeneration = 0;
        this._searchDebounce = null;
        this._supportsIsLoaded = true;
        this._onProviderChange = null;
        this._onModelCardClick = null;
        this._populatingProvider = false;
        this._guard = new AsyncGuard();
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
            debounceInput: dialog.querySelector('#autocompletions-debounce'),
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
        if (this.el.debounceInput) {
            this.el.debounceInput.value = this._config.debounceDelayMs ?? 300;
        }
        if (this._selectedProvider) {
            this.el.providerName.textContent = this._selectedProvider.name || this._selectedProvider.providerType || '—';
        } else {
            this.el.providerName.textContent = '—';
        }
        this.el.modelName.textContent = this._config.modelId || '—';
    }

    _populateProviderSelect() {
        this._populatingProvider = true;
        try {
            populateProviderSelect(
                this.el.providerSelect,
                this._providers,
                { providerType: this._config?.providerType, providerId: this._config?.providerId }
            );
            const selectedOpt = this.el.providerSelect.options[this.el.providerSelect.selectedIndex];
            this._selectedProvider = selectedOpt?._providerData || null;
        } finally {
            this._populatingProvider = false;
        }
    }

    async _loadModels(resetFilters = true) {
        if (!this.el) return;
        const generation = this._guard.start();
        if (resetFilters) {
            this._searchFilter = '';
            this._loadedOnly = false;
            this._sortAsc = true;
            if (this.el.modelSearch) this.el.modelSearch.value = '';
            if (this.el.loadedOnly) this.el.loadedOnly.checked = false;
            if (this._searchDebounce) { clearTimeout(this._searchDebounce); this._searchDebounce = null; }
        }
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
            if (!this.el || !this._guard.isCurrent(generation)) return;
            let raw = result;
            if (result?.success && result.data) raw = result.data;
            this._models = Array.isArray(raw?.models) ? raw.models : [];
            this._supportsIsLoaded = raw.supportsIsLoaded !== false;
            if (!this.el) return;
            if (this.el.loadedOnly) {
                const toggleContainer = this.el.loadedOnly.closest('.toggle-container');
                const target = toggleContainer || this.el.loadedOnly;
                if (this._supportsIsLoaded === false) {
                    target.classList.add('hidden');
                    this._loadedOnly = false;
                } else {
                    target.classList.remove('hidden');
                }
            }
            this._renderModels();
        } catch (err) {
            console.error('Failed to load models:', err);
            if (!this.el || !this._guard.isCurrent(generation)) return;
            const c = this.el.modelsContainer;
            if (c) c.innerHTML = `<div class="empty-placeholder"><span style="color:var(--danger-color);padding:20px;">Error: ${escapeHtml(err.message)}</span></div>`;
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
        if (this._loadedOnly && this._supportsIsLoaded !== false) {
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
            if (this._supportsIsLoaded !== false) {
                if (m.isLoaded === true) {
                    badgeHtml = '<span class="model-status-badge status-loaded">LOADED</span>';
                } else if (m.isLoaded === false) {
                    badgeHtml = '<span class="model-status-badge status-unloaded">NOT LOADED</span>';
                }
            }
            return `<div class="model-card ${isSelected ? 'active' : ''}" data-model-id="${escapeHtml(modelId)}">
                <div class="model-card-header">
                    <div class="model-name">${escapeHtml(modelName)}</div>
                    ${badgeHtml}
                </div>
                <div class="model-id">${escapeHtml(modelId)}</div>
            </div>`;
        }).join('');
    }

    _resetTestButton() {
        const testBtn = this.el?.testBtn;
        if (!testBtn) return;
        if (this._testBtnTimeout) { clearTimeout(this._testBtnTimeout); this._testBtnTimeout = null; }
        testBtn.disabled = false;
        testBtn.classList.remove('success', 'error');
        testBtn.innerHTML = `${Icons.LINK}&nbsp;<span>Test</span>`;
    }

    async show() {
        if (this.el?.dialog?.open) {
            this.el.dialog.close();
        }

        this.el = this._getElements();
        const { dialog, enableCheckbox, changeBtn, backBtn, testBtn, cancelBtn, saveBtn, providerSelect, modelSearch, loadedOnly, refreshBtn, sortBtn, modelsContainer } = this.el;
        if (!dialog) throw new Error('Dialog #autocompletions-selector-dialog not found');

        this._searchFilter = '';
        this._loadedOnly = false;
        this._sortAsc = true;
        if (modelSearch) modelSearch.value = '';
        if (loadedOnly) loadedOnly.checked = false;
        this._guard.invalidate();
        this._resetTestButton();
        if (this._searchDebounce) { clearTimeout(this._searchDebounce); this._searchDebounce = null; }

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
            this._config = { enabled: false, providerId: 0, providerType: 'lmstudio', modelId: '', debounceDelayMs: 300 };
            this._providers = [];
        }

        this._showInfoView();

        const onEnableChange = () => {
            if (this._config) this._config.enabled = enableCheckbox.checked;
        };
        enableCheckbox.addEventListener('change', onEnableChange);

        const onDebounceChange = () => {
            if (this._config && this.el.debounceInput) {
                const val = parseInt(this.el.debounceInput.value, 10);
                this._config.debounceDelayMs = !isNaN(val) && val > 0 ? val : 300;
            }
        };
        if (this.el.debounceInput) {
            this.el.debounceInput.addEventListener('input', onDebounceChange);
        }

        const onChangeClick = () => { this._showSelectionView(); this._loadModels(true); };
        changeBtn.addEventListener('click', onChangeClick);

        const onBackClick = () => { this._showInfoView(); };
        backBtn.addEventListener('click', onBackClick);

        this._onProviderChange = () => {
            if (this._populatingProvider) return;
            this._loadModels(true);
        };
        providerSelect.addEventListener('change', this._onProviderChange);

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
            this._loadModels(false);
        };
        refreshBtn.addEventListener('click', onRefreshClick);

        const onTestClick = async (e) => {
            e.preventDefault();
            if (this._testBtnTimeout) { clearTimeout(this._testBtnTimeout); this._testBtnTimeout = null; }
            const generation = ++this._testGeneration;
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
                if (!this.el || generation !== this._testGeneration) return;
                if (result?.success && result?.data) {
                    testBtn.innerHTML = `${Icons.SUCCESS} <span>Test</span>`;
                    testBtn.classList.add('success');
                } else {
                    testBtn.innerHTML = `${Icons.ERROR} <span>Test</span>`;
                    testBtn.classList.add('error');
                    toast.show(result?.error?.message || 'Connection test failed', 'error', 4000, testBtn);
                }
            } catch (err) {
                console.error('Test autocomplete error', err);
                if (!this.el || generation !== this._testGeneration) return;
                testBtn.innerHTML = `${Icons.ERROR} <span>Test</span>`;
                testBtn.classList.add('error');
                toast.show(err?.message || 'Connection test failed', 'error', 4000, testBtn);
            } finally {
                if (!this.el || generation !== this._testGeneration) return;
                this._testBtnTimeout = setTimeout(() => { this._resetTestButton(); this._testBtnTimeout = null; }, 3000);
            }
        };
        testBtn.addEventListener('click', onTestClick);

        this._onModelCardClick = (e) => {
            const card = e.target.closest('.model-card');
            if (!card) return;
            const modelId = card.dataset.modelId;
            this._config.modelId = modelId;
            this._config.providerType = this._selectedProvider.providerType;
            this._config.providerId = this._selectedProvider.id;
            this._showInfoView();
        };
        modelsContainer.addEventListener('click', this._onModelCardClick);

        dialog.showModal();

        return new Promise((resolve) => {
            const onCancel = () => { cleanup(); resolve(null); };
            const onSave = async () => {
                try {
                    await this.onSave.emit(this._config);
                    resolve(this._config);
                } catch (err) {
                    console.error('Failed to save autocompletions config:', err);
                    resolve(null);
                } finally {
                    cleanup();
                }
            };
            const cleanup = () => {
                enableCheckbox.removeEventListener('change', onEnableChange);
                changeBtn.removeEventListener('click', onChangeClick);
                backBtn.removeEventListener('click', onBackClick);
                providerSelect.removeEventListener('change', this._onProviderChange);
                testBtn.removeEventListener('click', onTestClick);
                if (this.el.debounceInput) {
                    this.el.debounceInput.removeEventListener('input', onDebounceChange);
                }
                cancelBtn.removeEventListener('click', onCancel);
                saveBtn.removeEventListener('click', onSave);
                dialog.removeEventListener('close', onClose);
                if (modelSearch) modelSearch.removeEventListener('input', onModelSearchInput);
                if (loadedOnly) loadedOnly.removeEventListener('change', onLoadedOnlyChange);
                if (sortBtn) sortBtn.removeEventListener('click', onSortClick);
                if (refreshBtn) refreshBtn.removeEventListener('click', onRefreshClick);
                if (modelsContainer && this._onModelCardClick) {
                    modelsContainer.removeEventListener('click', this._onModelCardClick);
                }
                if (this._searchDebounce) { clearTimeout(this._searchDebounce); this._searchDebounce = null; }
                if (this._testBtnTimeout) { clearTimeout(this._testBtnTimeout); this._testBtnTimeout = null; }
                this._testGeneration += 1;
                this._onProviderChange = null;
                this._onModelCardClick = null;
                this._guard.invalidate();
                this.onLoad.off();
                this.onSave.off();
                this.onTest.off();
                this.onListModels.off();
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
