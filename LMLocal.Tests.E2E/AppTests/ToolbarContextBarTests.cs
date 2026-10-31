namespace LMLocal.Tests.E2E.AppTests;
using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// Covers the live context-usage bar in the top toolbar (#token-bar-fill / #info-tooltip).
///
/// The bar shows: base context of the last finished request + rounds already finished in the
/// current request + the round currently streaming + an estimate of the tool results already
/// collected, and is overwritten with the exact value coming from ChatSessionComplete.
/// On STOP the approximate value must stay on screen.
///
/// The tooltip text is exposed through the data-tooltip attribute (a native title attribute is
/// unusable here: Chromium hides an already-visible native tooltip whenever the attribute
/// changes, and the toolbar re-renders on every chunk flush). The popover itself is opened by
/// hover only.
///
/// The mock ("webview-mock-context-bar.js") does not schedule anything by itself, so every
/// protocol step is emitted explicitly and the assertions are deterministic.
/// </summary>
[TestFixture]
public partial class ToolbarContextBarTests : AppTestBase
{
    private const int TokenMax = 16384;

    // The UI formats numbers with toLocaleString('en-US'), so the expected text must use
    // the invariant comma separator regardless of the machine culture (ru-RU would emit NBSP).
    // A plain string is compared exactly, which is stricter than a pattern and needs no regex.
    // Note the space between the percent sign and the line break — that is what the current
    // production format emits.
    private static string UsageTitle(string percent, string used, int tokenMax = TokenMax) =>
        $"Context usage: {percent}% \n{used} / {tokenMax.ToString("N0", CultureInfo.InvariantCulture)} tokens";

    // A partially filled bar has a scaleY() factor below 1 with leading zeros (e.g. 0.0549).
    [GeneratedRegex(@"scaleY\(0\.0[0-9]+\)")]
    private static partial Regex PartiallyFilledBarRegex();

    /// <summary>
    /// Asserts the tooltip text published by the toolbar for the context bar.
    /// </summary>
    private async Task ExpectTooltipAsync(string expected)
    {
        await Expect(Page.Locator("#info-tooltip"))
            .ToHaveAttributeAsync("data-tooltip", expected, new() { Timeout = 5000 });
    }

    private async Task StartSessionAsync()
    {
        await GotoWithMockAsync("webview-mock-context-bar.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();
    }

    private async Task EmitAsync(object message)
    {
        await Page.EvaluateAsync<object>("msg => window.__emitBridgeMessage(msg)", message);
    }

    /// <summary>
    /// Emits a content chunk and then forces a ChunkBuffer flush, so the UI state is updated
    /// without depending on the 100 ms flush interval timing.
    /// </summary>
    private async Task EmitContentAsync(string text, int count)
    {
        await EmitAsync(new { Type = "StreamContent", Payload = text, Count = count, TokensPerSecond = 15.5 });
        await Page.WaitForTimeoutAsync(140);
        await EmitAsync(new { Type = "StreamContent", Payload = " ", Count = count, TokensPerSecond = 15.5 });
    }

    [Test]
    [Category("TokenBar")]
    public async Task ContextBar_ShowsLiveValueWhileStreaming()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);

        // 900 / 16384 = 5.49% -> 5%
        await ExpectTooltipAsync(UsageTitle("5", "900"));

