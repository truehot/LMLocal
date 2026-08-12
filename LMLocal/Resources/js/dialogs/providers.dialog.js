import { Icons } from '@app/constants/app.globals.js';
import { createCallback } from '@app/lib/callback.js';
import toast from '@app/lib/toast.js';

export class ProvidersDialog {
    constructor() {
        this.onLoad = createCallback();
        this.onSave = createCallback();
        this.onTestConnection = createCallback();
        this._providers = [];
        this._providerTypes = [];
        this._toggleHandler = null;
        this._addBtnClickHandler = null;
        this._testBtnClickHandler = null;
        this._testBtnTimeout = null;
        this._testGeneration = 0;
        this.el = null;
        this._confirmHandler = null;
        this._cancelHandler = null;
        this._formSubmitHandler = null;
        this._formCancelHandler = null;
        this._dialogCloseHandler = null;
        this._filterText = '';
        this._sortAsc = null;
        this._filterInputHandler = null;
        this._sortClickHandler = null;
    }

    _getDialog() {
        return document.getElementById('providers-dialog');
    }

    _getElements() {
        const dialog = this._getDialog();
        if (!dialog) return {};

        return {
            dialog,
            body: dialog.querySelector('.modal-body'),
            form: dialog.querySelector('#provider-crud-form'),
            confirmBtn: dialog.querySelector('#providers-modal-confirm'),
            cancelBtn: dialog.querySelector('#providers-modal-cancel'),
            listView: dialog.querySelector('#providers-list-view'),
            formView: dialog.querySelector('#provider-form-view'),
            listContainer: dialog.querySelector('#providers-list-container'),
            addBtn: dialog.querySelector('#provider-add-btn'),
            idInput: dialog.querySelector('[data-setting="id"]'),
            nameInput: dialog.querySelector('[data-setting="name"]'),
            typeSelect: dialog.querySelector('[data-setting="providerType"]'),
            urlInput: dialog.querySelector('[data-setting="customBaseUrl"]'),
            keyInput: dialog.querySelector('[data-setting="customApiKey"]'),
            formCancelBtn: dialog.querySelector('#provider-form-cancel'),
            testBtn: dialog.querySelector('.test-connection-btn'),
            passwordToggle: dialog.querySelector('.password-toggle'),
            listActions: dialog.querySelector('#providers-list-actions'),
            formActions: dialog.querySelector('#providers-form-actions'),
            filterInput: dialog.querySelector('#provider-filter-input'),
            sortBtn: dialog.querySelector('#providers-sort-btn')
        };
    }

    _attachEvents() {
        const { addBtn, testBtn, passwordToggle, filterInput, sortBtn } = this.el;

        if (addBtn && this._addBtnClickHandler) {
            addBtn.addEventListener('click', this._addBtnClickHandler);
        }
        if (testBtn && this._testBtnClickHandler) {
            testBtn.addEventListener('click', this._testBtnClickHandler);
        }
        if (passwordToggle && this._toggleHandler) {
            passwordToggle.addEventListener('click', this._toggleHandler);
        }
        if (filterInput && this._filterInputHandler) {
            filterInput.addEventListener('input', this._filterInputHandler);
        }
        if (sortBtn && this._sortClickHandler) {
            sortBtn.addEventListener('click', this._sortClickHandler);
        }
    }

    _detachEvents() {
        if (!this.el) return;
        const { addBtn, testBtn, passwordToggle, filterInput, sortBtn } = this.el;

        if (addBtn && this._addBtnClickHandler) {
            addBtn.removeEventListener('click', this._addBtnClickHandler);
        }
        if (testBtn && this._testBtnClickHandler) {
            testBtn.removeEventListener('click', this._testBtnClickHandler);
        }
        if (passwordToggle && this._toggleHandler) {
            passwordToggle.removeEventListener('click', this._toggleHandler);
        }
        if (filterInput && this._filterInputHandler) {
            filterInput.removeEventListener('input', this._filterInputHandler);
        }
        if (sortBtn && this._sortClickHandler) {
            sortBtn.removeEventListener('click', this._sortClickHandler);
        }
    }

