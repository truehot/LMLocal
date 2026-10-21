import { AppStatus } from '@app/store/app.status.js';
import { BaseStoreClass } from "@app/store/base.store.js";

/**
 * Simple observable application store that holds UI-related state and notifies subscribers on changes.
 **/
class AppStoreClass extends BaseStoreClass {
    constructor() {
        super({
            status: AppStatus.INITIALIZING,
            roundTokens: 0,
            lastAnswerTokens: 0,
            tokenSpeed: 0,
            cachedTokens: 0,
            error: null,
            accumulatedText: "",
            accumulatedThoughtText: "",
            roundNumber: 0,
            toolCount: 0,
            toolCallId: "",
            toolWithError: false,
            toolStep: null,
            toolMessage: "",
            toolGroupId: "",
            toolGroupTotal: 0,
            toolGroupCompleted: 0,
        });
    }
}


const appStore = new AppStoreClass();
export default appStore;


