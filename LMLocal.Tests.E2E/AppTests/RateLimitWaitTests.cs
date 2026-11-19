using System.Threading.Tasks;

namespace LMLocal.Tests.E2E.AppTests;

/// <summary>
/// Covers the rate-limit banner driven by the RateLimitWait bridge message.
///
/// The banner is a separate piece of UI state (<c>appStore.rateLimitWait</c>) rather than an AppStatus, so the tests
/// also pin down that showing it does not disturb the regular status line (which drives isBusy and the Stop button).
///
/// Two guards are covered, both of them for sequences that really happen: an End only clears the banner it owns
/// (WaitId), and a Begin arriving after the session reached a terminal status is dropped. The second one matters
/// because terminal messages and rate-limit notifications travel on different channels: the terminal message can
/// overtake a queued Begin, and a banner shown after the session is over would never be cleared.
///
/// The mock ("webview-mock-context-bar.js") schedules nothing by itself, so every protocol step is emitted
/// explicitly and the assertions are deterministic.
/// </summary>
[TestFixture]
public class RateLimitWaitTests : AppTestBase
{
    private const string BannerSelector = "#rate-limit-status";

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

    private Task StartSessionMessageAsync()
        => EmitAsync(new { Type = "ChatSessionStart" });

    private Task EmitWaitAsync(string waitId, string phase, int attempt = 2, int delaySeconds = 30)
    {
        return EmitAsync(new
        {
            Type = "RateLimitWait",
            WaitId = waitId,
            Attempt = attempt,
            MaxAttempts = 2,
            DelaySeconds = delaySeconds,
            Source = "header",
            Phase = phase
        });
    }

    /// <summary>Terminal message that ends the request successfully; the status becomes IDLE.</summary>
    private Task CompleteRequestAsync()
    {
        return EmitAsync(new
        {
            Type = "ChatSessionComplete",
            TotalTokens = 100,
            PromptTokens = 80,
            CompletionTokens = 20,
            ReasoningTokens = 0,
            CachedTokens = 0,
            TokensPerSecond = 10.0
        });
    }

    private ILocator Banner => Page.Locator(BannerSelector);

    [Test]
    [Category("RateLimit")]
    public async Task RateLimitWait_Begin_ShowsBanner()
    {
        await StartSessionAsync();
        await StartSessionMessageAsync();

        await EmitWaitAsync("wait-1", "Begin");

        await Expect(Banner).ToBeVisibleAsync(new() { Timeout = 3000 });
        await Expect(Banner).ToContainTextAsync("attempt 2/2");
        await Expect(Banner).ToContainTextAsync("30s");
    }

    [Test]
    [Category("RateLimit")]
    public async Task RateLimitWait_End_WithMatchingWaitId_ClearsBanner()
    {
        await StartSessionAsync();
        await StartSessionMessageAsync();

        await EmitWaitAsync("wait-1", "Begin");
        await Expect(Banner).ToBeVisibleAsync(new() { Timeout = 3000 });

        await EmitWaitAsync("wait-1", "End");

        await Expect(Banner).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("RateLimit")]
    public async Task RateLimitWait_EndWithoutBanner_DoesNothing()
    {
        await StartSessionAsync();
        await StartSessionMessageAsync();

        // The terminal message overtakes the notification, then the End of that same wait arrives.
        await EmitWaitAsync("wait-1", "Begin");
        await Expect(Banner).ToBeVisibleAsync(new() { Timeout = 3000 });

        await CompleteRequestAsync();
        await Expect(Banner).ToBeHiddenAsync(new() { Timeout = 3000 });

        await EmitWaitAsync("wait-1", "End");

        await Expect(Banner).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("RateLimit")]
    public async Task RateLimitWait_BeginAfterTerminalStatus_IsIgnored()
    {
        await StartSessionAsync();
        await StartSessionMessageAsync();

        await CompleteRequestAsync();
        await Expect(Banner).ToBeHiddenAsync(new() { Timeout = 3000 });

        // A Begin that was still in flight when the session finished must not leave a banner nobody clears.
        await EmitWaitAsync("wait-1", "Begin");

        await Expect(Banner).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("RateLimit")]
    public async Task RateLimitWait_BeginAfterErrorStatus_IsIgnored()
    {
        await StartSessionAsync();
        await StartSessionMessageAsync();

        await EmitAsync(new { Type = "ChatSessionError", Payload = "boom" });

        await EmitWaitAsync("wait-1", "Begin");

        await Expect(Banner).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("RateLimit")]
    public async Task RateLimitWait_NewSession_ClearsBanner()
    {
        await StartSessionAsync();
        await StartSessionMessageAsync();

        await EmitWaitAsync("wait-1", "Begin");
        await Expect(Banner).ToBeVisibleAsync(new() { Timeout = 3000 });

        await StartSessionMessageAsync();

        await Expect(Banner).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("RateLimit")]
    public async Task RateLimitWait_TerminalError_ClearsBanner()
    {
        await StartSessionAsync();
        await StartSessionMessageAsync();

        await EmitWaitAsync("wait-1", "Begin");
        await Expect(Banner).ToBeVisibleAsync(new() { Timeout = 3000 });

        await EmitAsync(new { Type = "ChatSessionError", Payload = "boom" });

        await Expect(Banner).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("RateLimit")]
    public async Task RateLimitWait_DoesNotChangeTheStatusLine()
    {
        await StartSessionAsync();
        await StartSessionMessageAsync();

        var statusBefore = await Page.Locator("#status-text").TextContentAsync();

        await EmitWaitAsync("wait-1", "Begin");
        await Expect(Banner).ToBeVisibleAsync(new() { Timeout = 3000 });

        var statusAfter = await Page.Locator("#status-text").TextContentAsync();

        Assert.That(statusAfter, Is.EqualTo(statusBefore),
            "the banner must be an independent piece of state; the status line drives isBusy and Stop");
    }
}
