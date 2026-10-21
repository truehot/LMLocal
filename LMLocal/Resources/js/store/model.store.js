import { BaseStoreClass } from "@app/store/base.store.js";

/**
 * Observable state of the active model only: identity and window limits.
 */
class ModelStoreClass extends BaseStoreClass {
    constructor() {
        super({
            modelId: '',
            modelName: '',
            tokenMax: 0,
            supportsMaxTokens: false
        });
    }
}

const modelStore = new ModelStoreClass();
export default modelStore;
