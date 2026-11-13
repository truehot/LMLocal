namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public partial class ChatTests : AppTestBase
{
    [GeneratedRegex("btn-stop")]
    private static partial Regex StopBtnClassRegex();
    [Test]
    [Category("Chat")]
    public async Task Send_EmptyInput_DoesNotAppendMessage()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#mainBtn").ClickAsync();
        await Expect(Page.Locator("#chat-container > *")).ToHaveCountAsync(0);
    }

    [Test]
    [Category("Chat")]
    public async Task Send_WhitespaceOnly_DoesNotAppendMessage()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("   ");
        await Page.Locator("#mainBtn").ClickAsync();
        await Expect(Page.Locator("#chat-container > *")).ToHaveCountAsync(0);
    }

    [Test]
    [Category("Chat")]
    public async Task Send_AppendsUserMessageToChat()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        // The message also carries the hover-only copy action, so assert on the prompt body itself.
        await Expect(Page.Locator(".user-message .message-content"))
            .ToHaveTextAsync("Hello", new() { Timeout = 3000 });
    }

    [Test]
    [Category("Chat")]
    public async Task Send_ClearsInputAfterSend()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        await Expect(Page.Locator("#userInput"))
            .ToHaveValueAsync("", new() { Timeout = 3000 });
    }

    [Test]
    [Category("Chat")]
    public async Task Send_ButtonChangesToStopWhileGenerating()
    {
        // Use a mock whose stream never ends (it emits STREAMING content, then holds),
        // so the "generating" state persists. The default streaming mock completes in
        // ~200ms, which makes the transient "Stop"/btn-stop state a race for the
        // auto-retrying assertions below.
        await GotoWithMockAsync("webview-mock-persistent-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        // Confirm the app is actively streaming (not merely pre-processing), so the assertion
        // below genuinely observes the button's "Stop" state while generation is in progress.
        await Page.EvaluateAsync("() => { import('/js/store/app.store.js').then(m => { window.__appStore = m.default; }); }");
        await Page.WaitForFunctionAsync(
            "() => window.__appStore?.getState?.().status === 'STREAMING'",
            null, new() { Timeout = 3000 });

        var stopBtn = Page.Locator("#mainBtn");
        await Expect(stopBtn).ToHaveTextAsync("Stop", new() { Timeout = 5000 });

        await Expect(stopBtn).ToHaveClassAsync(StopBtnClassRegex(), new() { Timeout = 5000 });
    }

    [Test]
    [Category("Chat")]
    public async Task Send_InputIsDisabledWhileGenerating()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        await Expect(Page.Locator("#userInput"))
            .ToBeDisabledAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("Chat")]
    public async Task EnterKey_SendsMessage()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#userInput").PressAsync("Enter");

        // The message also carries the hover-only copy action, so assert on the prompt body itself.
        await Expect(Page.Locator(".user-message .message-content"))
            .ToHaveTextAsync("Hello", new() { Timeout = 3000 });
    }

    [Test]
    [Category("Chat")]
    public async Task ShiftEnterKey_DoesNotSendMessage()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#userInput").PressAsync("Shift+Enter");

        await Expect(Page.Locator("#chat-container > *")).ToHaveCountAsync(0);
    }

    [Test]
    [Category("Chat")]
    public async Task AiMessage_HasCompletedClassAfterStreamEnds()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for AI message to appear and stream to complete
        await Expect(Page.Locator(".ai-message")).ToHaveCountAsync(1, new() { Timeout = 5000 });
        await Expect(Page.Locator(".ai-response-container")).ToBeVisibleAsync();
        await Page.WaitForFunctionAsync("() => !document.querySelector('.ai-response-container')?.classList.contains('is-generating')");

        // Verify AI message exists and response has finished generating
        var aiMessageCount = await Page.Locator(".ai-message").CountAsync();
        Assert.That(aiMessageCount, Is.EqualTo(1), "Should have one AI message");

        var responseContainer = Page.Locator(".ai-response-container");
        var isNotGenerating = await responseContainer.EvaluateAsync<bool>("el => !el.classList.contains('is-generating')");
        Assert.That(isNotGenerating, Is.True, "Response should have finished generating");
    }

    [Test]
    [Category("PlusButton")]
    public async Task PlusButton_TogglesAndResetsAfterSend()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var plusBtn = Page.Locator("#plus-btn, .plus-btn, .btn-plus, button:has-text('+')");

        var count = await plusBtn.CountAsync();
        Assert.That(count, Is.GreaterThan(0), "Plus button not found. Update the test selector to match the actual markup.");

        var btn = plusBtn.Nth(0);

        async Task<bool> IsActiveAsync()
        {
            return await btn.EvaluateAsync<bool>("el => el.classList.contains('active') || el.classList.contains('is-active') || el.classList.contains('btn-active') || el.getAttribute('aria-pressed') === 'true'");
        }

        var initiallyActive = await IsActiveAsync();
        Assert.That(initiallyActive, Is.False, "Plus button should not be active initially");

        await btn.ClickAsync();
        await Task.Delay(200);
        Assert.That(await IsActiveAsync(), Is.True, "Plus button should be active after click");

        await btn.ClickAsync();
        await Task.Delay(200);
        Assert.That(await IsActiveAsync(), Is.False, "Plus button should be inactive after second click");

        await btn.ClickAsync();
        await Task.Delay(200);
        Assert.That(await IsActiveAsync(), Is.True, "Plus button should be active after third click");

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for streaming to complete instead of checking status text
        await Expect(Page.Locator(".ai-response-container")).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => !document.querySelector('.ai-response-container')?.classList.contains('is-generating')");

        await Task.Delay(200);
        Assert.That(await IsActiveAsync(), Is.False, "Plus button should be reset (inactive) after send/stream");
    }

    // Note: the code block copy button tests live in TokenAndHighlightTests.

    [Test]
    [Category("Chat")]
    public async Task CopyUserMessage_HoverAndClick_CopiesPromptText()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        var userMessage = Page.Locator(".user-message").First;
        await Expect(userMessage).ToBeVisibleAsync(new() { Timeout = 3000 });
        await Expect(userMessage.Locator(".message-copy-btn")).ToHaveCountAsync(1);

        await CaptureClipboardAsync();

        await userMessage.HoverAsync();
        await userMessage.Locator(".message-copy-btn").ClickAsync();

        await ExpectLastCopiedAsync("Hello");
    }

    [Test]
    [Category("Chat")]
    public async Task CopyAnswer_Live_CopiesRawMarkdown()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        var aiMessage = Page.Locator(".ai-message").First;
        // The button appears only once the answer is finished.
        await Expect(aiMessage.Locator(".message-copy-btn")).ToHaveCountAsync(1, new() { Timeout = 5000 });

        // The rendered answer drops the fences; the copy must keep the original markdown.
        var rendered = await aiMessage.Locator(".ai-response-container")
            .EvaluateAsync<string>("el => el.textContent");
        Assert.That(rendered, Does.Not.Contain("```"), "Rendered answer should not carry markdown fences");

        await CaptureClipboardAsync();

        await aiMessage.HoverAsync();
        await aiMessage.Locator(".message-copy-btn").ClickAsync();

        await ExpectLastCopiedAsync("```javascript\nconsole.log(\"hello\");\n```");
    }

    [Test]
    [Category("Chat")]
    public async Task CopyAnswer_History_EveryAiMessageHasOwnCopy()
    {
        await GotoWithMockAsync("webview-mock-chat-history.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action=\"open-chat-history\"]").ClickAsync();

        var dialog = Page.Locator("#chat-history-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 3000 });
        await Page.Locator("#chat-history-container .chat-history-card").First
            .Locator(".chat-history-load").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 5000 });

        var aiMessages = Page.Locator(".ai-message");
        await Expect(aiMessages).ToHaveCountAsync(2, new() { Timeout = 5000 });

        // Every historical answer owns its copy button.
        await Expect(Page.Locator(".ai-message .message-copy-btn")).ToHaveCountAsync(2);

        await CaptureClipboardAsync();

        await aiMessages.First.HoverAsync();
        await aiMessages.First.Locator(".message-copy-btn").ClickAsync();
        await ExpectLastCopiedAsync("**First answer** with `inline` code.");

        // The "1 step taken (history)" note is display-only: it must not end up in the clipboard.
        await Expect(aiMessages.Nth(1)).ToContainTextAsync("1 step taken (history)");
        await aiMessages.Nth(1).HoverAsync();
        await aiMessages.Nth(1).Locator(".message-copy-btn").ClickAsync();
        await ExpectLastCopiedAsync("**Second answer** with a list:\n\n- one\n- two");
    }
    [Test]
    [Category("Chat")]
    public async Task CopyAnswer_SurvivesIdleTransitionAfterFailedSummarize()
    {
        await GotoWithMockAsync("webview-mock-clear-actions.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        var aiMessage = Page.Locator(".ai-message").First;
        await Expect(aiMessage.Locator(".message-copy-btn")).ToHaveCountAsync(1, new() { Timeout = 5000 });

        var answerTextBefore = await aiMessage.Locator(".ai-response-container").InnerTextAsync();
        Assert.That(answerTextBefore, Is.Not.Empty, "The finished answer must have visible text before the failed summarize");

        await Page.EvaluateAsync("() => { window.__bridgeOverride.SummarizeAndCompactAsync = async () => false; }");

        await Page.Locator("#clear-chat-btn").ClickAsync();
        await Expect(Page.Locator("#confirm-dialog")).ToBeVisibleAsync(new() { Timeout = 3000 });

        await Page.Locator("#confirm-action-summarize").CheckAsync();
        await Page.Locator("#confirm-dialog #confirm-dialog-confirm").ClickAsync();
        await Expect(Page.Locator("#confirm-dialog")).ToBeHiddenAsync(new() { Timeout = 3000 });

        await Expect(aiMessage.Locator(".message-copy-btn")).ToHaveCountAsync(1, new() { Timeout = 3000 });

        var answerTextAfter = await aiMessage.Locator(".ai-response-container").InnerTextAsync();
        Assert.That(answerTextAfter, Is.EqualTo(answerTextBefore), "The IDLE transition must not erase the answer text");
    }

    [Test]
    [Category("Chat")]
    public async Task CopyAnswer_Collapsible_CopiesFinalRoundOnly()
    {
        await GotoWithMockAsync("webview-mock-collapsible.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Find files");
        await Page.Locator("#mainBtn").ClickAsync();

        var aiMessage = Page.Locator(".ai-message").First;
        await Expect(aiMessage.Locator(".message-copy-btn")).ToHaveCountAsync(1, new() { Timeout = 8000 });

        await CaptureClipboardAsync();

        await aiMessage.HoverAsync();
        await aiMessage.Locator(".message-copy-btn").ClickAsync();

        // The tool round and the Thoughts block stay out: only the final round is copied.
        await ExpectLastCopiedAsync("Here is the final summary: everything looks good.");
    }
    [Test]
    [Category("Chat")]
    public async Task CopyAnswer_Collapsible_SurvivesIdleTransitionAfterFailedSummarize()
    {
        await GotoWithMockAsync("webview-mock-collapsible.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Find files");
        await Page.Locator("#mainBtn").ClickAsync();

        var aiMessage = Page.Locator(".ai-message").First;
        await Expect(aiMessage.Locator(".message-copy-btn")).ToHaveCountAsync(1, new() { Timeout = 8000 });

        var finalResponse = aiMessage.Locator(".ai-final-response");
        await Expect(finalResponse).ToContainTextAsync("Here is the final summary", new() { Timeout = 5000 });

        // Make the summarize call fail: performSummarizeAndClear then goes COMPACTING -> IDLE with an
        // empty accumulatedText while the finished message is still on screen. That transition used to
        // run finalizeResult() a second time, and the second call rebuilt .ai-final-response from the
        // already emptied step, which wiped the visible answer.
        await Page.EvaluateAsync("() => { window.__bridgeOverride.SummarizeAndCompactAsync = async () => false; }");

        await Page.Locator("#clear-chat-btn").ClickAsync();
        await Expect(Page.Locator("#confirm-dialog")).ToBeVisibleAsync(new() { Timeout = 3000 });

        await Page.Locator("#confirm-action-summarize").CheckAsync();
        await Page.Locator("#confirm-dialog #confirm-dialog-confirm").ClickAsync();
        await Expect(Page.Locator("#confirm-dialog")).ToBeHiddenAsync(new() { Timeout = 3000 });

        // The answer keeps both its text and its copy button.
        await Expect(finalResponse).ToContainTextAsync("Here is the final summary", new() { Timeout = 3000 });
        await Expect(aiMessage.Locator(".message-copy-btn")).ToHaveCountAsync(1, new() { Timeout = 3000 });
    }

    [Test]
    [Category("Chat")]
    public async Task CopyAnswer_Collapsible_SurvivesHistoryCompactionAfterAnswer()
    {
        await GotoWithMockAsync("webview-mock-collapsible.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Find files");
        await Page.Locator("#mainBtn").ClickAsync();

        var aiMessage = Page.Locator(".ai-message").First;
        await Expect(aiMessage.Locator(".message-copy-btn")).ToHaveCountAsync(1, new() { Timeout = 8000 });

        var finalResponse = aiMessage.Locator(".ai-final-response");
        await Expect(finalResponse).ToContainTextAsync("Here is the final summary", new() { Timeout = 5000 });

        // The backend compacts history after ChatSessionComplete (Completing -> CompactingHistory) and always
        // closes it with CompactionEnd { Continues: false }, i.e. COMPACTING -> IDLE with no user action.
        // That transition used to run finalizeResult() a second time and wipe the visible answer.
        await Page.EvaluateAsync<object>("() => window.__emitBridgeMessage({ Type: 'CompactionStart', Payload: null })");
        await Page.EvaluateAsync<object>("() => window.__emitBridgeMessage({ Type: 'CompactionEnd', Payload: null, Continues: false })");

        await Expect(Page.Locator("#conn-status")).ToHaveTextAsync("Connected", new() { Timeout = 3000 });
        await Expect(finalResponse).ToContainTextAsync("Here is the final summary", new() { Timeout = 3000 });
        await Expect(aiMessage.Locator(".message-copy-btn")).ToHaveCountAsync(1, new() { Timeout = 3000 });
    }

    private async Task CaptureClipboardAsync()
    {
        await Page.EvaluateAsync<object>(@"() => {
            window.__lastCopied = null;
            const orig = window.__hostOverride?.CopyToClipboardAsync;
            if (orig) {
                window.__hostOverride.CopyToClipboardAsync = async (t) => { window.__lastCopied = t; return await orig(t); };
            } else {
                window.__hostOverride = { CopyToClipboardAsync: async (t) => { window.__lastCopied = t; return true; } };
            }
        }");
    }

    private async Task ExpectLastCopiedAsync(string expected)
    {
        var waitUntil = DateTime.UtcNow.AddMilliseconds(3000);
        string? copied = null;

        while (DateTime.UtcNow < waitUntil)
        {
            copied = await Page.EvaluateAsync<string>("() => window.__lastCopied");
            if (copied == expected) return;
            await Task.Delay(100);
        }

        Assert.That(copied, Is.EqualTo(expected));
    }


}
