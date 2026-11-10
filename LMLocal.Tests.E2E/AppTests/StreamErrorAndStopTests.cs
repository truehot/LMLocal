namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public class StreamErrorAndStopTests : AppTestBase
{
    [Test]
    [Category("StreamError")]
    public async Task StreamError_ShowsErrorInStatusBar()
    {
        await GotoWithMockAsync("webview-mock-stream-error.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        // Errors are now displayed in the chat as part of the AI message (generation-stopped)
        // and the status bar shows the generic "Error" label. Assert both behaviors.
        await Expect(Page.Locator("#status-text")).ToContainTextAsync("Error", new() { Timeout = 5000 });
        await Expect(Page.Locator(".ai-message .generation-stopped")).ToContainTextAsync("model crashed", new() { Timeout = 5000 });
    }

    [Test]
    [Category("StreamError")]
    public async Task StreamError_RemovesEmptyAiMessage()
    {
        await GotoWithMockAsync("webview-mock-stream-error.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();
        // Wait for the error to appear in the chat
        await Expect(Page.Locator(".ai-message .generation-stopped")).ToContainTextAsync("model crashed", new() { Timeout = 5000 });

        var aiCountAfterError = await Page.Locator(".ai-message").CountAsync();
        if (aiCountAfterError > 0)
        {
            var aiText = await Page.Locator(".ai-message").First.InnerTextAsync();
            Assert.That(string.IsNullOrWhiteSpace(aiText), Is.False, "If an AI message remains after a stream error it must contain an error/stop message, not be empty");
        }
    }

    [Test]
    [Category("StreamError")]
    public async Task StreamError_RestoresSendButton()
    {
        await GotoWithMockAsync("webview-mock-stream-error.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for the chat to show the error message
        await Expect(Page.Locator(".ai-message .generation-stopped")).ToContainTextAsync("model crashed", new() { Timeout = 5000 });

        await Expect(Page.Locator("#mainBtn")).ToHaveTextAsync("Send");
        await Expect(Page.Locator("#userInput")).ToBeEnabledAsync();
    }

    [Test]
    [Category("Stop")]
    public async Task Stop_ButtonRestoresToSendAfterStopping()
    {
        await GotoWithMockAsync("webview-mock-slow-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();

        await Expect(Page.Locator("#mainBtn"))
            .ToHaveTextAsync("Stop", new() { Timeout = 3000 });

        await Page.Locator("#mainBtn").ClickAsync();

        // After stopping, the app sends ChatSessionComplete (or ChatSessionCancelled)
        // Wait for the session to complete/cancel, which triggers state reset to IDLE
        // This is indicated by button returning to "Send"
        await Page.WaitForFunctionAsync("() => document.querySelector('#mainBtn')?.textContent.trim() === 'Send'");
    }

    /// <summary>
    /// Starts a parallel SubAgent group (3 running cards, no StreamToolEnd) and presses Stop.
    /// </summary>
    private async Task StopDuringRunningSubAgentsAsync()
    {
        await GotoWithMockAsync("webview-mock-tooling-stop.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Read the file with 3 agents");
        await Page.Locator("#mainBtn").ClickAsync();

        await Expect(Page.Locator(".tool-status")).ToHaveCountAsync(3, new() { Timeout = 4000 });
        await Expect(Page.Locator(".tool-status").First).ToContainTextAsync("read_file_lines", new() { Timeout = 4000 });
        await Expect(Page.Locator("#mainBtn")).ToHaveTextAsync("Stop", new() { Timeout = 3000 });

        await Page.Locator("#mainBtn").ClickAsync();

        await Expect(Page.Locator(".ai-message .generation-stopped"))
            .ToContainTextAsync("Generation stopped by user", new() { Timeout = 5000 });
    }

    [Test]
    [Category("Stop")]
    public async Task Stop_DuringParallelSubAgents_ClosesRunningToolCards()
    {
        await StopDuringRunningSubAgentsAsync();

        // No card may keep spinning: the backend never sends StreamToolEnd after Stop.
        await Expect(Page.Locator(".tool-status")).ToHaveCountAsync(0, new() { Timeout = 3000 });
        await Expect(Page.Locator(".tool-status-stopped")).ToHaveCountAsync(3);
        await Expect(Page.Locator(".tool-status-stopped").First).ToContainTextAsync("Stopped.");

        // Stop is neither a success nor an error.
        await Expect(Page.Locator(".tool-status-completed")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".tool-status-error")).ToHaveCountAsync(0);

        await Expect(Page.Locator(".tool-group-status-message")).ToContainTextAsync("(stopped)");
    }

    [Test]
    [Category("Stop")]
    public async Task Stop_DuringParallelSubAgents_StatusBarShowsStoppedNotError()
    {
        await StopDuringRunningSubAgentsAsync();

        var statusText = Page.Locator("#status-text");
        await Expect(statusText).ToHaveTextAsync("Stopped", new() { Timeout = 3000 });
        Assert.That(await statusText.EvaluateAsync<bool>("el => el.classList.contains('error')"), Is.False,
            "A user stop must not be styled as an error.");

        // Stop keeps the app usable: the user can send again.
        await Expect(Page.Locator("#mainBtn")).ToHaveTextAsync("Send");
        await Expect(Page.Locator("#userInput")).ToBeEnabledAsync();
    }

    [Test]
    [Category("Stop")]
    public async Task Stop_ThenNewRequest_StatusBarNoLongerShowsStopped()
    {
        await StopDuringRunningSubAgentsAsync();
        await Expect(Page.Locator("#status-text")).ToHaveTextAsync("Stopped", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Try again");
        await Page.Locator("#mainBtn").ClickAsync();

        // The "stopped" flag must not stick to the next request.
        await Expect(Page.Locator("#status-text")).Not.ToHaveTextAsync("Stopped", new() { Timeout = 3000 });
    }
}
