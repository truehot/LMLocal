namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public partial class ToolbarTests : AppTestBase
{
    [Test]
    [Category("Toolbar")]
    public async Task Toolbar_ModelNameShowsPlaceholderWhenDisconnected()
    {
        await GotoWithMockAsync("webview-mock-offline.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Disconnected", new() { Timeout = 3000 });

        await Expect(Page.Locator("#model-name"))
            .ToHaveTextAsync("Select model...", new() { Timeout = 3000 });
    }

    [Test]
    [Category("Toolbar")]
    public async Task Toolbar_ModelNameShowsActiveModelWhenConnected()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Expect(Page.Locator("#model-name"))
            .ToHaveTextAsync("Test Model", new() { Timeout = 3000 });
    }

    [Test]
    [Category("Toolbar")]
    public async Task Toolbar_ModelInfoVisibleAfterConnected()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Expect(Page.Locator("#model-info")).ToBeVisibleAsync();
        await Expect(Page.Locator("#status-separator")).ToBeVisibleAsync();
    }

    [Test]
    [Category("Toolbar")]
    public async Task Toolbar_TokenBarFillExists()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var tokenBar = Page.Locator("#token-bar-fill");
        await Expect(tokenBar).ToHaveCountAsync(1);
    }

    [Test]
    [Category("Toolbar")]
    public async Task Toolbar_InfoTooltipExists()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var tooltip = Page.Locator("#info-tooltip");
        await Expect(tooltip).ToHaveCountAsync(1);
    }

    [Test]
    [Category("Toolbar")]
    public async Task Toolbar_ModelNameHasPointerCursor()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Expect(Page.Locator("#model-name")).ToHaveCSSAsync("cursor", "pointer");
    }

    [Test]
    [Category("Toolbar")]
    public async Task Toolbar_TokenBarShowsContextUsageAfterModelStateUpdate()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Simulate model state update with token usage
        await Page.EvaluateAsync("() => { " +
            "import('/js/components/toolbar.component.js').then(m => { " +
            "  const comp = m.toolbarComponent; " +
            "  comp.updateModelState(" +
            "    { modelName: 'Test Model', tokenUsed: 4000, tokenMax: 16384, supportsMaxTokens: true }," +
            "    { modelName: 'Test Model', tokenUsed: 0, tokenMax: 16384, supportsMaxTokens: true }" +
            "  ); " +
            "}); }");
        await Task.Delay(200);

        // The token bar fill should have updated its transform
        var tokenBarFill = Page.Locator("#token-bar-fill");
        await Expect(tokenBarFill).ToHaveCountAsync(1);
        await Expect(tokenBarFill).ToBeVisibleAsync();

        // Info tooltip should have context usage info
        var tooltip = Page.Locator("#info-tooltip");
        var title = await tooltip.GetAttributeAsync("title");
        Assert.That(title, Does.Contain("Context usage"));
    }

    [Test]
    [Category("Toolbar")]
    public async Task Toolbar_TokenBarHiddenWhenModelDoesNotSupportMaxTokens()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Simulate model that doesn't support max tokens
        await Page.EvaluateAsync("() => { " +
            "import('/js/components/toolbar.component.js').then(m => { " +
            "  const comp = m.toolbarComponent; " +
            "  comp.updateModelState(" +
            "    { modelName: 'Test Model', tokenUsed: 100, tokenMax: 0, supportsMaxTokens: false }," +
            "    { modelName: 'Test Model', tokenUsed: 0, tokenMax: 0, supportsMaxTokens: false }" +
            "  ); " +
            "}); }");
        await Task.Delay(200);

        // Token bar fill should be hidden when max tokens not supported
        var tokenBarFill = Page.Locator("#token-bar-fill");
        var display = await tokenBarFill.EvaluateAsync<string>("el => el.style.display");
        Assert.That(display, Is.EqualTo("none"));
    }
}
