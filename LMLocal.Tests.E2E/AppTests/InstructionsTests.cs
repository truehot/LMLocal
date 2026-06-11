namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public class InstructionsTests : AppTestBase
{
    [Test]
    [Category("Instructions")]
    public async Task Open_InstructionsDialog_IsVisible()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open instructions dialog from menu
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-instructions']").ClickAsync();
        // Wait for dialog to open
        await Task.Delay(200);

        var dialog = Page.Locator("#instructions-dialog");
        // Wait for dialog to be visible in DOM
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        // Wait for modal body to be populated
        await Page.WaitForFunctionAsync("() => document.querySelector('#instructions-dialog .modal-body')?.children.length > 0");

        // Verify header
        await Expect(dialog.Locator(".modal-header")).ToHaveTextAsync("AI Instructions");
    }

    [Test]
    [Category("Instructions")]
    public async Task InstructionsDialog_HasModalContainer()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open instructions dialog
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-instructions']").ClickAsync();
        // Wait for dialog to open
        await Task.Delay(200);

        var dialog = Page.Locator("#instructions-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        // Wait for modal body to be populated
        await Page.WaitForFunctionAsync("() => document.querySelector('#instructions-dialog .modal-body')?.children.length > 0");

        // Verify modal container exists (where content is populated)
        var container = dialog.Locator(".modal-container");
        await Expect(container).ToHaveCountAsync(1);
    }

    [Test]
    [Category("Instructions")]
    public async Task InstructionsDialog_HasButtons()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open instructions dialog
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-instructions']").ClickAsync();
        // Wait for dialog to open
        await Task.Delay(200);

        var dialog = Page.Locator("#instructions-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        // Wait for modal body to be populated
        await Page.WaitForFunctionAsync("() => document.querySelector('#instructions-dialog .modal-body')?.children.length > 0");

        // Verify cancel button exists
        var cancelBtn = dialog.Locator("#dialog-cancel");
        await Expect(cancelBtn).ToHaveCountAsync(1);
        await Expect(cancelBtn).ToHaveTextAsync("Cancel");

        // Verify save button exists
        var saveBtn = dialog.Locator("#dialog-confirm");
        await Expect(saveBtn).ToHaveCountAsync(1);
        await Expect(saveBtn).ToHaveTextAsync("Save");
    }

    [Test]
    [Category("Instructions")]
    public async Task InstructionsDialog_HasModalFooter()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open instructions dialog
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-instructions']").ClickAsync();
        // Wait for dialog to open
        await Task.Delay(200);

        var dialog = Page.Locator("#instructions-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        // Wait for modal body to be populated
        await Page.WaitForFunctionAsync("() => document.querySelector('#instructions-dialog .modal-body')?.children.length > 0");

        // Verify modal footer exists
        var footer = dialog.Locator(".modal-footer");
        await Expect(footer).ToHaveCountAsync(1);
    }
}

