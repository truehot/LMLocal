namespace LMLocal.Tests.E2E.AppTests;

/// <summary>
/// Regenerate (rewrites the last answer) and Edit (returns the last prompt to the input) for the last exchange only.
/// </summary>
[TestFixture]
public class RegenerateTests : AppTestBase
{
    private ILocator AiMessage => Page.Locator(".ai-message").First;
    private ILocator UserMessage => Page.Locator(".user-message").First;
    private ILocator EditBanner => Page.Locator(".edit-prompt-banner");
    private ILocator PromptInput => Page.Locator("#userInput");

    private async Task StartAsync(string mockFileName)
    {
        await GotoWithMockAsync(mockFileName);
        await Expect(Page.Locator("#conn-status")).ToHaveTextAsync("Connected", new() { Timeout = 3000 });
    }

    private async Task SendPromptAsync(string text)
    {
        await PromptInput.FillAsync(text);
        await Page.Locator("#mainBtn").ClickAsync();
    }

    private async Task WaitForIdleAsync()
    {
        await Expect(Page.Locator("#mainBtn")).ToHaveTextAsync("Send", new() { Timeout = 10000 });
    }

    /// <summary>
    /// The replaceLast marker of every request the bridge received; empty string for a normal prompt.
    /// </summary>
    private Task<string[]> ReplaceLastCallsAsync() =>
        Page.EvaluateAsync<string[]>("() => window.__bridgeCalls.map(c => c.replaceLast || '')");

    private Task<string> LastPromptAsync() =>
        Page.EvaluateAsync<string>("() => { const c = window.__bridgeCalls; return c.length ? (c[c.length - 1].prompt || '') : ''; }");

    private Task<string[]> ResetCallsAsync() =>
        Page.EvaluateAsync<string[]>("() => window.__bridgeResetCalls");

    private async Task OpenEditModeAsync()
    {
        await Expect(UserMessage.Locator(".message-edit-btn")).ToHaveCountAsync(1, new() { Timeout = 5000 });
        await UserMessage.HoverAsync();
        await UserMessage.Locator(".message-edit-btn").ClickAsync();
    }

    [Test]
    [Category("Regenerate")]
    public async Task Regenerate_ReplacesTheAnswerWithTheSamePrompt()
    {
        await StartAsync("webview-mock-regenerate.js");

        await SendPromptAsync("Hello");
        await WaitForIdleAsync();

        await Expect(AiMessage.Locator(".message-regenerate-btn")).ToHaveCountAsync(1, new() { Timeout = 5000 });
        await Expect(UserMessage.Locator(".message-edit-btn")).ToHaveCountAsync(1);
        await Expect(AiMessage.Locator(".ai-response-container")).ToContainTextAsync("Answer #1");

        await AiMessage.HoverAsync();
        await AiMessage.Locator(".message-regenerate-btn").ClickAsync();

        // The old answer disappears, the prompt stays and the new answer takes its place.
        await Expect(AiMessage.Locator(".ai-response-container")).ToContainTextAsync("Answer #2", new() { Timeout = 5000 });
        await WaitForIdleAsync();

        await Expect(Page.Locator(".ai-message")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".user-message")).ToHaveCountAsync(1);

