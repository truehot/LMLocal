import { createCallback } from '@app/lib/callback.js';

export class SettingsDialog {
    constructor() {
        this.onLoad = createCallback();
        this.onSave = createCallback();
        this.onTestConnection = createCallback();
        this.el = {};
        this._toggleHandler = null;
        this._providerChangeHandler = null;
        this._testBtnClickHandler = null;
        this._testBtnTimeout = null;
    }

    _getElements() {

        const dialog = document.getElementById('settings-dialog');

        return {
            dialog,
            body: dialog.querySelector('.modal-body'),
            form: dialog.querySelector('form'),
            confirmBtn: dialog.querySelector('#dialog-confirm'),
            cancelBtn: dialog.querySelector('#dialog-cancel'),
            toggleBtn: dialog.querySelector('.password-toggle'),
            apiKeyInput: dialog.querySelector('[data-setting="ApiKey"]'),
            providerSelect: dialog.querySelector('[data-setting="Provider"]'),
            baseUrlInput: dialog.querySelector('[data-setting="LmStudioBaseUrl"]'),
            testBtn: dialog.querySelector('.test-connection-btn')
        };
    }

    _attachEvents() {
        const { toggleBtn, providerSelect, testBtn } = this.el;

        if (toggleBtn && this._toggleHandler) {
            toggleBtn.addEventListener('click', this._toggleHandler);
        }
        if (providerSelect && this._providerChangeHandler) {
            providerSelect.addEventListener('change', this._providerChangeHandler);
        }
        if (testBtn && this._testBtnClickHandler) {
            testBtn.addEventListener('click', this._testBtnClickHandler);
        }
    }

    _detachEvents() {
        const { toggleBtn, providerSelect, testBtn } = this.el;

        if (toggleBtn && this._toggleHandler) {
            toggleBtn.removeEventListener('click', this._toggleHandler);
        }
        if (providerSelect && this._providerChangeHandler) {
            providerSelect.removeEventListener('change', this._providerChangeHandler);
        }
        if (testBtn && this._testBtnClickHandler) {
            testBtn.removeEventListener('click', this._testBtnClickHandler);
        }
    }

