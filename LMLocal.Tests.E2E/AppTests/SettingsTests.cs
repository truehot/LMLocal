namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public class SettingsTests : AppTestBase
{
    [Test]
    [Category("Settings")]
    public async Task Open_SettingsDialog_IsVisible()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-settings']").ClickAsync();

        var dialog = Page.Locator("#settings-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        // Wait for form to be populated
        await Page.WaitForFunctionAsync("() => document.querySelector('#settings-dialog form')?.children.length > 0");

        await Expect(dialog.Locator("input[data-setting='LmStudioBaseUrl']")).ToHaveCountAsync(1);
        await Expect(dialog.Locator("input[data-setting='StreamInactivityTimeoutSeconds']")).ToHaveCountAsync(1);
        await Expect(dialog.Locator("input[data-setting='AutoLoadOnStartup']")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("Settings")]
    public async Task Save_Settings_CallsBridgeAndClosesDialog()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Instrument settings mock to capture the settings passed to UpdateSettingsAsync
        await Page.EvaluateAsync("() => { window.__capturedSettings = null; if(window.__settingsOverride){ const orig = window.__settingsOverride.UpdateSettingsAsync; window.__settingsOverride.UpdateSettingsAsync = async (json) => { window.__capturedSettings = json; return orig(json); } } }");

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-settings']").ClickAsync();

        var dialog = Page.Locator("#settings-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        // Wait for form to be populated
        await Page.WaitForFunctionAsync("() => document.querySelector('#settings-dialog form')?.children.length > 0");

        await Page.Locator("#settings-dialog input[data-setting='LmStudioBaseUrl']").FillAsync("https://example.test");
        await Page.Locator("#settings-dialog input[data-setting='StreamInactivityTimeoutSeconds']").FillAsync("0");

        var checkbox = Page.Locator("#settings-dialog input[data-setting='AutoLoadOnStartup']");
        var isChecked = await checkbox.EvaluateAsync<bool>("el => el.checked");
        if (isChecked)
        {
            await checkbox.ClickAsync();
        }

        // Set theme radio to value 1
        await Page.Locator("#settings-dialog input[type='radio'][data-setting='Theme'][value='1']").CheckAsync();

        await Page.Locator("#settings-dialog .modal-footer button.btn-primary").ClickAsync();

        // Wait for dialog to close
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });

        var capturedRaw = await Page.EvaluateAsync<string>("() => { const v = window.__capturedSettings; if (v === undefined || v === null) return null; return (typeof v === 'string') ? v : JSON.stringify(v); }");
        Assert.That(capturedRaw, Is.Not.Null, "Bridge did not receive settings payload");

        var json = capturedRaw!;
        // If the captured value is a quoted JSON string, deserialize to get the inner JSON
        if (json.Length > 0 && json[0] == '"' && json[^1] == '"')
        {
            json = System.Text.Json.JsonSerializer.Deserialize<string>(json)!;
        }

        var doc = System.Text.Json.JsonDocument.Parse(json).RootElement;
        int GetInt(System.Text.Json.JsonElement el)
        {
            return el.ValueKind == System.Text.Json.JsonValueKind.Number
                ? el.GetInt32()
                : int.TryParse(el.GetString(), out var v) ? v : throw new InvalidOperationException("Expected numeric value");
        }

        bool GetBool(System.Text.Json.JsonElement el)
        {
            return el.ValueKind == System.Text.Json.JsonValueKind.True || el.ValueKind == System.Text.Json.JsonValueKind.False
                ? el.GetBoolean()
                : bool.TryParse(el.GetString(), out var v) ? v : throw new InvalidOperationException("Expected boolean value");
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(doc.GetProperty("LmStudioBaseUrl").GetString(), Is.EqualTo("https://example.test"));
            Assert.That(GetInt(doc.GetProperty("StreamInactivityTimeoutSeconds")), Is.Zero);
            Assert.That(GetBool(doc.GetProperty("AutoLoadOnStartup")), Is.False);
            Assert.That(GetInt(doc.GetProperty("Theme")), Is.EqualTo(1));
        }
    }

    [Test]
    [Category("Settings")]
    public async Task Save_InvalidUrl_DoesNotCloseDialog()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-settings']").ClickAsync();

        var dialog = Page.Locator("#settings-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        await Page.Locator("#settings-dialog input[data-setting='LmStudioBaseUrl']").FillAsync("not-a-url");
        await Page.Locator("#settings-dialog .modal-footer button.btn-primary").ClickAsync();

        // Dialog should remain open due to validation failure
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 1000 });

        // Close the dialog via cancel
        await Page.Locator("#settings-dialog .modal-footer button#settings-dialog-cancel").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
    }

    [Test]
    [Category("Settings")]
    public async Task CloseDialog_CancelButton_ClosesDialog()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-settings']").ClickAsync();

        var dialog = Page.Locator("#settings-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#settings-dialog form')?.children.length > 0");

        // Click Cancel
        await dialog.Locator("#settings-dialog-cancel").ClickAsync();

        // Verify dialog is closed
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("Settings")]
    public async Task CloseDialog_EscapeKey_ClosesDialog()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-settings']").ClickAsync();

        var dialog = Page.Locator("#settings-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#settings-dialog form')?.children.length > 0");

        // Press Escape
        await Page.Keyboard.PressAsync("Escape");

        // Verify dialog is closed
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("Settings")]
    public async Task TestConnection_Error_ShowsToast()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Provide settings with a provider + URL so the Test button works,
        // and make TestConnectionAsync fail with a reason.
        // Providers for the settings dialog come from GetProvidersAsync (not from settings).
        await Page.EvaluateAsync(@"() => {
            window.__settingsOverride.GetSettingsAsync = async () => JSON.stringify({
                AutoLoadOnStartup: true,
                Provider: 'openai',
                ProviderId: 0,
                LmStudioBaseUrl: 'http://test.local'
            });
            window.__settingsOverride.TestConnectionAsync = async (json) => JSON.stringify({
                success: false,
                error: { message: 'Connection refused by remote host' }
            });
            window.__providersOverride.GetProvidersAsync = async () => JSON.stringify({
                defaultProviders: [
                    { id: 0, providerType: 'openai', name: 'OpenAI', customBaseUrl: 'http://test.local', customApiKey: 'key' }
                ],
                providers: [],
                providerTypes: [
                    { key: 'openai', displayName: 'OpenAI' }
                ]
            });
        }");

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-settings']").ClickAsync();

        var dialog = Page.Locator("#settings-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#settings-dialog form')?.children.length > 0");

        // Ensure the provider select is populated so the Test button can run
        await Expect(dialog.Locator("[data-setting='Provider'] option")).ToHaveCountAsync(1);

        // Click Test -> error toast near the button
        await dialog.Locator(".test-connection-btn").ClickAsync();

        var toast = Page.Locator("#app-toast.show");
        await Expect(toast).ToBeVisibleAsync();
        await Expect(toast).ToContainTextAsync("Connection refused by remote host");
    }

    [Test]
    [Category("Settings")]
    public async Task TestConnection_Error_WithV1Url_ShowsUrlAndTip()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Provider + URL are configured, and TestConnection fails echoing the probed URL.
        await Page.EvaluateAsync(@"() => {
            window.__settingsOverride.GetSettingsAsync = async () => JSON.stringify({
                AutoLoadOnStartup: true,
                Provider: 'openai',
                ProviderId: 0,
                LmStudioBaseUrl: 'https://api.x.ai/v1'
            });
            window.__settingsOverride.TestConnectionAsync = async (json) => JSON.stringify({
                success: false,
                error: { message: '404 Not Found' },
                url: 'https://api.x.ai/v1'
            });
            window.__providersOverride.GetProvidersAsync = async () => JSON.stringify({
                defaultProviders: [
                    { id: 0, providerType: 'openai', name: 'OpenAI', customBaseUrl: 'https://api.x.ai/v1', customApiKey: 'key' }
                ],
                providers: [],
                providerTypes: [
                    { key: 'openai', displayName: 'OpenAI' }
                ]
            });
        }");

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-settings']").ClickAsync();

        var dialog = Page.Locator("#settings-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#settings-dialog form')?.children.length > 0");
        await Expect(dialog.Locator("[data-setting='Provider'] option")).ToHaveCountAsync(1);

        await dialog.Locator(".test-connection-btn").ClickAsync();

        var toast = Page.Locator("#app-toast.show");
        await Expect(toast).ToBeVisibleAsync();
        await Expect(toast).ToContainTextAsync("404 Not Found");
        await Expect(toast).ToContainTextAsync("https://api.x.ai/v1");
        await Expect(toast).ToContainTextAsync("added automatically");
    }

    /// <summary>
    /// The backend replaces the whole settings file with the payload it receives, so every value the dialog displays
    /// must travel back unchanged even when the user never touched it. The dialog therefore has to populate the form
    /// from the loaded settings before it collects them.
    /// </summary>
    [Test]
    [Category("Settings")]
    public async Task Save_Settings_KeepsValuesTheUserDidNotTouch()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // The dialog reloads the settings each time it opens, so overriding the source here is enough. The payload
        // carries the required fields so the form is valid without the test filling anything in.
        await Page.EvaluateAsync(
            "() => { if (window.__settingsOverride) { window.__settingsOverride.GetSettingsAsync = async () => JSON.stringify({ LmStudioBaseUrl: 'http://localhost:1234', StreamInactivityTimeoutSeconds: 20, AutoLoadOnStartup: true, EnableHistoryCompaction: true, ShowTokenStats: true, EnableRateLimitHandling: true }); } }");
        await Page.EvaluateAsync(
            "() => { window.__capturedSettings = null; if (window.__settingsOverride) { const orig = window.__settingsOverride.UpdateSettingsAsync; window.__settingsOverride.UpdateSettingsAsync = async (json) => { window.__capturedSettings = json; return orig(json); } } }");

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-settings']").ClickAsync();

        var dialog = Page.Locator("#settings-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#settings-dialog form')?.children.length > 0");

        await Page.Locator("#settings-dialog .modal-footer button.btn-primary").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });

        var capturedRaw = await Page.EvaluateAsync<string>(
            "() => { const v = window.__capturedSettings; if (v === undefined || v === null) return null; return (typeof v === 'string') ? v : JSON.stringify(v); }");
        Assert.That(capturedRaw, Is.Not.Null, "Bridge did not receive settings payload");

        var json = capturedRaw!;
        if (json.Length > 0 && json[0] == '"' && json[^1] == '"')
        {
            json = System.Text.Json.JsonSerializer.Deserialize<string>(json)!;
        }

        var doc = System.Text.Json.JsonDocument.Parse(json).RootElement;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(doc.GetProperty("EnableHistoryCompaction").GetBoolean(), Is.True,
                "a setting the user did not touch must reach the bridge unchanged");
            Assert.That(doc.GetProperty("ShowTokenStats").GetBoolean(), Is.True);
            Assert.That(doc.GetProperty("EnableRateLimitHandling").GetBoolean(), Is.True);
        }
    }

    /// <summary>
    /// The tool call layout is a single select with three modes. Its value is resolved when the dialog opens, so an
    /// old settings file that only carries the CollapseToolCalls flag already shows the mode that flag stands for.
    /// </summary>
    [Test]
    [Category("Settings")]
    public async Task SettingsDialog_ToolCallsDisplay_ShowsTheModeTheLegacyFlagStandsFor()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.EvaluateAsync(
            "() => { if (window.__settingsOverride) { window.__settingsOverride.GetSettingsAsync = async () => JSON.stringify({ LmStudioBaseUrl: 'http://localhost:1234', StreamInactivityTimeoutSeconds: 20, AutoLoadOnStartup: true, CollapseToolCalls: true }); } }");

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-settings']").ClickAsync();

        var dialog = Page.Locator("#settings-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });

        var select = dialog.Locator("[data-setting='ToolCallsDisplay']");
        await Expect(select).ToBeVisibleAsync(new() { Timeout = 2000 });
        await Expect(select.Locator("option")).ToHaveCountAsync(3);
        await Expect(select).ToHaveValueAsync("expanded");
    }

    /// <summary>
    /// The checkbox is gone, so the dialog no longer sends CollapseToolCalls at all: the settings manager has to
    /// restore it from the mode, otherwise every save would reset it to false and an older build would lose the
    /// grouped layout.
    /// </summary>
    [Test]
    [Category("Settings")]
    public async Task Save_Settings_SendsTheChosenToolCallsDisplayMode()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.EvaluateAsync(
            "() => { if (window.__settingsOverride) { window.__settingsOverride.GetSettingsAsync = async () => JSON.stringify({ LmStudioBaseUrl: 'http://localhost:1234', StreamInactivityTimeoutSeconds: 20, AutoLoadOnStartup: true, ToolCallsDisplay: 'flat' }); } }");
        await Page.EvaluateAsync(
            "() => { window.__capturedSettings = null; if (window.__settingsOverride) { const orig = window.__settingsOverride.UpdateSettingsAsync; window.__settingsOverride.UpdateSettingsAsync = async (json) => { window.__capturedSettings = json; return orig(json); } } }");

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-settings']").ClickAsync();

        var dialog = Page.Locator("#settings-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });

        var select = dialog.Locator("[data-setting='ToolCallsDisplay']");
        await Expect(select).ToHaveValueAsync("flat", new() { Timeout = 2000 });
        await select.SelectOptionAsync("collapsed");

        await Page.Locator("#settings-dialog .modal-footer button.btn-primary").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });

        var capturedRaw = await Page.EvaluateAsync<string>(
            "() => { const v = window.__capturedSettings; if (v === undefined || v === null) return null; return (typeof v === 'string') ? v : JSON.stringify(v); }");
        Assert.That(capturedRaw, Is.Not.Null, "Bridge did not receive settings payload");

        var json = capturedRaw!;
        if (json.Length > 0 && json[0] == '"' && json[^1] == '"')
        {
            json = System.Text.Json.JsonSerializer.Deserialize<string>(json)!;
        }

        var doc = System.Text.Json.JsonDocument.Parse(json).RootElement;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(doc.GetProperty("ToolCallsDisplay").GetString(), Is.EqualTo("collapsed"),
                "the selected mode must reach the bridge");
            Assert.That(doc.TryGetProperty("CollapseToolCalls", out _), Is.False,
                "the checkbox is gone, so the legacy flag is no longer part of the payload");
        }
    }

}
