namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public class ProvidersTests : AppTestBase
{
    [Test]
    [Category("Providers")]
    public async Task Open_ProvidersDialog_IsVisible()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open providers dialog from menu
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-providers']").ClickAsync();

        var dialog = Page.Locator("#providers-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Verify header and content
        await Expect(dialog.Locator(".modal-header")).ToHaveTextAsync("Configure providers");
        await Expect(dialog.Locator("#provider-add-btn")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("Providers")]
    public async Task ProvidersDialog_HasAddProfileButton()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open providers dialog
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-providers']").ClickAsync();

        var dialog = Page.Locator("#providers-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Verify "Add Profile" button exists and is visible
        var addBtn = dialog.Locator("#provider-add-btn");
        await Expect(addBtn).ToHaveCountAsync(1);
        await Expect(addBtn).ToHaveTextAsync("+ Add Profile");
    }

    [Test]
    [Category("Providers")]
    public async Task ProvidersDialog_HasCancelAndSaveButtons()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open providers dialog
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-providers']").ClickAsync();

        var dialog = Page.Locator("#providers-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Verify cancel button
        var cancelBtn = dialog.Locator("#providers-modal-cancel");
        await Expect(cancelBtn).ToHaveCountAsync(1);
        await Expect(cancelBtn).ToHaveTextAsync("Cancel");

        // Verify save button
        var saveBtn = dialog.Locator("#providers-modal-confirm");
        await Expect(saveBtn).ToHaveCountAsync(1);
        await Expect(saveBtn).ToHaveTextAsync("Save Changes");
    }
}
