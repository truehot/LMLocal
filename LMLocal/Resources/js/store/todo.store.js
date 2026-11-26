import { BaseStoreClass } from "@app/store/base.store.js";

/**
 * Task list of the current request, as accepted by todo_write; empty when no request is running.
 */
class TodoStoreClass extends BaseStoreClass {
    constructor() {
        super({
            items: [],
            expanded: false,
        });
    }
}

const todoStore = new TodoStoreClass();
export default todoStore;
