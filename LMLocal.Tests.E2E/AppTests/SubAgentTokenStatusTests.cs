namespace LMLocal.Tests.E2E.AppTests;

/// <summary>
/// The live token usage of running SubAgents in the status bar: the elapsed time of the request, then their summed
/// numbers instead of the numbers of the main model (which produces nothing while tools run and would look frozen).
/// The counter never repeats the agent names (the progress header does), never rewrites the step line of the tool card,
/// and leaves the context bar of the main chat alone.
/// </summary>
[TestFixture]
public partial class SubAgentTokenStatusTests : AppTestBase
{
    [GeneratedRegex("Context usage")]
    private static partial Regex ContextUsageRegex();

    [GeneratedRegex(@"^\d+ tokens$")]
    private static partial Regex TokenCountOnlyRegex();

    [GeneratedRegex(@"^\d+[smh]")]
    private static partial Regex ElapsedTimeRegex();

    private const string LiveCount = "#live-token-count";
    private const string ElapsedTime = "#elapsed-time";
    private const string TokenNumber = "#token-number";
    private const string TokenSpeed = "#tokens-speed";
    private const string InfoBar = "#info-tooltip";

    private async Task StartAnswerAsync()
    {
        await GotoWithMockAsync("webview-mock-subagent-tokens.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("SubAgent tokens");
        await Page.Locator("#mainBtn").ClickAsync();
    }

    [Test]
    [Category("Status")]
    public async Task BuiltInTool_KeepsTheCounterWithTheTimeOnly()
    {
        await StartAnswerAsync();

        await Expect(Page.Locator(".tool-status").First)
            .ToContainTextAsync("Searching for", new() { Timeout = 5000 });

        // A built-in tool produces no tokens: the counter stays visible with the time and hides the numbers of the
        // previous main stream, which would otherwise look frozen.
        await Expect(Page.Locator(LiveCount)).ToBeVisibleAsync(new() { Timeout = 2000 });
        await Expect(Page.Locator(ElapsedTime)).ToHaveTextAsync(ElapsedTimeRegex(), new() { Timeout = 2000 });
        await Expect(Page.Locator(TokenNumber)).ToBeHiddenAsync();
        await Expect(Page.Locator(TokenSpeed)).ToBeHiddenAsync();
    }

    [Test]
    [Category("Status")]
    public async Task OneAgent_ShowsOnlyItsNumbers()
    {
        await StartAnswerAsync();

        await Expect(Page.Locator(TokenNumber)).ToContainTextAsync("1520 tokens", new() { Timeout = 6000 });
        await Expect(Page.Locator(TokenSpeed)).ToContainTextAsync("(95.3 t/s)", new() { Timeout = 6000 });

        // Numbers only: the names belong to the progress header, not to this text.
        await Expect(Page.Locator(TokenNumber)).ToHaveTextAsync(TokenCountOnlyRegex(), new() { Timeout = 2000 });
        await Expect(Page.Locator(ElapsedTime)).ToHaveTextAsync(ElapsedTimeRegex(), new() { Timeout = 2000 });

        // The time and the numbers are split by a CSS separator, so it is not part of the text: `42s · 1520 tokens`.
        var separator = await Page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('#token-number'), '::before').content");
        Assert.That(separator, Does.Contain("·"), $"The counter must separate the time from the numbers, got: {separator}");
    }

    [Test]
    [Category("Status")]
    public async Task ParallelAgents_AreSummedInOneLine()
    {
        await StartAnswerAsync();

        await Expect(Page.Locator(TokenNumber)).ToContainTextAsync("2000 tokens", new() { Timeout = 6000 });
        await Expect(Page.Locator(TokenSpeed)).ToContainTextAsync("(140.0 t/s)", new() { Timeout = 6000 });

        // Summed numbers, still without an agent count.
        await Expect(Page.Locator(TokenNumber)).ToHaveTextAsync(TokenCountOnlyRegex(), new() { Timeout = 2000 });
    }

    [Test]
    [Category("Status")]
    public async Task TokenEvent_DoesNotTouchTheStepLineOfTheCard()
    {
        await StartAnswerAsync();

        var stepLine = Page.Locator("[data-tool-call-id='call_agent_001'] .tool-status-step");
        await Expect(stepLine).ToContainTextAsync("search_solution_knowledge", new() { Timeout = 6000 });

        // The tokens of the same call arrived after that step: they must not have been appended to the step line.
        await Expect(Page.Locator(TokenNumber)).ToContainTextAsync("2000 tokens", new() { Timeout = 8000 });

        var text = await stepLine.TextContentAsync() ?? "";
        Assert.That(text, Does.Not.Contain("tokens"),
            $"A token event must not rewrite the step line, got: {text}");
    }

    [Test]
    [Category("Status")]
    public async Task AfterTheAgentsEnd_TheNumbersOfTheMainModelComeBack()
    {
        await StartAnswerAsync();

        await Expect(Page.Locator(TokenNumber)).ToContainTextAsync("1520 tokens", new() { Timeout = 6000 });

        // The agents ended and the main model streams again: its own numbers replace the SubAgent ones.
        await Expect(Page.Locator(TokenNumber)).ToHaveTextAsync("40 tokens", new() { Timeout = 10000 });
        await Expect(Page.Locator(TokenSpeed)).ToContainTextAsync("(20.0 t/s)", new() { Timeout = 10000 });
    }

    [Test]
    [Category("Status")]
    public async Task TheTimeRunsWhileBusyAndTheCounterIsHiddenAfterTheSession()
    {
        await StartAnswerAsync();

        // The elapsed time of the whole request comes first, also while no tokens flow.
        await Expect(Page.Locator(ElapsedTime)).ToHaveTextAsync(ElapsedTimeRegex(), new() { Timeout = 6000 });

        // With the session complete the whole counter disappears, as it covers the busy period only.
        await Expect(Page.Locator(LiveCount)).ToBeHiddenAsync(new() { Timeout = 15000 });
    }

    [Test]
    [Category("Status")]
    public async Task ContextBar_DoesNotGrowFromSubAgentTokens()
    {
        await StartAnswerAsync();

        var infoBar = Page.Locator(InfoBar);
        await Expect(infoBar).ToHaveAttributeAsync("data-tooltip", ContextUsageRegex(), new() { Timeout = 8000 });
        var afterFirstAgent = await infoBar.GetAttributeAsync("data-tooltip");

        await Expect(Page.Locator(TokenNumber)).ToContainTextAsync("2000 tokens", new() { Timeout = 6000 });

        var afterBothAgents = await infoBar.GetAttributeAsync("data-tooltip");
        Assert.That(afterBothAgents, Is.EqualTo(afterFirstAgent),
            "Tokens of a SubAgent must not be counted into the context of the main chat");
    }
}
