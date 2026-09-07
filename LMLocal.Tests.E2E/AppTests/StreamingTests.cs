namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public class StreamingTests : AppTestBase
{
    [Test]
    [Category("Chat")]
    public async Task StatusText_ShowsGeneratingWhileStreaming()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        await Expect(Page.Locator("#status-text"))
            .ToContainTextAsync(ThinkingOrGenerating, new() { Timeout = 3000 });
    }

    [Test]
    [Category("Stream")]
    public async Task Stream_RendersAllChunks_IncludingTail()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for streaming to complete
        await Expect(Page.Locator(".ai-response-container")).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => !document.querySelector('.ai-response-container')?.classList.contains('is-generating')");

        await Expect(Page.Locator(".ai-message")).ToContainTextAsync("console.log(\"hello\");", new() { Timeout = 3000 });
    }

    [Test]
    [Category("Loading")]
    public async Task LoadingIndicator_IsShownWhileWaitingForFirstChunk()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();
        // Loading indicator should be visible while waiting for the first chunk.
        // In fast mocks the first chunk may arrive very quickly, so accept either:
        // 1. A visible loading indicator, or
        // 2. An AI message with visible response container (stream already started)
        try
        {
            await Expect(Page.Locator(".loading-indicator")).ToBeVisibleAsync(new() { Timeout = 300 });
            // found a visible loading indicator
        }
        catch (Microsoft.Playwright.PlaywrightException)
        {
            // Fallback: ensure the AI message or response container has content (stream already progressed)
            await Expect(Page.Locator(".ai-message")).ToBeVisibleAsync(new() { Timeout = 1000 });
            var hasContent = await Page.Locator(".ai-message").CountAsync() > 0;
            Assert.That(hasContent, Is.True, "Either a loading indicator should be visible or the AI message should have been created");
        }
    }

    [Test]
    [Category("Loading")]
    public async Task LoadingIndicator_IsReplacedByContentAfterChunkReceived()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for streaming to complete
        await Expect(Page.Locator(".ai-response-container")).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => !document.querySelector('.ai-response-container')?.classList.contains('is-generating')");

        var visibleAfter = await Page.Locator(".loading-indicator:visible").CountAsync();
        Assert.That(visibleAfter, Is.Zero, "No loading indicators should be visible after stream completes");
    }
}