    _cleanup() {
        this.onLoad.off();
        this.onSave.off();
        this.onTestConnection.off();
        this._detachEvents();

        const { dialog, confirmBtn, cancelBtn, form, formCancelBtn } = this.el || {};
        if (confirmBtn && this._confirmHandler) {
            confirmBtn.onclick = null;
            this._confirmHandler = null;
        }
        if (cancelBtn && this._cancelHandler) {
            cancelBtn.onclick = null;
            this._cancelHandler = null;
        }
        if (form && this._formSubmitHandler) {
            form.onsubmit = null;
            this._formSubmitHandler = null;
        }
        if (formCancelBtn && this._formCancelHandler) {
            formCancelBtn.onclick = null;
            this._formCancelHandler = null;
        }
        if (dialog && this._dialogCloseHandler) {
            dialog.onclose = null;
            this._dialogCloseHandler = null;
        }

        if (this._testBtnTimeout) {
            clearTimeout(this._testBtnTimeout);
            this._testBtnTimeout = null;
        }
        this._testGeneration += 1;
        this._providers = [];
        this._providerTypes = [];
        this._toggleHandler = null;
        this._addBtnClickHandler = null;
        this._testBtnClickHandler = null;
        this._filterInputHandler = null;
        this._sortClickHandler = null;
        this.el = null;
        this._filterText = '';
        this._sortAsc = null;
    }

    _getNextId() {
        const maxId = this._providers.reduce((max, p) => Math.max(max, p.id || p.Id || 0), 0);
        return maxId + 1;
    }

    _getDisplayName(typeKey) {
        const found = this._providerTypes.find(pt => pt.key === typeKey);
        return found ? found.displayName : typeKey;
    }

    _matchesFilter(provider) {
        if (this._filterText.trim() === '') return true;
        const search = this._filterText.trim().toLowerCase();
        const name = (provider.name || '').toLowerCase();
        const type = (provider.providerType || '').toLowerCase();
        const url = (provider.customBaseUrl || '').toLowerCase();
        return name.includes(search) || type.includes(search) || url.includes(search);
    }

    _renderList() {
        const el = this._getElements();
        const { listContainer } = el;
        if (!listContainer) return;

        let filteredProviders = [...this._providers];
        if (this._filterText.trim() !== '') {
            filteredProviders = filteredProviders.filter(provider => this._matchesFilter(provider));
        }

        if (this._sortAsc !== null) {
            filteredProviders.sort((a, b) => {
                const nameA = (a.name || '').toLowerCase();
                const nameB = (b.name || '').toLowerCase();
                const cmp = nameA.localeCompare(nameB);
                return this._sortAsc ? cmp : -cmp;
            });
        }

        listContainer.innerHTML = '';

        if (filteredProviders.length === 0) {
            const emptyMsg = document.createElement('div');
            emptyMsg.className = 'loading-placeholder';
            emptyMsg.id = 'providers-empty-state';
            const filterActive = this._filterText.trim() !== '';
            emptyMsg.innerHTML = filterActive
                ? '<span>No providers match the current filter.</span>'
                : '<span>No custom providers added yet. Click "+ Add Profile" to create one.</span>';
            listContainer.appendChild(emptyMsg);
            return;
        }

        filteredProviders.forEach((provider) => {
            const card = document.createElement('div');
            card.className = 'provider-card';

            const infoStack = document.createElement('div');
            infoStack.className = 'provider-info-stack';

            const pName = provider.name || 'Unnamed';
            const pType = provider.providerType || 'openai';
            const pDisplayType = this._getDisplayName(pType);
            const pUrl = provider.customBaseUrl || '';

            const nameEl = document.createElement('div');
            nameEl.className = 'provider-card-name';
            nameEl.textContent = pName;

            const typeEl = document.createElement('div');
            typeEl.className = 'provider-card-type';
            typeEl.textContent = pDisplayType;

            const metaEl = document.createElement('div');
            metaEl.className = 'provider-card-meta';
            metaEl.textContent = pUrl;

            infoStack.appendChild(nameEl);
            infoStack.appendChild(typeEl);
            infoStack.appendChild(metaEl);

            const actions = document.createElement('div');
            actions.className = 'provider-card-actions';

            const editBtn = document.createElement('button');
            editBtn.type = 'button';
            editBtn.className = 'btn-secondary provider-card-btn';
            editBtn.textContent = 'Edit';
            editBtn.onclick = (e) => { e.preventDefault(); this._showForm(provider); };

            const deleteBtn = document.createElement('button');
            deleteBtn.type = 'button';
            deleteBtn.className = 'btn-secondary provider-card-btn btn-danger-text';
            deleteBtn.textContent = 'Remove';
            deleteBtn.onclick = (e) => { e.preventDefault(); this._handleDeleteProvider(provider); };

            actions.appendChild(editBtn);
            actions.appendChild(deleteBtn);

            card.appendChild(infoStack);
            card.appendChild(actions);
            listContainer.appendChild(card);
        });
    }