    _cleanup() {
        this.onLoad.off();
        this.onSave.off();
        this.onTestConnection.off();
        this._detachEvents();
        if (this._testBtnTimeout) {
            clearTimeout(this._testBtnTimeout);
            this._testBtnTimeout = null;
        }
        this.el = {};
        this._toggleHandler = null;
        this._providerChangeHandler = null;
        this._testBtnClickHandler = null;
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
            iconSlot.innerHTML = `<svg class="btn-icon" width="14" height="14" viewBox="0 0 16 16" fill="currentColor" xmlns="http://www.w3.org/2000/svg">
            <path d="M6 11.5a.5.5 0 0 1 .5-.5h3a.5.5 0 0 1 0 1h-3a.5.5 0 0 1-.5-.5zm-2-3a.5.5 0 0 1 .5-.5h7a.5.5 0 0 1 0 1h-7a.5.5 0 0 1-.5-.5zm-2-3a.5.5 0 0 1 .5-.5h11a.5.5 0 0 1 0 1h-11a.5.5 0 0 1-.5-.5z" opacity="0.3" />
            <path d="M11.5 2a.5.5 0 0 1 .5-.5v4a.5.5 0 0 1-1 0V3H8.5v3.293l1.854 1.853a.5.5 0 0 1-.708.708L8.5 7.707V14H7.5V7.707L6.354 8.854a.5.5 0 1 1-.708-.708L7.5 6.293V3H4.5v3.5a.5.5 0 0 1-1 0v-4a.5.5 0 0 1 .5-.5h8z" />
        </svg>`;
        }
    }

    async show() {
        let resolved = false;
        this.el = this._getElements();

        const { dialog, body, form, confirmBtn, cancelBtn, toggleBtn, apiKeyInput, providerSelect, baseUrlInput, testBtn } = this.el;

        if (!dialog || !body || !confirmBtn || !cancelBtn || !toggleBtn || !apiKeyInput || !providerSelect || !baseUrlInput) {
            throw new Error('Missing required dialog elements');
        }

        this._resetTestButton();

        const populateProvidersSelect = async (defaultProviders = [], providers = []) => {
            try {
                providerSelect.innerHTML = '';

                if (defaultProviders && defaultProviders.length > 0) {
                    defaultProviders.forEach(provider => {
                        const option = document.createElement('option');
                        option.value = provider.id;
                        option.textContent = provider.name;
                        option._providerData = provider;
                        providerSelect.appendChild(option);
                    });
                }


                if (providers && providers.length > 0) {
                    providers.forEach(provider => {
                        const option = document.createElement('option');
                        option.value = provider.id;
                        option.textContent = provider.name;
                        option._providerData = provider;
                        providerSelect.appendChild(option);
                    });
                }

                const allProviders = [...(defaultProviders || []), ...(providers || [])];
                return allProviders;
            } catch (e) {
                console.error('Failed to load providers for select', e);
                return [];
            }
        };

        try {
            const result = await this.onLoad.emitResult();

            const settings = result.success ? result.data : {};
            const defaultProviders = result.success ? (result.data?.defaultProviders || []) : [];
            const providers = result.success ? (result.data?.providers || []) : [];
            const allProviders = await populateProvidersSelect(defaultProviders, providers);

            if (settings) {
                const elems = body.querySelectorAll('[data-setting]');
                elems.forEach(el => {
                    const key = el.getAttribute('data-setting');
                    if (!key) return;
                    let val = settings[key];

                    if (el.type === 'checkbox') {
                        el.checked = Boolean(val);
                    } else if (el.type === 'radio') {
                        if (val === undefined || val === null) return;
                        el.checked = String(val) === String(el.value);
                    } else if (el.id === 'provider-select') {
                        return;
                    } else {
                        el.value = val !== undefined && val !== null ? String(val) : '';
                    }
                });

                if (settings.Provider && settings.LmStudioBaseUrl && allProviders.length > 0) {
                    const savedProviderType = settings.Provider;
                    const savedBaseUrl = settings.LmStudioBaseUrl;
                    const savedApiKey = settings.ApiKey;
                    let matchedProviderIdx = null;
                    for (let i = 0; i < allProviders.length; i++) {
                        const opt = allProviders[i];

                        const isMatched = opt && opt.providerType === savedProviderType && opt.customBaseUrl === savedBaseUrl && opt.customApiKey === savedApiKey;
                        if (isMatched) {
                            matchedProviderIdx = i;
                            break;
                        }
                    }

                    if (matchedProviderIdx !== null) {
                        providerSelect.selectedIndex = matchedProviderIdx;
                        const changeEvent = new Event('change', { bubbles: true });
                        providerSelect.dispatchEvent(changeEvent);
                    }
                }
            }
        }
        catch (e) {
            console.error('Failed to populate settings dialog', e);
        }

        this._toggleHandler = () => {
            const isPassword = apiKeyInput.type === 'password';
            apiKeyInput.type = isPassword ? 'text' : 'password';
            toggleBtn.style.color = isPassword ? 'var(--accent-color)' : 'var(--muted-color)';
        };

        this._providerChangeHandler = (e) => {
            const selectedOption = e.target.options[e.target.selectedIndex];

            if (selectedOption._providerData) {
                const provider = selectedOption._providerData;
                baseUrlInput.value = provider.customBaseUrl || '';
                apiKeyInput.value = provider.customApiKey || '';
            }
        };

        this._testBtnClickHandler = async (e) => {
            e.preventDefault();

            if (this._testBtnTimeout) {
                clearTimeout(this._testBtnTimeout);
                this._testBtnTimeout = null;
            }

            const provider = providerSelect.value;
            const url = baseUrlInput.value;

            if (!provider) { providerSelect.focus(); return; }
            if (!url) { baseUrlInput.focus(); return; }

            const iconSlot = testBtn.querySelector('.btn-icon-slot');
            if (!iconSlot) return;

            const successIcon = `<svg class="btn-icon" width="14" height="14" viewBox="0 0 16 16" fill="currentColor"><path d="M13.854 3.646a.5.5 0 0 1 0 .708l-7 7a.5.5 0 0 1-.708 0l-3.5-3.5a.5.5 0 1 1 .708-.708L6.5 10.293l6.646-6.647a.5.5 0 0 1 .708 0z"/></svg>`;
            const errorIcon = `<svg class="btn-icon" width="14" height="14" viewBox="0 0 16 16" fill="currentColor"><path d="M4.646 4.646a.5.5 0 0 1 .708 0L8 7.293l2.646-2.647a.5.5 0 0 1 .708.708L8.707 8l2.647 2.646a.5.5 0 0 1-.708.708L8 8.707l-2.646 2.647a.5.5 0 0 1-.708-.708L7.293 8 4.646 5.354a.5.5 0 0 1 0-.708z"/></svg>`;

            testBtn.disabled = true;
            testBtn.classList.remove('success', 'error');
            iconSlot.innerHTML = '<span class="btn-spinner"></span>';

            try {
                const selectedOption = providerSelect.options[providerSelect.selectedIndex];
                let providerType = 'openai';
                if (selectedOption._providerData) {
                    const type = selectedOption._providerData.providerType;
                    if (type) providerType = type;
                }

                const result = await this.onTestConnection.emitResult({
                    provider: providerType,
                    url: url,
                    apiKey: apiKeyInput.value
                });

                if (result && result.success) {
                    iconSlot.innerHTML = successIcon;
                    testBtn.classList.add('success');
                } else {
                    iconSlot.innerHTML = errorIcon;
                    testBtn.classList.add('error');
                }
            } catch (err) {
                console.error('Test connection error', err);
                iconSlot.innerHTML = errorIcon;
                testBtn.classList.add('error');
            } finally {
                this._testBtnTimeout = setTimeout(() => {
                    this._resetTestButton();
                    this._testBtnTimeout = null;
                }, 3000);
            }
        };

        this._attachEvents();

        return new Promise((resolve) => {
            dialog.showModal();

            const onConfirm = async () => {
                if (resolved) return;
                try {
                    if (form && !form.checkValidity()) {
                        if (typeof form.reportValidity === 'function') form.reportValidity();
                        const firstInvalid = form.querySelector(':invalid');
                        if (firstInvalid) firstInvalid.focus();
                        return;
                    }

                    const elems = body.querySelectorAll('[data-setting]');
                    const newSettings = {};
                    elems.forEach(el => {
                        const key = el.getAttribute('data-setting');
                        if (!key) return;
                        if (el.type === 'checkbox') {
                            newSettings[key] = !!el.checked;
                        } else if (el.type === 'radio') {
                            if (!el.checked) return;
                            const asNum = parseInt(el.value, 10);
                            newSettings[key] = Number.isNaN(asNum) ? el.value : asNum;
                        } else {
                            // For select and text inputs, keep string value
                            newSettings[key] = el.value;
                        }
                    });


                    if (newSettings.Provider) {
                        const selectedOption = providerSelect.options[providerSelect.selectedIndex];
                        if (selectedOption._providerData) {
                            newSettings.Provider = selectedOption._providerData.providerType || 'openai';
                        }
                    }

                    const result = await this.onSave.emitResult(newSettings);
                    resolved = true;
                    dialog.close();
                    resolve(result.success);
                }
                catch (err) {
                    console.error('Failed to save settings', err);
                    resolved = true;
                    dialog.close();
                    resolve(false);
                }
            };

            const onCancel = () => {
                if (resolved) return;
                resolved = true;
                dialog.close();
                resolve(false);
            };

            const onClose = () => {
                if (!resolved) {
                    resolve(false);
                }
                this._cleanup();
            };

            confirmBtn.onclick = onConfirm;
            cancelBtn.onclick = onCancel;
            dialog.onclose = onClose;
        });
    }
}
