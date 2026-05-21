import { UIText } from '@app/store/app.globals.js';
import { AppStatus } from '@app/store/app.status.js';
import { appSelectors } from '@app/store/app.selectors.js';
import { createCallback } from '@app/lib/callback.js';

/**
 * InputComponent - manages the user input area and submit controls.
 * Handles input resizing, Enter/Send events, and exposes `onClick` and `onEnter`
 * callbacks for the controller to handle send/stop behavior.
 * Provides `setup` and `reset` methods to (re)initialize or clean up DOM connections.
 */
class InputComponent {
    constructor() {
        this.elements = {};

        this.onClick = createCallback();
        this.onEnter = createCallback();
        this.onTabChanged = createCallback();
    }

    _getElements() {
        return {
            inputWrapper: document.querySelector('.input-wrapper'),
            userInput: document.getElementById('userInput'),
            mainBtn: document.getElementById('mainBtn'),
            contextToggleBtn: document.getElementById('contextToggleBtn'),
            dropdown: document.getElementById('actionDropdown'),
            dropdownTrigger: document.querySelector('.dropdown-trigger'),
            selectedOption: document.getElementById('selectedOption'),
            dropdownMenu: document.querySelector('.dropdown-menu'),
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

    _attachEvents() {
        const { userInput, mainBtn, contextToggleBtn, dropdownTrigger, dropdown } = this.elements;
        if (!userInput || !mainBtn || !contextToggleBtn || !dropdownTrigger || !dropdown) return;

        userInput.addEventListener('input', this._handleInput);
        userInput.addEventListener('keydown', this._handleKeydown);
        mainBtn.addEventListener('click', this._handleClick);
        contextToggleBtn.addEventListener('click', this._handleContextToggle);

        dropdownTrigger.addEventListener('click', this._handleDropdownToggle);
        dropdown.addEventListener('click', this._handleDropdownItemClick);
    }

    _detachEvents() {
        const { userInput, mainBtn, contextToggleBtn, dropdownTrigger, dropdown } = this.elements;
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
        if (dropdownTrigger) {
            dropdownTrigger.removeEventListener('click', this._handleDropdownToggle);
        }
        if (dropdown) {
            dropdown.removeEventListener('click', this._handleDropdownItemClick);
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
        el.style.height = 'auto';
        if (this.elements.inputWrapper) this.elements.inputWrapper.classList.remove('expanded');
        if (this.elements.contextToggleBtn) this.elements.contextToggleBtn.classList.remove('active');
    }

    setup() {
        this.reset();
        this.elements = this._getElements();
        if (!this.elements.userInput || !this.elements.mainBtn) {
            console.error('InputComponent setup failed: required elements not found');
            return this;
        }
        this._attachEvents();
        return this;
    }

    update(state, prev) {
        if (this.elements.userInput && this.elements.mainBtn) {
            this._updateControls(state, prev);
        }
    }

    reset() {
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

        if (!instructions || instructions.length === 0) {
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
};

const inputComponent = new InputComponent();
export { inputComponent };