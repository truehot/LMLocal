import { AppStatus } from '@app/store/app.status.js';
import appStore from '@app/store/app.store.js';
import modelStore from '@app/store/model.store.js';
import { createModelSelectorDialog } from '@app/lib/model-selector.factory.js';
import appDataService from '@app/services/app.data.service.js';

/**
 * StartupManager - handles application initialization and model selection logic.
 * Initializes the application by selecting an active model based on priority:
 * 1. Use active model from backend if available
 * 2. Use first active (loaded) model from the list
 * 3. Show model selector dialog for user to choose
 */
class StartupManager {

    async initialize() {
        try {

            const response = await appDataService.loadModels();

            if (response.error || !response.models || response.models.length === 0) {
                appStore.setState({
                    status: AppStatus.OFFLINE,
                    error: response.error || "No models available",
                    tokenSpeed: 0
                });
                return;
            }

            // Priority 1: Use active model from backend if available
            if (response.activeModel) {
                this._setLocalModelState(response.activeModel);
                return;
            }

            // Priority 2: Find first active (loaded) model.
            const firstActiveModel = response.models.find(m => m.isLoaded);
            if (firstActiveModel) {
                await appDataService.setActiveModel(firstActiveModel.id, firstActiveModel.name, firstActiveModel.supportsMaxTokens, firstActiveModel.maxTokens || 0);
                return;
            }

            // Priority 3: Show model selector dialog if no active model found
            await this._showModelSelectorDialog(response.models, response.supportsIsLoaded);
        } catch (e) {
            console.error("Failed to initialize models:", e);
            appStore.setState({
                status: AppStatus.OFFLINE,
                error: "Failed to initialize models: " + e.message,
                tokenSpeed: 0
            });
        }
    }

    _setLocalModelState(model) {
        modelStore.setState({
            modelId: model.id,
            modelName: model.name || model.id,
            tokenMax: model.maxTokens || 0,
            tokenUsed: 0,
            supportsMaxTokens: model.supportsMaxTokens
        });

        appStore.setState({
            status: AppStatus.IDLE,
            tokenUsed: 0,
            tokenSpeed: 0,
            error: null
        });
    }

    async _showModelSelectorDialog(models, supportsIsLoaded = true) {
        const dialog = createModelSelectorDialog(models, null, supportsIsLoaded);

        try {
            const selectedModel = await dialog.show();

            if (!selectedModel) {
                appStore.setState({
                    status: AppStatus.OFFLINE,
                    error: "No model selected",
                    tokenSpeed: 0
                });
            }
        } catch (error) {
            console.error("Model selection failed:", error);
            appStore.setState({
                status: AppStatus.OFFLINE,
                error: "Failed to select model",
                tokenSpeed: 0
            });
        }
    }
}

const startupManager = new StartupManager();
export { startupManager };
