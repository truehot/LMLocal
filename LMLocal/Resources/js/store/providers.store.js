import { BaseStoreClass } from "@app/store/base.store.js";

class ProvidersStoreClass extends BaseStoreClass {
    constructor() {
        super({
            defaultProviders: [],
            providers: [],
            loading: false,
            loaded: false,
            error: null
        });
    }
}

const providersStore = new ProvidersStoreClass();
export default providersStore;
