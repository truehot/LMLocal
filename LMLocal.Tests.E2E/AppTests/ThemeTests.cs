namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public partial class ThemeTests : AppTestBase
{
    [Test]
    [Category("Theme")]
    public async Task Theme_DefaultThemeIsDark()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var body = Page.Locator("body");
        await Expect(body).ToHaveAttributeAsync("data-theme", "dark");
    }

    [Test]
    [Category("Theme")]
    public async Task Theme_CanChangeToLightViaEvaluateAsync()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Simulate changing theme to Light (1) via the component
        await Page.EvaluateAsync("() => { " +
            "import('/js/components/theme.component.js').then(m => { " +
            "  window.__theme = m.themeComponent; " +
            "  m.themeComponent.updateSettingsState({ Theme: 1 }, { Theme: 0 }); " +
            "}); }");
        await Task.Delay(200);

        var body = Page.Locator("body");
        await Expect(body).ToHaveAttributeAsync("data-theme", "light");
    }

    [Test]
    [Category("Theme")]
    public async Task Theme_HljsLinkUpdatesWithTheme()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Default (0 = dark) should set hljs.dark.css
        var link = Page.Locator("#hljs-theme");
        await Expect(link).ToHaveAttributeAsync("href", "https://app.local/css/hljs.dark.css");

        // Switch to Light (1)
        await Page.EvaluateAsync("() => { " +
            "import('/js/components/theme.component.js').then(m => { " +
            "  m.themeComponent.updateSettingsState({ Theme: 1 }, { Theme: 0 }); " +
            "}); }");
        await Task.Delay(200);

        await Expect(link).ToHaveAttributeAsync("href", "https://app.local/css/hljs.light.css");
    }

    [Test]
    [Category("Theme")]
    public async Task Theme_MidLightAndMidDarkAreSupported()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var body = Page.Locator("body");

        // Mid-light (2)
        await Page.EvaluateAsync("() => { " +
            "import('/js/components/theme.component.js').then(m => { " +
            "  m.themeComponent.updateSettingsState({ Theme: 2 }, { Theme: 0 }); " +
            "}); }");
        await Task.Delay(200);
        await Expect(body).ToHaveAttributeAsync("data-theme", "mid-light");

        // Mid-dark (3)
        await Page.EvaluateAsync("() => { " +
            "import('/js/components/theme.component.js').then(m => { " +
            "  m.themeComponent.updateSettingsState({ Theme: 3 }, { Theme: 2 }); " +
            "}); }");
        await Task.Delay(200);
        await Expect(body).ToHaveAttributeAsync("data-theme", "mid-dark");
    }

    [Test]
    [Category("Theme")]
    public async Task Theme_NoChangeWhenSameThemeApplied()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var body = Page.Locator("body");
        await Expect(body).ToHaveAttributeAsync("data-theme", "dark");

        // Applying the same theme should keep dark
        await Page.EvaluateAsync("() => { " +
            "import('/js/components/theme.component.js').then(m => { " +
            "  m.themeComponent.updateSettingsState({ Theme: 0 }, { Theme: 0 }); " +
            "}); }");
        await Task.Delay(200);

        await Expect(body).ToHaveAttributeAsync("data-theme", "dark");
    }
}
