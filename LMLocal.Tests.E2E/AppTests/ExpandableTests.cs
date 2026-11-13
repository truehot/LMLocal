namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public class ExpandableTests : AppTestBase
{
    [Test]
    [Category("Expandable")]
    public async Task LongUserMessage_ShowsMoreButtonWhenTruncated()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var longText = string.Concat(Enumerable.Repeat("This is a long message that will definitely exceed the 150px limit. ", 10));
        await Page.Locator("#userInput").FillAsync(longText);
        await Page.Locator("#mainBtn").ClickAsync();

        var expandableCount = await Page.Locator(".user-message.expandable").CountAsync();
        Assert.That(expandableCount, Is.GreaterThan(0), "Expected at least one expandable user message");

        var userMessage = Page.Locator(".user-message.expandable").Nth(0);
        await Expect(userMessage).ToBeVisibleAsync(new() { Timeout = 3000 });

        var hasExpandable = await userMessage.EvaluateAsync<bool>("el => el.classList.contains('expandable')");
        Assert.That(hasExpandable, Is.True, "User message should have 'expandable' class");
    }

    [Test]
    [Category("Expandable")]
    public async Task ExpandableMessage_ShowsLessButtonWhenExpanded()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var longText = string.Concat(Enumerable.Repeat("This is a long message that will definitely exceed the 150px limit. ", 10));
        await Page.Locator("#userInput").FillAsync(longText);
        await Page.Locator("#mainBtn").ClickAsync();

        await Expect(Page.Locator(".user-message.expandable").Nth(0))
            .ToBeVisibleAsync(new() { Timeout = 3000 });

        var userMessage = Page.Locator(".user-message.expandable").Nth(0);
        var toggle = userMessage.Locator(".show-more-btn");
        await Expect(toggle).ToBeVisibleAsync(new() { Timeout = 3000 });
        await toggle.ClickAsync();

        var isExpanded = await userMessage.EvaluateAsync<bool>("el => el.classList.contains('expanded')");
        Assert.That(isExpanded, Is.True, "User message should have 'expanded' after clicking toggle");
    }

    [Test]
    [Category("Expandable")]
    public async Task ExpandedMessage_ShowsFullContent()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var longText = string.Concat(Enumerable.Repeat("This is a long message that will definitely exceed the 150px limit. ", 10));
        await Page.Locator("#userInput").FillAsync(longText);
        await Page.Locator("#mainBtn").ClickAsync();

        await Expect(Page.Locator(".user-message.expandable").Nth(0))
            .ToBeVisibleAsync(new() { Timeout = 3000 });

        var messageContent = Page.Locator(".user-message.expandable").Nth(0).Locator(".message-content");
        var maxHeightBefore = await messageContent.EvaluateAsync<string>("el => window.getComputedStyle(el).maxHeight");
        Assert.That(maxHeightBefore, Is.Not.EqualTo("none"), "Message should be truncated initially (max-height != 'none')");

        var userMessage = Page.Locator(".user-message.expandable").Nth(0);
        var toggle = userMessage.Locator(".show-more-btn");
        await toggle.ClickAsync();

        var maxHeightAfter = await messageContent.EvaluateAsync<string>("el => window.getComputedStyle(el).maxHeight");
        Assert.That(maxHeightAfter, Is.EqualTo("none"), "Message should have no max-height when expanded");
    }

    [Test]
    [Category("Expandable")]
    public async Task CollapsedMessage_ReturnsToTruncated()
    {
        await GotoWithMockAsync("webview-mock-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var longText = string.Concat(Enumerable.Repeat("This is a long message that will definitely exceed the 150px limit. ", 10));
        await Page.Locator("#userInput").FillAsync(longText);
        await Page.Locator("#mainBtn").ClickAsync();

        await Expect(Page.Locator(".user-message.expandable"))
            .ToBeVisibleAsync(new() { Timeout = 3000 });

        var userMessage = Page.Locator(".user-message.expandable").Nth(0);
        var messageContent = userMessage.Locator(".message-content");

        var toggle = userMessage.Locator(".show-more-btn");
        await toggle.ClickAsync();

        await toggle.ClickAsync();

        var isExpanded = await userMessage.EvaluateAsync<bool>("el => el.classList.contains('expanded')");
        Assert.That(isExpanded, Is.False, "User message should not have 'expanded' after collapsing");

        var maxHeightAfterCollapse = await messageContent.EvaluateAsync<string>("el => window.getComputedStyle(el).maxHeight");
        Assert.That(maxHeightAfterCollapse, Is.Not.EqualTo("none"), "Message should be truncated again after collapse (max-height != 'none')");
    }
}
