import { BaseStoreClass } from "@app/store/base.store.js";

/**
 * Observable state of the current conversation (as opposed to the active model or the transient UI state).
 */
class SessionStoreClass extends BaseStoreClass {
    constructor() {
        super({
            contextTokens: 0,
            sessionPromptTokens: 0,
            sessionCompletionTokens: 0,
            sessionCachedTokens: 0
        });
    }

    /**
     * Drops the traffic accumulated for the model that was active.
     */
    resetTraffic() {
        this.setState({
            sessionPromptTokens: 0,
            sessionCompletionTokens: 0,
            sessionCachedTokens: 0
        });
    }
}

const sessionStore = new SessionStoreClass();
export default sessionStore;
