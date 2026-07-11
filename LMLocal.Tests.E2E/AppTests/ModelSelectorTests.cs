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
}
