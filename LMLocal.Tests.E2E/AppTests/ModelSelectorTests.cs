namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public class ModelSelectorTests : AppTestBase
{
    // Expected recency order used by ModelSelector_DefaultOrder_ShowsRecentlyUsedFirst (CA1861).
    private static readonly string[] RecentOrder = new[] { "model-zzz", "model-aaa" };

    [Test]
    [Category("ModelSelector")]
    public async Task Open_ModelSelector_IsVisibleAndShowsModels()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Click model name to open selector
        await Page.Locator("#model-name").ClickAsync();

        var dialog = Page.Locator("#model-selector-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Should render model cards from the mock
        await Expect(Page.Locator("#models-list-container .model-card[data-model-id='test-model-1']")).ToHaveCountAsync(1);
        // There may be a separate activeModel provided by the bridge response that isn't
        // included in the 'models' list; ensure at least one model card is present.
        await Expect(Page.Locator("#models-list-container .model-card")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("ModelSelector")]
    public async Task SelectModel_CallsBridgeAndClosesDialog()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Instrument models mock to capture the model id passed to SetActiveModelAsync
        await Page.EvaluateAsync("() => { window.__capturedModelId = null; if(window.__modelsOverride){ const orig = window.__modelsOverride.SetActiveModelAsync; window.__modelsOverride.SetActiveModelAsync = async (modelId, contextLength) => { window.__capturedModelId = modelId; return orig(modelId, contextLength); } } }");

        await Page.Locator("#model-name").ClickAsync();

        var dialog = Page.Locator("#model-selector-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Click a model card
        await Page.Locator("#models-list-container .model-card[data-model-id='test-model-1']").ClickAsync();

        // Wait for dialog to close
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });

        var captured = await Page.EvaluateAsync<string>("() => window.__capturedModelId");
        Assert.That(captured, Is.EqualTo("test-model-1"));
    }

    [Test]
    [Category("ModelSelector")]
    public async Task ActiveOnlyToggle_FiltersToLoadedModels()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#model-name").ClickAsync();

        var dialog = Page.Locator("#model-selector-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Toggle "Loaded only" - programmatically set the checkbox and dispatch change
        // (click can fail if element isn't visible/interactive in test env)
        await Page.EvaluateAsync("() => { const el = document.getElementById('model-active-only-toggle'); if (el) { el.checked = true; el.dispatchEvent(new Event('change', { bubbles: true })); } }");

        // Wait for the filtered list to render. Depending on whether the active model
        // is present in the `models` array returned by the bridge, the filtered
        // list may be empty (placeholder) or contain the active model card.
        // Check both possibilities.
        var cardCount = await Page.Locator("#models-list-container .model-card").CountAsync();
        if (cardCount > 0)
        {
            await Expect(Page.Locator("#models-list-container .model-card")).ToHaveCountAsync(cardCount);
            await Expect(Page.Locator("#models-list-container .model-card.active")).ToHaveCountAsync(1);
        }
        else
        {
            await Expect(Page.Locator("#models-list-container .empty-placeholder")).ToBeVisibleAsync();
        }
    }

    [Test]
    [Category("ModelSelector")]
    public async Task CloseDialog_CloseButton_ClosesDialog()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#model-name").ClickAsync();

        var dialog = Page.Locator("#model-selector-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Click close button
        await dialog.Locator("#model-selector-close").ClickAsync();

        // Verify dialog is closed
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("ModelSelector")]
    public async Task CloseDialog_EscapeKey_ClosesDialog()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#model-name").ClickAsync();

        var dialog = Page.Locator("#model-selector-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Press Escape
        await Page.Keyboard.PressAsync("Escape");

        // Verify dialog is closed
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("ModelSelector")]
    public async Task Reopen_ResetsFilterAndCheckbox()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open dialog
        await Page.Locator("#model-name").ClickAsync();
        var dialog = Page.Locator("#model-selector-dialog");
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(Page.Locator("#models-list-container .model-card[data-model-id='test-model-1']")).ToHaveCountAsync(1);

        // Apply a filter and enable "Loaded only" — list becomes empty
        await Page.Locator("#model-filter-input").FillAsync("zzz");
        await Page.EvaluateAsync("() => { const el = document.getElementById('model-active-only-toggle'); if (el) { el.checked = true; el.dispatchEvent(new Event('change', { bubbles: true })); } }");
        await Expect(Page.Locator("#models-list-container .empty-placeholder")).ToBeVisibleAsync();

        // Close
        await dialog.Locator("#model-selector-close").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();

        // Reopen — filter text and checkbox must be reset
        await Page.Locator("#model-name").ClickAsync();
        await Expect(dialog).ToBeVisibleAsync();

        await Expect(Page.Locator("#model-filter-input")).ToHaveValueAsync("");

        var isChecked = await Page.EvaluateAsync<bool>("() => document.getElementById('model-active-only-toggle').checked");
        Assert.That(isChecked, Is.False, "Loaded-only checkbox must be reset on reopen");

        await Expect(Page.Locator("#models-list-container .model-card[data-model-id='test-model-1']")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("ModelSelector")]
    public async Task Refresh_PreservesFilter()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#model-name").ClickAsync();
        var dialog = Page.Locator("#model-selector-dialog");
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(Page.Locator("#models-list-container .model-card")).ToHaveCountAsync(1);

        // Apply a filter that still matches the model
        await Page.Locator("#model-filter-input").FillAsync("test");
        await Expect(Page.Locator("#models-list-container .model-card")).ToHaveCountAsync(1);

        // Refresh
        var refreshBtn = Page.Locator("#model-refresh-btn");
        await refreshBtn.ClickAsync();
        await Expect(refreshBtn).Not.ToHaveClassAsync("spinning", new() { Timeout = 3000 });

        // Filter text and filtered list must be preserved after refresh
        await Expect(Page.Locator("#model-filter-input")).ToHaveValueAsync("test");
        await Expect(Page.Locator("#models-list-container .model-card")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("ModelSelector")]
    public async Task ProviderSwitch_NoStaleResponse()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Override mocks AFTER startup so the next ListModelsAsync call belongs to the dialog.
        // The dialog's initial load is made slow; the reload after provider switch is fast.
        await Page.EvaluateAsync(@"
            () => {
                window.__providerSwitch = { currentProvider: 'lmstudio', slowCall: false };

                window.__providersOverride = {
                    GetProvidersAsync: async () => JSON.stringify({
                        defaultProviders: [
                            { id: 1, providerType: 'lmstudio', name: 'Provider A', customBaseUrl: 'http://a.local', customApiKey: '' },
                            { id: 2, providerType: 'ollama', name: 'Provider B', customBaseUrl: 'http://b.local', customApiKey: '' }
                        ],
                        providers: [],
                        providerTypes: [
                            { key: 'lmstudio', displayName: 'LM Studio' },
                            { key: 'ollama', displayName: 'Ollama' }
                        ]
                    }),
                    UpdateProvidersAsync: async (json) => true,
                };

                window.__settingsOverride = {
                    GetSettingsAsync: async () => JSON.stringify({ AutoLoadOnStartup: true }),
                    UpdateSettingsAsync: async (json) => {
                        const parsed = JSON.parse(json);
                        window.__providerSwitch.currentProvider = parsed.Provider || 'lmstudio';
                        return true;
                    },
                    TestConnectionAsync: async (json) => JSON.stringify({ success: true }),
                    SetAiToolsAsync: async (json) => true,
                };

                window.__modelsOverride = {
                    ListModelsAsync: async () => {
                        const slow = window.__providerSwitch.slowCall;
                        window.__providerSwitch.slowCall = false;
                        const provider = window.__providerSwitch.currentProvider;
                        const modelId = provider === 'ollama' ? 'model-b' : 'model-a';
                        const modelName = provider === 'ollama' ? 'Model B' : 'Model A';
                        if (slow) {
                            await new Promise(r => setTimeout(r, 10000));
                        }
                        return JSON.stringify({
                            models: [{ id: modelId, name: modelName, isLoaded: true, supportsIsLoaded: true }],
                            hasActiveModel: false,
                            activeModel: null,
                            supportsIsLoaded: true,
                            error: null
                        });
                    },
                    SetActiveModelAsync: async (modelId, contextLength) => true,
                };
            }
        ");

        // Mark the next ListModelsAsync call (dialog initial load) as slow
        await Page.EvaluateAsync("() => { window.__providerSwitch.slowCall = true; }");

        // Open model selector
        await Page.Locator("#model-name").ClickAsync();
        var dialog = Page.Locator("#model-selector-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Wait until provider select is populated with both providers
        await Page.WaitForFunctionAsync("() => document.getElementById('model-provider-select')?.options.length === 2");

        // Switch to provider B while the slow initial load for provider A is still in flight
        await Page.Locator("#model-provider-select").SelectOptionAsync("1");

        // The final list must reflect provider B, not the stale provider A response
        await Expect(Page.Locator("#models-list-container .model-card[data-model-id='model-b']"))
            .ToHaveCountAsync(1, new() { Timeout = 8000 });
    }

    [Test]
    [Category("ModelSelector")]
    public async Task CloseDuringProviderSwitch_DoesNotRaiseUnhandledErrors()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Capture unhandled promise rejections. Closing the dialog while a provider
        // change is pending used to throw in `_handleProviderChange().finally`
        // (`this._setControlsEnabled(true)` ran with `this.el === null`).
        await Page.EvaluateAsync(@"() => {
            window.__unhandledRejections = [];
            window.addEventListener('unhandledrejection', (e) => {
                const reason = e.reason;
                window.__unhandledRejections.push(
                    reason && reason.message ? reason.message : String(reason)
                );
            });
        }");

        // Slow UpdateSettingsAsync keeps the provider-change save in flight while we close.
        await Page.EvaluateAsync(@"() => {
            window.__providersOverride = {
                GetProvidersAsync: async () => JSON.stringify({
                    defaultProviders: [
                        { id: 1, providerType: 'lmstudio', name: 'Provider A', customBaseUrl: 'http://a.local', customApiKey: '' },
                        { id: 2, providerType: 'ollama', name: 'Provider B', customBaseUrl: 'http://b.local', customApiKey: '' }
                    ],
                    providers: [],
                    providerTypes: [
                        { key: 'lmstudio', displayName: 'LM Studio' },
                        { key: 'ollama', displayName: 'Ollama' }
                    ]
                }),
                UpdateProvidersAsync: async (json) => true,
            };

            window.__settingsOverride = {
                GetSettingsAsync: async () => JSON.stringify({ AutoLoadOnStartup: true }),
                UpdateSettingsAsync: async () => {
                    await new Promise(r => setTimeout(r, 1500));
                    return true;
                },
                TestConnectionAsync: async (json) => JSON.stringify({ success: true }),
                SetAiToolsAsync: async (json) => true,
            };

            window.__modelsOverride = {
                ListModelsAsync: async () => JSON.stringify({
                    models: [{ id: 'model-a', name: 'Model A', isLoaded: true, supportsIsLoaded: true }],
                    hasActiveModel: false,
                    activeModel: null,
                    supportsIsLoaded: true,
                    error: null
                }),
                SetActiveModelAsync: async (modelId, contextLength) => true,
            };
        }");

        // Open model selector
        await Page.Locator("#model-name").ClickAsync();
        var dialog = Page.Locator("#model-selector-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Wait until provider select is populated with both providers
        await Page.WaitForFunctionAsync("() => document.getElementById('model-provider-select')?.options.length === 2");

        // Switch provider (option "1" = Provider B) -> slow save starts
        await Page.Locator("#model-provider-select").SelectOptionAsync("1");

        // Close the dialog while the provider change is still pending
        await Page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });

        // Wait for the pending save (1500ms) and the dialog's finally to run
        await Page.WaitForTimeoutAsync(2500);

        var rejections = await Page.EvaluateAsync<string[]>("() => window.__unhandledRejections");
        Assert.That(rejections, Is.Empty, "Provider change must not throw after the dialog is closed");

        // The page must still work: reopen and switch provider again
        await Page.Locator("#model-name").ClickAsync();
        await Expect(dialog).ToBeVisibleAsync();
        await Page.WaitForFunctionAsync("() => document.getElementById('model-provider-select')?.options.length === 2");
        await Page.Locator("#model-provider-select").SelectOptionAsync("1");
        await Page.WaitForTimeoutAsync(2000);

        rejections = await Page.EvaluateAsync<string[]>("() => window.__unhandledRejections");
        Assert.That(rejections, Is.Empty, "Reopened dialog must handle provider change without unhandled rejections");
    }

    private async Task SeedProviderSelectorAsync(string settingsJson, string providersJson)
    {
        await Page.EvaluateAsync(
            @"(args) => {
                const settings = JSON.parse(args.settingsJson);
                const providers = JSON.parse(args.providersJson);

                window.__settingsOverride = {
                    GetSettingsAsync: async () => JSON.stringify(settings),
                    UpdateSettingsAsync: async (json) => {
                        window.__lastSavedSettings = json;
                        return true;
                    },
                    TestConnectionAsync: async (json) => JSON.stringify({ success: true }),
                    SetAiToolsAsync: async (json) => true,
                };

                window.__providersOverride = {
                    GetProvidersAsync: async () => JSON.stringify(providers),
                    UpdateProvidersAsync: async (json) => true,
                };
            }",
            new { settingsJson, providersJson });

        // Перезагружаем settingsStore из нашего __settingsOverride.
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-settings']").ClickAsync();
        await Expect(Page.Locator("#settings-dialog")).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#settings-dialog form')?.children.length > 0");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.Locator("#settings-dialog")).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    private async Task<string> GetSelectedProviderTextAsync() =>
        await Page.EvaluateAsync<string>(
            "() => { const s = document.getElementById('model-provider-select'); return s && s.options[s.selectedIndex] ? s.options[s.selectedIndex].textContent : null; }");

    private async Task<bool> HasProviderOptionAsync(string name) =>
        await Page.EvaluateAsync<bool>(
            "(n) => Array.from(document.getElementById('model-provider-select').options).some(o => o.textContent === n)",
            name);

    private async Task SelectProviderByNameAsync(string name) =>
        await Page.EvaluateAsync(
            @"(n) => {
                const s = document.getElementById('model-provider-select');
                const idx = Array.from(s.options).findIndex(o => o.textContent === n);
                if (idx >= 0) {
                    s.selectedIndex = idx;
                    s.dispatchEvent(new Event('change', { bubbles: true }));
                }
            }",
            name);

    [Test]
    [Category("ModelSelector")]
    public async Task ProviderSelect_ActiveOpenAiCompatible_IsSelected()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        const string settings = """
            {
              "Provider": "openai",
              "ProviderId": 3,
              "LmStudioBaseUrl": "https://api.x.ai/v1",
              "ApiKey": "",
              "AutoLoadOnStartup": true
            }
            """;

        const string providers = """
            {
              "defaultProviders": [
                { "id": 0, "providerType": "lmstudio", "name": "LM Studio (local)", "customBaseUrl": "http://localhost:1234", "customApiKey": "" },
                { "id": 3, "providerType": "openai", "name": "OpenAI compatible", "customBaseUrl": "", "customApiKey": "" }
              ],
              "providers": [],
              "providerTypes": [
                { "key": "lmstudio", "displayName": "LM Studio" },
                { "key": "openai", "displayName": "OpenAI" }
              ]
            }
            """;

        await SeedProviderSelectorAsync(settings, providers);

        await Page.Locator("#model-name").ClickAsync();
        await Expect(Page.Locator("#model-selector-dialog")).ToBeVisibleAsync();
        await Page.WaitForFunctionAsync("() => document.getElementById('model-provider-select')?.options.length === 2");

        var selected = await GetSelectedProviderTextAsync();
        Assert.That(selected, Is.EqualTo("OpenAI compatible"));
        await Expect(Page.Locator("#models-list-container .model-card")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("ModelSelector")]
    public async Task ProviderSelect_InactiveOpenAiCompatible_IsHidden()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        const string settings = """
            {
              "Provider": "lmstudio",
              "ProviderId": 0,
              "LmStudioBaseUrl": "http://localhost:1234",
              "ApiKey": "",
              "AutoLoadOnStartup": true
            }
            """;

        const string providers = """
            {
              "defaultProviders": [
                { "id": 0, "providerType": "lmstudio", "name": "LM Studio (local)", "customBaseUrl": "http://localhost:1234", "customApiKey": "" },
                { "id": 3, "providerType": "openai", "name": "OpenAI compatible", "customBaseUrl": "", "customApiKey": "" }
              ],
              "providers": [],
              "providerTypes": [
                { "key": "lmstudio", "displayName": "LM Studio" },
                { "key": "openai", "displayName": "OpenAI" }
              ]
            }
            """;

        await SeedProviderSelectorAsync(settings, providers);

        await Page.Locator("#model-name").ClickAsync();
        await Expect(Page.Locator("#model-selector-dialog")).ToBeVisibleAsync();
        await Page.WaitForFunctionAsync("() => document.getElementById('model-provider-select')?.options.length === 1");

        Assert.That(await HasProviderOptionAsync("OpenAI compatible"), Is.False);
        Assert.That(await GetSelectedProviderTextAsync(), Is.EqualTo("LM Studio (local)"));
    }

    [Test]
    [Category("ModelSelector")]
    public async Task ProviderSelect_Save_UpdatesSettingsWithProviderDetails()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        const string settings = """
            {
              "Provider": "lmstudio",
              "ProviderId": 0,
              "LmStudioBaseUrl": "http://localhost:1234",
              "ApiKey": "",
              "AutoLoadOnStartup": true
            }
            """;

        const string providers = """
            {
              "defaultProviders": [
                { "id": 0, "providerType": "lmstudio", "name": "LM Studio (local)", "customBaseUrl": "http://localhost:1234", "customApiKey": "" },
                { "id": 1, "providerType": "ollama", "name": "Ollama", "customBaseUrl": "http://localhost:11434", "customApiKey": "" }
              ],
              "providers": [],
              "providerTypes": [
                { "key": "lmstudio", "displayName": "LM Studio" },
                { "key": "ollama", "displayName": "Ollama" }
              ]
            }
            """;

        await SeedProviderSelectorAsync(settings, providers);

        await Page.Locator("#model-name").ClickAsync();
        await Expect(Page.Locator("#model-selector-dialog")).ToBeVisibleAsync();
        await Page.WaitForFunctionAsync("() => document.getElementById('model-provider-select')?.options.length === 2");

        await SelectProviderByNameAsync("Ollama");

        await Page.WaitForFunctionAsync("() => window.__lastSavedSettings != null");
        var ok = await Page.EvaluateAsync<bool>(
            "() => { const s = JSON.parse(window.__lastSavedSettings); return s.Provider === 'ollama' && s.ProviderId === 1 && s.LmStudioBaseUrl === 'http://localhost:11434'; }");
        Assert.That(ok, Is.True, "Provider change must persist provider details to settings");

        // после сохранения происходит перезагрузка моделей
        await Expect(Page.Locator("#models-list-container .model-card")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("ModelSelector")]
    public async Task ProviderSelect_LegacyFallback_MatchesByUrlAndKey()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        const string settings = """
            {
              "Provider": "openai",
              "ProviderId": null,
              "LmStudioBaseUrl": "https://api.x.ai/v1",
              "ApiKey": "key-a",
              "AutoLoadOnStartup": true
            }
            """;

        const string providers = """
            {
              "defaultProviders": [],
              "providers": [
                { "id": 10, "providerType": "openai", "name": "My xAI", "customBaseUrl": "https://api.x.ai/v1", "customApiKey": "key-a" }
              ],
              "providerTypes": [
                { "key": "openai", "displayName": "OpenAI" }
              ]
            }
            """;

        await SeedProviderSelectorAsync(settings, providers);

        await Page.Locator("#model-name").ClickAsync();
        await Expect(Page.Locator("#model-selector-dialog")).ToBeVisibleAsync();
        await Page.WaitForFunctionAsync("() => document.getElementById('model-provider-select')?.options.length === 1");

        Assert.That(await GetSelectedProviderTextAsync(), Is.EqualTo("My xAI"));
    }

    [Test]
    [Category("ModelSelector")]
    public async Task ProviderSelect_CustomOpenAiActive_DefaultCompatibleHidden()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        const string settings = """
            {
              "Provider": "openai",
              "ProviderId": 10,
              "LmStudioBaseUrl": "https://api.x.ai/v1",
              "ApiKey": "key-a",
              "AutoLoadOnStartup": true
            }
            """;

        const string providers = """
            {
              "defaultProviders": [
                { "id": 0, "providerType": "lmstudio", "name": "LM Studio (local)", "customBaseUrl": "http://localhost:1234", "customApiKey": "" },
                { "id": 3, "providerType": "openai", "name": "OpenAI compatible", "customBaseUrl": "", "customApiKey": "" }
              ],
              "providers": [
                { "id": 10, "providerType": "openai", "name": "My xAI", "customBaseUrl": "https://api.x.ai/v1", "customApiKey": "key-a" }
              ],
              "providerTypes": [
                { "key": "lmstudio", "displayName": "LM Studio" },
                { "key": "openai", "displayName": "OpenAI" }
              ]
            }
            """;

        await SeedProviderSelectorAsync(settings, providers);

        await Page.Locator("#model-name").ClickAsync();
        await Expect(Page.Locator("#model-selector-dialog")).ToBeVisibleAsync();
        await Page.WaitForFunctionAsync("() => document.getElementById('model-provider-select')?.options.length === 2");

        Assert.That(await HasProviderOptionAsync("OpenAI compatible"), Is.False);
        Assert.That(await GetSelectedProviderTextAsync(), Is.EqualTo("My xAI"));
    }

    [Test]
    [Category("ModelSelector")]
    public async Task ProviderSelect_IdMatchOnIndexZero_Wins()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        const string settings = """
            {
              "Provider": "lmstudio",
              "ProviderId": 0,
              "LmStudioBaseUrl": "http://localhost:1234",
              "ApiKey": "",
              "AutoLoadOnStartup": true
            }
            """;

        const string providers = """
            {
              "defaultProviders": [
                { "id": 0, "providerType": "lmstudio", "name": "LM Studio (local)", "customBaseUrl": "http://localhost:1234", "customApiKey": "" },
                { "id": 2, "providerType": "lmstudio", "name": "LM Studio Dup", "customBaseUrl": "http://localhost:1234", "customApiKey": "other" }
              ],
              "providers": [],
              "providerTypes": [
                { "key": "lmstudio", "displayName": "LM Studio" }
              ]
            }
            """;

        await SeedProviderSelectorAsync(settings, providers);

        await Page.Locator("#model-name").ClickAsync();
        await Expect(Page.Locator("#model-selector-dialog")).ToBeVisibleAsync();
        await Page.WaitForFunctionAsync("() => document.getElementById('model-provider-select')?.options.length === 2");

        Assert.That(await GetSelectedProviderTextAsync(), Is.EqualTo("LM Studio (local)"));
    }

    [Test]
    [Category("ModelSelector")]
    public async Task ProviderSelect_AfterSwitchingAway_OpenAiCompatibleHidden()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        const string settings = """
            {
              "Provider": "openai",
              "ProviderId": 3,
              "LmStudioBaseUrl": "https://api.x.ai/v1",
              "ApiKey": "",
              "AutoLoadOnStartup": true
            }
            """;

        const string providers = """
            {
              "defaultProviders": [
                { "id": 0, "providerType": "lmstudio", "name": "LM Studio (local)", "customBaseUrl": "http://localhost:1234", "customApiKey": "" },
                { "id": 3, "providerType": "openai", "name": "OpenAI compatible", "customBaseUrl": "", "customApiKey": "" },
                { "id": 1, "providerType": "ollama", "name": "Ollama", "customBaseUrl": "http://localhost:11434", "customApiKey": "" }
              ],
              "providers": [],
              "providerTypes": [
                { "key": "lmstudio", "displayName": "LM Studio" },
                { "key": "openai", "displayName": "OpenAI" },
                { "key": "ollama", "displayName": "Ollama" }
              ]
            }
            """;

        await SeedProviderSelectorAsync(settings, providers);

        await Page.Locator("#model-name").ClickAsync();
        await Expect(Page.Locator("#model-selector-dialog")).ToBeVisibleAsync();
        await Page.WaitForFunctionAsync("() => document.getElementById('model-provider-select')?.options.length === 3");

        await SelectProviderByNameAsync("Ollama");
        await Page.WaitForFunctionAsync("() => window.__lastSavedSettings != null");
        await Page.WaitForTimeoutAsync(250); // даём store обновиться после успешного save

        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.Locator("#model-selector-dialog")).ToBeHiddenAsync(new() { Timeout = 3000 });

        await Page.Locator("#model-name").ClickAsync();
        await Expect(Page.Locator("#model-selector-dialog")).ToBeVisibleAsync();
        await Page.WaitForFunctionAsync(
            "() => { const s = document.getElementById('model-provider-select'); return s && s.options[s.selectedIndex]?.textContent === 'Ollama'; }");

        Assert.That(await HasProviderOptionAsync("OpenAI compatible"), Is.False);
    }


    [Test]
    [Category("ModelSelector")]
    public async Task ProviderSelect_LegacyFallback_KeyInSettings_NotInProviderList()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Ключ задан в Settings вручную; в профилях провайдеров его нет (customApiKey="").
        // Строгое сравнение p.customApiKey === savedApiKey дало бы «нет совпадения» -> индекс 0.
        const string settings = """
            {
              "Provider": "openai",
              "ProviderId": null,
              "LmStudioBaseUrl": "https://api.x.ai/v1",
              "ApiKey": "user-secret-key",
              "AutoLoadOnStartup": true
            }
            """;

        const string providers = """
            {
              "defaultProviders": [
                { "id": 0, "providerType": "lmstudio", "name": "LM Studio (local)", "customBaseUrl": "http://localhost:1234", "customApiKey": "" },
                { "id": 3, "providerType": "openai", "name": "OpenAI compatible", "customBaseUrl": "https://api.x.ai/v1", "customApiKey": "" }
              ],
              "providers": [],
              "providerTypes": [
                { "key": "lmstudio", "displayName": "LM Studio" },
                { "key": "openai", "displayName": "OpenAI" }
              ]
            }
            """;

        await SeedProviderSelectorAsync(settings, providers);

        await Page.Locator("#model-name").ClickAsync();
        await Expect(Page.Locator("#model-selector-dialog")).ToBeVisibleAsync();
        await Page.WaitForFunctionAsync("() => document.getElementById('model-provider-select')?.options.length === 2");

        Assert.That(await GetSelectedProviderTextAsync(), Is.EqualTo("OpenAI compatible"));
    }


    [Test]
    [Category("ModelSelector")]
    public async Task ModelSelector_DefaultOrder_ShowsRecentlyUsedFirst()
    {
        // Two models; alphabetical order differs from recency order.
        await Page.AddInitScriptAsync(@"window.__mockModels = [
                { id: 'model-aaa', name: 'A Model', isLoaded: true, supportsMaxTokens: false, maxTokens: 0 },
                { id: 'model-zzz', name: 'Z Model', isLoaded: true, supportsMaxTokens: false, maxTokens: 0 }
            ];
            window.__mockRecentEntries = [
                { providerType: 'lmstudio', providerId: null, modelId: 'model-zzz', modelName: 'Z Model', lastUsedUtc: '2026-01-15T12:34:56.1Z' },
                { providerType: 'lmstudio', providerId: null, modelId: 'model-aaa', modelName: 'A Model', lastUsedUtc: '2026-01-10T09:00:00Z' }
            ];");
        await GotoWithMockAsync("webview-mock-recent-models.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#model-name").ClickAsync();
        var dialog = Page.Locator("#model-selector-dialog");
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(Page.Locator("#models-list-container .model-card")).ToHaveCountAsync(2);

        var order = await Page.EvaluateAsync<string[]>(
            "() => Array.from(document.querySelectorAll('#models-list-container .model-card')).map(c => c.getAttribute('data-model-id'))");
        Assert.That(order, Is.EqualTo(RecentOrder),
            "Recently used model must be first, not alphabetical");
    }

    [Test]
    [Category("ModelSelector")]
    public async Task ModelSelector_SortButton_CyclesRecent_NameAsc_NameDesc()
    {
        await Page.AddInitScriptAsync(@"window.__mockModels = [
                { id: 'model-aaa', name: 'A Model', isLoaded: true, supportsMaxTokens: false, maxTokens: 0 },
                { id: 'model-zzz', name: 'Z Model', isLoaded: true, supportsMaxTokens: false, maxTokens: 0 }
            ];
            window.__mockRecentEntries = [
                { providerType: 'lmstudio', providerId: null, modelId: 'model-zzz', modelName: 'Z Model', lastUsedUtc: '2026-01-15T12:34:56.1Z' },
                { providerType: 'lmstudio', providerId: null, modelId: 'model-aaa', modelName: 'A Model', lastUsedUtc: '2026-01-10T09:00:00Z' }
            ];");
        await GotoWithMockAsync("webview-mock-recent-models.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#model-name").ClickAsync();
        var dialog = Page.Locator("#model-selector-dialog");
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(Page.Locator("#models-list-container .model-card")).ToHaveCountAsync(2);

        var sortBtn = Page.Locator("#model-sort-btn");

        // Default: recent first.
        Assert.That(await FirstModelIdAsync(), Is.EqualTo("model-zzz"));
        Assert.That(await sortBtn.GetAttributeAsync("title"), Is.EqualTo("Sort: Recently used"));

        // Click 1 -> Name A-Z.
        await sortBtn.ClickAsync();
        Assert.That(await FirstModelIdAsync(), Is.EqualTo("model-aaa"));
        Assert.That(await sortBtn.GetAttributeAsync("title"), Is.EqualTo("Sort: Name A-Z"));

        // Click 2 -> Name Z-A.
        await sortBtn.ClickAsync();
        Assert.That(await FirstModelIdAsync(), Is.EqualTo("model-zzz"));
        Assert.That(await sortBtn.GetAttributeAsync("title"), Is.EqualTo("Sort: Name Z-A"));

        // Click 3 -> back to recent.
        await sortBtn.ClickAsync();
        Assert.That(await FirstModelIdAsync(), Is.EqualTo("model-zzz"));
        Assert.That(await sortBtn.GetAttributeAsync("title"), Is.EqualTo("Sort: Recently used"));
    }

    [Test]
    [Category("ModelSelector")]
    public async Task ModelSelector_SelectModel_RecordsUsage()
    {
        await Page.AddInitScriptAsync(@"window.__mockSettings = { AutoLoadOnStartup: true, Provider: 'lmstudio', ProviderId: 3 };
            window.__mockModels = [
                { id: 'model-1', name: 'Model One', isLoaded: true, supportsMaxTokens: false, maxTokens: 0 }
            ];");
        await GotoWithMockAsync("webview-mock-recent-models.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Capture the RecordModelUsageAsync payload.
        await Page.EvaluateAsync(@"() => {
            window.__capturedUsage = null;
            if (window.__recentModelsOverride && typeof window.__recentModelsOverride.RecordModelUsageAsync === 'function') {
                const orig = window.__recentModelsOverride.RecordModelUsageAsync;
                window.__recentModelsOverride.RecordModelUsageAsync = async (payload) => {
                    window.__capturedUsage = JSON.parse(payload);
                    return orig(payload);
                };
            }
        }");

        await Page.Locator("#model-name").ClickAsync();
        var dialog = Page.Locator("#model-selector-dialog");
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(Page.Locator("#models-list-container .model-card")).ToHaveCountAsync(1);

        await Page.Locator("#models-list-container .model-card[data-model-id='model-1']").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });

        // recordModelUsage is fire-and-forget; give it a moment.
        await Page.WaitForFunctionAsync("() => window.__capturedUsage != null", new PageWaitForFunctionOptions { Timeout = 3000 });
        var captured = await Page.EvaluateAsync<dynamic>("() => window.__capturedUsage");
        Assert.That((string)captured.modelId, Is.EqualTo("model-1"));
        Assert.That((string)captured.modelName, Is.EqualTo("Model One"));
        Assert.That((string)captured.providerType, Is.EqualTo("lmstudio"));
        Assert.That(captured.providerId, Is.EqualTo(3));
    }


    private async Task<string> FirstModelIdAsync() =>
        await Page.EvaluateAsync<string>(
            "() => { const c = document.querySelector('#models-list-container .model-card'); return c ? c.getAttribute('data-model-id') : null; }");

}
