using System.Text.RegularExpressions;

namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public partial class SubAgentsToggleTests : AppTestBase
{
    [GeneratedRegex(@"\bactive\b")]
    private static partial Regex ActiveClassRegex();
    private static Regex ActiveClass => ActiveClassRegex();

    [Test]
    [Category("Input")]
    public async Task SubAgentsToggle_Exists_InInputFooter()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // The input footer is only visible once the user starts typing
        await TypeInInputAsync("hello");

        var toggle = Page.Locator("#subAgentsToggleBtn");
        await Expect(toggle).ToBeVisibleAsync();

        // Off by default (EnableSubAgents defaults to false)
        await Expect(toggle).Not.ToHaveClassAsync(ActiveClass);
    }

    [Test]
    [Category("Input")]
    public async Task SubAgentsToggle_Click_CallsBridgeAndUpdatesStore()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await TypeInInputAsync("hello");
        await InstrumentSetSubAgentsAsync();

        await Page.Locator("#subAgentsToggleBtn").ClickAsync();

        // Button becomes active immediately (state comes from the settings store)
        await Expect(Page.Locator("#subAgentsToggleBtn")).ToHaveClassAsync(ActiveClass);

        // Bridge received { "enabled": true }
        var payload = await GetCapturedSubAgentsPayloadAsync();
        Assert.That(payload, Is.EqualTo("{\"enabled\":true}"));

        // Settings store updated
        await WaitForSettingsStoreAsync(enableSubAgents: true);

        // Status bar shows the SubAgents suffix next to the tools mode
        await Expect(Page.Locator("#tools-mode-status")).ToHaveTextAsync("");
    }

    [Test]
    [Category("Input")]
    public async Task SubAgentsToggle_ClickTwice_TogglesOff()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await TypeInInputAsync("hello");

        // Enable first
        await InstrumentSetSubAgentsAsync();
        await Page.Locator("#subAgentsToggleBtn").ClickAsync();
        await WaitForSettingsStoreAsync(enableSubAgents: true);

        // Disable again
        await InstrumentSetSubAgentsAsync();
        await Page.Locator("#subAgentsToggleBtn").ClickAsync();

        await Expect(Page.Locator("#subAgentsToggleBtn")).Not.ToHaveClassAsync(ActiveClass);

        var payload = await GetCapturedSubAgentsPayloadAsync();
        Assert.That(payload, Is.EqualTo("{\"enabled\":false}"));

        await WaitForSettingsStoreAsync(enableSubAgents: false);
    }

    [Test]
    [Category("Input")]
    public async Task SubAgentsToggle_Enabled_StatusBarShowsSuffixForBothModes()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await TypeInInputAsync("hello");

        // Enable Sub Agents
        await Page.Locator("#subAgentsToggleBtn").ClickAsync();
        await WaitForSettingsStoreAsync(enableSubAgents: true);

        // No tools mode -> no indicator at all (suffix only applies to tools modes)
        await Expect(Page.Locator("#tools-mode-status")).ToHaveTextAsync("");

        // Switch to Read Only via the AI tools dropdown
        await Page.Locator("#aiToolsDropdown .dropdown-trigger").ClickAsync();
        await Page.Locator("#aiToolsDropdownMenu .dropdown-item", new() { HasText = "Read Only" }).ClickAsync();
        await Expect(Page.Locator("#tools-mode-status")).ToHaveTextAsync("Tools: Read + SubAgents");

        // Switch to Read & Write
        await Page.Locator("#aiToolsDropdown .dropdown-trigger").ClickAsync();
        await Page.Locator("#aiToolsDropdownMenu .dropdown-item", new() { HasText = "Read & Write" }).ClickAsync();
        await Expect(Page.Locator("#tools-mode-status")).ToHaveTextAsync("Tools: Read & Write + SubAgents");

        // Disable Sub Agents -> suffix disappears
        await Page.Locator("#subAgentsToggleBtn").ClickAsync();
        await WaitForSettingsStoreAsync(enableSubAgents: false);
        await Expect(Page.Locator("#tools-mode-status")).ToHaveTextAsync("Tools: Read & Write");
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private async Task TypeInInputAsync(string text)
    {
        var input = Page.Locator("#userInput");
        await input.FillAsync(text);
        // input-wrapper gets 'expanded' on input, revealing the input footer
        await Expect(Page.Locator(".input-wrapper")).ToHaveClassAsync(ExpandedClassRegex());
    }

    [GeneratedRegex(@"\bexpanded\b")]
    private static partial Regex ExpandedClassRegex();

    private async Task InstrumentSetSubAgentsAsync()
    {
        await Page.EvaluateAsync(
            "() => { window.__capturedSubAgentsPayload = null; " +
            "if (window.__settingsOverride) { " +
            "  const orig = window.__settingsOverride.SetSubAgentsAsync; " +
            "  window.__settingsOverride.SetSubAgentsAsync = async (json) => { window.__capturedSubAgentsPayload = json; return orig(json); }; " +
            "} }");
    }

    private async Task<string?> GetCapturedSubAgentsPayloadAsync()
    {
        return await Page.EvaluateAsync<string?>("() => window.__capturedSubAgentsPayload ?? null");
    }

    private async Task WaitForSettingsStoreAsync(bool enableSubAgents)
    {
        var expr =
            "async () => { " +
            "  const m = await import('/js/store/settings.store.js'); " +
            "  const s = m.default.getState(); " +
            $"  return s.EnableSubAgents === {enableSubAgents.ToString().ToLowerInvariant()}; " +
            "}";
        await Page.WaitForFunctionAsync(expr, null, new() { Timeout = 3000 });
    }
}
