import { createCallback } from '@app/lib/callback.js';
import toast from '@app/lib/toast.js';

export class ToolsDialog {
    constructor() {
        this.onLoad = createCallback();
        this.onSave = createCallback();
        this._tools = [];
        this.el = null;

        this._filterText = '';
        this._dialogResolve = null;
        this._sortState = null;
    }

    _getDialog() {
        return document.getElementById('tools-dialog');
    }

    _getElements() {
        const dialog = this._getDialog();
        if (!dialog) return {};

        return {
            dialog,
            body: dialog.querySelector('.modal-body'),
            confirmBtn: dialog.querySelector('#tools-modal-save'),
            cancelBtn: dialog.querySelector('#tools-modal-close'),
            listContainer: dialog.querySelector('.tools-list-grid'),
            filterInput: dialog.querySelector('#tool-filter-input'),
            enableAllBtn: dialog.querySelector('#tools-enable-all-btn'),
            disableAllBtn: dialog.querySelector('#tools-disable-all-btn'),
            sortBtn: dialog.querySelector('#tools-sort-btn')
        };
    }

    _onSortClick = () => {
        if (this._sortState === null) this._sortState = 'asc';
        else if (this._sortState === 'asc') this._sortState = 'desc';
        else this._sortState = null;

        this._renderList();
    };

    _onFilterInput = (e) => {
        this._filterText = e.target.value;
        this._renderList();
    };

    _onEnableAllClick = (e) => {
        e.preventDefault();
        this._tools.forEach(tool => tool.enabled = true);
        this._renderList();
    };

    _onDisableAllClick = (e) => {
        e.preventDefault();
        this._tools.forEach(tool => tool.enabled = false);
        this._renderList();
    };

    _onToolToggle = (toolId, checked) => {
        const tool = this._tools.find(t => t.id === toolId);
        if (tool) {
            tool.enabled = checked;
            this._renderList();
        }
    };

    _onDialogConfirm = async () => {
        try {
            const toolsConfig = this._tools.map(tool => ({
                id: tool.id,
                enabled: tool.enabled
            }));
            const config = { tools: toolsConfig };
            const result = await this.onSave.emitResult(config);
            if (!(result && result.success)) {
                console.error('Failed to save tools state', result?.error);
                this._showSaveError(result?.error?.message || 'Failed to save tools');
                return;
            }
            this._closeDialog(true);
        } catch (err) {
            console.error('Failed to save tools state', err);
            this._showSaveError(err?.message || 'Failed to save tools');
        }
    };

    _showSaveError(message) {
        toast.show(message, 'error', 4000, this.el?.confirmBtn);
    }

    _onDialogClose = () => {
        this._closeDialog(false);
    };

    _attachEvents() {
        const { dialog, confirmBtn, cancelBtn, filterInput, enableAllBtn, disableAllBtn, sortBtn } = this.el;

        if (filterInput) filterInput.addEventListener('input', this._onFilterInput);
        if (enableAllBtn) enableAllBtn.addEventListener('click', this._onEnableAllClick);
        if (disableAllBtn) disableAllBtn.addEventListener('click', this._onDisableAllClick);
        if (sortBtn) sortBtn.addEventListener('click', this._onSortClick);

        if (confirmBtn) confirmBtn.onclick = this._onDialogConfirm;
        if (cancelBtn) cancelBtn.onclick = this._onDialogClose;
        if (dialog) dialog.onclose = this._onDialogClose;
    }

    _detachEvents() {
        if (!this.el) return;
        const { dialog, confirmBtn, cancelBtn, filterInput, enableAllBtn, disableAllBtn, sortBtn } = this.el;

        if (filterInput) filterInput.removeEventListener('input', this._onFilterInput);
        if (enableAllBtn) enableAllBtn.removeEventListener('click', this._onEnableAllClick);
        if (disableAllBtn) disableAllBtn.removeEventListener('click', this._onDisableAllClick);
        if (sortBtn) sortBtn.removeEventListener('click', this._onSortClick);

        if (confirmBtn) confirmBtn.onclick = null;
        if (cancelBtn) cancelBtn.onclick = null;
        if (dialog) dialog.onclose = null;
    }

