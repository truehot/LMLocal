namespace LMLocal.Tests.E2E.AppTests;
using System.Text.RegularExpressions;

[TestFixture]
public partial class SubAgentsDialogTests : AppTestBase
{
    [GeneratedRegex(@"\btool-disabled\b")]
    private static partial Regex ToolDisabledClassRegex();

    private static readonly string[] DefaultSharedTools = ["read_file_lines"];

    private async Task OpenDialogAsync()
    {
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-subagents']").ClickAsync();
        await Expect(Page.Locator("#subagents-dialog")).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#subagents-list-container')?.children.length > 0");
    }

    private async Task InstrumentUpdateSubAgentsAsync()
    {
        await Page.EvaluateAsync(
            "() => { window.__capturedSubAgentsPayload = null; " +
            "if (window.__subAgentsOverride) { " +
            "  const orig = window.__subAgentsOverride.UpdateSubAgentsAsync; " +
            "  window.__subAgentsOverride.UpdateSubAgentsAsync = async (json) => { window.__capturedSubAgentsPayload = json; return orig(json); }; " +
            "} }");
    }

    private async Task<string?> GetCapturedSubAgentsPayloadAsync()
    {
        return await Page.EvaluateAsync<string?>("() => window.__capturedSubAgentsPayload ?? null");
    }

    [Test]
    [Category("SubAgents")]
    public async Task SubAgentsDialog_OpensFromMenu_ShowsHeaderAndToolbar()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await OpenDialogAsync();

        var dialog = Page.Locator("#subagents-dialog");
        await Expect(dialog.Locator(".modal-header")).ToHaveTextAsync("Sub Agents");
        await Expect(dialog.Locator("#subagents-filter-input")).ToHaveCountAsync(1);
        await Expect(dialog.Locator("#subagents-enable-all-btn")).ToHaveCountAsync(1);
        await Expect(dialog.Locator("#subagents-disable-all-btn")).ToHaveCountAsync(1);
        await Expect(dialog.Locator("#subagents-modal-save")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("SubAgents")]
    public async Task SubAgentsDialog_RendersAgentCardsWithDetails()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await OpenDialogAsync();

        var cards = Page.Locator("#subagents-dialog .tool-item-card");
        await Expect(cards).ToHaveCountAsync(2);

        // First card: researcher (title uses displayName)
        var first = cards.Nth(0);
        await Expect(first.Locator(".tool-title")).ToHaveTextAsync("Researcher");
        await Expect(first.Locator(".tool-description")).ToHaveTextAsync("Research agent");
        await Expect(first.Locator(".subagents-details")).ToContainTextAsync("deepseek-chat");
        await Expect(first.Locator(".subagents-details")).ToContainTextAsync("deepseek");
        await Expect(first.Locator(".subagents-details")).ToContainTextAsync("https://api.deepseek.com");
        await Expect(first.Locator(".subagents-params")).ToContainTextAsync("temp 0.3");
        await Expect(first.Locator(".subagents-params")).ToContainTextAsync("timeout 90s");
        await Expect(first.Locator(".subagents-chips .chip")).ToHaveCountAsync(2);

        // Second card: coder (disabled -> tool-disabled class, empty tools -> muted chip)
        var second = cards.Nth(1);
        await Expect(second).ToHaveClassAsync(ToolDisabledClassRegex());
        await Expect(second.Locator(".tool-title")).ToHaveTextAsync("Coder");
        await Expect(second.Locator(".subagents-chips .chip.chip-muted")).ToHaveTextAsync("no tools");

