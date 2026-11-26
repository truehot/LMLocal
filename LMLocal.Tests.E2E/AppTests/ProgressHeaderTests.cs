namespace LMLocal.Tests.E2E.AppTests;

/// <summary>The "Step N: &lt;activity&gt;" header of a grouped answer.</summary>
[TestFixture]
public class ProgressHeaderTests : AppTestBase
{
    private async Task StartAnswerAsync()
    {
        await GotoWithMockAsync("webview-mock-progress-header.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Progress header test");
        await Page.Locator("#mainBtn").ClickAsync();
    }

    [Test]
    [Category("Collapsible")]
    public async Task Header_FollowsTheActivityOfTheAnswer()
    {
        await StartAnswerAsync();

        var title = Page.Locator(".collapsible-title");

        // Set by the tool call itself, not by the round transition that follows it.
        await Expect(title).ToContainTextAsync("Step 1: Reading code", new() { Timeout = 5000 });

        // The round transition arrives right after the tool ended; round 1's activity must not get round 2's number.
        var seen = new List<string>();
        for (var i = 0; i < 80; i++)
        {
            var text = await title.TextContentAsync() ?? "";
            seen.Add(text);
            if (text.Contains("Thinking")) break;
            await Page.WaitForTimeoutAsync(50);
        }

        Assert.That(seen.Any(t => t.Contains("Step 2: Reading code")), Is.False,
            "Titles seen: " + string.Join(" | ", seen.Distinct()));
        await Expect(title).ToContainTextAsync("Step 2: Thinking", new() { Timeout = 5000 });

        await Expect(title).ToContainTextAsync("Step 2: Code explorer", new() { Timeout = 8000 });
        await Expect(title).ToContainTextAsync("Step 2: Code explorer, Reviewer", new() { Timeout = 8000 });
        await Expect(title).ToContainTextAsync("Step 2: Code explorer, Reviewer +1", new() { Timeout = 8000 });
    }
}
