namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public class ClearChatTests : AppTestBase
{
    [Test]
    [Category("ClearChat")]
    public async Task ClearChat_RemovesAllMessagesFromContainer()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for streaming to complete
        await Expect(Page.Locator(".ai-response-container")).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => !document.querySelector('.ai-response-container')?.classList.contains('is-generating')");

        // IMPORTANT: Wait for app to return to IDLE state (not just finishing)
        // The FINISHING state doesn't allow clear-chat to work properly
        // Wait until button is "Send" (not "Stop") which indicates IDLE
        await Page.WaitForFunctionAsync("() => document.querySelector('#mainBtn')?.textContent.trim() === 'Send'");

        await Task.Delay(500); // Extra buffer for state transitions

        // Verify messages exist before clearing
        var messagesBefore = await Page.Locator("#chat-container > *").CountAsync();
        Assert.That(messagesBefore, Is.GreaterThan(0), "Should have messages before clearing");

        await Page.Locator("#clear-chat-btn").ClickAsync();

        var confirmDialog = Page.Locator("#confirm-dialog");
        await Expect(confirmDialog).ToBeVisibleAsync(new() { Timeout = 3000 });
        await Page.Locator("#confirm-dialog #confirm-dialog-confirm").ClickAsync();

        // Wait for dialog to close
        await Expect(confirmDialog).ToBeHiddenAsync(new() { Timeout = 3000 });

        // Wait a bit more for DOM to settle after clear
        await Task.Delay(500);

        // Verify chat container is empty
        var finalCount = await Page.Locator("#chat-container > *").CountAsync();
        Assert.That(finalCount, Is.EqualTo(0), "Chat container should be empty after clear");
    }

    [Test]
    [Category("ClearChat")]
    public async Task ClearChat_CancelledByUser_KeepsMessages()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for streaming to complete
        await Expect(Page.Locator(".ai-response-container")).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => !document.querySelector('.ai-response-container')?.classList.contains('is-generating')");

        await Page.Locator("#clear-chat-btn").ClickAsync();

        var confirmDialog = Page.Locator("#confirm-dialog");
        await Expect(confirmDialog).ToBeVisibleAsync(new() { Timeout = 3000 });
        await Page.Locator("#confirm-dialog #confirm-dialog-cancel").ClickAsync();

        await Expect(Page.Locator("#chat-container > *")).Not.ToHaveCountAsync(0);
    }
}
