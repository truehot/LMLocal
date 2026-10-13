import { BaseStoreClass } from "@app/store/base.store.js";

class SettingsStoreClass extends BaseStoreClass {
    constructor() {
        super({
            Provider: "",
            ProviderId: null,
            LmStudioBaseUrl: "http://localhost:1234",
            TrustedServerCertificatePath: "",
            KnowledgeBasePaths: "./; ./docs",
            ApiKey: "",
            AutoLoadOnStartup: true,
            EnableHistoryCompression: false,
            EnableHistoryCompaction: false,
            Theme: 0,
            StreamInactivityTimeoutSeconds: 20,
            EnableChatLogging: true,
            AutoLoadLastHistory: true,
            EnableAiTools: false,
            EnableAiWriteTools: false,
            EnableSubAgents: false,
            CollapseToolCalls: false,
            EnableCodeCollapse: false,
            OpenToolFilesInEditor: false,
            PreventCloseWhileGenerating: true,
            ShowTokenStats: false
        });
    }
}

const settingsStore = new SettingsStoreClass();
export default settingsStore;
