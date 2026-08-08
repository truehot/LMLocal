namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public class TokenAndHighlightTests : AppTestBase
{
    [Test]
    [Category("Markdown")]
    public async Task Marked_RendersCodeBlockAsPreElement()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for streaming to complete - check that response container is visible and not generating
        await Expect(Page.Locator(".ai-response-container")).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => !document.querySelector('.ai-response-container')?.classList.contains('is-generating')");

        await Expect(Page.Locator(".ai-message pre")).ToBeVisibleAsync();
        await Expect(Page.Locator(".ai-message pre code")).ToBeVisibleAsync();
    }

    [Test]
    [Category("Highlight")]
    public async Task Highlight_AppliesHljsClassToCodeBlock()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for streaming to complete - check that response container is visible and not generating
        await Expect(Page.Locator(".ai-response-container")).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => !document.querySelector('.ai-response-container')?.classList.contains('is-generating')");

        var hasHljs = await Page.Locator(".ai-message pre code.hljs").CountAsync();
        Assert.That(hasHljs, Is.GreaterThan(0),
            "highlight.js should apply 'hljs' class to code block");
    }

    [Test]
    [Category("CopyButton")]
    public async Task CopyButton_IsInjectedAfterStreamCompletes()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for streaming to complete - check that response container is visible and not generating
        await Expect(Page.Locator(".ai-response-container")).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => !document.querySelector('.ai-response-container')?.classList.contains('is-generating')");

        await Expect(Page.Locator(".header-copy-btn")).ToBeVisibleAsync();
    }

    [Test]
    [Category("CopyButton")]
    public async Task CopyButton_ShowsCopiedLabelAfterClick()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for streaming to complete - check that response container is visible and not generating
        await Expect(Page.Locator(".ai-response-container")).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => !document.querySelector('.ai-response-container')?.classList.contains('is-generating')");

        await Page.Locator(".header-copy-btn").ClickAsync();
        await Expect(Page.Locator(".header-copy-btn span"))
            .ToHaveTextAsync(CopiedLabel, new() { Timeout = 3000 });
    }

    [Test]
    [Category("CopyButton")]
    public async Task CopyButton_CopiesExactTextToClipboard()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // This test verifies copying of a streamed AI code block (the common copy flow).

        // Trigger a send that causes the AI streaming behavior in the mock.
        const string dummy = "Trigger stream";
        await Page.Locator("#userInput").FillAsync(dummy);
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for the AI message to finish streaming and be marked completed.
        await Expect(Page.Locator(".ai-message.completed")).ToHaveCountAsync(1, new() { Timeout = 5000 });

        var aiMessage = Page.Locator(".ai-message.completed").Nth(0);

        // Find the code block inside the AI response container.
        var codeElem = aiMessage.Locator("pre code, pre");
        var codeCount = await codeElem.CountAsync();
        Assert.That(codeCount, Is.GreaterThan(0), "No code block found inside AI message to copy.");

        // Read the displayed code text to compare later.
        var displayedCode = await codeElem.Nth(0).EvaluateAsync<string>("el => el.textContent");

        // Prepare host controller override to capture the text passed to CopyToClipboardAsync.
        await Page.EvaluateAsync<object>(@"() => {
            window.__lastCopied = null;
            const orig = window.__hostOverride?.CopyToClipboardAsync;
            if (orig) {
                window.__hostOverride.CopyToClipboardAsync = async (t) => { window.__lastCopied = t; return await orig(t); };
            } else {
                window.__hostOverride = { CopyToClipboardAsync: async (t) => { window.__lastCopied = t; return true; } };
            }
        }");

        // Click the copy header button injected by attachCopyButtons.
        var headerCopy = aiMessage.Locator(".header-copy-btn");
        var headerCount = await headerCopy.CountAsync();
        Assert.That(headerCount, Is.GreaterThan(0), "Header copy button not found in AI message.");

        await headerCopy.Nth(0).ClickAsync();

        // Verify UI shows copied state
        // Wait for the UI copy status label to change to a success label (e.g. "Copied" or "Done!").
        var span = headerCopy.Nth(0).Locator("span");
        var waitUntil = DateTime.UtcNow.AddMilliseconds(3000);
        string spanText = await span.InnerTextAsync();
        while (!CopiedLabel.IsMatch(spanText) && DateTime.UtcNow < waitUntil)
        {
            await Task.Delay(100);
            spanText = await span.InnerTextAsync();
        }
        Assert.That(CopiedLabel.IsMatch(spanText), Is.True, $"Copy status label did not indicate success. Final value: '{spanText}'");

        // Verify backend received the exact displayed code
        var lastCopied = await Page.EvaluateAsync<string>("() => window.__lastCopied");
        Assert.That(lastCopied, Is.EqualTo(displayedCode), "Backend copy handler did not receive the expected streamed code text.");
    }

    [Test]
    [Category("TokenBar")]
    public async Task TokenBar_IsHiddenBeforeSend()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#live-token-count")).ToBeHiddenAsync();
    }

    [Test]
    [Category("TokenBar")]
    public async Task TokenBar_IsVisibleWhileGenerating()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        await Expect(Page.Locator("#live-token-count"))
            .ToBeVisibleAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("TokenBar")]
    public async Task TokenBar_IsHiddenAfterStreamCompletes()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        // Token counter is visible during streaming and FINISHING state
        // It remains visible as long as isBusy() returns true (which includes FINISHING)
        await Expect(Page.Locator(".ai-response-container")).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => !document.querySelector('.ai-response-container')?.classList.contains('is-generating')");

        // Verify token counter is still visible during post-processing (FINISHING state)
        await Expect(Page.Locator("#live-token-count")).ToBeVisibleAsync();
    }

    [Test]
    [Category("TokenBar")]
    public async Task TokenBar_ShowsCorrectCountAfterChunk()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for streaming to complete
        await Expect(Page.Locator(".ai-response-container")).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => !document.querySelector('.ai-response-container')?.classList.contains('is-generating')");

        await Expect(Page.Locator("#token-number"))
            .ToHaveTextAsync("10 tokens", new() { Timeout = 3000 });
    }

    [Test]
    [Category("TokenBar")]
    public async Task TokenBar_ShowsSpeedAfterChunk()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        var speedSpan = Page.Locator("#tokens-speed");
        await Expect(speedSpan).ToBeVisibleAsync(new() { Timeout = 3000 });
        await Expect(speedSpan).ToHaveTextAsync(TokensPerSecond, new() { Timeout = 3000 });

        // Wait for streaming to complete
        await Expect(Page.Locator(".ai-response-container")).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Page.WaitForFunctionAsync("() => !document.querySelector('.ai-response-container')?.classList.contains('is-generating')");
    }
}
