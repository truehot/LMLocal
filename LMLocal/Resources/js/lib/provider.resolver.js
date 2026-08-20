import settingsStore from '@app/store/settings.store.js';
import providersStore from '@app/store/providers.store.js';

/**
 * Resolves the human-readable display name of the currently active provider.
 */
export const providerResolver = {

    getCurrentDisplayName() {
        const { Provider, ProviderId, LmStudioBaseUrl, ApiKey } = settingsStore.getState();
        if (!Provider) return null;

        const { defaultProviders, providers } = providersStore.getState();
        const all = [...(defaultProviders || []), ...(providers || [])];

        if (ProviderId != null) {
            const found = all.find(p => p && p.providerType === Provider && p.id === ProviderId);
            return found?.name || null;
        }

        // Legacy: no ProviderId — triple match
        const found = all.find(p => {
            if (!p) return false;
            if (p.providerType !== Provider) return false;
            if (p.customBaseUrl !== LmStudioBaseUrl) return false;
            if (p.customApiKey && p.customApiKey !== ApiKey) return false;
            return true;
        });

        return found?.name || null;
    },

    /**
     * Calls `callback(name)` whenever the resolved provider name changes.
     */
    subscribe(callback) {
        let prevName = null;

        const handler = () => {
            const name = this.getCurrentDisplayName();
            if (name === prevName) return;
            prevName = name;
            callback(name);
        };

        settingsStore.subscribe(handler);
        providersStore.subscribe(handler);

        handler();

        return () => {
            settingsStore.unsubscribe(handler);
            providersStore.unsubscribe(handler);
        };
    }
};
