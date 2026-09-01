import { createCallback } from '@app/lib/callback.js';
import toast from '@app/lib/toast.js';

export class SubAgentsDialog {
    constructor() {
        this.onLoad = createCallback();
        this.onSave = createCallback();
        this._agents = [];
        this.el = null;

        this._filterText = '';
        this._dialogResolve = null;
        this._loadError = null;
    }

    _getDialog() {
        return document.getElementById('subagents-dialog');
    }

    _getElements() {
        const dialog = this._getDialog();
        if (!dialog) return {};

        return {
            dialog,
            body: dialog.querySelector('.modal-body'),
            confirmBtn: dialog.querySelector('#subagents-modal-save'),
            cancelBtn: dialog.querySelector('#subagents-modal-close'),
            listContainer: dialog.querySelector('#subagents-list-container'),
            filterInput: dialog.querySelector('#subagents-filter-input'),
            enableAllBtn: dialog.querySelector('#subagents-enable-all-btn'),
            disableAllBtn: dialog.querySelector('#subagents-disable-all-btn')
        };
    }

    _onFilterInput = (e) => {
        this._filterText = e.target.value;
        this._renderList();
    };

    _onEnableAllClick = (e) => {
        e.preventDefault();
        this._agents.forEach(agent => agent.enabled = true);
        this._renderList();
    };

    _onDisableAllClick = (e) => {
        e.preventDefault();
        this._agents.forEach(agent => agent.enabled = false);
        this._renderList();
    };

    _onAgentToggle = (index, checked) => {
        const agent = this._agents[index];
        if (agent) {
            agent.enabled = checked;
            this._renderList();
        }
    };

    _onDialogConfirm = async () => {
        try {
            const config = {
                agents: this._agents.map(agent => ({
                    id: agent.id,
                    enabled: !!agent.enabled
                }))
            };
            const result = await this.onSave.emitResult(config);
            if (!(result && result.success)) {
                console.error('Failed to save subagents state', result?.error);
                this._showSaveError(result?.error?.message || 'Failed to save subagents');
                return;
            }
            this._closeDialog(true);
        } catch (err) {
            console.error('Failed to save subagents state', err);
            this._showSaveError(err?.message || 'Failed to save subagents');
        }
    };

    _showSaveError(message) {
        toast.show(message, 'error', 4000, this.el?.confirmBtn);
    }

    _onDialogClose = () => {
        this._closeDialog(false);
    };

    _attachEvents() {
        const { dialog, confirmBtn, cancelBtn, filterInput, enableAllBtn, disableAllBtn } = this.el;

        if (filterInput) filterInput.addEventListener('input', this._onFilterInput);
        if (enableAllBtn) enableAllBtn.addEventListener('click', this._onEnableAllClick);
        if (disableAllBtn) disableAllBtn.addEventListener('click', this._onDisableAllClick);

        if (confirmBtn) confirmBtn.onclick = this._onDialogConfirm;
        if (cancelBtn) cancelBtn.onclick = this._onDialogClose;
        if (dialog) dialog.onclose = this._onDialogClose;
    }

    _detachEvents() {
        if (!this.el) return;
        const { dialog, confirmBtn, cancelBtn, filterInput, enableAllBtn, disableAllBtn } = this.el;

        if (filterInput) filterInput.removeEventListener('input', this._onFilterInput);
        if (enableAllBtn) enableAllBtn.removeEventListener('click', this._onEnableAllClick);
        if (disableAllBtn) disableAllBtn.removeEventListener('click', this._onDisableAllClick);

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

        this._agents = [];
        this.el = null;
        this._filterText = '';
        this._loadError = null;
    }

    _renderList() {
        const { listContainer } = this.el || {};
        if (!listContainer) return;

        listContainer.innerHTML = '';

        if (this._loadError) {
            const emptyMsg = document.createElement('div');
            emptyMsg.className = 'loading-placeholder';
            emptyMsg.innerHTML = '<span>Failed to load Sub Agents.</span><span>' +
                this._escapeHtml(this._loadError) + '</span>';
            listContainer.appendChild(emptyMsg);
            return;
        }

        let workingAgents = [...this._agents];

        if (this._filterText.trim() !== '') {
            const search = this._filterText.trim().toLowerCase();
            workingAgents = workingAgents.filter(agent => {
                const id = (agent.id || '').toLowerCase();
                const displayName = (agent.displayName || '').toLowerCase();
                const desc = (agent.description || '').toLowerCase();
                const model = (agent.model || '').toLowerCase();
                return id.includes(search) || displayName.includes(search) || desc.includes(search) || model.includes(search);
            });
        }

        if (workingAgents.length === 0) {
            const emptyMsg = document.createElement('div');
            emptyMsg.className = 'loading-placeholder';
            emptyMsg.innerHTML = '<span>No SubAgents configured.</span>';
            listContainer.appendChild(emptyMsg);
            return;
        }

        workingAgents.forEach((agent) => {
            listContainer.appendChild(this._createAgentCard(agent));
        });
    }

