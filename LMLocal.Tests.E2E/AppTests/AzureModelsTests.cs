namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public class AzureModelsTests : AppTestBase
{
    [Test]
    [Category("AzureModels")]
    public async Task ModelSelector_CanBeOpenedFromMenu()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open model selector from menu
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-model-selector']").ClickAsync();

        var dialog = Page.Locator("#model-selector-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Verify header
        await Expect(dialog.Locator("span").First).ToHaveTextAsync("Select model");
    }

    [Test]
    [Category("AzureModels")]
    public async Task ModelSelector_HasFilterInput()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open model selector
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-model-selector']").ClickAsync();

        var dialog = Page.Locator("#model-selector-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Verify filter input exists
        var filterInput = dialog.Locator("#model-filter-input");
        await Expect(filterInput).ToHaveCountAsync(1);
        await Expect(filterInput).ToHaveAttributeAsync("placeholder", "Filter models...");
    }

    [Test]
    [Category("AzureModels")]
    public async Task ModelSelector_HasRefreshButton()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open model selector
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-model-selector']").ClickAsync();

        var dialog = Page.Locator("#model-selector-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Verify refresh button exists
        var refreshBtn = dialog.Locator("#model-refresh-btn");
        await Expect(refreshBtn).ToHaveCountAsync(1);
    }

    [Test]
    [Category("AzureModels")]
    public async Task ModelSelector_HasModelsListContainer()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open model selector
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-model-selector']").ClickAsync();

        var dialog = Page.Locator("#model-selector-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Verify models list container exists
        var container = dialog.Locator("#models-list-container");
        await Expect(container).ToHaveCountAsync(1);
        await Expect(container).ToHaveClassAsync("models-grid");
    }

    [Test]
    [Category("AzureModels")]
    public async Task ModelSelector_HasCloseButton()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open model selector
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-model-selector']").ClickAsync();

        var dialog = Page.Locator("#model-selector-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Verify close button exists
        var closeBtn = dialog.Locator("#model-selector-close");
        await Expect(closeBtn).ToHaveCountAsync(1);
        await Expect(closeBtn).ToHaveTextAsync("Close");
    }
}
