namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public class ConfirmDialogTests : AppTestBase
{
    [Test]
    [Category("ConfirmDialog")]
    public async Task Open_ConfirmDialog_IsVisible()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Trigger a confirmation dialog (e.g., through a clear history action if available)
        // For this test, we simulate showing it via JavaScript
        await Page.EvaluateAsync("() => { const dialog = document.getElementById('confirm-dialog'); if (dialog) dialog.showModal(); }");

        var dialog = Page.Locator("#confirm-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Verify header and buttons exist
        await Expect(dialog.Locator(".modal-header")).ToHaveTextAsync("Confirmation");
        await Expect(dialog.Locator("#confirm-dialog-cancel")).ToHaveCountAsync(1);
        await Expect(dialog.Locator("#confirm-dialog-confirm")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("ConfirmDialog")]
    public async Task ConfirmDialog_HasCancelAndConfirmButtons()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Show confirm dialog
        await Page.EvaluateAsync("() => { const dialog = document.getElementById('confirm-dialog'); if (dialog) dialog.showModal(); }");

        var dialog = Page.Locator("#confirm-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Verify cancel button
        var cancelBtn = dialog.Locator("#confirm-dialog-cancel");
        await Expect(cancelBtn).ToHaveCountAsync(1);
        await Expect(cancelBtn).ToHaveTextAsync("Cancel");

        // Verify confirm button
        var confirmBtn = dialog.Locator("#confirm-dialog-confirm");
        await Expect(confirmBtn).ToHaveCountAsync(1);
        await Expect(confirmBtn).ToHaveTextAsync("Clear");
    }

    [Test]
    [Category("ConfirmDialog")]
    public async Task ConfirmDialog_HasModalBody()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Show confirm dialog
        await Page.EvaluateAsync("() => { const dialog = document.getElementById('confirm-dialog'); if (dialog) dialog.showModal(); }");

        var dialog = Page.Locator("#confirm-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Verify modal body exists
        var body = dialog.Locator(".modal-body");
        await Expect(body).ToHaveCountAsync(1);
    }
}

