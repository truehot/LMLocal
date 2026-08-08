import bridgeClient from '@app/api/bridge.client.js';

import { UIText, Config } from '@app/constants/app.globals.js';
import { AppStatus } from '@app/store/app.status.js';
import { appSelectors } from '@app/store/app.selectors.js';
import { createCallback } from '@app/lib/callback.js';
import { wrapAsCodeFence } from '@app/lib/file.fence.js';

/**
 * InputComponent - manages the user input area and submit controls.
 * Handles input resizing, Enter/Send events, and exposes `onClick` and `onEnter`
 * callbacks for the controller to handle send/stop behavior.
 * Provides `setup` and `reset` methods to (re)initialize or clean up DOM connections.
 */
class InputComponent {
    static RESIZE_MIN = 80;
    static RESIZE_MAX = 600;
    static AI_TOOLS_OPTIONS = [
        { value: 'none', label: 'No tools' },
        { value: 'readonly', label: 'Read Only' },
        { value: 'readwrite', label: 'Read & Write' }
    ];

    constructor() {
        this.elements = {};

        this.onClick = createCallback();
        this.onEnter = createCallback();
        this.onTabChanged = createCallback();
        this.onAiToolsChanged = createCallback();

        this._resizeState = { active: false, startY: 0, startHeight: 0 };
        this._resizerBound = { down: null, move: null, up: null };
    }

    _getElements() {
        return {
            inputWrapper: document.querySelector('.input-wrapper'),
            userInput: document.getElementById('userInput'),
            mainBtn: document.getElementById('mainBtn'),
            contextToggleBtn: document.getElementById('contextToggleBtn'),
            openFileBtn: document.getElementById('openFileBtn'),
            attachFileInput: document.getElementById('attachFileInput'),
            dropdown: document.getElementById('actionDropdown'),
            dropdownTrigger: document.querySelector('.dropdown-trigger'),
            selectedOption: document.getElementById('selectedOption'),
            dropdownMenu: document.querySelector('.dropdown-menu'),
            aiToolsDropdown: document.getElementById('aiToolsDropdown'),
            aiToolsSelectedOption: document.getElementById('aiToolsSelectedOption'),
            aiToolsDropdownMenu: document.getElementById('aiToolsDropdownMenu'),
        };
    }

    _handleInput = () => {
        const el = this.elements.userInput;
        if (!el) return;

        el.style.height = 'auto';
        if (el.value.length > 0) {
            el.style.height = `${el.scrollHeight}px`;
        }
        if (this.elements.inputWrapper) this.elements.inputWrapper.classList.add('expanded');
    };

