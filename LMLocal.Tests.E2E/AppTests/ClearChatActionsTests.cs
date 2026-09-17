namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public class ClearChatActionsTests : AppTestBase
{
    private async Task SendMessageAndWaitIdleAsync()
    {
        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        await Expect(Page.Locator(".ai-response-container"))
            .ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync(
            "() => !document.querySelector('.ai-response-container')?.classList.contains('is-generating')");
        await Page.WaitForFunctionAsync(
            "() => document.querySelector('#mainBtn')?.textContent.trim() === 'Send'");
        await Task.Delay(300);
    }

    private async Task OpenClearDialogAsync()
    {
        await Page.Locator("#clear-chat-btn").ClickAsync();
        await Expect(Page.Locator("#confirm-dialog")).ToBeVisibleAsync(new() { Timeout = 3000 });
    }

    private async Task ConfirmClearAsync(string radioId)
    {
        await Page.Locator(radioId).CheckAsync();
        await Page.Locator("#confirm-dialog #confirm-dialog-confirm").ClickAsync();
        await Expect(Page.Locator("#confirm-dialog")).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    private async Task ExpectCarriedOverHistoryAsync()
    {
        await Expect(Page.Locator("#chat-container > *")).ToHaveCountAsync(2);
        // The assistant message is rendered asynchronously (via a Web Worker markdown
        // parser), so use auto-waiting assertions instead of reading InnerTextAsync
        // immediately, which races the async rendering.
        await Expect(Page.Locator("#chat-container")).ToContainTextAsync("moved prompt");
        await Expect(Page.Locator("#chat-container")).ToContainTextAsync("moved response");
    }

    [Test]
    [Category("ClearChat")]
    public async Task ClearChat_LastPromptOption_ReRendersCarriedOverHistory()
    {
        await GotoWithMockAsync("webview-mock-clear-actions.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await SendMessageAndWaitIdleAsync();
        await OpenClearDialogAsync();
        await ConfirmClearAsync("#confirm-action-last-prompt");

        await ExpectCarriedOverHistoryAsync();

        var action = await Page.EvaluateAsync<string>("window.__resetAction");
        Assert.That(action, Is.EqualTo("last-prompt"));
    }

    [Test]
    [Category("ClearChat")]
    public async Task ClearChat_LastExchangeOption_ReRendersCarriedOverHistory()
    {
        await GotoWithMockAsync("webview-mock-clear-actions.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await SendMessageAndWaitIdleAsync();
        await OpenClearDialogAsync();
        await ConfirmClearAsync("#confirm-action-last-exchange");

        await ExpectCarriedOverHistoryAsync();

        var action = await Page.EvaluateAsync<string>("window.__resetAction");
        Assert.That(action, Is.EqualTo("last-exchange"));
    }

    [Test]
    [Category("ClearChat")]
    public async Task ClearChat_SummarizeOption_SummarizesThenReRendersCarriedOverHistory()
    {
        await GotoWithMockAsync("webview-mock-clear-actions.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await SendMessageAndWaitIdleAsync();
        await OpenClearDialogAsync();

        var summarizeRadio = Page.Locator("#confirm-action-summarize");
        await Expect(summarizeRadio).ToBeEnabledAsync();

        await ConfirmClearAsync("#confirm-action-summarize");
        await ExpectCarriedOverHistoryAsync();

        var summarizeModel = await Page.EvaluateAsync<string>("window.__summarizeModel");
        Assert.That(summarizeModel, Is.EqualTo("test-model-instance"));

        // performSummarizeAndClear reuses the last-prompt carry-over path.
        var action = await Page.EvaluateAsync<string>("window.__resetAction");
        Assert.That(action, Is.EqualTo("last-prompt"));
    }
}
