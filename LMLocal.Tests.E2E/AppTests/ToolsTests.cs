namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public class ToolsTests : AppTestBase
{
    [Test]
    [Category("Tools")]
    public async Task Open_ToolsDialog_IsVisible()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open tools dialog from menu
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-tools']").ClickAsync();

        var dialog = Page.Locator("#tools-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        // Wait for tool cards to render
        await Page.WaitForFunctionAsync("() => document.querySelector('.tools-list-grid')?.children.length > 0");

        // Verify header and toolbar
        await Expect(dialog.Locator(".modal-header")).ToHaveTextAsync("Built-in Tools");
        await Expect(dialog.Locator("#tool-filter-input")).ToHaveCountAsync(1);
        await Expect(dialog.Locator("#tools-enable-all-btn")).ToHaveCountAsync(1);
        await Expect(dialog.Locator("#tools-disable-all-btn")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("Tools")]
    public async Task ToolsDialog_RendersToolCards()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-tools']").ClickAsync();

        var dialog = Page.Locator("#tools-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('.tools-list-grid')?.children.length > 0");

        // Verify at least one tool card exists
        var toolCards = dialog.Locator(".tool-item-card");
        await Expect(toolCards).ToHaveCountAsync(3);

        // Verify content of first card
        var firstCard = toolCards.First;
        await Expect(firstCard.Locator(".tool-title")).ToHaveTextAsync("read file");
        await Expect(firstCard.Locator(".tool-description")).ToHaveTextAsync("Read file contents");
    }

    [Test]
    [Category("Tools")]
    public async Task CloseDialog_CancelButton_ClosesDialog()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-tools']").ClickAsync();

        var dialog = Page.Locator("#tools-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('.tools-list-grid')?.children.length > 0");

        // Click Cancel
        await dialog.Locator("#tools-modal-close").ClickAsync();

        // Verify dialog is closed
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("Tools")]
    public async Task CloseDialog_EscapeKey_ClosesDialog()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-tools']").ClickAsync();

        var dialog = Page.Locator("#tools-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('.tools-list-grid')?.children.length > 0");

        // Press Escape
        await Page.Keyboard.PressAsync("Escape");

        // Verify dialog is closed
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("Tools")]
    public async Task CloseDialog_SaveButton_ClosesDialog()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-tools']").ClickAsync();

        var dialog = Page.Locator("#tools-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('.tools-list-grid')?.children.length > 0");

        // Click Save
        await dialog.Locator("#tools-modal-save").ClickAsync();

        // Verify dialog is closed
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });
    }
}