        // The bar itself must be filled, not flat.
        await Expect(Page.Locator("#token-bar-fill"))
            .ToHaveAttributeAsync("style", PartiallyFilledBarRegex(), new() { Timeout = 5000 });
    }

    [Test]
    [Category("TokenBar")]
    public async Task ContextBar_KeepsValueAcrossToolRoundBoundary()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);
        await EmitAsync(new { Type = "StreamEnd" });

        // Round finished: value must stay, it must not drop back to 0.
        await ExpectTooltipAsync(UsageTitle("5", "900"));

        // A tool round begins; Count restarts from 0 on the backend.
        await EmitAsync(new { Type = "ChatSessionIterating", RoundNumber = 1, ToolCount = 1, IsFinalRound = false });
        await EmitContentAsync("world", 400);

        // 900 (finished round) + 400 (current round) = 1300 -> 7.93% -> 8%
        await ExpectTooltipAsync(UsageTitle("8", "1,300"));
    }

    [Test]
    [Category("TokenBar")]
    public async Task ContextBar_IsOverwrittenByAuthoritativeTotalOnComplete()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);
        await EmitAsync(new { Type = "StreamEnd" });
        await EmitAsync(new { Type = "ChatSessionIterating", RoundNumber = 1, ToolCount = 1, IsFinalRound = false });
        await EmitContentAsync("world", 400);
        await EmitAsync(new { Type = "StreamEnd" });

        await ExpectTooltipAsync(UsageTitle("8", "1,300"));

        // Authoritative context size for the whole session: 5000 tokens (30.5% -> 31%).
        await EmitAsync(new
        {
            Type = "ChatSessionComplete",
            TotalTokens = 5000,
            ReasoningTokens = 0,
            CachedTokens = 0,
            TokensPerSecond = 12.0
        });

        await ExpectTooltipAsync(UsageTitle("31", "5,000"));
    }

    [Test]
    [Category("TokenBar")]
    public async Task ContextBar_KeepsApproximateValueAfterStop()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);
        await EmitAsync(new { Type = "StreamEnd" });
        await EmitAsync(new { Type = "ChatSessionIterating", RoundNumber = 1, ToolCount = 1, IsFinalRound = false });
        await EmitContentAsync("world", 400);

        await ExpectTooltipAsync(UsageTitle("8", "1,300"));

        // STOP: cancellation carries no usage, the approximate number must survive.
        await EmitAsync(new { Type = "ChatSessionCancelled", Payload = "Generation stopped by user" });

        await ExpectTooltipAsync(UsageTitle("8", "1,300"));
    }

    /// <summary>
    /// Emits a reasoning chunk and then forces a ChunkBuffer flush (see EmitContentAsync).
    /// </summary>
    private async Task EmitThoughtAsync(string text, int count)
    {
        await EmitAsync(new { Type = "StreamThought", Payload = text, Count = count, TokensPerSecond = 15.5 });
        await Page.WaitForTimeoutAsync(140);
        await EmitAsync(new { Type = "StreamThought", Payload = " ", Count = count, TokensPerSecond = 15.5 });
    }

    [Test]
    [Category("TokenBar")]
    public async Task ContextBar_CountsReasoningTokensWhileThinking()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitThoughtAsync("pondering", 300);

        // THINKING is part of the live phase: 300 / 16384 = 1.83% -> 2%
        await ExpectTooltipAsync(UsageTitle("2", "300"));

        // Reasoning -> content transition must keep the same round counter (no double count).
        await EmitContentAsync("answer", 500);
        await ExpectTooltipAsync(UsageTitle("3", "500"));
    }

    [Test]
    [Category("TokenBar")]
    public async Task ContextBar_KeepsAuthoritativeValueAcrossHistoryCompaction()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);
        await EmitAsync(new { Type = "StreamEnd" });
        await EmitAsync(new
        {
            Type = "ChatSessionComplete",
            TotalTokens = 5000,
            ReasoningTokens = 0,
            CachedTokens = 0,
            TokensPerSecond = 12.0
        });

        await ExpectTooltipAsync(UsageTitle("31", "5,000"));

        // History compaction runs after completion: it must not add anything nor reset the bar.
        await EmitAsync(new { Type = "CompactionStart" });
        await ExpectTooltipAsync(UsageTitle("31", "5,000"));

        await EmitAsync(new { Type = "CompactionEnd" });
        await ExpectTooltipAsync(UsageTitle("31", "5,000"));
    }

    [Test]
    [Category("TokenBar")]
    public async Task ContextBar_KeepsValueWhenActiveModelChanges()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);
        await EmitAsync(new { Type = "StreamEnd" });
        await EmitAsync(new
        {
            Type = "ChatSessionComplete",
            TotalTokens = 5000,
            ReasoningTokens = 0,
            CachedTokens = 0,
            TokensPerSecond = 12.0
        });

        // 5000 / 16384 = 30.5% -> 31%
        await ExpectTooltipAsync(UsageTitle("31", "5,000"));

        // Switch to a model with a smaller window (8192). The host does not clear the
        // conversation, so the occupied context must survive and only the percentage changes.
        await Page.Locator("#model-name").ClickAsync();
        var dialog = Page.Locator("#model-selector-dialog");
        await Expect(dialog).ToBeVisibleAsync();
        await Page.Locator("#models-list-container .model-card[data-model-id='test-model-2']").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });

        // Same 5000 tokens, new window: 5000 / 8192 = 61.03% -> 61%
        await ExpectTooltipAsync(UsageTitle("61", "5,000", tokenMax: 8192));
    }

    [Test]
    [Category("TokenBar")]
    public async Task StatusBar_StillReportsRoundTokensNotContextSize()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);
        await EmitAsync(new { Type = "StreamEnd" });
        await EmitAsync(new { Type = "ChatSessionIterating", RoundNumber = 1, ToolCount = 1, IsFinalRound = false });
        await EmitContentAsync("world", 400);

        // The bottom status bar keeps showing the current round counter (400), not the
        // accumulated context (1300) shown by the toolbar. This guards the separation
        // between the two indicators.
        await Expect(Page.Locator("#token-number"))
            .ToHaveTextAsync("400 tokens", new() { Timeout = 5000 });

        await ExpectTooltipAsync(UsageTitle("8", "1,300"));
    }

    [Test]
    [Category("TokenBar")]
    public async Task ContextBar_ScaleMatchesRoundedPercent()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);
        await EmitAsync(new { Type = "StreamEnd" });

        // 66 / 16384 = 0.40% — the tooltip rounds it to 0%, so the bar must be empty as well.
        // Before the shared rounding rule the bar was scaled to 0.004 and drew a visible sliver.
        await EmitAsync(new
        {
            Type = "ChatSessionComplete",
            TotalTokens = 66,
            ReasoningTokens = 0,
            CachedTokens = 0,
            TokensPerSecond = 12.0
        });

        await ExpectTooltipAsync(UsageTitle("0", "66"));

        var style = await Page.Locator("#token-bar-fill").GetAttributeAsync("style");
        Assert.That(style, Does.Contain("scaleY(0)"), "0% must leave the bar empty");

        // 16319 / 16384 = 99.60% — the tooltip rounds it to 100%, so the bar must be full.
        // Before the shared rounding rule the bar was scaled to 0.996 while the title said 100%.
        await EmitAsync(new
        {
            Type = "ChatSessionComplete",
            TotalTokens = 16319,
            ReasoningTokens = 0,
            CachedTokens = 0,
            TokensPerSecond = 12.0
        });

        await ExpectTooltipAsync(UsageTitle("100", "16,319"));

        style = await Page.Locator("#token-bar-fill").GetAttributeAsync("style");
        Assert.That(style, Does.Contain("scaleY(1)"), "100% must fill the bar completely");
    }

    [Test]
    [Category("TokenBar")]
    public async Task ContextBar_HasNoNativeTitleAttribute()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);

        // A native title would compete with the custom popover and be dismissed on every
        // value update, so neither anchor may carry one.
        var barTitle = await Page.Locator("#info-tooltip").GetAttributeAsync("title");
        Assert.That(barTitle, Is.Null, "#info-tooltip must not use the native title");

        var modelTitle = await Page.Locator("#model-name").GetAttributeAsync("title");
        Assert.That(modelTitle, Is.Null, "#model-name must not use the native title");
    }

    [Test]
    [Category("TokenBar")]
    public async Task ContextTooltip_StaysOpenWhileValuesChange()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);

        await Page.Locator("#info-tooltip").HoverAsync();

        var tooltip = Page.Locator("#context-tooltip");
        await Expect(tooltip).ToBeVisibleAsync(new() { Timeout = 3000 });

        // This is exactly where a native title died: every value update dismissed it.
        await EmitContentAsync(" more", 1200);
        await EmitContentAsync(" more", 1500);

        await Expect(tooltip).ToBeVisibleAsync(new() { Timeout = 3000 });
        await Expect(tooltip).ToContainTextAsync("1,500");
    }

    [Test]
    [Category("TokenBar")]
    public async Task ContextTooltip_ClosesOnMouseLeave()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);

        await Page.Locator("#info-tooltip").HoverAsync();
        var tooltip = Page.Locator("#context-tooltip");
        await Expect(tooltip).ToBeVisibleAsync(new() { Timeout = 3000 });

        // Move the pointer away from the bar.
        await Page.Locator("#userInput").HoverAsync();
        await Expect(tooltip).ToBeHiddenAsync(new() { Timeout = 3000 });
    }
}
