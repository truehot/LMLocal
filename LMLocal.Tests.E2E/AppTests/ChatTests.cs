using System.Text.RegularExpressions;

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

        await Expect(Page.Locator(".user-message"))
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
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

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

        await Expect(Page.Locator(".user-message"))
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

    // Note: CopyButton_ShowsCopiedLabelAfterClick already exists elsewhere in the suite.


}
