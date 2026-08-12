namespace LMLocal.Tests.E2E.AppTests;

using System.Text.RegularExpressions;

[TestFixture]
public partial class ChatHistoryDialogTests : AppTestBase
{
    [GeneratedRegex(@"\bshow\b")]
    private static partial Regex ShowClassRegex();
    private static Regex ShowClass => ShowClassRegex();

    [GeneratedRegex(@"\bspinning\b")]
    private static partial Regex SpinningClassRegex();
    private static Regex SpinningClass => SpinningClassRegex();

    [Test]
    [Category("ChatHistory")]
    public async Task ChatHistory_DialogOpens_FromMenu()
    {
        await GotoWithMockAsync("webview-mock-chat-history.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open menu
        await Page.Locator("#menu-btn").ClickAsync();
        await Expect(Page.Locator("#dropdown-menu")).ToHaveClassAsync(ShowClass);

        // Click "Chat history..." action
        await Page.Locator("button[data-action=\"open-chat-history\"]").ClickAsync();

        // Dialog must be visible
        var dialog = Page.Locator("#chat-history-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 3000 });

        // Menu must close after action
        await Expect(Page.Locator("#dropdown-menu")).Not.ToHaveClassAsync(ShowClass);
    }

    [Test]
    [Category("ChatHistory")]
    public async Task ChatHistory_ShowsSessions_WhenSessionsExist()
    {
        await GotoWithMockAsync("webview-mock-chat-history.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action=\"open-chat-history\"]").ClickAsync();

        var dialog = Page.Locator("#chat-history-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 3000 });

        // Wait for loading to complete — cards appear
        var cards = Page.Locator("#chat-history-container .chat-history-card");
        await Expect(cards).ToHaveCountAsync(3, new() { Timeout = 3000 });

        // Verify first card content
        var firstCard = cards.First;
        await Expect(firstCard.Locator(".chat-history-prompt"))
            .ToContainTextAsync("How do I refactor this class to use dependency injection?");

        // Each card must have a "Load" button
        var loadButtons = Page.Locator("#chat-history-container .chat-history-load");
        await Expect(loadButtons).ToHaveCountAsync(3);
    }

    [Test]
    [Category("ChatHistory")]
    public async Task ChatHistory_Close_ClosesDialog()
    {
        await GotoWithMockAsync("webview-mock-chat-history.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action=\"open-chat-history\"]").ClickAsync();

        var dialog = Page.Locator("#chat-history-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 3000 });

        await Page.Locator("#chat-history-close").ClickAsync();

        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("ChatHistory")]
    public async Task ChatHistory_Filter_FiltersSessions()
    {
        await GotoWithMockAsync("webview-mock-chat-history.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action=\"open-chat-history\"]").ClickAsync();

        var dialog = Page.Locator("#chat-history-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 3000 });

        var cards = Page.Locator("#chat-history-container .chat-history-card");
        await Expect(cards).ToHaveCountAsync(3, new() { Timeout = 3000 });

        // Type filter that matches only one session
        await Page.Locator("#chat-history-filter-input").FillAsync("CS1061");

        // Only one card should remain
        await Expect(cards).ToHaveCountAsync(1, new() { Timeout = 3000 });
        await Expect(cards.First.Locator(".chat-history-prompt"))
            .ToContainTextAsync("CS1061");

        // Clear filter — all 3 should return
        await Page.Locator("#chat-history-filter-input").FillAsync("");
        await Expect(cards).ToHaveCountAsync(3, new() { Timeout = 3000 });
    }

    [Test]
    [Category("ChatHistory")]
    public async Task ChatHistory_Filter_NoMatch_ShowsEmptyState()
    {
        await GotoWithMockAsync("webview-mock-chat-history.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action=\"open-chat-history\"]").ClickAsync();

        var dialog = Page.Locator("#chat-history-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 3000 });

        var cards = Page.Locator("#chat-history-container .chat-history-card");
        await Expect(cards).ToHaveCountAsync(3, new() { Timeout = 3000 });

        // Type filter that matches nothing
        await Page.Locator("#chat-history-filter-input").FillAsync("ZZZ_NONEXISTENT_ZZZ");

        // Cards disappear, empty placeholder visible
        await Expect(cards).ToHaveCountAsync(0, new() { Timeout = 3000 });
        await Expect(Page.Locator("#chat-history-container .empty-placeholder"))
            .ToBeVisibleAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("ChatHistory")]
    public async Task ChatHistory_LoadSession_TriggersLoadAndClosesDialog()
    {
        await GotoWithMockAsync("webview-mock-chat-history.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action=\"open-chat-history\"]").ClickAsync();

        var dialog = Page.Locator("#chat-history-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 3000 });

        var cards = Page.Locator("#chat-history-container .chat-history-card");
        await Expect(cards).ToHaveCountAsync(3, new() { Timeout = 3000 });

        // Click "Load" on the first card (session-aaa-111)
        await cards.First.Locator(".chat-history-load").ClickAsync();

        // Dialog must close after successful load
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 5000 });

        // Messages must be rendered in chat container
        await Expect(Page.Locator("#chat-container > *")).Not.ToHaveCountAsync(0, new() { Timeout = 3000 });
    }

    [Test]
    [Category("ChatHistory")]
    public async Task ChatHistory_Refresh_ReloadsSessions()
    {
        await GotoWithMockAsync("webview-mock-chat-history.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action=\"open-chat-history\"]").ClickAsync();

        var dialog = Page.Locator("#chat-history-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 3000 });

        var cards = Page.Locator("#chat-history-container .chat-history-card");
        await Expect(cards).ToHaveCountAsync(3, new() { Timeout = 3000 });

        // Click refresh — spinner should appear then disappear
        var refreshBtn = Page.Locator("#chat-history-refresh-btn");
        await refreshBtn.ClickAsync();

        // After refresh (same mock data), cards should still be 3
        await Expect(cards).ToHaveCountAsync(3, new() { Timeout = 3000 });

        // Spinner should not persist (class removed after reload)
        await Expect(refreshBtn).Not.ToHaveClassAsync(SpinningClass, new() { Timeout = 3000 });
    }

    [Test]
    [Category("ChatHistory")]
    public async Task ChatHistory_Reopen_ResetsFilter()
    {
        await GotoWithMockAsync("webview-mock-chat-history.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open chat history
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action=\"open-chat-history\"]").ClickAsync();

        var dialog = Page.Locator("#chat-history-dialog");
        await Expect(dialog).ToBeVisibleAsync();
        var cards = Page.Locator("#chat-history-container .chat-history-card");
        await Expect(cards).ToHaveCountAsync(3);

        // Apply filter → 1 session remains
        await Page.Locator("#chat-history-filter-input").FillAsync("CS1061");
        await Expect(cards).ToHaveCountAsync(1);

        // Close
        await Page.Locator("#chat-history-close").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();

        // Reopen — filter must be reset, all sessions shown again
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action=\"open-chat-history\"]").ClickAsync();
        await Expect(dialog).ToBeVisibleAsync();

        await Expect(Page.Locator("#chat-history-filter-input")).ToHaveValueAsync("");
        await Expect(cards).ToHaveCountAsync(3);
    }

    [Test]
    [Category("ChatHistory")]
    public async Task ChatHistory_Refresh_PreservesFilter()
    {
        await GotoWithMockAsync("webview-mock-chat-history.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action=\"open-chat-history\"]").ClickAsync();

        var dialog = Page.Locator("#chat-history-dialog");
        await Expect(dialog).ToBeVisibleAsync();
        var cards = Page.Locator("#chat-history-container .chat-history-card");
        await Expect(cards).ToHaveCountAsync(3);

        // Apply filter → 1 session remains
        await Page.Locator("#chat-history-filter-input").FillAsync("CS1061");
        await Expect(cards).ToHaveCountAsync(1);

        // Refresh
        var refreshBtn = Page.Locator("#chat-history-refresh-btn");
        await refreshBtn.ClickAsync();
        await Expect(refreshBtn).Not.ToHaveClassAsync(SpinningClass, new() { Timeout = 3000 });

        // Filter must be preserved after refresh → still 1 session
        await Expect(Page.Locator("#chat-history-filter-input")).ToHaveValueAsync("CS1061");
        await Expect(cards).ToHaveCountAsync(1);
    }

    [Test]
    [Category("ChatHistory")]
    public async Task ChatHistory_Reopen_ResetsSort()
    {
        await GotoWithMockAsync("webview-mock-chat-history.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action=\"open-chat-history\"]").ClickAsync();

        var dialog = Page.Locator("#chat-history-dialog");
        await Expect(dialog).ToBeVisibleAsync();
        var cards = Page.Locator("#chat-history-container .chat-history-card");
        await Expect(cards).ToHaveCountAsync(3);

        // Default sort: newest first → latest session (session-aaa-111) is first
        await Expect(cards.First.Locator(".chat-history-prompt"))
            .ToContainTextAsync("How do I refactor this class to use dependency injection?");

        // Toggle sort → oldest first → earliest session (session-ccc-333) is first
        await Page.Locator("#chat-history-sort-btn").ClickAsync();
        await Expect(cards.First.Locator(".chat-history-prompt"))
            .ToContainTextAsync("Generate unit tests for the OrderService class");

        // Close
        await Page.Locator("#chat-history-close").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();

        // Reopen — sort must be reset to newest first
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action=\"open-chat-history\"]").ClickAsync();
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(cards).ToHaveCountAsync(3);

        await Expect(cards.First.Locator(".chat-history-prompt"))
            .ToContainTextAsync("How do I refactor this class to use dependency injection?");
    }
}
