import { BaseStoreClass } from "@app/store/base.store.js";

class ModelsConfigStoreClass extends BaseStoreClass {
    constructor() {
        super({
            models: [],
            loading: false,
            loaded: false,
            error: null
        });
    }
}

const modelsConfigStore = new ModelsConfigStoreClass();
export default modelsConfigStore;