    _closeDialog(resultValue) {
        const dialog = this.el?.dialog;
        if (!dialog) return;

        dialog.onclose = null;
        if (dialog.open) {
            dialog.close();
        }

        this._cleanup();

        if (this._dialogResolve) {
            this._dialogResolve(resultValue);
            this._dialogResolve = null;
        }
    }

    _cleanup() {
        this.onLoad.off();
        this.onSave.off();
        this._detachEvents();

        this._tools = [];
        this.el = null;
        this._filterText = '';
        this._sortState = null;
    }

    _renderList() {
        const { listContainer } = this.el || {};
        if (!listContainer) return;

        let workingTools = [...this._tools];

        if (this._sortState !== null) {
            workingTools.sort((a, b) => {
                const nameA = (a.name || '').toLowerCase();
                const nameB = (b.name || '').toLowerCase();
                if (this._sortState === 'asc') {
                    return nameA.localeCompare(nameB);
                } else {
                    return nameB.localeCompare(nameA);
                }
            });
        }

        let filteredTools = workingTools;
        if (this._filterText.trim() !== '') {
            const search = this._filterText.trim().toLowerCase();
            filteredTools = filteredTools.filter(tool => {
                const name = (tool.name || '').toLowerCase();
                const desc = (tool.description || '').toLowerCase();
                return name.includes(search) || desc.includes(search);
            });
        }

        listContainer.innerHTML = '';

        if (filteredTools.length === 0) {
            const emptyMsg = document.createElement('div');
            emptyMsg.className = 'loading-placeholder';
            emptyMsg.innerHTML = '<span>No built-in tools match the filter.</span>';
            listContainer.appendChild(emptyMsg);
            return;
        }

        filteredTools.forEach((tool) => {
            const card = document.createElement('div');
            card.className = `tool-item-card${tool.enabled ? '' : ' tool-disabled'}`;
            card.setAttribute('data-tool-id', tool.id);

            const infoBlock = document.createElement('div');
            infoBlock.className = 'tool-info-block';

            const nameRow = document.createElement('div');
            nameRow.className = 'tool-name-row';

            const title = document.createElement('span');
            title.className = 'tool-title';
            title.textContent = (tool.name || '').replace(/_/g, ' ') || 'unnamed-tool';
            nameRow.appendChild(title);

            const description = document.createElement('div');
            description.className = 'tool-description';
            description.textContent = tool.description || 'No description available.';

            infoBlock.appendChild(nameRow);
            infoBlock.appendChild(description);

            const switchLabel = document.createElement('label');
            switchLabel.className = 'switch-control';

            const input = document.createElement('input');
            input.type = 'checkbox';
            input.checked = !!tool.enabled;
            input.onchange = (e) => this._onToolToggle(tool.id, e.target.checked);

            const slider = document.createElement('span');
            slider.className = 'switch-slider';

            switchLabel.appendChild(input);
            switchLabel.appendChild(slider);

            card.appendChild(infoBlock);
            card.appendChild(switchLabel);
            listContainer.appendChild(card);
        });
    }

    async show() {
        this.el = this._getElements();
        this._detachEvents();

        const { dialog, filterInput } = this.el;
        if (!dialog) throw new Error('Missing required dialog element #tools-dialog');

        try {
            const result = await this.onLoad.emitResult();
            if (result && result.tools) {
                this._tools = result.tools;
            } else if (result && result.success && result.data && result.data.tools) {
                this._tools = result.data.tools;
            } else {
                this._tools = [];
            }
        } catch (e) {
            console.error('Failed to load built-in tools', e);
            this._tools = [];
        }

        this._filterText = '';
        this._sortState = 'asc';
        if (filterInput) filterInput.value = '';

        this._attachEvents();
        this._renderList();

        return new Promise((resolve) => {
            this._dialogResolve = resolve;
            dialog.showModal();
        });
    }
}