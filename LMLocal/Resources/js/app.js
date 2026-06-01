"use strict";

import appManager from '@app/services/app.manager.js';
import appController from '@app/app.controller.js';

window.lmInit = async () => {
    try {
        if (!appController.initialized) {
            // Call setup if it hasn't been called yet (for tests, for example)
            await appController.setup();
        }
        await appManager.onAppInit();

    } catch (error) {
        console.error('Error during app initialization:', error);
    }
};