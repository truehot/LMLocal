namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public class AutocompletionsTests : AppTestBase
{
    private const string DialogId = "#autocompletions-selector-dialog";
    private const string InfoViewId = "#autocompletions-info-view";
    private const string SelectionViewId = "#autocompletions-selection-view";
    private const string TestBtnId = "#autocompletions-test-btn";
    private const string CancelBtnId = "#autocompletions-cancel-btn";
    private const string SaveBtnId = "#autocompletions-save-btn";
    private const string ChangeBtnId = "#autocompletions-change-btn";
    private const string BackBtnId = "#autocompletions-back-btn";

    /// <summary>
    /// Sets up the autocompletions bridge mock on the page.
    /// </summary>
    private async Task SetupAutocompletionsMockAsync()
    {
        await Page.EvaluateAsync(@"() => {
            window.__autocompletionsOverride = {
                GetConfigAsync: async () => JSON.stringify({
                    enabled: true,
                    providerId: 0,
                    providerType: 'lmstudio',
                    modelId: 'test-model-1'
                }),
                UpdateConfigAsync: async (json) => {
                    window.__capturedUpdateConfig = json;
                    return true;
                },
                GetCompletionAsync: async (json) => {
                    const params = JSON.parse(json);
                    window.__capturedCompletionParams = json;
                    if (params.prompt && params.prompt.includes('add')) {
                        return '\n  return a + b;\n}';
                    }
                    return '';
                },
                ListModelsForProviderAsync: async (json) => JSON.stringify({
                    models: [
                        { id: 'test-model-1', name: 'Test Model', isLoaded: true },
                        { id: 'test-model-2', name: 'Another Model', isLoaded: false }
                    ],
                    hasActiveModel: true,
                    supportsIsLoaded: true
                }),
                TestCompletionAsync: async (json) => {
                    window.__capturedTestParams = json;
                    return JSON.stringify({
                        success: true,
                        data: '\n  return a + b;\n}'
                    });
                }
            };
            // Override providers to include at least one provider so provider select works
            // Note: Response format must match GetProvidersResponse (top-level defaultProviders/providers, no success/data wrapper)
            window.__providersOverride = {
                GetProvidersAsync: async () => JSON.stringify({
                    defaultProviders: [
                        { id: 0, providerType: 'lmstudio', name: 'LM Studio', customBaseUrl: '', customApiKey: '' }
                    ],
                    providers: [],
                    providerTypes: [
                        { key: 'lmstudio', displayName: 'LM Studio' }
                    ]
                }),
                UpdateProvidersAsync: async (json) => true,
            };
        }");
    }

    private async Task OpenDialogViaMenuAsync()
    {
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-fim']").ClickAsync();
    }

    private async Task WaitForTestButtonClassAsync(string className)
    {
        await Page.WaitForFunctionAsync(
            $"() => document.querySelector('{TestBtnId}')?.classList.contains('{className}')");
    }

    [Test]
    [Category("Autocompletions")]
    public async Task Open_AutocompletionsDialog_IsVisible()
    {
        await GotoWithMockAsync("webview-mock.js");
        await SetupAutocompletionsMockAsync();
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await OpenDialogViaMenuAsync();

        var dialog = Page.Locator(DialogId);
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Verify header
        await Expect(dialog.Locator(".modal-header")).ToHaveTextAsync("Autocompletions");
    }

    [Test]
    [Category("Autocompletions")]
    public async Task AutocompletionsDialog_HasRequiredElements()
    {
        await GotoWithMockAsync("webview-mock.js");
        await SetupAutocompletionsMockAsync();
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await OpenDialogViaMenuAsync();

        var dialog = Page.Locator(DialogId);
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Wait for info view to be visible (config loaded)
        await Expect(dialog.Locator(InfoViewId)).ToBeVisibleAsync();

        // Enable checkbox
        await Expect(dialog.Locator("#autocompletions-dialog-enable-checkbox")).ToHaveCountAsync(1);

        // Provider and model display
        await Expect(dialog.Locator("#autocompletions-provider-name")).ToHaveCountAsync(1);
        await Expect(dialog.Locator("#autocompletions-model-name")).ToHaveCountAsync(1);

        // Action buttons
        await Expect(dialog.Locator(ChangeBtnId)).ToHaveCountAsync(1);
        await Expect(dialog.Locator(TestBtnId)).ToHaveCountAsync(1);
        await Expect(dialog.Locator(CancelBtnId)).ToHaveCountAsync(1);
        await Expect(dialog.Locator(SaveBtnId)).ToHaveCountAsync(1);
    }

    [Test]
    [Category("Autocompletions")]
    public async Task AutocompletionsDialog_ShowsProviderAndModel()
    {
        await GotoWithMockAsync("webview-mock.js");
        await SetupAutocompletionsMockAsync();
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await OpenDialogViaMenuAsync();

        var dialog = Page.Locator(DialogId);
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });

        // The config says modelId: 'test-model-1', providerType: 'lmstudio'
        // The mock providers list includes 'LM Studio' with providerType 'lmstudio', so it is matched
        await Expect(dialog.Locator("#autocompletions-provider-name")).ToHaveTextAsync("LM Studio");
    }

    [Test]
    [Category("Autocompletions")]
    public async Task TestButton_Success_WhenCompletionContainsReturn()
    {
        await GotoWithMockAsync("webview-mock.js");
        await SetupAutocompletionsMockAsync();
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await OpenDialogViaMenuAsync();

        var dialog = Page.Locator(DialogId);
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Click Test button
        var testBtn = dialog.Locator(TestBtnId);
        await testBtn.ClickAsync();

        // Wait for success state: button should have class 'success'
        await WaitForTestButtonClassAsync("success");

        // Verify that the mock received the test completion call
        var capturedParams = await Page.EvaluateAsync<string>("window.__capturedTestParams || null");
        Assert.That(capturedParams, Is.Not.Null, "TestCompletionAsync should have been called");
        Assert.That(capturedParams, Does.Contain("\"modelId\":\"test-model-1\""));
    }

    [Test]
    [Category("Autocompletions")]
    public async Task TestButton_Error_WhenCompletionIsEmpty()
    {
        await GotoWithMockAsync("webview-mock.js");
        await SetupAutocompletionsMockAsync();
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Override TestCompletionAsync to return empty data
        await Page.EvaluateAsync(@"() => {
            window.__autocompletionsOverride.TestCompletionAsync = async (json) => JSON.stringify({
                success: true,
                data: ''
            });
        }");

        await OpenDialogViaMenuAsync();

        var dialog = Page.Locator(DialogId);
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Click Test button
        var testBtn = dialog.Locator(TestBtnId);
        await testBtn.ClickAsync();

        // Wait for error state: button should have class 'error'
        await WaitForTestButtonClassAsync("error");
    }

    [Test]
    [Category("Autocompletions")]
    public async Task TestButton_ShowsSpinnerDuringTest()
    {
        await GotoWithMockAsync("webview-mock.js");
        await SetupAutocompletionsMockAsync();
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Delay the mock response to see the spinner
        await Page.EvaluateAsync(@"() => {
            const orig = window.__autocompletionsOverride.TestCompletionAsync;
            window.__autocompletionsOverride.TestCompletionAsync = async (json) => {
                await new Promise(r => setTimeout(r, 500));
                return orig(json);
            };
        }");

        await OpenDialogViaMenuAsync();

        var dialog = Page.Locator(DialogId);
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });

        var testBtn = dialog.Locator(TestBtnId);
        await testBtn.ClickAsync();

        // Button should contain spinner element during test
        await Expect(testBtn.Locator(".btn-spinner")).ToBeVisibleAsync(new() { Timeout = 1000 });

        // Wait for completion (success state)
        await WaitForTestButtonClassAsync("success");
    }

    [Test]
    [Category("Autocompletions")]
    public async Task ChangeButton_ShowsSelectionView()
    {
        await GotoWithMockAsync("webview-mock.js");
        await SetupAutocompletionsMockAsync();
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await OpenDialogViaMenuAsync();

        var dialog = Page.Locator(DialogId);
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Expect(dialog.Locator(InfoViewId)).ToBeVisibleAsync();

        // Click Change button
        await dialog.Locator(ChangeBtnId).ClickAsync();

        // Selection view should be visible, info view hidden
        await Expect(dialog.Locator(SelectionViewId)).ToBeVisibleAsync();
        await Expect(dialog.Locator(InfoViewId)).ToBeHiddenAsync();
    }

    [Test]
    [Category("Autocompletions")]
    public async Task BackButton_ReturnsToInfoView()
    {
        await GotoWithMockAsync("webview-mock.js");
        await SetupAutocompletionsMockAsync();
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await OpenDialogViaMenuAsync();

        var dialog = Page.Locator(DialogId);
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Go to selection view
        await dialog.Locator(ChangeBtnId).ClickAsync();
        await Expect(dialog.Locator(SelectionViewId)).ToBeVisibleAsync();

        // Click Back button
        await dialog.Locator(BackBtnId).ClickAsync();

        // Back to info view
        await Expect(dialog.Locator(InfoViewId)).ToBeVisibleAsync();
        await Expect(dialog.Locator(SelectionViewId)).ToBeHiddenAsync();
    }

    [Test]
    [Category("Autocompletions")]
    public async Task CloseDialog_CancelButton_ClosesDialog()
    {
        await GotoWithMockAsync("webview-mock.js");
        await SetupAutocompletionsMockAsync();
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await OpenDialogViaMenuAsync();

        var dialog = Page.Locator(DialogId);
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Click Cancel
        await dialog.Locator(CancelBtnId).ClickAsync();

        // Verify dialog is closed
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("Autocompletions")]
    public async Task CloseDialog_EscapeKey_ClosesDialog()
    {
        await GotoWithMockAsync("webview-mock.js");
        await SetupAutocompletionsMockAsync();
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await OpenDialogViaMenuAsync();

        var dialog = Page.Locator(DialogId);
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Press Escape
        await dialog.PressAsync("Escape");

        // Verify dialog is closed
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("Autocompletions")]
    public async Task SelectionView_LoadsModelsList()
    {
        await GotoWithMockAsync("webview-mock.js");
        await SetupAutocompletionsMockAsync();
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await OpenDialogViaMenuAsync();

        var dialog = Page.Locator(DialogId);
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Click Change to go to selection view
        await dialog.Locator(ChangeBtnId).ClickAsync();

        // Wait for models container to have model cards
        var modelsContainer = dialog.Locator("#autocompletions-models-container");
        await Expect(modelsContainer).ToBeVisibleAsync();

        // Wait for model cards to render (mock returns 2 models)
        await Page.WaitForFunctionAsync(
            @"() => document.querySelector('#autocompletions-models-container .model-card') !== null");

        // Verify models are shown
        var modelCards = modelsContainer.Locator(".model-card");
        await Expect(modelCards).ToHaveCountAsync(2);

        // Verify model names (models are sorted alphabetically by name in _getFilteredModels)
        await Expect(modelCards.Nth(0)).ToContainTextAsync("Another Model");
        await Expect(modelCards.Nth(1)).ToContainTextAsync("Test Model");
    }

    [Test]
    [Category("Autocompletions")]
    public async Task SelectionView_HasSearchFilterAndSortControls()
    {
        await GotoWithMockAsync("webview-mock.js");
        await SetupAutocompletionsMockAsync();
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await OpenDialogViaMenuAsync();

        var dialog = Page.Locator(DialogId);
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });

        await dialog.Locator(ChangeBtnId).ClickAsync();

        // Verify filter controls exist
        await Expect(dialog.Locator("#autocompletions-model-search")).ToHaveCountAsync(1);
        await Expect(dialog.Locator("#autocompletions-sort-btn")).ToHaveCountAsync(1);
        await Expect(dialog.Locator("#autocompletions-loaded-only")).ToHaveCountAsync(1);
        await Expect(dialog.Locator("#autocompletions-refresh-models")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("Autocompletions")]
    public async Task SaveButton_SavesConfiguration()
    {
        await GotoWithMockAsync("webview-mock.js");
        await SetupAutocompletionsMockAsync();
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Instrument the UpdateConfigAsync mock to capture the call
        await Page.EvaluateAsync(@"() => {
            window.__capturedUpdateConfig = null;
        }");

        await OpenDialogViaMenuAsync();

        var dialog = Page.Locator(DialogId);
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Toggle the enable checkbox off
        var checkbox = dialog.Locator("#autocompletions-dialog-enable-checkbox");
        if (await checkbox.IsCheckedAsync())
        {
            await checkbox.ClickAsync();
        }

        // Click Save
        await dialog.Locator(SaveBtnId).ClickAsync();

        // Wait for dialog to close
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });

        // Verify that the bridge was called with the config
        var captured = await Page.EvaluateAsync<string>("window.__capturedUpdateConfig || null");
        Assert.That(captured, Is.Not.Null, "UpdateConfigAsync should have been called");
        Assert.That(captured, Does.Contain("\"enabled\":false"));
    }
}
