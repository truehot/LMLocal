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
    public async Task CopyButton_ShowsSuccessIconAfterClick()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for streaming to complete - check that response container is visible and not generating
        await Expect(Page.Locator(".ai-response-container")).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => !document.querySelector('.ai-response-container')?.classList.contains('is-generating')");

        var headerCopy = Page.Locator(".header-copy-btn").First;
        await Expect(headerCopy.Locator("svg")).ToBeVisibleAsync();

        await headerCopy.ClickAsync();

        // The result is reported by an icon swap plus a class, there is no label to change any more.
        await Expect(headerCopy).ToHaveClassAsync(SuccessClass, new() { Timeout = 3000 });
        await Expect(headerCopy.Locator("svg")).ToBeVisibleAsync();

        // The result is temporary: the button goes back to the copy icon.
        await Expect(headerCopy).Not.ToHaveClassAsync(SuccessClass, new() { Timeout = 5000 });
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

        // Verify UI shows the copied state: the icon is swapped and the success class is set.
        await Expect(headerCopy.Nth(0)).ToHaveClassAsync(SuccessClass, new() { Timeout = 3000 });

        // Verify backend received the exact displayed code
        var lastCopied = await Page.EvaluateAsync<string>("() => window.__lastCopied");
        Assert.That(lastCopied, Is.EqualTo(displayedCode), "Backend copy handler did not receive the expected streamed code text.");
    }

    [Test]
    [Category("CopyButton")]
    public async Task CopyButton_HasSameIconAsMessageCopy()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        // The message copy button appears once the answer is finished.
        await Expect(Page.Locator(".ai-message .message-copy-btn")).ToHaveCountAsync(1, new() { Timeout = 5000 });

        // Both buttons share one icon, so the worker copy (single-line literal) and Icons.COPY must not
        // drift apart. Compare normalized markup: the raw strings differ in line breaks only.
        const string normalize = "el => el.outerHTML.replace(/\\s+/g, ' ').trim()";
        var headerSvg = await Page.Locator(".header-copy-btn svg").First.EvaluateAsync<string>(normalize);
        var messageSvg = await Page.Locator(".message-copy-btn svg").First.EvaluateAsync<string>(normalize);

        Assert.That(headerSvg, Is.Not.Empty, "Code block copy button should render an icon");
        Assert.That(headerSvg, Is.EqualTo(messageSvg),
            "Code block copy button and message copy button must use the same icon");
    }

    [Test]
    [Category("CopyButton")]
    public async Task CopyButton_IsIconOnlyAndAccessible()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        var headerCopy = Page.Locator(".header-copy-btn").First;
        await Expect(headerCopy).ToBeVisibleAsync(new() { Timeout = 5000 });

        // No label any more: the button is icon only, but keeps its tooltip and accessible name.
        await Expect(headerCopy.Locator("span")).ToHaveCountAsync(0);
        await Expect(headerCopy).ToHaveAttributeAsync("aria-label", "Copy");
        await Expect(headerCopy).ToHaveAttributeAsync("title", "Copy");
        await Expect(headerCopy).ToHaveAttributeAsync("type", "button");
    }

    [Test]
    [Category("CopyButton")]
    public async Task CopyButton_DoubleClick_CopiesOnce()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        var headerCopy = Page.Locator(".header-copy-btn").First;
        await Expect(headerCopy).ToBeVisibleAsync(new() { Timeout = 5000 });

        await Page.EvaluateAsync<object>(@"() => {
            window.__copyCount = 0;
            const orig = window.__hostOverride?.CopyToClipboardAsync;
            window.__hostOverride = window.__hostOverride || {};
            window.__hostOverride.CopyToClipboardAsync = async (t) => {
                window.__copyCount++;
                return orig ? await orig(t) : true;
            };
        }");

        await headerCopy.ClickAsync();
        await headerCopy.ClickAsync();

        await Page.WaitForFunctionAsync("() => window.__copyCount > 0");
        // The second click lands while the result is still displayed and must be ignored.
        await Task.Delay(500);

        var copyCount = await Page.EvaluateAsync<int>("() => window.__copyCount");
        Assert.That(copyCount, Is.EqualTo(1), "A second click on the copy button must not copy again");
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
    public async Task TokenBar_RemainsVisibleAfterStreamCompletes()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        // Token counter is visible during streaming and stays visible after
        // completion, keeping the last token/speed values until the next turn.
        await Expect(Page.Locator(".ai-response-container")).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => !document.querySelector('.ai-response-container')?.classList.contains('is-generating')");

        // Verify the counter keeps the last numbers after the stream completes.
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

    [Test]
    [Category("TokenStats")]
    public async Task TokenStats_Badge_ShowsTotalCachedAndSpeed()
    {
        await GotoWithMockAsync("webview-mock-token-stats.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        await Expect(Page.Locator(".token-stats")).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Expect(Page.Locator(".token-stats"))
            .ToHaveTextAsync(TokenStatsBadgeWithTime, new() { Timeout = 3000 });
    }

    [Test]
    [Category("TokenStats")]
    public async Task TokenStats_NotShownWhenSettingDisabled()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        await Expect(Page.Locator(".ai-response-container")).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => !document.querySelector('.ai-response-container')?.classList.contains('is-generating')");

        await Expect(Page.Locator(".token-stats")).ToHaveCountAsync(0);
    }

    [Test]
    [Category("TokenStats")]
    public async Task TokenStats_ShowsOneBadgePerMessage()
    {
        await GotoWithMockAsync("webview-mock-token-stats.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        // First message gets exactly one badge (no duplicates on repeated IDLE transitions).
        await Expect(Page.Locator(".token-stats")).ToHaveCountAsync(1, new() { Timeout = 5000 });

        // Send a second message: it should get its own badge (one per message), not duplicate on the first.
        await Page.Locator("#userInput").FillAsync("Again");
        await Page.Locator("#mainBtn").ClickAsync();

        await Expect(Page.Locator(".ai-response-container").Last).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => !document.querySelectorAll('.ai-response-container')[document.querySelectorAll('.ai-response-container').length - 1]?.classList.contains('is-generating')");

        await Expect(Page.Locator(".token-stats")).ToHaveCountAsync(2, new() { Timeout = 3000 });
    }
}
