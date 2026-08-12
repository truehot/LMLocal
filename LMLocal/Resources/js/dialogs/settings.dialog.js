import { Icons } from '@app/constants/app.globals.js';
import { createCallback } from '@app/lib/callback.js';
import { populateProviderSelect } from '@app/lib/populate-provider.select.js';
import toast from '@app/lib/toast.js';

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
        this._testGeneration = 0;
    }

    _getElements() {

        const dialog = document.getElementById('settings-dialog');

        return {
            dialog,
            body: dialog.querySelector('.modal-body'),
            form: dialog.querySelector('form'),
            confirmBtn: dialog.querySelector('#settings-dialog-confirm'),
            cancelBtn: dialog.querySelector('#settings-dialog-cancel'),
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
        this._testGeneration += 1;
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
            iconSlot.innerHTML = Icons.LINK;
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

        try {
            const result = await this.onLoad.emitResult();

            const settings = result.success ? result.data : {};

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

                populateProviderSelect(providerSelect, result.data || {}, settings);

                providerSelect.dispatchEvent(new Event('change', { bubbles: true }));
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

            const generation = ++this._testGeneration;

            const provider = providerSelect.value;
            const url = baseUrlInput.value;

            if (!provider) { providerSelect.focus(); return; }
            if (!url) { baseUrlInput.focus(); return; }

            const iconSlot = testBtn.querySelector('.btn-icon-slot');
            if (!iconSlot) return;

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
                            newSettings.ProviderId = selectedOption._providerData.id ?? null;
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
