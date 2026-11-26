namespace LMLocal.Tests.E2E.AppTests;

/// <summary>
/// The `collapsed` tool call display mode: the steps of one answer live in a group that is closed from the start and
/// is never opened by the code. Only the answer, moved out of the group when the stream ends, is visible while the
/// model works - and a Stop or an error titles the group instead of leaving "In Progress..." behind.
/// </summary>
[TestFixture]
public class ToolCallsCollapsedTests : AppTestBase
{
    private const string BlockSelector = "[data-element=\"collapsible-block\"]";
    private const string ContentSelector = "[data-element=\"collapsible-content\"]";

    private async Task StartCollapsedAnswerAsync()
    {
        await GotoWithMockAsync("webview-mock-tool-calls-collapsed.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Collapsed mode test");
        await Page.Locator("#mainBtn").ClickAsync();

        await Expect(Page.Locator(BlockSelector)).ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    [Test]
    [Category("Collapsible")]
    public async Task Group_StaysClosedFromTheStartWithAVisibleHeader()
    {
        await StartCollapsedAnswerAsync();

        await Expect(Page.Locator(".collapsible-header")).ToBeVisibleAsync(new() { Timeout = 2000 });
        await Expect(Page.Locator(ContentSelector)).ToBeHiddenAsync(new() { Timeout = 2000 });

        // Until the first tool of the answer runs, the header holds the idle title.
        var title = Page.Locator(".collapsible-title");
        await Expect(title).ToHaveTextAsync("In Progress...", new() { Timeout = 2000 });

        // Nothing opens the group by itself while the rounds arrive: no layout jump.
        var block = Page.Locator(BlockSelector);
        for (var i = 0; i < 5; i++)
        {
            var isExpanded = await block.EvaluateAsync<bool>("el => el.classList.contains('expanded')");
            Assert.That(isExpanded, Is.False, "The group must not open itself while the model works");
            await Page.WaitForTimeoutAsync(80);
        }

        // From the first tool call on, the header names what the tool does instead of the idle title.
        await Expect(title).ToContainTextAsync("Reading code", new() { Timeout = 5000 });

        await Expect(Page.Locator(ContentSelector)).ToBeHiddenAsync(new() { Timeout = 2000 });
    }

    [Test]
    [Category("Collapsible")]
    public async Task FinalAnswer_AppearsOutsideTheClosedGroup()
    {
        await StartCollapsedAnswerAsync();

        await Expect(Page.Locator("#mainBtn"))
            .ToHaveTextAsync("Send", new() { Timeout = 10000 });

        var finalResponse = Page.Locator("[data-element=\"final-response\"]");
        await Expect(finalResponse).ToBeVisibleAsync(new() { Timeout = 3000 });
        await Expect(finalResponse).ToContainTextAsync("final summary");

        // The answer is not a child of the group any more, so it is readable with the group closed.
        var insideGroup = await finalResponse.EvaluateAsync<bool>(
            "el => !!el.closest('[data-element=\"collapsible-content\"]')");
        Assert.That(insideGroup, Is.False, "The final answer must be moved out of the group");

        await Expect(Page.Locator(".collapsible-title")).ToContainTextAsync("Completed", new() { Timeout = 2000 });
        await Expect(Page.Locator(ContentSelector)).ToBeHiddenAsync(new() { Timeout = 2000 });

        var isExpanded = await Page.Locator(BlockSelector).EvaluateAsync<bool>("el => el.classList.contains('expanded')");
        Assert.That(isExpanded, Is.False, "The group is closed once the answer is complete");
    }

    [Test]
    [Category("Collapsible")]
    public async Task GroupOpenedByHand_StaysOpenThroughTheNextRound()
    {
        await StartCollapsedAnswerAsync();

        await Page.Locator(".toggle-collapsible-btn").ClickAsync();
        await Expect(Page.Locator(ContentSelector)).ToBeVisibleAsync(new() { Timeout = 2000 });

        // The next round must not close the group; polled per frame, since Expect's back-off can miss it until finalize.
        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.step').length >= 2", null, new() { Timeout = 5000 });

        var isExpanded = await Page.Locator(BlockSelector).EvaluateAsync<bool>("el => el.classList.contains('expanded')");
        Assert.That(isExpanded, Is.True, "A group opened by the user keeps its state through later rounds");
    }

    [Test]
    [Category("Collapsible")]
    public async Task Error_TitlesTheGroupAndKeepsItsState()
    {
        await StartCollapsedAnswerAsync();

        // Stop while the first round's tool card is in the closed group (attached, not visible).
        await Expect(Page.Locator(".tool-status, .tool-status-completed, .tool-status-error").First)
            .ToBeAttachedAsync(new() { Timeout = 5000 });

        await Page.EvaluateAsync(
            "() => window.__emitBridgeMessage({ Type: 'ChatSessionError', Payload: 'Test error occurred' })");

        // The step title of the round is replaced; without that the stopped answer would look unfinished.
        await Expect(Page.Locator(".collapsible-title"))
            .ToHaveTextAsync("Stopped after 1 step", new() { Timeout = 3000 });

        await Expect(Page.Locator("[data-element=\"final-response\"]"))
            .ToContainTextAsync("Test error occurred", new() { Timeout = 3000 });

        await Expect(Page.Locator(ContentSelector)).ToBeHiddenAsync(new() { Timeout = 2000 });

        var isExpanded = await Page.Locator(BlockSelector).EvaluateAsync<bool>("el => el.classList.contains('expanded')");
        Assert.That(isExpanded, Is.False, "A stop must not change the open/closed state of the group");
    }
}