        Assert.That(await ReplaceLastCallsAsync(), Is.EqualTo(new[] { "", "answer" }),
            "Regenerate must be one bridge call marked as replacing the answer");
        Assert.That(await LastPromptAsync(), Is.Empty,
            "The prompt already sits in the history; regenerating must not send it again");
        Assert.That(await ResetCallsAsync(), Is.Empty,
            "The exchange is replaced inside the session, not by a separate history reset");
    }

    [Test]
    [Category("Regenerate")]
    public async Task ExchangeActions_ExistOnlyForTheLastExchange()
    {
        await StartAsync("webview-mock-regenerate.js");

        await SendPromptAsync("First");
        await WaitForIdleAsync();

        await SendPromptAsync("Second");
        await WaitForIdleAsync();

        await Expect(Page.Locator(".user-message")).ToHaveCountAsync(2);
        await Expect(Page.Locator(".ai-message")).ToHaveCountAsync(2);

        await Expect(Page.Locator(".message-regenerate-btn")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".message-edit-btn")).ToHaveCountAsync(1);

        await Expect(Page.Locator(".user-message").Nth(1).Locator(".message-edit-btn")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".user-message").First.Locator(".message-edit-btn")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".ai-message").Nth(1).Locator(".message-regenerate-btn")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".ai-message").First.Locator(".message-regenerate-btn")).ToHaveCountAsync(0);
    }

    [Test]
    [Category("Regenerate")]
    public async Task Regenerate_DoubleClickSendsOneRequest()
    {
        await StartAsync("webview-mock-regenerate.js");

        await SendPromptAsync("Hello");
        await WaitForIdleAsync();

        await Expect(AiMessage.Locator(".message-regenerate-btn")).ToHaveCountAsync(1, new() { Timeout = 5000 });
        await AiMessage.HoverAsync();

        // A real double click, dispatched synchronously so both hits arrive before the round starts.
        await AiMessage.Locator(".message-regenerate-btn").EvaluateAsync("el => { el.click(); el.click(); }");

        await Expect(AiMessage.Locator(".ai-response-container")).ToContainTextAsync("Answer #2", new() { Timeout = 5000 });
        await WaitForIdleAsync();

        Assert.That(await ReplaceLastCallsAsync(), Has.Length.EqualTo(2),
            "A double click must not start a second regeneration");
        await Expect(Page.Locator(".ai-message")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("Regenerate")]
    public async Task Edit_ReturnsThePromptWithoutTouchingTheChat()
    {
        await StartAsync("webview-mock-regenerate.js");

        await SendPromptAsync("Hello");
        await WaitForIdleAsync();

        await OpenEditModeAsync();

        await Expect(EditBanner).ToBeVisibleAsync(new() { Timeout = 3000 });
        await Expect(PromptInput).ToHaveValueAsync("Hello");

        // Nothing is removed and nothing is sent until the user presses Send.
        await Expect(Page.Locator(".user-message")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".ai-message")).ToHaveCountAsync(1);
        Assert.That(await ReplaceLastCallsAsync(), Has.Length.EqualTo(1), "Edit must not call the bridge");
    }

    [Test]
    [Category("Regenerate")]
    public async Task Edit_EscapeCancelsAndLeavesTheChatAlone()
    {
        await StartAsync("webview-mock-regenerate.js");

        await SendPromptAsync("Hello");
        await WaitForIdleAsync();

        await OpenEditModeAsync();
        await Expect(EditBanner).ToBeVisibleAsync(new() { Timeout = 3000 });

        await PromptInput.PressAsync("Escape");

        await Expect(PromptInput).ToHaveValueAsync("");
        await Expect(EditBanner).ToBeHiddenAsync(new() { Timeout = 3000 });
        await Expect(Page.Locator(".user-message")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".ai-message")).ToHaveCountAsync(1);
        Assert.That(await ReplaceLastCallsAsync(), Has.Length.EqualTo(1));
    }

    [Test]
    [Category("Regenerate")]
    public async Task Edit_SendReplacesTheLastExchange()
    {
        await StartAsync("webview-mock-regenerate.js");

        await SendPromptAsync("Hello");
        await WaitForIdleAsync();

        await OpenEditModeAsync();
        await Expect(PromptInput).ToHaveValueAsync("Hello");

        await PromptInput.FillAsync("Hello, corrected");
        await Page.Locator("#mainBtn").ClickAsync();

        await Expect(Page.Locator(".user-message")).ToHaveCountAsync(1, new() { Timeout = 5000 });
        await Expect(Page.Locator(".user-message")).ToContainTextAsync("Hello, corrected");
        await WaitForIdleAsync();

        await Expect(Page.Locator(".ai-message")).ToHaveCountAsync(1);
        await Expect(EditBanner).ToBeHiddenAsync(new() { Timeout = 3000 });

        Assert.That(await ReplaceLastCallsAsync(), Has.Length.EqualTo(2));
        Assert.That((await ReplaceLastCallsAsync())[1], Is.EqualTo("exchange"),
            "Sending in edit mode must replace the exchange, not append to it");
        Assert.That(await LastPromptAsync(), Is.EqualTo("Hello, corrected"));
    }

    [Test]
    [Category("Regenerate")]
    public async Task ImagePlaceholderPrompt_FromLoadedSession_HasNoExchangeActions()
    {
        await StartAsync("webview-mock-image-placeholder.js");

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action=\"open-chat-history\"]").ClickAsync();

        var dialog = Page.Locator("#chat-history-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 3000 });

        await Page.Locator("#chat-history-container .chat-history-card").Nth(1)
            .Locator(".chat-history-load").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 5000 });

        await Expect(Page.Locator(".user-message")).ToHaveCountAsync(1, new() { Timeout = 5000 });

        // The log keeps only a placeholder instead of the images, and the prompt text is lost with them.
        await Expect(Page.Locator(".message-regenerate-btn")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".message-edit-btn")).ToHaveCountAsync(0);

        // Copy still works: the placeholder can be copied, it just cannot be answered again.
        await Expect(Page.Locator(".user-message .message-copy-btn")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("Regenerate")]
    public async Task SummaryPlaceholderPrompt_FromLoadedSession_HasNoExchangeActions()
    {
        await StartAsync("webview-mock-image-placeholder.js");

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action=\"open-chat-history\"]").ClickAsync();

        var dialog = Page.Locator("#chat-history-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 3000 });

        await Page.Locator("#chat-history-container .chat-history-card").Nth(2)
            .Locator(".chat-history-load").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 5000 });

        await Expect(Page.Locator(".user-message")).ToHaveCountAsync(1, new() { Timeout = 5000 });
        await Expect(Page.Locator(".user-message")).ToContainTextAsync("Provide a brief summary");

        // The summary request is a service prompt, not something the model can answer again.
        await Expect(Page.Locator(".message-regenerate-btn")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".message-edit-btn")).ToHaveCountAsync(0);
    }

    // ---------- Refused before the session started: the chat is drawn again from the history ----------

    private ILocator UserMessageText => Page.Locator(".user-message .message-content");

    [Test]
    [Category("Regenerate")]
    public async Task Regenerate_Refused_ChatIsDrawnFromTheHistory()
    {
        await StartAsync("webview-mock-regenerate.js");

        await SendPromptAsync("Hello");
        await WaitForIdleAsync();

        await Expect(AiMessage.Locator(".ai-response-container")).ToContainTextAsync("Answer #1");

        await AiMessage.HoverAsync();
        await Page.EvaluateAsync("() => { window.__failReplacement = true; }");
        await AiMessage.Locator(".message-regenerate-btn").ClickAsync();

        // The backend changed nothing, so the chat shows its history again and the reason is a toast, not an answer.
        await Expect(Page.Locator("#app-toast")).ToContainTextAsync("out of sync", new() { Timeout = 5000 });
        await Expect(AiMessage.Locator(".ai-response-container")).ToContainTextAsync("Answer #1", new() { Timeout = 5000 });
        await Expect(Page.Locator(".ai-message")).ToHaveCountAsync(1);
        await Expect(UserMessageText).ToHaveTextAsync("Hello");

        // The chat matches the history again, so the actions are back.
        await Expect(Page.Locator(".message-regenerate-btn")).ToHaveCountAsync(1, new() { Timeout = 5000 });
        await Expect(Page.Locator(".message-edit-btn")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("Regenerate")]
    public async Task Edit_Refused_ChatIsDrawnFromTheHistoryAndThePromptGoesBack()
    {
        await StartAsync("webview-mock-regenerate.js");

        await SendPromptAsync("Hello");
        await WaitForIdleAsync();

        await OpenEditModeAsync();
        await Expect(PromptInput).ToHaveValueAsync("Hello");

        await Page.EvaluateAsync("() => { window.__failReplacement = true; }");

        await PromptInput.FillAsync("Hello, corrected");
        await Page.Locator("#mainBtn").ClickAsync();

        await Expect(Page.Locator("#app-toast")).ToContainTextAsync("out of sync", new() { Timeout = 5000 });
        await Expect(UserMessageText).ToHaveTextAsync("Hello", new() { Timeout = 5000 });
        await Expect(Page.Locator(".ai-message")).ToHaveCountAsync(1);
        await Expect(AiMessage.Locator(".ai-response-container")).ToContainTextAsync("Answer #1");

        // The corrected prompt is not lost: it waits in the field as a normal prompt.
        await Expect(PromptInput).ToHaveValueAsync("Hello, corrected");
        await Expect(EditBanner).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("Regenerate")]
    public async Task EditSend_SessionLockTimeout_ChatMatchesTheHistory()
    {
        await StartAsync("webview-mock-regenerate.js");

        await SendPromptAsync("Hello");
        await WaitForIdleAsync();

        await OpenEditModeAsync();

        // The session lock times out before the backend even looks at the history, so the old exchange is still there.
        await Page.EvaluateAsync("() => { window.__failSessionLock = true; }");

        await PromptInput.FillAsync("Hello, corrected");
        await Page.Locator("#mainBtn").ClickAsync();

        await Expect(Page.Locator("#app-toast")).ToContainTextAsync("still finishing", new() { Timeout = 5000 });
        await Expect(UserMessageText).ToHaveTextAsync("Hello", new() { Timeout = 5000 });
        await Expect(AiMessage.Locator(".ai-response-container")).ToContainTextAsync("Answer #1");
        await Expect(PromptInput).ToHaveValueAsync("Hello, corrected");
    }

    [Test]
    [Category("Regenerate")]
    public async Task ErrorAfterStart_IsAnOrdinaryFailedRound()
    {
        await StartAsync("webview-mock-regenerate.js");

        await SendPromptAsync("Hello");
        await WaitForIdleAsync();

        await AiMessage.HoverAsync();
        await Page.EvaluateAsync("() => { window.__failAfterStart = true; }");
        await AiMessage.Locator(".message-regenerate-btn").ClickAsync();

        // The history was replaced before the round failed, so the error stays in the chat with Continue, not a redraw.
        await Expect(Page.Locator(".generation-stopped .continue-btn")).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Expect(Page.Locator(".ai-message")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".ai-message")).Not.ToContainTextAsync("Answer #1");
    }

    [Test]
    [Category("Regenerate")]
    public async Task Continue_WhileEditing_KeepsTheNewActionsDisabled()
    {
        await StartAsync("webview-mock-regenerate.js");

        await Page.EvaluateAsync("() => { window.__failAfterStart = true; }");
        await SendPromptAsync("Hello");

        var continueButton = Page.Locator(".generation-stopped .continue-btn");
        await Expect(continueButton).ToBeVisibleAsync(new() { Timeout = 5000 });

        await OpenEditModeAsync();
        await Expect(EditBanner).ToBeVisibleAsync(new() { Timeout = 3000 });

        await continueButton.ClickAsync();
        await WaitForIdleAsync();

        // Still editing: the actions of the finished round must not overwrite the field.
        await Expect(EditBanner).ToBeVisibleAsync();
        await Expect(Page.Locator(".message-regenerate-btn")).ToBeDisabledAsync(new() { Timeout = 5000 });
        await Expect(Page.Locator(".message-edit-btn")).ToBeDisabledAsync();
    }

    // ---------- The draft in the field survives an edit ----------

    [Test]
    [Category("Regenerate")]
    public async Task Edit_DraftIsRestoredAfterCancel()
    {
        await StartAsync("webview-mock-regenerate.js");

        await SendPromptAsync("Hello");
        await WaitForIdleAsync();

        await PromptInput.FillAsync("my unfinished draft");
        await OpenEditModeAsync();

        await Expect(PromptInput).ToHaveValueAsync("Hello");
        await Expect(Page.Locator(".edit-prompt-hint")).ToBeVisibleAsync(new() { Timeout = 3000 });

        await PromptInput.PressAsync("Escape");

        await Expect(PromptInput).ToHaveValueAsync("my unfinished draft");
        await Expect(EditBanner).ToBeHiddenAsync(new() { Timeout = 3000 });
        await Expect(Page.Locator(".user-message")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("Regenerate")]
    public async Task Edit_DraftIsRestoredAfterSend()
    {
        await StartAsync("webview-mock-regenerate.js");

        await SendPromptAsync("Hello");
        await WaitForIdleAsync();

        await PromptInput.FillAsync("my unfinished draft");
        await OpenEditModeAsync();
        await Expect(PromptInput).ToHaveValueAsync("Hello");

        await PromptInput.FillAsync("Hello, corrected");
        await Page.Locator("#mainBtn").ClickAsync();

        await Expect(Page.Locator(".user-message")).ToContainTextAsync("Hello, corrected", new() { Timeout = 5000 });
        await WaitForIdleAsync();

        await Expect(PromptInput).ToHaveValueAsync("my unfinished draft", new() { Timeout = 3000 });
    }

    // ---------- Edit mode must not overwrite the field it is holding ----------

    [Test]
    [Category("Regenerate")]
    public async Task Edit_DisablesExchangeActionsWhileEditing()
    {
        await StartAsync("webview-mock-regenerate.js");

        await SendPromptAsync("Hello");
        await WaitForIdleAsync();

        await OpenEditModeAsync();

        await Expect(AiMessage.Locator(".message-regenerate-btn")).ToBeDisabledAsync();
        await Expect(UserMessage.Locator(".message-edit-btn")).ToBeDisabledAsync();

        // A disabled button does not start anything.
        await AiMessage.HoverAsync();
        await AiMessage.Locator(".message-regenerate-btn").ClickAsync(new() { Force = true });
        Assert.That(await ReplaceLastCallsAsync(), Has.Length.EqualTo(1), "A disabled Regenerate must not send a request");

        await PromptInput.PressAsync("Escape");

        await Expect(AiMessage.Locator(".message-regenerate-btn")).ToBeEnabledAsync(new() { Timeout = 3000 });
        await Expect(UserMessage.Locator(".message-edit-btn")).ToBeEnabledAsync();
    }
}
