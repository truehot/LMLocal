import { BaseStoreClass } from "@app/store/base.store.js";


class InstructionsStoreClass extends BaseStoreClass {
    constructor() {
        super({
            selectedTabId: null,
            instructions: null,
            loading: false,
            error: null
        });
    }
}


const instructionsStore = new InstructionsStoreClass();
export default instructionsStore;
