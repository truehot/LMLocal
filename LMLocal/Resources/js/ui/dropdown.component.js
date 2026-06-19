
export class Dropdown {

    constructor(container, options = [], config = {}) {
        this.container = container;
        this.options = options;
        this.selected = null;
        this.isOpen = false;
        this.onSelect = config.onSelect || null;
        this.placeholder = config.placeholder || 'Select...';
        this.renderOption = config.renderOption || null;

        this._trigger = null;
        this._menu = null;
        this._label = null;
        this._outsideHandler = null;

        this._render();
        this._attachEvents();

        if (config.selectedId) {
            const found = this.options.find(o => o.id === config.selectedId);
            if (found) this.select(found);
        } else if (this.options.length) {
            this.select(this.options[0]);
        }
    }

    _render() {
        this.container.innerHTML = '';

        this.container.classList.add('dropdown');


        this._trigger = document.createElement('button');
        this._trigger.type = 'button';
        this._trigger.className = 'dropdown-trigger';
        this._trigger.innerHTML = `
            <span class="dropdown-selected">${this.placeholder}</span>
            <span class="dropdown-arrow">▾</span>
        `;
        this.container.appendChild(this._trigger);

        this._menu = document.createElement('div');
        this._menu.className = 'dropdown-menu';
        this.container.appendChild(this._menu);

        this._label = this._trigger.querySelector('.dropdown-selected');
        this._updateMenu();
    }

    _updateMenu() {
        this._menu.innerHTML = '';
        this.options.forEach((option) => {
            const item = document.createElement('div');
            item.className = 'dropdown-item';
            if (this.selected && this.selected.id === option.id) {
                item.classList.add('selected');
            }

            if (option.icon) {
                const iconSpan = document.createElement('span');
                iconSpan.className = 'dropdown-icon';
                iconSpan.innerHTML = option.icon;
                item.appendChild(iconSpan);
            }
            const textSpan = document.createElement('span');
            textSpan.className = 'dropdown-label';
            textSpan.textContent = option.label || option.name || option.id;
            item.appendChild(textSpan);

            if (this.renderOption) {
                const customContent = this.renderOption(option);
                if (customContent) {

                    item.innerHTML = '';
                    item.appendChild(customContent);
                }
            }

            item.dataset.id = option.id;
            item.addEventListener('click', (e) => {
                e.stopPropagation();
                this.select(option);
                this.close();
            });
            this._menu.appendChild(item);
        });
    }

    _attachEvents() {
        this._trigger.addEventListener('click', (e) => {
            e.preventDefault();
            this.toggle();
        });

        this._outsideHandler = (e) => {
            if (!this.container.contains(e.target)) {
                this.close();
            }
        };
        document.addEventListener('click', this._outsideHandler);
    }

    toggle() {
        this.isOpen ? this.close() : this.open();
    }

    open() {
        this.isOpen = true;
        this._menu.style.display = 'block';
        this._trigger.classList.add('open');
    }

    close() {
        this.isOpen = false;
        this._menu.style.display = 'none';
        this._trigger.classList.remove('open');
    }

    select(option) {
        this.selected = option;
        const label = option.label || option.name || option.id;
        this._label.textContent = label;

        const items = this._menu.querySelectorAll('.dropdown-item');
        items.forEach(item => {
            item.classList.toggle('selected', item.dataset.id === option.id);
        });
        if (this.onSelect) {
            this.onSelect(option);
        }
    }


    setOptions(options) {
        this.options = options;
        if (!this.selected && options.length) {
            this.select(options[0]);
        } else if (this.selected) {
            const found = options.find(o => o.id === this.selected.id);
            if (found) {
                this.selected = found;
            } else if (options.length) {
                this.select(options[0]);
            } else {
                this.selected = null;
                this._label.textContent = this.placeholder;
            }
        }
        this._updateMenu();
    }

    getSelected() {
        return this.selected;
    }

    getSelectedId() {
        return this.selected ? this.selected.id : null;
    }

    destroy() {
        if (this._outsideHandler) {
            document.removeEventListener('click', this._outsideHandler);
        }
        this.container.innerHTML = '';
        this._trigger = null;
        this._menu = null;
        this._label = null;
        this.options = [];
        this.selected = null;
        this.isOpen = false;
    }
}