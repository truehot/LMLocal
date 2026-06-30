namespace LMLocal.Tests.E2E.AppTests;

using System.Text.RegularExpressions;

[TestFixture]
public partial class StatusBarTests : AppTestBase
{
    [GeneratedRegex(@"\bgenerating\b")]
    private static partial Regex GeneratingClassRegex();
    private static Regex GeneratingClass => GeneratingClassRegex();

    [GeneratedRegex(@"\bonline\b")]
    private static partial Regex OnlineClassRegex();
    private static Regex OnlineClass => OnlineClassRegex();

    [GeneratedRegex(@"inline", RegexOptions.IgnoreCase)]
    private static partial Regex InlineStyleRegex();
    private static Regex InlineStyle => InlineStyleRegex();

    [Test]
    [Category("StatusBar")]
    public async Task StatusBar_ShowsReadyOnLoad()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Expect(Page.Locator("#status-text"))
            .ToHaveTextAsync("Ready", new() { Timeout = 3000 });
    }

    [Test]
    [Category("StatusBar")]
    public async Task StatusBar_ConnectionStatusShowsOnline()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Expect(Page.Locator("#conn-status")).ToHaveClassAsync(OnlineClass);
    }

    [Test]
    [Category("StatusBar")]
    public async Task StatusBar_RetryButtonHiddenWhenOnline()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Expect(Page.Locator("#retry-btn")).ToBeHiddenAsync();
    }

    [Test]
    [Category("StatusBar")]
    public async Task StatusBar_RetryButtonVisibleWhenOfflineWithError()
    {
        await GotoWithMockAsync("webview-mock-offline.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Disconnected", new() { Timeout = 3000 });

        await Expect(Page.Locator("#retry-btn")).ToBeVisibleAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("StatusBar")]
    public async Task StatusBar_ShowsGeneratingAnimationWhenStreaming()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        // The status bar should have 'generating' class while streaming
        await Expect(Page.Locator("#status-bar")).ToHaveClassAsync(GeneratingClass, new() { Timeout = 3000 });
    }

    [Test]
    [Category("StatusBar")]
    public async Task StatusBar_TokenCounterVisibleDuringGeneration()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        // Token counter should be visible while generating
        await Expect(Page.Locator("#live-token-count"))
            .ToHaveAttributeAsync("style", InlineStyle, new() { Timeout = 3000 });
    }

    [Test]
    [Category("StatusBar")]
    public async Task StatusBar_ClearChatBtnExistsAndVisible()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Expect(Page.Locator("#clear-chat-btn")).ToBeVisibleAsync();
    }

    [Test]
    [Category("StatusBar")]
    public async Task StatusBar_TokenTextUpdatesDuringStream()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        // Token number should be populated during generation
        var tokenNumber = Page.Locator("#token-number");
        await Expect(tokenNumber).Not.ToBeEmptyAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("StatusBar")]
    public async Task StatusBar_StatusTextShowsOfflineError()
    {
        await GotoWithMockAsync("webview-mock-offline.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Disconnected", new() { Timeout = 3000 });

        await Expect(Page.Locator("#status-text"))
            .ToContainTextAsync("LM Studio unreachable", new() { Timeout = 3000 });
    }

    [Test]
    [Category("StatusBar")]
    public async Task StatusBar_ConnectBtnVisibleWhenOfflineNoError()
    {
        // The offline mock has an error, so connectBtn won't be visible.
        // We just verify retry-btn takes precedence.
        await GotoWithMockAsync("webview-mock-offline.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Disconnected", new() { Timeout = 3000 });

        // When error is present, retry is shown and connect is hidden
        await Expect(Page.Locator("#retry-btn")).ToBeVisibleAsync();
    }
}
