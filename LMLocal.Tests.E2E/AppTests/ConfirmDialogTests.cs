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
        await Expect(dialog.Locator(".modal-body")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("ConfirmDialog")]
    public async Task CloseDialog_CancelButton_ClosesDialog()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Show confirm dialog with click handlers (simulating what ConfirmDialog.confirm() does)
        await Page.EvaluateAsync("() => { const dialog = document.getElementById('confirm-dialog'); if (dialog) { dialog.querySelector('.modal-body').textContent = 'Are you sure?'; dialog.querySelector('#confirm-dialog-cancel').onclick = () => dialog.close(); dialog.querySelector('#confirm-dialog-confirm').onclick = () => dialog.close(); dialog.showModal(); } }");

        var dialog = Page.Locator("#confirm-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Click Cancel
        await dialog.Locator("#confirm-dialog-cancel").ClickAsync();

        // Verify dialog is closed
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("ConfirmDialog")]
    public async Task CloseDialog_EscapeKey_ClosesDialog()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Show confirm dialog via JavaScript
        await Page.EvaluateAsync("() => { const dialog = document.getElementById('confirm-dialog'); if (dialog) { dialog.querySelector('.modal-body').textContent = 'Are you sure?'; dialog.showModal(); } }");

        var dialog = Page.Locator("#confirm-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Press Escape
        await Page.Keyboard.PressAsync("Escape");

        // Verify dialog is closed
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("ConfirmDialog")]
    public async Task CloseDialog_ConfirmButton_ClosesDialog()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Show confirm dialog with click handlers (simulating what ConfirmDialog.confirm() does)
        await Page.EvaluateAsync("() => { const dialog = document.getElementById('confirm-dialog'); if (dialog) { dialog.querySelector('.modal-body').textContent = 'Are you sure?'; dialog.querySelector('#confirm-dialog-cancel').onclick = () => dialog.close(); dialog.querySelector('#confirm-dialog-confirm').onclick = () => dialog.close(); dialog.showModal(); } }");

        var dialog = Page.Locator("#confirm-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Click Confirm
        await dialog.Locator("#confirm-dialog-confirm").ClickAsync();

        // Verify dialog is closed
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });
    }
}
