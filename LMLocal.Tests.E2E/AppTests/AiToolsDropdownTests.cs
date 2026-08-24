namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public partial class AiToolsDropdownTests : AppTestBase
{
    [GeneratedRegex(@"\bactive\b")]
    private static partial Regex ActiveClassRegex();
    private static Regex ActiveClass => ActiveClassRegex();

    private static readonly string[] ExpectedOptions =
        ["No tools", "Read Only", "Read & Write"];

    [Test]
    [Category("Input")]
    public async Task AiToolsDropdown_Exists_ShowsNoToolsByDefault()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // The input footer (and the dropdown) is only visible once the user
        // starts typing (input-wrapper gets the 'expanded' class).
        await TypeInInputAsync("hello");

        // Dropdown is always visible (unlike the instructions dropdown)
        await Expect(Page.Locator("#aiToolsDropdown")).ToBeVisibleAsync();
        await Expect(Page.Locator("#aiToolsSelectedOption")).ToHaveTextAsync("No tools");

        // Status bar shows no tools mode indicator by default
        await Expect(Page.Locator("#tools-mode-status")).ToHaveTextAsync("");
    }

    [Test]
    [Category("Input")]
    public async Task AiToolsDropdown_OpensAndShowsThreeOptions()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await TypeInInputAsync("hello");

        var dropdown = Page.Locator("#aiToolsDropdown");
        await Expect(dropdown).ToBeVisibleAsync();

        // Closed by default
        await Expect(dropdown).Not.ToHaveClassAsync(ActiveClass);

        // Open via trigger click
        await Page.Locator("#aiToolsDropdown .dropdown-trigger").ClickAsync();
        await Expect(dropdown).ToHaveClassAsync(ActiveClass);

        // Menu contains exactly the 3 modes in order
        var menu = Page.Locator("#aiToolsDropdownMenu");
        await Expect(menu.Locator(".dropdown-item")).ToHaveCountAsync(3);
        await Expect(menu.Locator(".dropdown-item")).ToHaveTextAsync(ExpectedOptions);

        // Close by clicking the trigger again
        await Page.Locator("#aiToolsDropdown .dropdown-trigger").ClickAsync();
        await Expect(dropdown).Not.ToHaveClassAsync(ActiveClass);
    }

    [Test]
    [Category("Input")]
    public async Task AiToolsDropdown_SelectReadOnly_CallsBridgeAndUpdatesStore()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await TypeInInputAsync("hello");
        await InstrumentSetAiToolsAsync();

        await Page.Locator("#aiToolsDropdown .dropdown-trigger").ClickAsync();
        await Page.Locator("#aiToolsDropdownMenu .dropdown-item", new() { HasText = "Read Only" }).ClickAsync();

        // Label updates immediately
        await Expect(Page.Locator("#aiToolsSelectedOption")).ToHaveTextAsync("Read Only");
        await Expect(Page.Locator("#tools-mode-status")).ToHaveTextAsync("Tools: Read");

        // Bridge received { "mode": "readonly" }
        var payload = await GetCapturedAiToolsPayloadAsync();
        Assert.That(payload, Is.EqualTo("{\"mode\":\"readonly\"}"));

        // Settings store updated (EnableAiTools=true, EnableAiWriteTools=false)
        await WaitForSettingsStoreAsync(enableAiTools: true, enableAiWriteTools: false);
    }

    [Test]
    [Category("Input")]
    public async Task AiToolsDropdown_SelectReadWrite_CallsBridgeAndUpdatesStore()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await TypeInInputAsync("hello");
        await InstrumentSetAiToolsAsync();

        await Page.Locator("#aiToolsDropdown .dropdown-trigger").ClickAsync();
        await Page.Locator("#aiToolsDropdownMenu .dropdown-item", new() { HasText = "Read & Write" }).ClickAsync();

        await Expect(Page.Locator("#aiToolsSelectedOption")).ToHaveTextAsync("Read & Write");
        await Expect(Page.Locator("#tools-mode-status")).ToHaveTextAsync("Tools: Read & Write");

        var payload = await GetCapturedAiToolsPayloadAsync();
        Assert.That(payload, Is.EqualTo("{\"mode\":\"readwrite\"}"));

        await WaitForSettingsStoreAsync(enableAiTools: true, enableAiWriteTools: true);
    }

    [Test]
    [Category("Input")]
    public async Task AiToolsDropdown_SelectNoTools_CallsBridgeAndUpdatesStore()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await TypeInInputAsync("hello");
        await InstrumentSetAiToolsAsync();

        // Start from Read & Write so "No tools" is an actual change
        await Page.Locator("#aiToolsDropdown .dropdown-trigger").ClickAsync();
        await Page.Locator("#aiToolsDropdownMenu .dropdown-item", new() { HasText = "Read & Write" }).ClickAsync();
        await WaitForSettingsStoreAsync(enableAiTools: true, enableAiWriteTools: true);

        await Page.Locator("#aiToolsDropdown .dropdown-trigger").ClickAsync();
        await Page.Locator("#aiToolsDropdownMenu .dropdown-item", new() { HasText = "No tools" }).ClickAsync();

        await Expect(Page.Locator("#aiToolsSelectedOption")).ToHaveTextAsync("No tools");

        // Status bar clears the tools mode indicator
        await Expect(Page.Locator("#tools-mode-status")).ToHaveTextAsync("");

        var payload = await GetCapturedAiToolsPayloadAsync();
        Assert.That(payload, Is.EqualTo("{\"mode\":\"none\"}"));

        await WaitForSettingsStoreAsync(enableAiTools: false, enableAiWriteTools: false);
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private async Task TypeInInputAsync(string text)
    {
        var input = Page.Locator("#userInput");
        await input.FillAsync(text);
        // input-wrapper gets 'expanded' on input, revealing the input footer
        await Expect(Page.Locator(".input-wrapper")).ToHaveClassAsync(ExpandedClass);
    }

    [GeneratedRegex(@"\bexpanded\b")]
    private static partial Regex ExpandedClassRegex();
    private static Regex ExpandedClass => ExpandedClassRegex();

    private async Task InstrumentSetAiToolsAsync()
    {
        await Page.EvaluateAsync(
            "() => { window.__capturedAiToolsPayload = null; " +
            "if (window.__settingsOverride) { " +
            "  const orig = window.__settingsOverride.SetAiToolsAsync; " +
            "  window.__settingsOverride.SetAiToolsAsync = async (json) => { window.__capturedAiToolsPayload = json; return orig(json); }; " +
            "} }");
    }

    private async Task<string?> GetCapturedAiToolsPayloadAsync()
    {
        return await Page.EvaluateAsync<string?>("() => window.__capturedAiToolsPayload ?? null");
    }

    private async Task WaitForSettingsStoreAsync(bool enableAiTools, bool enableAiWriteTools)
    {
        var expr =
            "async () => { " +
            "  const m = await import('/js/store/settings.store.js'); " +
            "  const s = m.default.getState(); " +
            $"  return s.EnableAiTools === {enableAiTools.ToString().ToLowerInvariant()} " +
            $"      && s.EnableAiWriteTools === {enableAiWriteTools.ToString().ToLowerInvariant()}; " +
            "}";
        await Page.WaitForFunctionAsync(expr, null, new() { Timeout = 3000 });
    }
}
