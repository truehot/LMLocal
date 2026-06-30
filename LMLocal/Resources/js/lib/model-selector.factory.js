import { ModelSelectorDialog } from '@app/dialogs/models.list.dialog.js';
import appDataService from '@app/services/app.data.service.js';
import providersStore from '@app/store/providers.store.js';
import settingsStore from '@app/store/settings.store.js';

/**
 * Factory: creates a ModelSelectorDialog pre-wired with all callbacks
 * (onRefresh, onSelect, onLoadProviders, onSaveProvider).
 * Used by both startup flow and toolbar model name click.
 */
export function createModelSelectorDialog(models, activeModel = null) {
    const dialog = new ModelSelectorDialog(models, activeModel);

    dialog.onRefresh.on(async () => {
        return await appDataService.loadModels();
    });

    dialog.onSelect.on(async (selectedModel) => {
        if (selectedModel) {
            await appDataService.setActiveModel(
                selectedModel.id,
                selectedModel.name,
                selectedModel.supportsMaxTokens,
                selectedModel.maxTokens || 0,
            );
            return true;
        }
        return false;
    });

    dialog.onLoadProviders.on(async () => {
        const storeState = providersStore.getState();
        let providers;
        if (storeState.loaded) {
            providers = {
                defaultProviders: storeState.defaultProviders,
                providers: storeState.providers,
            };
        } else {
            providers = await appDataService.getProvidersAsync();
        }
        const settings = settingsStore.getState();
        return {
            defaultProviders: providers.defaultProviders,
            providers: providers.providers,
            Provider: settings.Provider,
            LmStudioBaseUrl: settings.LmStudioBaseUrl,
            ApiKey: settings.ApiKey,
        };
    });

    dialog.onSaveProvider.on(async (providerFields) => {
        const fullSettings = settingsStore.getState();
        return await appDataService.updateSettingsAsync({
            ...fullSettings,
            ...providerFields,
        });
    });

    return dialog;
}