    _handleKeydown = async (e) => {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            const value = this.elements.userInput?.value;
            const hasActiveContent = this.elements.contextToggleBtn.classList.contains('active');
            if (await this.onEnter.emit(value, hasActiveContent)) {
                this.clearInput();
            }
        }
    };

    _handleClick = async () => {
        const value = this.elements.userInput?.value;
        const hasActiveContent = this.elements.contextToggleBtn.classList.contains('active');
        if (await this.onClick.emit(value, hasActiveContent)) {
            this.clearInput();
        }
    };

    _handleContextToggle = async (e) => {
        if (e && typeof e.preventDefault === 'function') e.preventDefault();
        this.elements.contextToggleBtn.classList.toggle('active');
    };


    _handleDropdownToggle = (e) => {
        if (e && typeof e.stopPropagation === 'function') e.stopPropagation();
        this.elements.dropdown.classList.toggle('active');
    };

    _handleDropdownItemClick = (e) => {
        const item = e.target.closest && e.target.closest('.dropdown-item');
        if (!item) return;

        const tabId = item.getAttribute('data-value');
        const displayName = item.textContent || '';
        const selected = this.elements.selectedOption;
        const dropdown = this.elements.dropdown;

        if (selected) {
            selected.textContent = displayName;
        }
        if (dropdown) {
            dropdown.classList.remove('active');
        }

        this.onTabChanged.emit(tabId);
    };

    _handleAiToolsDropdownToggle = (e) => {
        if (e && typeof e.stopPropagation === 'function') e.stopPropagation();
        this.elements.aiToolsDropdown.classList.toggle('active');
    };

    _handleAiToolsDropdownItemClick = (e) => {
        const item = e.target.closest && e.target.closest('.dropdown-item');
        if (!item) return;

        const mode = item.getAttribute('data-value');
        const displayName = item.textContent || '';
        const selected = this.elements.aiToolsSelectedOption;
        const dropdown = this.elements.aiToolsDropdown;

        if (selected) {
            selected.textContent = displayName;
        }
        if (dropdown) {
            dropdown.classList.remove('active');
        }

        this.onAiToolsChanged.emit(mode);
    };

    // ─── Drag-and-drop file handling ────────────────────────────────────────

    _preventDefaults(e) {
        e.preventDefault();
        e.stopPropagation();
    }

    _handleDragEnter = () => {
        if (this.elements.inputWrapper) {
            this.elements.inputWrapper.classList.add('drag-over');
        }
    };

    _handleDragOver = () => {
        if (this.elements.inputWrapper) {
            this.elements.inputWrapper.classList.add('drag-over');
        }
    };

    _handleDragLeave = () => {
        if (this.elements.inputWrapper) {
            this.elements.inputWrapper.classList.remove('drag-over');
        }
    };

    _handleDrop = async (e) => {
        if (this.elements.inputWrapper) {
            this.elements.inputWrapper.classList.remove('drag-over');
        }

        const dt = e.dataTransfer;

        if (dt.files && dt.files.length > 0) {
            await this._appendFilesAsMarkdown(Array.from(dt.files));
            this._focusUserInput();
            return;
        }

        const text = dt.getData('text/plain');
        if (text) {
            const el = this.elements.userInput;
            if (!el) return;
            const separator = el.value.length > 0 && !el.value.endsWith('\n') ? '\n' : '';
            el.value += separator + text;
            this._handleInput();
        }
    };

    _handleOpenFileClick = (e) => {
        if (e && typeof e.preventDefault === 'function') e.preventDefault();
        if (this.elements.attachFileInput) {
            this.elements.attachFileInput.click();
        }
    };

    _handleAttachFileChange = async (e) => {
        const input = e.target;
        const files = input && input.files ? Array.from(input.files) : [];
        if (files.length > 0) {
            await this._appendFilesAsMarkdown(files);
        }
        if (input) {
            input.value = '';
        }
        this._focusUserInput();
    };

    _appendFilesAsMarkdown = async (files) => {
        const results = [];

        for (const file of files.slice(0, Config.DRAG_DROP_MAX_FILES)) {
            const ext = file.name.split('.').pop()?.toLowerCase();
            if (!ext) continue;

            if (!Config.DRAG_DROP_ALLOWED_EXTENSIONS.test('.' + ext)) {
                console.warn(`[FileAttach] Skipped unsupported file: ${file.name}`);
                continue;
            }

            if (file.size > Config.DRAG_DROP_MAX_FILE_SIZE_BYTES) {
                console.warn(`[FileAttach] Skipped oversized file: ${file.name} (${(file.size / 1024).toFixed(0)} KB)`);
                continue;
            }

            try {
                const text = await file.text();
                results.push(wrapAsCodeFence(file.name, text));
            } catch (err) {
                console.warn(`[FileAttach] Failed to read file: ${file.name}`, err);
            }
        }

        if (results.length === 0) return;

        const el = this.elements.userInput;
        if (!el) return;

        const separator = el.value.length > 0 && !el.value.endsWith('\n') ? '\n' : '';
        el.value += separator + results.join('\n');
        this._handleInput();
    };

    _focusUserInput() {
        const el = this.elements.userInput;
        if (!el || el.disabled) return;

        bridgeClient.focusAsync().catch((e) => { console.error(e); });
        el.focus();
        el.setSelectionRange(el.value.length, el.value.length);
        el.scrollTop = el.scrollHeight;
    }

    _attachDragDrop() {
        const wrapper = this.elements.inputWrapper;
        if (!wrapper) return;

        const events = ['dragenter', 'dragover', 'dragleave', 'drop'];
        for (const name of events) {
            wrapper.addEventListener(name, this._preventDefaults, false);
        }
        wrapper.addEventListener('dragenter', this._handleDragEnter, false);
        wrapper.addEventListener('dragover', this._handleDragOver, false);
        wrapper.addEventListener('dragleave', this._handleDragLeave, false);
        wrapper.addEventListener('drop', this._handleDrop, false);
    }

    _detachDragDrop() {
        const wrapper = this.elements.inputWrapper;
        if (!wrapper) return;

        const events = ['dragenter', 'dragover', 'dragleave', 'drop'];
        for (const name of events) {
            wrapper.removeEventListener(name, this._preventDefaults, false);
        }
        wrapper.removeEventListener('dragenter', this._handleDragEnter, false);
        wrapper.removeEventListener('dragover', this._handleDragOver, false);
        wrapper.removeEventListener('dragleave', this._handleDragLeave, false);
        wrapper.removeEventListener('drop', this._handleDrop, false);
    }


    _attachEvents() {

        const { userInput, mainBtn, contextToggleBtn, dropdownTrigger, dropdown, aiToolsDropdown } = this.elements;
        if (!userInput || !mainBtn || !contextToggleBtn || !dropdownTrigger || !dropdown) return;

        userInput.addEventListener('input', this._handleInput);
        userInput.addEventListener('keydown', this._handleKeydown);
        mainBtn.addEventListener('click', this._handleClick);
        contextToggleBtn.addEventListener('click', this._handleContextToggle);
        if (this.elements.openFileBtn) {
            this.elements.openFileBtn.addEventListener('click', this._handleOpenFileClick);
        }
        if (this.elements.attachFileInput) {
            this.elements.attachFileInput.addEventListener('change', this._handleAttachFileChange);
        }

        dropdownTrigger.addEventListener('click', this._handleDropdownToggle);
        dropdown.addEventListener('click', this._handleDropdownItemClick);

        if (aiToolsDropdown) {
            const aiToolsTrigger = aiToolsDropdown.querySelector('.dropdown-trigger');
            if (aiToolsTrigger) {
                aiToolsTrigger.addEventListener('click', this._handleAiToolsDropdownToggle);
            }
            aiToolsDropdown.addEventListener('click', this._handleAiToolsDropdownItemClick);
        }
    }

    _detachEvents() {
        const { userInput, mainBtn, contextToggleBtn, dropdownTrigger, dropdown, aiToolsDropdown } = this.elements;
        if (userInput) {
            userInput.removeEventListener('input', this._handleInput);
            userInput.removeEventListener('keydown', this._handleKeydown);
        }
        if (mainBtn) {
            mainBtn.removeEventListener('click', this._handleClick);
        }
        if (contextToggleBtn) {
            contextToggleBtn.removeEventListener('click', this._handleContextToggle);
        }
        if (this.elements.openFileBtn) {
            this.elements.openFileBtn.removeEventListener('click', this._handleOpenFileClick);
        }
        if (this.elements.attachFileInput) {
            this.elements.attachFileInput.removeEventListener('change', this._handleAttachFileChange);
        }
        if (dropdownTrigger) {
            dropdownTrigger.removeEventListener('click', this._handleDropdownToggle);
        }
        if (dropdown) {
            dropdown.removeEventListener('click', this._handleDropdownItemClick);
        }
        if (aiToolsDropdown) {
            const aiToolsTrigger = aiToolsDropdown.querySelector('.dropdown-trigger');
            if (aiToolsTrigger) {
                aiToolsTrigger.removeEventListener('click', this._handleAiToolsDropdownToggle);
            }
            aiToolsDropdown.removeEventListener('click', this._handleAiToolsDropdownItemClick);
        }
    }

    _updateControls(state, prev) {
        if (
            prev &&
            state.status === prev.status &&
            appSelectors.isBusy(state.status) === appSelectors.isBusy(prev.status)
        ) {
            return;
        }

        const isBusy = appSelectors.isBusy(state.status);
        const canSend = appSelectors.canSend(state.status);
        const isStopping = state.status === AppStatus.STOPPING;

        this.elements.userInput.disabled = !canSend;
        this.elements.contextToggleBtn.disabled = !canSend;
        this.elements.mainBtn.disabled = isStopping;
        if (this.elements.openFileBtn) this.elements.openFileBtn.disabled = !canSend;
        if (this.elements.attachFileInput) this.elements.attachFileInput.disabled = !canSend;

        const buttonText = isBusy
            ? UIText.BUTTON_STOP
            : isStopping
                ? UIText.BUTTON_WAIT
                : UIText.BUTTON_SEND


        this.elements.mainBtn.textContent = buttonText;
        this.elements.mainBtn.className = `main-btn ${isBusy || isStopping ? 'btn-stop' : ''}`;
    }

    clearInput() {
        const el = this.elements.userInput;
        if (!el) return;
        el.value = '';
        el.style.removeProperty('min-height');
        el.style.removeProperty('height');
        el.style.height = 'auto';
        if (this.elements.inputWrapper) this.elements.inputWrapper.classList.remove('expanded');
        if (this.elements.contextToggleBtn) this.elements.contextToggleBtn.classList.remove('active');
    }

    _initResizer() {
        const resizer = document.getElementById('input-drag-resizer');
        if (!resizer) return;
        this._resizerBound.down = this._onResizerMouseDown.bind(this);
        resizer.addEventListener('mousedown', this._resizerBound.down);
    }

    _onResizerMouseDown(e) {
        const wrapper = this.elements.inputWrapper;
        const el = this.elements.userInput;
        if (!wrapper || !el || !wrapper.classList.contains('expanded')) return;

        this._resizeState.active = true;
        this._resizeState.startY = e.clientY;
        this._resizeState.startHeight = el.getBoundingClientRect().height;

        wrapper.classList.remove('expanded-full');

        const resizer = document.getElementById('input-drag-resizer');
        if (resizer) resizer.classList.add('is-resizing');
        document.body.style.cursor = 'ns-resize';
        document.body.style.userSelect = 'none';

        this._resizerBound.move = this._onResizerMouseMove.bind(this);
        this._resizerBound.up = this._onResizerMouseUp.bind(this);
        document.addEventListener('mousemove', this._resizerBound.move);
        document.addEventListener('mouseup', this._resizerBound.up);
    }

    _onResizerMouseMove(e) {
        if (!this._resizeState.active) return;

        const deltaY = e.clientY - this._resizeState.startY;
        const newHeight = Math.min(
            InputComponent.RESIZE_MAX,
            Math.max(InputComponent.RESIZE_MIN, this._resizeState.startHeight - deltaY)
        );
        this.elements.userInput.style.setProperty('min-height', `${newHeight}px`, 'important');
    }

    _onResizerMouseUp() {
        this._resizeState.active = false;

        const resizer = document.getElementById('input-drag-resizer');
        if (resizer) resizer.classList.remove('is-resizing');
        document.body.style.cursor = '';
        document.body.style.userSelect = '';

        document.removeEventListener('mousemove', this._resizerBound.move);
        document.removeEventListener('mouseup', this._resizerBound.up);
        this._resizerBound.move = null;
        this._resizerBound.up = null;
    }

    _destroyResizer() {
        const resizer = document.getElementById('input-drag-resizer');
        if (resizer && this._resizerBound.down)
            resizer.removeEventListener('mousedown', this._resizerBound.down);
        if (this._resizerBound.move)
            document.removeEventListener('mousemove', this._resizerBound.move);
        if (this._resizerBound.up)
            document.removeEventListener('mouseup', this._resizerBound.up);
        this._resizerBound = { down: null, move: null, up: null };
    }

    setup() {
        this.reset();
        this.elements = this._getElements();
        if (!this.elements.userInput || !this.elements.mainBtn) {
            console.error('InputComponent setup failed: required elements not found');
            return this;
        }
        this._attachEvents();
        this._attachDragDrop();
        this._initResizer();
        this.renderAiToolsOptions();
        return this;
    }

    updateAppState(state, prev) {
        if (this.elements.userInput && this.elements.mainBtn) {
            this._updateControls(state, prev);
        }
    }

    reset() {
        this._detachDragDrop();
        this._destroyResizer();
        this._detachEvents();
        this.clearInput();
        this.elements = {};
    }

    hideDropdown() {
        if (this.elements.dropdown && this.elements.dropdown.classList.contains('active')) {
            this.elements.dropdown.classList.remove('active');
        }
    }

    updateInstructionsState(state, prev) {
        if (!this.elements.dropdownMenu || !this.elements.selectedOption) return;

        if (state.instructions === prev?.instructions && state.selectedTabId === prev?.selectedTabId) return;

        const instructions = state.instructions || [];
        const selectedTabId = state.selectedTabId;

        if (!instructions || !instructions?.length) {
            this.elements.dropdown.style.display = "none";
            return;
        }

        this.elements.dropdownMenu.innerHTML = '';

        let defaultTab = null;
        let tabToSelect = null;

        for (const tab of instructions) {
            if (tab.enabled) {
                const item = document.createElement('div');
                item.className = 'dropdown-item';
                item.setAttribute('data-value', tab.id);
                item.textContent = tab.displayName;
                this.elements.dropdownMenu.appendChild(item);

                if (selectedTabId && tab.id == selectedTabId) {
                    tabToSelect = tab;
                }
                if (!defaultTab) {
                    defaultTab = tab;
                }
            }
        }

        if (!tabToSelect && defaultTab) {
            tabToSelect = defaultTab;
        }

        if (tabToSelect) {
            this.elements.selectedOption.textContent = tabToSelect.displayName;
            this.elements.dropdown.style.display = "block";
        } else {
            this.elements.dropdown.style.display = "none";
        }
    }

    renderAiToolsOptions() {
        if (!this.elements.aiToolsDropdownMenu) return;
        this.elements.aiToolsDropdownMenu.innerHTML = '';
        for (const option of InputComponent.AI_TOOLS_OPTIONS) {
            const item = document.createElement('div');
            item.className = 'dropdown-item';
            item.setAttribute('data-value', option.value);
            item.textContent = option.label;
            this.elements.aiToolsDropdownMenu.appendChild(item);
        }
    }

    updateSettingsState(state, prev) {
        if (!this.elements.aiToolsSelectedOption) return;

        if (
            prev &&
            state.EnableAiTools === prev.EnableAiTools &&
            state.EnableAiWriteTools === prev.EnableAiWriteTools
        ) {
            return;
        }

        const mode = state.EnableAiTools
            ? (state.EnableAiWriteTools ? 'readwrite' : 'readonly')
            : 'none';

        const option = InputComponent.AI_TOOLS_OPTIONS.find(o => o.value === mode);
        if (option) {
            this.elements.aiToolsSelectedOption.textContent = option.label;
        }
    }
};

const inputComponent = new InputComponent();
export { inputComponent };