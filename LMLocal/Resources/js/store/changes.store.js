import { BaseStoreClass } from "@app/store/base.store.js";

class ChangesStoreClass extends BaseStoreClass {
    constructor() {
        super({
            changedFiles: [],
            visible: false,
            loading: false,
            loaded: false,
            error: null
        });
    }
}

const changesStore = new ChangesStoreClass();
export default changesStore;
