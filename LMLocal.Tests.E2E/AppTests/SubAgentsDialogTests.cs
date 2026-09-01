namespace LMLocal.Tests.E2E.AppTests;
using System.Text.RegularExpressions;

[TestFixture]
public partial class SubAgentsDialogTests : AppTestBase
{
    [GeneratedRegex(@"\btool-disabled\b")]
    private static partial Regex ToolDisabledClassRegex();

    private async Task OpenDialogAsync()
    {
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-subagents']").ClickAsync();
        await Expect(Page.Locator("#subagents-dialog")).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#subagents-list-container')?.children.length > 0");
    }

    private async Task InstrumentUpdateSubAgentsAsync()
    {
        await Page.EvaluateAsync(
            "() => { window.__capturedSubAgentsPayload = null; " +
            "if (window.__subAgentsOverride) { " +
            "  const orig = window.__subAgentsOverride.UpdateSubAgentsAsync; " +
            "  window.__subAgentsOverride.UpdateSubAgentsAsync = async (json) => { window.__capturedSubAgentsPayload = json; return orig(json); }; " +
            "} }");
    }

    private async Task<string?> GetCapturedSubAgentsPayloadAsync()
    {
        return await Page.EvaluateAsync<string?>("() => window.__capturedSubAgentsPayload ?? null");
    }

    [Test]
    [Category("SubAgents")]
    public async Task SubAgentsDialog_OpensFromMenu_ShowsHeaderAndToolbar()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await OpenDialogAsync();

        var dialog = Page.Locator("#subagents-dialog");
        await Expect(dialog.Locator(".modal-header")).ToHaveTextAsync("Sub Agents");
        await Expect(dialog.Locator("#subagents-filter-input")).ToHaveCountAsync(1);
        await Expect(dialog.Locator("#subagents-enable-all-btn")).ToHaveCountAsync(1);
        await Expect(dialog.Locator("#subagents-disable-all-btn")).ToHaveCountAsync(1);
        await Expect(dialog.Locator("#subagents-modal-save")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("SubAgents")]
    public async Task SubAgentsDialog_RendersAgentCardsWithDetails()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await OpenDialogAsync();

        var cards = Page.Locator("#subagents-dialog .tool-item-card");
        await Expect(cards).ToHaveCountAsync(2);

        // First card: researcher (title uses displayName)
        var first = cards.Nth(0);
        await Expect(first.Locator(".tool-title")).ToHaveTextAsync("Researcher");
        await Expect(first.Locator(".tool-description")).ToHaveTextAsync("Research agent");
        await Expect(first.Locator(".subagents-details")).ToContainTextAsync("deepseek-chat");
        await Expect(first.Locator(".subagents-details")).ToContainTextAsync("deepseek");
        await Expect(first.Locator(".subagents-details")).ToContainTextAsync("https://api.deepseek.com");
        await Expect(first.Locator(".subagents-params")).ToContainTextAsync("temp 0.3");
        await Expect(first.Locator(".subagents-params")).ToContainTextAsync("timeout 90s");
        await Expect(first.Locator(".subagents-chips .chip")).ToHaveCountAsync(2);

        // Second card: coder (disabled -> tool-disabled class, empty tools -> muted chip)
        var second = cards.Nth(1);
        await Expect(second).ToHaveClassAsync(ToolDisabledClassRegex());
        await Expect(second.Locator(".tool-title")).ToHaveTextAsync("Coder");
        await Expect(second.Locator(".subagents-chips .chip.chip-muted")).ToHaveTextAsync("no tools");

        // Switch states reflect the enabled flags
        await Expect(first.Locator("input[type=checkbox]")).ToBeCheckedAsync();
        await Expect(second.Locator("input[type=checkbox]")).Not.ToBeCheckedAsync();
    }

    [Test]
    [Category("SubAgents")]
    public async Task SubAgentsDialog_ToggleAndSave_SendsEnabledFlags()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await OpenDialogAsync();
        await InstrumentUpdateSubAgentsAsync();

        var cards = Page.Locator("#subagents-dialog .tool-item-card");
        // Toggle both: researcher off, coder on. The list is re-rendered on each toggle,
        // so click the visible switch slider (the checkbox itself is hidden/zero-sized).
        await cards.Nth(0).Locator(".switch-slider").ClickAsync(new() { Force = true });
        await cards.Nth(1).Locator(".switch-slider").ClickAsync(new() { Force = true });

        await Page.Locator("#subagents-modal-save").ClickAsync();

        var payload = await GetCapturedSubAgentsPayloadAsync();
        Assert.That(payload, Is.EqualTo(
            "{\"agents\":[{\"id\":\"researcher\",\"enabled\":false},{\"id\":\"coder\",\"enabled\":true}]}"));

        // Dialog closes after a successful save
        await Expect(Page.Locator("#subagents-dialog")).Not.ToBeVisibleAsync();
    }

    [Test]
    [Category("SubAgents")]
    public async Task SubAgentsDialog_Cancel_DoesNotSendPayload()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await OpenDialogAsync();
        await InstrumentUpdateSubAgentsAsync();

        await Page.Locator("#subagents-modal-close").ClickAsync();

        var payload = await GetCapturedSubAgentsPayloadAsync();
        Assert.That(payload, Is.Null);
        await Expect(Page.Locator("#subagents-dialog")).Not.ToBeVisibleAsync();
    }

    [Test]
    [Category("SubAgents")]
    public async Task SubAgentsDialog_EnableDisableAll_TogglesAllSwitches()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await OpenDialogAsync();

        // Disable all
        await Page.Locator("#subagents-disable-all-btn").ClickAsync();
        var cards = Page.Locator("#subagents-dialog .tool-item-card");
        await Expect(cards.Nth(0).Locator("input[type=checkbox]")).Not.ToBeCheckedAsync();
        await Expect(cards.Nth(1).Locator("input[type=checkbox]")).Not.ToBeCheckedAsync();

        // Enable all
        await Page.Locator("#subagents-enable-all-btn").ClickAsync();
        await Expect(cards.Nth(0).Locator("input[type=checkbox]")).ToBeCheckedAsync();
        await Expect(cards.Nth(1).Locator("input[type=checkbox]")).ToBeCheckedAsync();
    }

    [Test]
    [Category("SubAgents")]
    public async Task SubAgentsDialog_LoadError_ShowsErrorMessage()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Override GetSubAgentsAsync to fail before opening the dialog.
        await Page.EvaluateAsync(
            "() => { window.__subAgentsOverride.GetSubAgentsAsync = async () => JSON.stringify({ success: false, error: { message: 'boom: missing subagents.json' } }); }");

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-subagents']").ClickAsync();

        var dialog = Page.Locator("#subagents-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Expect(dialog.Locator(".loading-placeholder")).ToContainTextAsync("missing subagents.json");
        // No cards rendered on error
        await Expect(dialog.Locator(".tool-item-card")).ToHaveCountAsync(0);
    }
}
