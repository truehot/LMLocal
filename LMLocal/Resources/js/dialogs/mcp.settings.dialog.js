import { createCallback } from '@app/lib/callback.js';

export class McpSettingsDialog {
    constructor() {
        this.onLoad = createCallback();
        this.onSave = createCallback();
        this.onTestConnection = createCallback();
        this.el = {};

        this._checkboxHandler = null;
        this._textareaInputHandler = null;
        this._testBtnClickHandler = null;
    }

    _getElements() {
        const dialog = document.getElementById('mcp-settings-dialog');
        return {
            dialog,
            body: dialog.querySelector('.modal-body'),
            form: dialog.querySelector('#mcp-settings-form'),
            confirmBtn: dialog.querySelector('#mcp-dialog-confirm'),
            cancelBtn: dialog.querySelector('#mcp-dialog-cancel'),
            enableCheckbox: dialog.querySelector('#mcp-dialog-enable-checkbox'),
            configContainer: dialog.querySelector('#mcp-dialog-config-container'),
            textarea: dialog.querySelector('#mcp-dialog-textarea'),
            testBtn: dialog.querySelector('#mcp-dialog-test-btn'),
            statusContainer: dialog.querySelector('#mcp-dialog-status-container'),
            statusBadge: dialog.querySelector('#mcp-dialog-status-badge'),
            capabilitiesContainer: dialog.querySelector('#mcp-dialog-discovered-capabilities'),
            statusMessage: dialog.querySelector('#mcp-dialog-status-message'),
            jsonErrorContainer: dialog.querySelector('#mcp-json-error'),
            jsonErrorStatusContainer: dialog.querySelector('#mcp-json-error-text')
        };
    }

    _attachEvents() {
        const { enableCheckbox, textarea, testBtn } = this.el;

        if (enableCheckbox && this._checkboxHandler) {
            enableCheckbox.addEventListener('change', this._checkboxHandler);
        }
        if (textarea && this._textareaInputHandler) {
            textarea.addEventListener('input', this._textareaInputHandler);
        }
        if (testBtn && this._testBtnClickHandler) {
            testBtn.addEventListener('click', this._testBtnClickHandler);
        }
    }