    _fillTypeSelect(typeSelect) {
        typeSelect.innerHTML = '';
        this._providerTypes.forEach(pt => {
            const option = document.createElement('option');
            option.value = pt.key;
            option.textContent = pt.displayName;
            typeSelect.appendChild(option);
        });
    }

    _showForm(provider = null) {
        const el = this._getElements();
        const { listView, listActions, formView, formActions, idInput, nameInput, typeSelect, urlInput, keyInput } = el;

        if (!listView || !formView || !idInput) {
            console.error('Required elements not found for form view');
            return;
        }

        listView.classList.add('hidden');
        if (listActions) listActions.classList.add('hidden');

        formView.classList.remove('hidden');
        if (formActions) formActions.classList.remove('hidden');

        this._fillTypeSelect(typeSelect);

        if (provider) {
            idInput.value = provider.id || '';
            nameInput.value = provider.name || '';
            typeSelect.value = provider.providerType || 'openai';
            urlInput.value = provider.customBaseUrl || '';
            keyInput.value = provider.customApiKey || '';
        } else {
            idInput.value = this._getNextId();
            nameInput.value = '';
            typeSelect.value = 'openai';
            urlInput.value = '';
            keyInput.value = '';
        }

        nameInput.focus();
    }

    _showList(resetView = false) {
        const el = this._getElements();
        const { listView, listActions, formView, formActions, filterInput } = el;

        if (!listView || !formView) return;

        if (resetView) {
            this._filterText = '';
            this._sortAsc = null;
            if (filterInput) filterInput.value = '';
        }

        listView.classList.remove('hidden');
        if (listActions) listActions.classList.remove('hidden');

        formView.classList.add('hidden');
        if (formActions) formActions.classList.add('hidden');

        this._renderList();
    }

    _handleDeleteProvider(provider) {
        if (!provider) return;
        this._providers = this._providers.filter(p => (p.id || p.Id) !== (provider.id || provider.Id));
        this._renderList();
    }


    _showSaveError(message) {
        toast.show(message, 'error', 4000, this.el?.confirmBtn);
    }

    _resetTestButton() {
        const testBtn = this.el?.testBtn;
        if (!testBtn) return;

        if (this._testBtnTimeout) {
            clearTimeout(this._testBtnTimeout);
            this._testBtnTimeout = null;
        }

        testBtn.disabled = false;
        testBtn.classList.remove('success', 'error');

        const iconSlot = testBtn.querySelector('.btn-icon-slot');
        if (iconSlot) {
            iconSlot.innerHTML = Icons.LINK;
        }
    }

