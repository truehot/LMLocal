import { AppStatus } from '@app/store/app.status.js';
import { BaseStoreClass } from "@app/store/base.store.js";

/**
 * Simple observable application store that holds UI-related state and notifies
 * subscribers on changes. Designed as a minimal, synchronous state container
 * with a small API surface suitable for UI components to subscribe to updates.
 **/
class AppStoreClass extends BaseStoreClass {
    constructor() {
        super({
            status: AppStatus.INITIALIZING,
            tokenUsed: 0,
            tokenSpeed: 0,
            totalTokens: 0,
            cachedTokens: 0,
            sessionId: null,
            error: null,
            accumulatedText: "",
            accumulatedThoughtText: "",
            roundNumber: 0,
            toolCount: 0,
            toolCallId: "",
            toolWithError: false,
            toolMessage: "",
        });
    }
}


const appStore = new AppStoreClass();
export default appStore;


