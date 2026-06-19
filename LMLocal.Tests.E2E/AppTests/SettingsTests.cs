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

        // Instrument bridge mock to capture the settings passed to UpdateSettingsAsync
        await Page.EvaluateAsync("() => { window.__capturedSettings = null; if(window.__bridgeOverride){ const orig = window.__bridgeOverride.UpdateSettingsAsync; window.__bridgeOverride.UpdateSettingsAsync = async (json) => { window.__capturedSettings = json; return orig(json); } } }");

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

        Assert.Multiple(() =>
        {
            Assert.That(doc.GetProperty("LmStudioBaseUrl").GetString(), Is.EqualTo("https://example.test"));
            Assert.That(GetInt(doc.GetProperty("StreamInactivityTimeoutSeconds")), Is.EqualTo(0));
            Assert.That(GetBool(doc.GetProperty("AutoLoadOnStartup")), Is.False);
            Assert.That(GetInt(doc.GetProperty("Theme")), Is.EqualTo(1));
        });
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
}