    async show() {
        this.el = this._getElements();
        this._detachEvents();
        this._resetTestButton();

        const { dialog, form, confirmBtn, cancelBtn, formCancelBtn, filterInput, sortBtn } = this.el;

        if (!dialog || !form || !confirmBtn || !cancelBtn || !formCancelBtn) {
            throw new Error('Missing required dialog elements');
        }

        try {
            const result = await this.onLoad.emitResult();
            if (result && result.success && result.data) {
                const config = result.data;
                this._providers = config.providers || [];
                this._providerTypes = config.providerTypes || [];
            } else if (result && result.providers) {
                this._providers = result.providers;
                this._providerTypes = result.providerTypes || [];
            } else {
                this._providers = [];
                this._providerTypes = [];
            }
        } catch (e) {
            console.error('Failed to load providers', e);
            this._providers = [];
            this._providerTypes = [];
        }

        this._toggleHandler = () => {
            const { keyInput, passwordToggle } = this.el;
            if (!keyInput) return;
            const isPassword = keyInput.type === 'password';
            keyInput.type = isPassword ? 'text' : 'password';
            if (passwordToggle) {
                passwordToggle.style.color = isPassword ? 'var(--accent-color)' : 'var(--muted-color)';
            }
        };

        this._addBtnClickHandler = () => { this._showForm(); };

        this._testBtnClickHandler = async (e) => {
            e.preventDefault();

            if (this._testBtnTimeout) {
                clearTimeout(this._testBtnTimeout);
                this._testBtnTimeout = null;
            }

            const generation = ++this._testGeneration;

            const { nameInput, urlInput, keyInput, testBtn, typeSelect } = this.el;
            if (!nameInput || !urlInput || !keyInput || !testBtn) return;

            const name = nameInput.value.trim();
            const url = urlInput.value.trim();
            const key = keyInput.value;

            if (!name) { nameInput.focus(); return; }
            if (!url) { urlInput.focus(); return; }

            const iconSlot = testBtn.querySelector('.btn-icon-slot');
            if (!iconSlot) return;

            testBtn.disabled = true;
            testBtn.classList.remove('success', 'error');
            iconSlot.innerHTML = '<span class="btn-spinner"></span>';

            try {
                const result = await this.onTestConnection.emitResult({
                    provider: typeSelect.value,
                    url: url,
                    apiKey: key
                });

                if (!this.el || generation !== this._testGeneration) return;

                if (result && result.success) {
                    iconSlot.innerHTML = Icons.SUCCESS;
                    testBtn.classList.add('success');
                } else {
                    iconSlot.innerHTML = Icons.ERROR;
                    testBtn.classList.add('error');

                    toast.show(result?.error?.message || 'Connection test failed', 'error', 4000, testBtn);
                }
            } catch (err) {
                console.error('Test connection error', err);
                if (!this.el || generation !== this._testGeneration) return;
                iconSlot.innerHTML = Icons.ERROR;
                testBtn.classList.add('error');
                toast.show(err?.message || 'Connection test failed', 'error', 4000, testBtn);
            } finally {
                if (!this.el || generation !== this._testGeneration) return;
                this._testBtnTimeout = setTimeout(() => {
                    this._resetTestButton();
                    this._testBtnTimeout = null;
                }, 3000);
            }
        };

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
                    const config = { providers: this._providers };
                    const result = await this.onSave.emitResult(config);
                    if (!this.el) return;
                    if (!(result && result.success)) {
                        console.error('Failed to save providers', result?.error);
                        this._showSaveError(result?.error?.message || 'Failed to save providers');
                        return;
                    }
                    this._cleanup();
                    dialog.close();
                    resolve(true);
                } catch (err) {
                    console.error('Failed to save providers', err);
                    if (!this.el) return;
                    this._showSaveError(err?.message || 'Failed to save providers');
                }
            };

            this._formSubmitHandler = (e) => {
                if (e) e.preventDefault();

                const form = document.getElementById('provider-crud-form');
                if (!form) return;

                try {
                    if (!form.checkValidity()) {
                        if (typeof form.reportValidity === 'function') form.reportValidity();
                        const firstInvalid = form.querySelector(':invalid');
                        if (firstInvalid) firstInvalid.focus();
                        return;
                    }

                    const idInput = form.querySelector('[name="id"]');
                    const nameInput = form.querySelector('[name="name"]');
                    const typeSelect = form.querySelector('[name="providerType"]');
                    const urlInput = form.querySelector('[name="customBaseUrl"]');
                    const keyInput = form.querySelector('[name="customApiKey"]');

                    const idValue = parseInt(idInput.value, 10) || this._getNextId();

                    const newProvider = {
                        id: idValue,
                        name: nameInput.value.trim(),
                        providerType: typeSelect.value,
                        customBaseUrl: urlInput.value.trim(),
                        customApiKey: keyInput.value ? keyInput.value.trim() : ''
                    };

                    const existingIndex = this._providers.findIndex(p => (p.id === idValue || p.Id === idValue));
                    if (existingIndex >= 0) {
                        this._providers[existingIndex] = newProvider;
                    } else {
                        this._providers.push(newProvider);
                    }

                    if (!this._matchesFilter(newProvider)) {
                        this._filterText = '';
                        this._sortAsc = null;
                        const flt = this.el?.filterInput;
                        if (flt) flt.value = '';
                    }

                    this._showList();
                } catch (err) {
                    console.error('Failed to save provider', err);
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
