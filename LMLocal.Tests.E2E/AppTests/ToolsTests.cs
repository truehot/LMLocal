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

    [Test]
    [Category("Tools")]
    public async Task ToolsDialog_Filter_FiltersTools()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-tools']").ClickAsync();

        var dialog = Page.Locator("#tools-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('.tools-list-grid')?.children.length > 0");

        var cards = dialog.Locator(".tool-item-card");
        await Expect(cards).ToHaveCountAsync(3);

        // By name only: "read_file" (with underscore) is present only in the tool name,
        // not in the description ("Read file contents") — proves name matching.
        await Page.Locator("#tool-filter-input").FillAsync("read_file");
        await Expect(cards).ToHaveCountAsync(1);
        await Expect(cards.First.Locator(".tool-title")).ToHaveTextAsync("read file");

        // By description only: "contents" appears only in descriptions
        // ("Read file contents", "Write file contents"), never in names — proves description matching.
        await Page.Locator("#tool-filter-input").FillAsync("contents");
        await Expect(cards).ToHaveCountAsync(2);

        // No match -> empty list
        await Page.Locator("#tool-filter-input").FillAsync("zzz");
        await Expect(cards).ToHaveCountAsync(0);

        // Clear -> all back
        await Page.Locator("#tool-filter-input").FillAsync("");
        await Expect(cards).ToHaveCountAsync(3);
    }

    [Test]
    [Category("Tools")]
    public async Task ToolsDialog_Sort_CyclesStates()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-tools']").ClickAsync();

        var dialog = Page.Locator("#tools-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('.tools-list-grid')?.children.length > 0");

        var cards = dialog.Locator(".tool-item-card");
        await Expect(cards).ToHaveCountAsync(3);

        // Initial state: 'asc' (dialog opens sorted ascending by name)
        // order: read file, search files, write file
        await Expect(cards.Nth(0).Locator(".tool-title")).ToHaveTextAsync("read file");
        await Expect(cards.Nth(1).Locator(".tool-title")).ToHaveTextAsync("search files");
        await Expect(cards.Nth(2).Locator(".tool-title")).ToHaveTextAsync("write file");

        // Click -> desc by name: write file, search files, read file
        await Page.Locator("#tools-sort-btn").ClickAsync();
        await Expect(cards.Nth(0).Locator(".tool-title")).ToHaveTextAsync("write file");
        await Expect(cards.Nth(1).Locator(".tool-title")).ToHaveTextAsync("search files");
        await Expect(cards.Nth(2).Locator(".tool-title")).ToHaveTextAsync("read file");

        // Click -> null (no sort): backend order read file, write file, search files
        await Page.Locator("#tools-sort-btn").ClickAsync();
        await Expect(cards.Nth(0).Locator(".tool-title")).ToHaveTextAsync("read file");
        await Expect(cards.Nth(1).Locator(".tool-title")).ToHaveTextAsync("write file");
        await Expect(cards.Nth(2).Locator(".tool-title")).ToHaveTextAsync("search files");

        // Click -> back to asc
        await Page.Locator("#tools-sort-btn").ClickAsync();
        await Expect(cards.Nth(0).Locator(".tool-title")).ToHaveTextAsync("read file");
        await Expect(cards.Nth(1).Locator(".tool-title")).ToHaveTextAsync("search files");
        await Expect(cards.Nth(2).Locator(".tool-title")).ToHaveTextAsync("write file");
    }

    [Test]
    [Category("Tools")]
    public async Task ToolsDialog_EnableDisableAll_TogglesAllSwitches()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-tools']").ClickAsync();

        var dialog = Page.Locator("#tools-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('.tools-list-grid')?.children.length > 0");

        var cards = dialog.Locator(".tool-item-card");
        await Expect(cards).ToHaveCountAsync(3);

        // Initial state: tool-1 on, tool-2 off, tool-3 on
        await Expect(dialog.Locator(".tool-item-card[data-tool-id='tool-1'] input[type=checkbox]")).ToBeCheckedAsync();
        await Expect(dialog.Locator(".tool-item-card[data-tool-id='tool-2'] input[type=checkbox]")).Not.ToBeCheckedAsync();
        await Expect(dialog.Locator(".tool-item-card[data-tool-id='tool-3'] input[type=checkbox]")).ToBeCheckedAsync();

        // Disable All -> all off
        await Page.Locator("#tools-disable-all-btn").ClickAsync();
        await Expect(dialog.Locator(".tool-item-card[data-tool-id='tool-1'] input[type=checkbox]")).Not.ToBeCheckedAsync();
        await Expect(dialog.Locator(".tool-item-card[data-tool-id='tool-2'] input[type=checkbox]")).Not.ToBeCheckedAsync();
        await Expect(dialog.Locator(".tool-item-card[data-tool-id='tool-3'] input[type=checkbox]")).Not.ToBeCheckedAsync();

        // Enable All -> all on
        await Page.Locator("#tools-enable-all-btn").ClickAsync();
        await Expect(dialog.Locator(".tool-item-card[data-tool-id='tool-1'] input[type=checkbox]")).ToBeCheckedAsync();
        await Expect(dialog.Locator(".tool-item-card[data-tool-id='tool-2'] input[type=checkbox]")).ToBeCheckedAsync();
        await Expect(dialog.Locator(".tool-item-card[data-tool-id='tool-3'] input[type=checkbox]")).ToBeCheckedAsync();
    }

    [Test]
    [Category("Tools")]
    public async Task ToolsDialog_Reopen_ResetsFilterAndSort()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-tools']").ClickAsync();

        var dialog = Page.Locator("#tools-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('.tools-list-grid')?.children.length > 0");

        // Filter "contents" matches read_file + write_file descriptions only -> 2 cards
        await Page.Locator("#tool-filter-input").FillAsync("contents");
        await Expect(dialog.Locator(".tool-item-card")).ToHaveCountAsync(2);

        // Switch sort to 'desc' (write file before read file) — must be reset on reopen
        await Page.Locator("#tools-sort-btn").ClickAsync();
        await Expect(dialog.Locator(".tool-item-card").Nth(0).Locator(".tool-title")).ToHaveTextAsync("write file");

        // Close via Cancel
        await dialog.Locator("#tools-modal-close").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });

        // Reopen — filter and sort must be reset: all tools shown in 'asc' order
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-tools']").ClickAsync();
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('.tools-list-grid')?.children.length > 0");

        await Expect(Page.Locator("#tool-filter-input")).ToHaveValueAsync("");
        await Expect(dialog.Locator(".tool-item-card")).ToHaveCountAsync(3);

        // Sort reset to 'asc' by name: read file, search files, write file
        await Expect(dialog.Locator(".tool-item-card").Nth(0).Locator(".tool-title")).ToHaveTextAsync("read file");
        await Expect(dialog.Locator(".tool-item-card").Nth(1).Locator(".tool-title")).ToHaveTextAsync("search files");
        await Expect(dialog.Locator(".tool-item-card").Nth(2).Locator(".tool-title")).ToHaveTextAsync("write file");
    }
    [Test]
    [Category("Tools")]
    public async Task ToolsDialog_SaveError_KeepsDialogOpenAndShowsToast()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-tools']").ClickAsync();

        var dialog = Page.Locator("#tools-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('.tools-list-grid')?.children.length > 0");

        // Make UpdateToolsAsync fail
        await Page.EvaluateAsync(@"() => {
            window.__toolsOverride.UpdateToolsAsync = async (json) => { throw new Error('tools save boom'); };
        }");

        // Toggle tool-1 off (starts enabled) — the local change must survive the failed save.
        // The native checkbox is visually hidden; click the visible .switch-slider inside its <label>.
        var tool1Card = dialog.Locator(".tool-item-card[data-tool-id='tool-1']");
        var tool1Checkbox = tool1Card.Locator("input[type=checkbox]");
        await Expect(tool1Checkbox).ToBeCheckedAsync();
        await tool1Card.Locator(".switch-slider").ClickAsync();
        await Expect(tool1Checkbox).Not.ToBeCheckedAsync();

        // Save -> fails -> dialog stays open, toast shows the reason
        await dialog.Locator("#tools-modal-save").ClickAsync();

        await Expect(dialog).ToBeVisibleAsync();
        var toast = Page.Locator("#app-toast.show");
        await Expect(toast).ToBeVisibleAsync();
        await Expect(toast).ToContainTextAsync("tools save boom");

        // Local change is preserved for retry
        await Expect(tool1Checkbox).Not.ToBeCheckedAsync();

        // Now succeed and save again -> dialog closes
        await Page.EvaluateAsync(@"() => {
            window.__toolsOverride.UpdateToolsAsync = async (json) => true;
        }");
        await dialog.Locator("#tools-modal-save").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });
    }
}