        // Switch states reflect the enabled flags
        await Expect(first.Locator("input[type=checkbox]")).ToBeCheckedAsync();
        await Expect(second.Locator("input[type=checkbox]")).Not.ToBeCheckedAsync();
    }

    [Test]
    [Category("SubAgents")]
    public async Task SubAgentsDialog_ToggleAndSave_SendsEnabledFlags()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await OpenDialogAsync();
        await InstrumentUpdateSubAgentsAsync();

        var cards = Page.Locator("#subagents-dialog .tool-item-card");
        // Toggle both: researcher off, coder on. The list is re-rendered on each toggle,
        // so click the visible switch slider (the checkbox itself is hidden/zero-sized).
        await cards.Nth(0).Locator(".switch-slider").ClickAsync(new() { Force = true });
        await cards.Nth(1).Locator(".switch-slider").ClickAsync(new() { Force = true });

        await Page.Locator("#subagents-modal-save").ClickAsync();

        var payload = await GetCapturedSubAgentsPayloadAsync();
        Assert.That(payload, Is.EqualTo(
            "{\"agents\":[{\"id\":\"researcher\",\"enabled\":false},{\"id\":\"coder\",\"enabled\":true}]}"));

        // Dialog closes after a successful save
        await Expect(Page.Locator("#subagents-dialog")).Not.ToBeVisibleAsync();
    }

    [Test]
    [Category("SubAgents")]
    public async Task SubAgentsDialog_Cancel_DoesNotSendPayload()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await OpenDialogAsync();
        await InstrumentUpdateSubAgentsAsync();

        await Page.Locator("#subagents-modal-close").ClickAsync();

        var payload = await GetCapturedSubAgentsPayloadAsync();
        Assert.That(payload, Is.Null);
        await Expect(Page.Locator("#subagents-dialog")).Not.ToBeVisibleAsync();
    }

    [Test]
    [Category("SubAgents")]
    public async Task SubAgentsDialog_EnableDisableAll_TogglesAllSwitches()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await OpenDialogAsync();

        // Disable all
        await Page.Locator("#subagents-disable-all-btn").ClickAsync();
        var cards = Page.Locator("#subagents-dialog .tool-item-card");
        await Expect(cards.Nth(0).Locator("input[type=checkbox]")).Not.ToBeCheckedAsync();
        await Expect(cards.Nth(1).Locator("input[type=checkbox]")).Not.ToBeCheckedAsync();

        // Enable all
        await Page.Locator("#subagents-enable-all-btn").ClickAsync();
        await Expect(cards.Nth(0).Locator("input[type=checkbox]")).ToBeCheckedAsync();
        await Expect(cards.Nth(1).Locator("input[type=checkbox]")).ToBeCheckedAsync();
    }

    [Test]
    [Category("SubAgents")]
    public async Task SubAgentsDialog_LoadError_ShowsErrorMessage()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Override GetSubAgentsAsync to fail before opening the dialog.
        await Page.EvaluateAsync(
            "() => { window.__subAgentsOverride.GetSubAgentsAsync = async () => JSON.stringify({ success: false, error: { message: 'boom: missing subagents.json' } }); }");

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-subagents']").ClickAsync();

        var dialog = Page.Locator("#subagents-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Expect(dialog.Locator(".loading-placeholder")).ToContainTextAsync("missing subagents.json");
        // No cards rendered on error
        await Expect(dialog.Locator(".tool-item-card")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Built-in default used by the full-replace test: shared values in the root, personal values on the agents,
    /// one agent without its own parallel settings, and a pipeline.
    /// </summary>
    private const string DefaultConfigJson = """
        {
          "agents": [
            {
              "id": "explorer", "description": "Explorer", "system": "You explore.",
              "temperature": 0.0, "timeoutSeconds": 240, "parallel": true, "maxParallel": 2,
              "enabled": true, "allowedTools": ["find_files", "read_file_lines"]
            },
            {
              "id": "reviewer", "description": "Reviewer", "enabled": true, "allowedTools": ["get_changes_diff"]
            }
          ],
          "pipelines": [
            {
              "id": "review_flow", "description": "Review flow",
              "steps": [ { "agent": "explorer", "prompt": "{{task}}" }, { "agent": "reviewer", "prompt": "{{prev}}" } ]
            }
          ],
          "providerType": "lmstudio",
          "customBaseUrl": "http://localhost:1234",
          "model": "default-model",
          "temperature": 0.1,
          "reasoningEffort": "medium",
          "maxParallel": 3,
          "sharedTools": ["read_file_lines"]
        }
        """;

    [Test]
    [Category("SubAgents")]
    public async Task SubAgentsDialog_FullReplaceFromDefaults_KeepsRootAndAgentsAsTheyAre()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // The built-in default the dialog downloads. Registered after the generic app.local route, so it wins.
        await Page.RouteAsync("https://app.local/json/subagents.json", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = DefaultConfigJson
        }));

        // No custom subagents.json yet: an empty list, so "Use Active Model" loads the defaults and Save replaces.
        await Page.EvaluateAsync(
            "() => { window.__capturedReplacePayload = null; " +
            "window.__subAgentsOverride.GetSubAgentsAsync = async () => JSON.stringify({ success: true, data: { agents: [] } }); " +
            "window.__subAgentsOverride.ReplaceSubAgentsConfigAsync = async (json) => { " +
            "  window.__capturedReplacePayload = json; return JSON.stringify({ success: true }); }; }");

        await OpenDialogAsync();
        await Page.Locator("#subagents-use-active-model-btn").ClickAsync();
        await Expect(Page.Locator("#subagents-list-container .tool-item-card")).ToHaveCountAsync(2);

        await Page.Locator("#subagents-modal-save").ClickAsync();
        await Expect(Page.Locator("#subagents-dialog")).Not.ToBeVisibleAsync();

        var payload = await Page.EvaluateAsync<string?>("() => window.__capturedReplacePayload ?? null");
        Assert.That(payload, Is.Not.Null, "the full replace must go through ReplaceSubAgentsConfigAsync");

        using var document = System.Text.Json.JsonDocument.Parse(payload!);
        var root = document.RootElement;

        // The root of the default stays in the root.
        Assert.That(root.GetProperty("temperature").GetDouble(), Is.EqualTo(0.1));
        Assert.That(root.GetProperty("reasoningEffort").GetString(), Is.EqualTo("medium"));
        Assert.That(root.GetProperty("maxParallel").GetInt32(), Is.EqualTo(3));
        Assert.That(root.GetProperty("sharedTools").EnumerateArray().Select(e => e.GetString()),
            Is.EqualTo(DefaultSharedTools), "losing sharedTools takes read_file_lines away from the main model");
        Assert.That(root.GetProperty("model").GetString(), Is.EqualTo("test-model-instance"),
            "Use Active Model replaces the root model");

        // The agents are copied as they are: personal values stay, nothing from the root is expanded into them.
        var agents = root.GetProperty("agents").EnumerateArray().ToList();
        Assert.That(agents, Has.Count.EqualTo(2));

        var explorer = agents[0];
        Assert.That(explorer.GetProperty("id").GetString(), Is.EqualTo("explorer"));
        Assert.That(explorer.GetProperty("system").GetString(), Is.EqualTo("You explore."));
        Assert.That(explorer.GetProperty("parallel").GetBoolean(), Is.True);
        Assert.That(explorer.GetProperty("maxParallel").GetInt32(), Is.EqualTo(2));
        Assert.That(explorer.GetProperty("timeoutSeconds").GetInt32(), Is.EqualTo(240));
        Assert.That(explorer.GetProperty("temperature").GetDouble(), Is.EqualTo(0.0));

        var reviewer = agents[1];
        Assert.That(reviewer.GetProperty("id").GetString(), Is.EqualTo("reviewer"));
        Assert.That(reviewer.TryGetProperty("parallel", out _), Is.False,
            "an agent without its own parallel inherits the root value; the key must not appear");
        Assert.That(reviewer.TryGetProperty("maxParallel", out _), Is.False);
        Assert.That(reviewer.TryGetProperty("temperature", out _), Is.False,
            "the root temperature must not be expanded into the agent");
        Assert.That(reviewer.TryGetProperty("reasoningEffort", out _), Is.False);

        // The pipelines of the default are kept.
        var pipelines = root.GetProperty("pipelines").EnumerateArray().ToList();
        Assert.That(pipelines, Has.Count.EqualTo(1));
        Assert.That(pipelines[0].GetProperty("id").GetString(), Is.EqualTo("review_flow"));
        Assert.That(pipelines[0].GetProperty("steps").GetArrayLength(), Is.EqualTo(2));
    }
}
