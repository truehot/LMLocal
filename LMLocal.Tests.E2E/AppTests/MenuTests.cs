namespace LMLocal.Tests.E2E.AppTests;

using System.Text.RegularExpressions;

[TestFixture]
public partial class MenuTests : AppTestBase
{
    [GeneratedRegex(@"\bshow\b")]
    private static partial Regex ShowClassRegex();
    private static Regex ShowClass => ShowClassRegex();

    [Test]
    [Category("Menu")]
    public async Task Menu_DropdownIsHiddenByDefault()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var dropdown = Page.Locator("#dropdown-menu");
        await Expect(dropdown).Not.ToHaveClassAsync(ShowClass);
    }

    [Test]
    [Category("Menu")]
    public async Task Menu_ClickOpensDropdown()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var menuBtn = Page.Locator("#menu-btn");
        await menuBtn.ClickAsync();

        var dropdown = Page.Locator("#dropdown-menu");
        await Expect(dropdown).ToHaveClassAsync(ShowClass);
    }

    [Test]
    [Category("Menu")]
    public async Task Menu_ClickTwiceClosesDropdown()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var menuBtn = Page.Locator("#menu-btn");
        var dropdown = Page.Locator("#dropdown-menu");

        // Open
        await menuBtn.ClickAsync();
        await Expect(dropdown).ToHaveClassAsync(ShowClass);

        // Close
        await menuBtn.ClickAsync();
        await Expect(dropdown).Not.ToHaveClassAsync(ShowClass);
    }

    [Test]
    [Category("Menu")]
    public async Task Menu_HasAllRequiredActionButtons()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var dropdown = Page.Locator("#dropdown-menu");

        await Expect(dropdown.Locator("button[data-action=\"open-instructions\"]")).ToHaveCountAsync(1);
        await Expect(dropdown.Locator("button[data-action=\"open-settings\"]")).ToHaveCountAsync(1);
        await Expect(dropdown.Locator("button[data-action=\"open-providers\"]")).ToHaveCountAsync(1);
        await Expect(dropdown.Locator("button[data-action=\"open-tools\"]")).ToHaveCountAsync(1);
        await Expect(dropdown.Locator("button[data-action=\"mcp-settings\"]")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("Menu")]
    public async Task Menu_ClickingActionButton_ClosesDropdown()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var menuBtn = Page.Locator("#menu-btn");
        var dropdown = Page.Locator("#dropdown-menu");

        // Open menu
        await menuBtn.ClickAsync();
        await Expect(dropdown).ToHaveClassAsync(ShowClass);

        // Click the Settings action (which opens the settings dialog)
        await dropdown.Locator("button[data-action=\"open-settings\"]").ClickAsync();

        // Menu should close after action
        await Expect(dropdown).Not.ToHaveClassAsync(ShowClass);
    }
}
