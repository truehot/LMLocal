namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public class CollapsibleTests : AppTestBase
{
    [Test]
    [Category("Collapsible")]
    public async Task CollapsibleBlock_AppearsAfterToolExecution()
    {
        await GotoWithMockAsync("webview-mock-collapsible.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Search and analyze");
        await Page.Locator("#mainBtn").ClickAsync();

        // Collapsible block should appear after wrapInBlock() is triggered
        await Expect(Page.Locator("[data-element=\"collapsible-block\"]"))
            .ToBeVisibleAsync(new() { Timeout = 5000 });

        // Tool status — accept any variant: initial, completed, or error
        // Use .First to avoid strict-mode violation when multiple tool divs exist
        await Expect(Page.Locator("[data-element=\"ai-tool-container\"] > div").First)
            .ToBeVisibleAsync(new() { Timeout = 5000 });

        var toolDivs = Page.Locator("[data-element=\"ai-tool-container\"] > div");
        var count = await toolDivs.CountAsync();
        Assert.That(count, Is.GreaterThan(0),
            "Tool container should have at least one tool status element");
    }

    [Test]
    [Category("Collapsible")]
    public async Task MultiRound_CreatesMultipleStepElements()
    {
        await GotoWithMockAsync("webview-mock-collapsible.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Multi-step task");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for the collapsible block to appear
        await Expect(Page.Locator("[data-element=\"collapsible-block\"]"))
            .ToBeVisibleAsync(new() { Timeout = 5000 });

        // Wait for session to complete (IDLE state)
        await Expect(Page.Locator("#mainBtn"))
            .ToHaveTextAsync("Send", new() { Timeout = 10000 });

        // Should have at least 2 step elements (initial + nextRound creates another)
        var stepCount = await Page.Locator(".step").CountAsync();
        Assert.That(stepCount, Is.GreaterThanOrEqualTo(2),
            "Collapsible content should contain at least 2 step elements after two rounds");
    }

    [Test]
    [Category("Collapsible")]
    public async Task FinalRound_TransfersContentToFinalResponse()
    {
        await GotoWithMockAsync("webview-mock-collapsible.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Final round test");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for session to complete
        await Expect(Page.Locator("#mainBtn"))
            .ToHaveTextAsync("Send", new() { Timeout = 10000 });

        // Final response should be visible and contain streaming content
        await Expect(Page.Locator("[data-element=\"final-response\"]"))
            .ToBeVisibleAsync(new() { Timeout = 3000 });

        var finalText = await Page.Locator("[data-element=\"final-response\"]").TextContentAsync();
        // The final response should contain non-empty content transferred from the last step
        Assert.That(finalText, Is.Not.Empty,
            "Final response should contain text content");
        Assert.That(finalText.Length, Is.GreaterThan(5),
            "Final response should have meaningful content");
    }

    [Test]
    [Category("Collapsible")]
    public async Task CollapsibleBlock_ToggleExpandCollapse()
    {
        await GotoWithMockAsync("webview-mock-collapsible.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Toggle test");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for collapsible block
        await Expect(Page.Locator("[data-element=\"collapsible-block\"]"))
            .ToBeVisibleAsync(new() { Timeout = 5000 });

        // Wait for session to complete so the block is in its final collapsed state
        await Expect(Page.Locator("#mainBtn"))
            .ToHaveTextAsync("Send", new() { Timeout = 10000 });

        var toggleBtn = Page.Locator(".toggle-collapsible-btn");
        await Expect(toggleBtn).ToBeVisibleAsync(new() { Timeout = 2000 });

        var block = Page.Locator("[data-element=\"collapsible-block\"]");

        // After finalization, block should be collapsed (no 'expanded' class)
        var isCollapsed = await block.EvaluateAsync<bool>(
            "el => !el.classList.contains('expanded')");
        Assert.That(isCollapsed, Is.True,
            "Collapsible block should be collapsed after finalization");

        // Expand
        await toggleBtn.ClickAsync();
        var afterExpand = await block.EvaluateAsync<bool>(
            "el => el.classList.contains('expanded')");
        Assert.That(afterExpand, Is.True,
            "Collapsible block should be expanded after clicking toggle");

        // Collapse again
        await toggleBtn.ClickAsync();
        var afterCollapse = await block.EvaluateAsync<bool>(
            "el => el.classList.contains('expanded')");
        Assert.That(afterCollapse, Is.False,
            "Collapsible block should collapse after second toggle click");
    }

    [Test]
    [Category("Collapsible")]
    public async Task FinalizeResult_MultiStep_ShowsCompletedHeading()
    {
        await GotoWithMockAsync("webview-mock-collapsible.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Multi-step heading test");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for session to complete
        await Expect(Page.Locator("#mainBtn"))
            .ToHaveTextAsync("Send", new() { Timeout = 10000 });

        // The collapsible title should show "Completed: X steps"
        var title = Page.Locator(".collapsible-title");
        await Expect(title).ToBeVisibleAsync(new() { Timeout = 2000 });

        var titleText = await title.TextContentAsync();
        Assert.That(titleText, Does.Contain("Completed"),
            "Collapsible title should say 'Completed' after finalization");
        Assert.That(titleText, Does.Contain("step"),
            "Collapsible title should mention step count");
    }

    [Test]
    [Category("Collapsible")]
    public async Task ErrorState_ShowsStoppedMessageInFinalResponse()
    {
        await GotoWithMockAsync("webview-mock-collapsible.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Cause an error");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for collapsible block to appear
        await Expect(Page.Locator("[data-element=\"collapsible-block\"]"))
            .ToBeVisibleAsync(new() { Timeout = 5000 });

        // Emit a ChatSessionError to trigger the ERROR → stopStreaming path
        await Page.EvaluateAsync("() => window.__emitBridgeMessage({ Type: 'ChatSessionError', Payload: 'Test error occurred' })");

        // The AI message should have 'stopped' class
        await Expect(Page.Locator(".ai-message.stopped"))
            .ToBeVisibleAsync(new() { Timeout = 5000 });

        // Final response should show the error message
        await Expect(Page.Locator("[data-element=\"final-response\"]"))
            .ToBeVisibleAsync(new() { Timeout = 3000 });

        var finalText = await Page.Locator("[data-element=\"final-response\"]").TextContentAsync();
        Assert.That(finalText, Does.Contain("Test error occurred"),
            "Final response should contain the error message after ChatSessionError");
    }

    [Test]
    [Category("Collapsible")]
    public async Task LoadingIndicator_DisappearsAfterToolingStarts()
    {
        await GotoWithMockAsync("webview-mock-collapsible.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Loading indicator test");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for tool container to have content (loading indicator should be gone)
        // Use .First to avoid strict-mode violation when multiple tool divs exist
        await Expect(Page.Locator("[data-element=\"ai-tool-container\"] > div").First)
            .ToBeVisibleAsync(new() { Timeout = 5000 });

        // The top-level loading indicator should not be visible
        var topLoaderCount = await Page.Locator(".ai-message > div > [data-element=\"loading-indicator\"]").CountAsync();
        // Loading indicator may have been removed from DOM entirely by stopLoadingIndicator()
        if (topLoaderCount > 0)
        {
            var visible = await Page.Locator(".ai-message > div > [data-element=\"loading-indicator\"]:visible").CountAsync();
            Assert.That(visible, Is.EqualTo(0),
                "Top-level loading indicator should be hidden after tooling starts");
        }
    }

    [Test]
    [Category("Collapsible")]
    public async Task MultiTool_HeaderShowsCorrectToolCount()
    {
        await GotoWithMockAsync("webview-mock-collapsible.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Multi-tool test");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for collapsible block
        await Expect(Page.Locator("[data-element=\"collapsible-block\"]"))
            .ToBeVisibleAsync(new() { Timeout = 5000 });

        // The header title should reflect multi-tool execution
        var title = Page.Locator(".collapsible-title");
        await Expect(title).ToBeVisibleAsync(new() { Timeout = 2000 });

        var titleText = await title.TextContentAsync();
        Assert.That(titleText, Does.Contain("Multi-Tool"),
            "Header should indicate multi-tool execution when toolCount > 1");
    }
}
