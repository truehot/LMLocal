namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public class ToolingTests : AppTestBase
{
    [Test]
    [Category("Tools")]
    public async Task ToolCall_DisplaysToolStatus()
    {
        await GotoWithMockAsync("webview-mock-tooling.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Search and find");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for tool container to be populated
        await Expect(Page.Locator(".tool-status"))
            .ToBeVisibleAsync(new() { Timeout = 3000 });

        // Check that tool status message is displayed
        await Expect(Page.Locator(".tool-status"))
            .ToContainTextAsync("Searching for", new() { Timeout = 3000 });
    }

    [Test]
    [Category("Tools")]
    public async Task ToolEnd_CompletedToolUpdatesStatus()
    {
        await GotoWithMockAsync("webview-mock-tooling.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Search and find");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for completed tool status
        await Expect(Page.Locator(".tool-status-completed"))
            .ToBeVisibleAsync(new() { Timeout = 3000 });

        // Check that both initial and completion messages are displayed
        var completedToolText = await Page.Locator(".tool-status-completed").TextContentAsync();
        Assert.That(completedToolText, Does.Contain("Searching for"), "Should contain initial search message");
        Assert.That(completedToolText, Does.Contain("Found 3 matches"), "Should contain completion message");
    }

    [Test]
    [Category("Tools")]
    public async Task ToolEnd_ErrorToolUpdatesStatus()
    {
        await GotoWithMockAsync("webview-mock-tooling.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Search and find");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for error tool status
        await Expect(Page.Locator(".tool-status-error"))
            .ToBeVisibleAsync(new() { Timeout = 3000 });

        // Check that both initial and error messages are displayed
        var errorToolText = await Page.Locator(".tool-status-error").TextContentAsync();
        Assert.That(errorToolText, Does.Contain("Finding symbol references"), "Should contain initial message");
        Assert.That(errorToolText, Does.Contain("not found"), "Should contain error text");
    }

    [Test]
    [Category("Tools")]
    public async Task MultipleTools_DisplayedInOrder()
    {
        await GotoWithMockAsync("webview-mock-tooling.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Search and find");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for all tool statuses to be rendered
        await Page.WaitForFunctionAsync("() => document.querySelectorAll('.tool-status, .tool-status-completed, .tool-status-error').length >= 2");

        // Verify both tools are present
        var toolCount = await Page.Locator(".tool-status, .tool-status-completed, .tool-status-error").CountAsync();
        Assert.That(toolCount, Is.GreaterThanOrEqualTo(2), "Should have at least 2 tool status elements");

        // Verify first tool is completed and contains the search message and completion message
        var completedTools = await Page.Locator(".tool-status-completed").AllAsync();
        Assert.That(completedTools.Count, Is.GreaterThan(0), "Should have at least one completed tool");
        var firstToolText = await completedTools[0].TextContentAsync();
        Assert.That(firstToolText, Does.Contain("Searching for"), "Completed tool should contain initial message");
        Assert.That(firstToolText, Does.Contain("Found 3 matches"), "Completed tool should contain completion message");

        // Verify second tool has error
        var errorElements = await Page.Locator(".tool-status-error").AllAsync();
        Assert.That(errorElements.Count, Is.GreaterThan(0), "Should have at least one error tool status");
        var errorToolText = await errorElements[0].TextContentAsync();
        Assert.That(errorToolText, Does.Contain("Finding symbol references"), "Error tool should contain initial message");
        Assert.That(errorToolText, Does.Contain("not found"), "Error tool should contain error message");
    }

    [Test]
    [Category("Tools")]
    public async Task StreamingContinues_AfterToolExecution()
    {
        await GotoWithMockAsync("webview-mock-tooling.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Search and find");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for streaming to complete
        await Expect(Page.Locator(".ai-response-container")).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => !document.querySelector('.ai-response-container')?.classList.contains('is-generating')");

        // Verify response contains both tool outputs and final content
        var messageText = await Page.Locator(".ai-message").TextContentAsync();
        Assert.That(messageText, Does.Contain("Based on the search results"), "Response should contain content before tool");
        Assert.That(messageText, Does.Contain("here is the summary"), "Response should contain content after tool");
    }
}
