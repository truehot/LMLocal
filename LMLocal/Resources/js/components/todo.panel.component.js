import { TodoStatus } from '@app/constants/app.globals.js';
import { AppStatus } from '@app/store/app.status.js';
import { appSelectors } from '@app/store/app.selectors.js';
import todoStore from '@app/store/todo.store.js';

/**
 * Live view of the task list the model writes with todo_write.
 */
class TodoPanelComponent {
    /** Icons of done / in_progress / pending. */
    static icons = {
        [TodoStatus.Done]: '\u2713',
        [TodoStatus.InProgress]: '\u25d0',
        [TodoStatus.Pending]: '\u25cb',
    };

    constructor() {
        this.panelElement = null;
        this.headerTrigger = null;
        this.summaryElement = null;
        this.bodyElement = null;

        this._items = [];
        this._togglePanel = this._togglePanel.bind(this);
    }

    setup() {
        this.panelElement = document.getElementById('global-todo-panel');
        if (!this.panelElement) return;

        this.headerTrigger = this.panelElement.querySelector('#todo-header-trigger');
        this.summaryElement = this.panelElement.querySelector('#todo-summary');
        this.bodyElement = this.panelElement.querySelector('#todo-body');

        this._attachEvents();
        this.updateTodoState(todoStore.getState(), null);
    }

    reset() {
        this._detachEvents();
        this.panelElement = null;
        this.headerTrigger = null;
        this.summaryElement = null;
        this.bodyElement = null;
        this._items = [];
    }

    _attachEvents() {
        if (this.headerTrigger) this.headerTrigger.addEventListener('click', this._togglePanel);
    }

    _detachEvents() {
        if (this.headerTrigger) this.headerTrigger.removeEventListener('click', this._togglePanel);
    }

    /**
     * The list belongs to one response: a completed, stopped or failed request clears it.
     */
    updateAppState(state, prev) {
        if (!prev) return;
        if (!TodoPanelComponent._isAnswerRunning(prev.status)) return;
        if (TodoPanelComponent._isAnswerRunning(state.status)) return;

        if (todoStore.getState().items.length > 0) {
            todoStore.setState({ items: [] });
        }
    }

    /**
     * A response that runs out of context is not over: the history is compacted and the same session
     * repeats the request (`FINISHING → COMPACTING → PROCESSING`, see app.status.js), so COMPACTING still
     * belongs to the running answer.
     */
    static _isAnswerRunning(status) {
        return appSelectors.isGenerating(status) || status === AppStatus.COMPACTING;
    }

    updateTodoState(state, prev) {
        if (!this.panelElement) return;
        if (prev && state.items === prev.items && state.expanded === prev.expanded) return;

        this._items = state.items || [];
        this._render(state.expanded);
    }

    _togglePanel() {
        todoStore.setState({ expanded: !todoStore.getState().expanded });
    }

    _render(expanded) {
        if (!this.panelElement) return;

        const hasItems = this._items.length > 0;
        this.panelElement.classList.toggle('hidden', !hasItems);
        this.panelElement.classList.toggle('expanded', !!expanded && hasItems);

        if (!hasItems) {
            if (this.summaryElement) this.summaryElement.textContent = '';
            if (this.bodyElement) this.bodyElement.textContent = '';
            return;
        }

        this._renderSummary();
        this._renderItems();
    }

    _renderSummary() {
        if (!this.summaryElement) return;

        let done = 0;
        let current = null;

        this._items.forEach(item => {
            if (item.status === TodoStatus.Done) {
                done++;
            } else if (item.status === TodoStatus.InProgress && !current) {
                current = item.text;
            }
        });

        const counter = `Tasks ${done}/${this._items.length}`;
        this.summaryElement.textContent = current ? `${counter} \u2014 ${current}` : counter;
    }

    _renderItems() {
        if (!this.bodyElement) return;

        const fragment = document.createDocumentFragment();

        this._items.forEach(item => {
            const status = TodoPanelComponent.icons[item.status] ? item.status : TodoStatus.Pending;

            // The status doubles as the CSS class (.todo-item.done etc. in component.css).
            const row = document.createElement('div');
            row.className = status === TodoStatus.InProgress ? `todo-item ${status} current` : `todo-item ${status}`;
            row.setAttribute('data-status', status);

            const icon = document.createElement('span');
            icon.className = 'todo-icon';
            icon.textContent = TodoPanelComponent.icons[status];

            const text = document.createElement('span');
            text.className = 'task-text';
            text.textContent = item.text || '';

            row.appendChild(icon);
            row.appendChild(text);
            fragment.appendChild(row);
        });

        this.bodyElement.textContent = '';
        this.bodyElement.appendChild(fragment);
    }
}

const todoPanelComponent = new TodoPanelComponent();
export { todoPanelComponent };