    _detachEvents() {
        const { enableCheckbox, textarea, testBtn } = this.el;

        if (enableCheckbox && this._checkboxHandler) {
            enableCheckbox.removeEventListener('change', this._checkboxHandler);
        }
        if (textarea && this._textareaInputHandler) {
            textarea.removeEventListener('input', this._textareaInputHandler);
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
        this.el = {};
    }

    _updateStatus(type, badgeText, logText = '', serversData = null, configData = null) {
        const { statusContainer, statusBadge, statusMessage, capabilitiesContainer, confirmBtn, testBtn } = this.el;

        if (type === 'reset') {
            statusContainer.style.display = 'none';
            confirmBtn.disabled = false;
            return;
        }

        statusContainer.style.display = 'block';
        statusBadge.className = `mcp-badge ${type}`;
        statusBadge.textContent = badgeText;

        capabilitiesContainer.innerHTML = '';

        if (type === 'error') {
            confirmBtn.disabled = true;
            statusMessage.textContent = logText;
            statusMessage.style.display = logText ? 'block' : 'none';
        } else if (type === 'success') {
            confirmBtn.disabled = false;
            statusMessage.textContent = logText;
            statusMessage.style.display = logText ? 'block' : 'none';

            if (serversData && typeof serversData === 'object') {
                Object.entries(serversData).forEach(([serverName, info]) => {
                    const block = document.createElement('div');
                    block.className = 'server-info-block';

                    const title = document.createElement('div');
                    title.className = 'server-name';
                    title.textContent = `● Server [${serverName}]:`;

                    const serversConfig = configData?.mcpServers || configData?.servers;
                    if (serversConfig?.[serverName]?.disabled) {
                        title.textContent += ' (disabled)';
                    }

                    block.appendChild(title);

                    const serverPermissions = serversConfig?.[serverName]?.permissions || {};
                    const disabledTools = new Set();
                    Object.entries(serverPermissions).forEach(([toolName, permission]) => {
                        if (permission === 'disable') {
                            disabledTools.add(toolName);
                        }
                    });

                    if (info.tools && Array.isArray(info.tools)) {
                        info.tools.forEach(tool => {
                            const tag = document.createElement('span');
                            tag.className = 'capability-tag';

                            if (disabledTools.has(tool.name || tool)) {
                                tag.classList.add('disabled');
                                tag.textContent = `🛠️ `;
                                const del = document.createElement('del');
                                del.textContent = tool.name || tool;
                                tag.appendChild(del);
                            } else {
                                tag.textContent = `🛠️ ${tool.name || tool}`;
                            }

                            tag.title = tool.description || '';
                            block.appendChild(tag);
                        });
                    }

                    if (info.resources && Array.isArray(info.resources)) {
                        info.resources.forEach(res => {
                            const tag = document.createElement('span');
                            tag.className = 'capability-tag';
                            tag.textContent = `📁 ${res.name || res}`;
                            block.appendChild(tag);
                        });
                    }

                    capabilitiesContainer.appendChild(block);
                });
            }
        }
    }

    async show() {
        this.el = this._getElements();
        const { dialog, body, form, confirmBtn, cancelBtn, enableCheckbox, configContainer, textarea, testBtn } = this.el;

        if (!dialog || !body || !confirmBtn || !cancelBtn || !enableCheckbox || !configContainer || !textarea || !testBtn) {
            throw new Error('Missing required MCP dialog elements');
        }

        confirmBtn.disabled = false;
        cancelBtn.disabled = false;
        testBtn.disabled = false;


        try {

            const result = await this.onLoad.emitResult();
            const settings = result.success ? result.data : null;

            if (settings) {

                const elems = body.querySelectorAll('[data-setting]');
                elems.forEach(el => {
                    const key = el.getAttribute('data-setting');
                    if (!key) return;
                    let val = settings[key];

                    if (el.type === 'checkbox') {
                        el.checked = Boolean(val);
                    } else {
                        el.value = val !== undefined && val !== null ? String(val) : '';
                    }
                });

                configContainer.style.display = enableCheckbox.checked ? 'block' : 'none';
            }
        } catch (e) {
            console.error('Failed to populate MCP settings dialog', e);
        }


        this._checkboxHandler = (e) => {
            configContainer.style.display = e.target.checked ? 'block' : 'none';
        };

        this._textareaInputHandler = () => {
            const { jsonErrorContainer, jsonErrorStatusContainer, testBtn, confirmBtn, textarea } = this.el;
            const value = textarea.value.trim();
            if (!value) {
                this._updateStatus('reset');
                jsonErrorContainer.style.display = 'none';
                jsonErrorStatusContainer.textContent = '';
                jsonErrorStatusContainer.style.display = 'none';
                testBtn.style.display = 'block';
                testBtn.disabled = false;
                confirmBtn.disabled = false;
                return;
            }
            try {
                JSON.parse(value);
                this._updateStatus('reset');
                jsonErrorContainer.style.display = 'none';
                jsonErrorStatusContainer.textContent = '';
                jsonErrorStatusContainer.style.display = 'none';
                testBtn.style.display = 'block';
                testBtn.disabled = false;
                confirmBtn.disabled = false;
            } catch (err) {
                jsonErrorContainer.style.display = 'block';
                jsonErrorStatusContainer.textContent = `❌ Invalid JSON Syntax: ${err.message}`;
                jsonErrorStatusContainer.style.display = 'block';
                testBtn.style.display = 'none';
                confirmBtn.disabled = true;
            }
        };

        this._testBtnClickHandler = async (e) => {
            e.preventDefault();
            const { jsonErrorContainer, jsonErrorStatusContainer, testBtn, confirmBtn } = this.el;
            const configText = textarea.value.trim();

            if (!configText) { textarea.focus(); return; }

            let serversConfig = null;
            try {
                serversConfig = JSON.parse(configText);
                jsonErrorContainer.style.display = 'none';
                jsonErrorStatusContainer.textContent = '';
                jsonErrorStatusContainer.style.display = 'none';
                testBtn.style.display = 'block';
            } catch (err) {
                this._textareaInputHandler();
                return;
            }

            const iconSlot = testBtn.querySelector('.btn-icon-slot');
            const originalIconHtml = iconSlot.innerHTML;

            testBtn.disabled = true;
            confirmBtn.disabled = true;
            iconSlot.innerHTML = '<span class="btn-spinner"></span> Discovering...';

            try {
                const payload = {
                    EnableMcp: enableCheckbox.checked,
                    McpServersJson: JSON.stringify(serversConfig) // should be json
                };

                const emitResult = await this.onTestConnection.emitResult(payload);
                const result = emitResult.success ? emitResult.data : null;


                if (result && result.error) {
                    this._updateStatus('error', '❌ Connection Failed', result.error, null, serversConfig);
                    return;
                }

                if (!result || !Array.isArray(result.servers)) {
                    this._updateStatus('error', '❌ Invalid Response', 'Server returned invalid response structure', null, serversConfig);
                    return;
                }

                if (result.servers.length === 0) {
                    this._updateStatus('error', '❌ No servers tested', 'No servers were configured for discovery', null, serversConfig);
                    return;
                }

                const { hasErrors, hasSuccesses } = result;

                if (hasErrors && !hasSuccesses) {
                    // All servers failed
                    const errorMsg = result.servers
                        .filter(item => item.error)
                        .map(item => `${item.serverName}: ${item.error}`)
                        .join('\n');
                    this._updateStatus('error', '❌ All servers failed', errorMsg, null, serversConfig);
                } else if (hasErrors && hasSuccesses) {
                    // Mixed results - some success, some errors
                    const errorMsg = result.servers
                        .filter(item => item.error)
                        .map(item => `${item.serverName}: ${item.error}`)
                        .join('\n');
                    const successData = {};
                    result.servers.filter(item => item.tools).forEach(item => {
                        successData[item.serverName] = { tools: item.tools };
                    });
                    this._updateStatus(
                        'warning',
                        '⚠️ Partially connected (some servers failed)',
                        errorMsg,
                        successData,
                        serversConfig
                    );
                } else {
                    // All servers succeeded
                    const successData = {};
                    result.servers.forEach(item => {
                        successData[item.serverName] = { tools: item.tools };
                    });
                    this._updateStatus(
                        'success',
                        '✓ Connected successfully! Servers loaded:',
                        '',
                        successData,
                        serversConfig
                    );
                }
            } catch (err) {
                console.error('MCP test execution error', err);
                this._updateStatus('error', '❌ Runtime Error', err.toString(), null, serversConfig);
            } finally {
                testBtn.disabled = false;
                iconSlot.innerHTML = originalIconHtml;
            }
        };

        this._attachEvents();

        return new Promise((resolve) => {
            dialog.showModal();

            const onConfirm = async () => {
                try {
                    if (form && !form.checkValidity()) {
                        if (typeof form.reportValidity === 'function') form.reportValidity();
                        return;
                    }

                    const originalConfirmHtml = confirmBtn.innerHTML;

                    confirmBtn.disabled = true;
                    cancelBtn.disabled = true;
                    if (testBtn) testBtn.disabled = true;

                    confirmBtn.textContent = 'Saving...';

                    const elems = body.querySelectorAll('[data-setting]');
                    const newSettings = {};
                    elems.forEach(el => {
                        const key = el.getAttribute('data-setting');
                        if (!key) return;
                        newSettings[key] = el.type === 'checkbox' ? !!el.checked : el.value;
                    });

                    const result = await this.onSave.emitResult(newSettings);
                    confirmBtn.textContent = 'Save';
                    if (result.success) {
                        confirmBtn.disabled = false;
                        cancelBtn.disabled = false;
                        if (testBtn) testBtn.disabled = false;
                        this._cleanup();
                        dialog.close();
                        resolve(true);
                    } else {
                        confirmBtn.disabled = false;
                        cancelBtn.disabled = false;
                        if (testBtn) testBtn.disabled = false;
                        confirmBtn.innerHTML = originalConfirmHtml;

                        if (result.error) {
                            this._updateStatus('error', '❌ Save Failed', result.error);
                        }
                    }
                } catch (err) {
                    console.error('Failed to save MCP settings', err);
                    this._cleanup();
                    dialog.close();
                    resolve(false);
                }
            };

            const onCancel = () => {
                this._cleanup();
                dialog.close();
                resolve(false);
            };

            confirmBtn.onclick = onConfirm;
            cancelBtn.onclick = onCancel;
            dialog.onclose = onCancel;
        });
    }
}