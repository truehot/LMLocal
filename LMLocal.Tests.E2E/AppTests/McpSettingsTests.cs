namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public class McpSettingsTests : AppTestBase
{
    [Test]
    [Category("McpSettings")]
    public async Task Open_McpSettingsDialog_IsVisible()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open MCP settings dialog from menu
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='mcp-settings']").ClickAsync();

        var dialog = Page.Locator("#mcp-settings-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Verify header exists
        await Expect(dialog.Locator(".modal-header")).ToHaveTextAsync("MCP Extensions");
    }

    [Test]
    [Category("McpSettings")]
    public async Task McpSettingsDialog_HasRequiredElements()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open MCP settings dialog
        await Page.EvaluateAsync("() => { const dialog = document.getElementById('mcp-settings-dialog'); if (dialog) dialog.showModal(); }");

        var dialog = Page.Locator("#mcp-settings-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Verify cancel button exists
        var cancelBtn = dialog.Locator("#mcp-dialog-cancel");
        await Expect(cancelBtn).ToHaveCountAsync(1);
        await Expect(cancelBtn).ToHaveTextAsync("Cancel");
    }

    [Test]
    [Category("McpSettings")]
    public async Task McpSettingsDialog_HasSaveButton()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open MCP settings dialog
        await Page.EvaluateAsync("() => { const dialog = document.getElementById('mcp-settings-dialog'); if (dialog) dialog.showModal(); }");

        var dialog = Page.Locator("#mcp-settings-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Verify save button exists
        var saveBtn = dialog.Locator("#mcp-dialog-confirm");
        await Expect(saveBtn).ToHaveCountAsync(1);
        await Expect(saveBtn).ToHaveTextAsync("Save");
    }

    [Test]
    [Category("McpSettings")]
    public async Task CloseDialog_CancelButton_ClosesDialog()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open MCP settings dialog via app controller
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='mcp-settings']").ClickAsync();

        var dialog = Page.Locator("#mcp-settings-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Click Cancel
        await dialog.Locator("#mcp-dialog-cancel").ClickAsync();

        // Verify dialog is closed
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("McpSettings")]
    public async Task CloseDialog_EscapeKey_ClosesDialog()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open MCP settings dialog via app controller
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='mcp-settings']").ClickAsync();

        var dialog = Page.Locator("#mcp-settings-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Press Escape
        await Page.Keyboard.PressAsync("Escape");

        // Verify dialog is closed
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("McpSettings")]
    public async Task CloseDialog_SaveButton_ClosesDialog()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open MCP settings dialog via app controller
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='mcp-settings']").ClickAsync();

        var dialog = Page.Locator("#mcp-settings-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Click Save
        await dialog.Locator("#mcp-dialog-confirm").ClickAsync();

        // Verify dialog is closed
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });
    }
}
