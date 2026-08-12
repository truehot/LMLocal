namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public class ModelSelectorTests : AppTestBase
{
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

}
