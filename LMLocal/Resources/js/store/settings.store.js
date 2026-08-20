import { BaseStoreClass } from "@app/store/base.store.js";

class SettingsStoreClass extends BaseStoreClass {
    constructor() {
        super({
            Provider: "",
            ProviderId: null,
            LmStudioBaseUrl: "http://localhost:1234",
            TrustedServerCertificatePath: "",
            ApiKey: "",
            AutoLoadOnStartup: true,
            EnableHistoryCompression: true,
            EnableHistoryCompaction: true,
            Theme: 0,
            StreamInactivityTimeoutSeconds: 20,
            EnableChatLogging: false,
            AutoLoadLastHistory: false,
            EnableAiTools: false,
            EnableAiWriteTools: false,
            CollapseToolCalls: false,
            EnableCodeCollapse: false,
            ShowTokenStats: false
        });
    }
}

const settingsStore = new SettingsStoreClass();
export default settingsStore;