    _createAgentCard(agent) {
        const card = document.createElement('div');
        card.className = `tool-item-card${agent.enabled ? '' : ' tool-disabled'}`;
        card.setAttribute('data-agent-id', agent.id || '');

        const infoBlock = document.createElement('div');
        infoBlock.className = 'tool-info-block';

        const nameRow = document.createElement('div');
        nameRow.className = 'tool-name-row';

        const title = document.createElement('span');
        title.className = 'tool-title';
        title.textContent = (agent.displayName || agent.id || '').replace(/_/g, ' ') || 'unnamed-agent';
        nameRow.appendChild(title);

        const description = document.createElement('div');
        description.className = 'tool-description';
        description.textContent = agent.description || 'No description available.';

        infoBlock.appendChild(nameRow);
        infoBlock.appendChild(description);

        // Details: model · provider · base url
        const detailsParts = [agent.model, agent.providerType, agent.customBaseUrl].filter(Boolean);
        if (detailsParts.length > 0) {
            const details = document.createElement('div');
            details.className = 'subagents-details';
            details.textContent = detailsParts.join(' · ');
            infoBlock.appendChild(details);
        }

        // Generation parameters
        const params = [];
        if (typeof agent.temperature === 'number') params.push(`temp ${agent.temperature}`);
        if (typeof agent.timeoutSeconds === 'number') params.push(`timeout ${agent.timeoutSeconds}s`);
        if (typeof agent.maxRounds === 'number') params.push(`rounds ${agent.maxRounds}`);
        if (typeof agent.maxTokens === 'number') params.push(`max tokens ${agent.maxTokens}`);
        if (params.length > 0) {
            const paramsLine = document.createElement('div');
            paramsLine.className = 'subagents-params';
            paramsLine.textContent = params.join(' · ');
            infoBlock.appendChild(paramsLine);
        }

        // Allowed tools chips
        const tools = agent.allowedTools || [];
        const chipsRow = document.createElement('div');
        chipsRow.className = 'subagents-chips';
        if (tools.length === 0) {
            const chip = document.createElement('span');
            chip.className = 'chip chip-muted';
            chip.textContent = 'no tools';
            chipsRow.appendChild(chip);
        } else {
            tools.forEach(tool => {
                const chip = document.createElement('span');
                chip.className = 'chip';
                chip.textContent = tool;
                chipsRow.appendChild(chip);
            });
        }
        infoBlock.appendChild(chipsRow);

        const switchLabel = document.createElement('label');
        switchLabel.className = 'switch-control';

        const input = document.createElement('input');
        input.type = 'checkbox';
        input.checked = !!agent.enabled;
        input.onchange = (e) => this._onAgentToggle(this._agents.indexOf(agent), e.target.checked);

        const slider = document.createElement('span');
        slider.className = 'switch-slider';

        switchLabel.appendChild(input);
        switchLabel.appendChild(slider);

        card.appendChild(infoBlock);
        card.appendChild(switchLabel);
        return card;
    }

    _escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    async show() {
        this.el = this._getElements();
        this._detachEvents();

        const { dialog, filterInput } = this.el;
        if (!dialog) throw new Error('Missing required dialog element #subagents-dialog');

        this._loadError = null;
        try {
            const result = await this.onLoad.emitResult();
            if (result && result.success === false && result.error) {
                this._loadError = result.error.message || 'Failed to load subagents';
                this._agents = [];
            } else if (result && Array.isArray(result.agents)) {
                this._agents = result.agents;
            } else if (result && result.success && result.data && Array.isArray(result.data.agents)) {
                this._agents = result.data.agents;
            } else {
                this._agents = [];
            }
        } catch (e) {
            console.error('Failed to load subagents', e);
            this._loadError = e?.message || 'Failed to load subagents';
            this._agents = [];
        }

        this._filterText = '';
        if (filterInput) filterInput.value = '';

        this._attachEvents();
        this._renderList();

        return new Promise((resolve) => {
            this._dialogResolve = resolve;
            dialog.showModal();
        });
    }
}
