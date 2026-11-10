namespace LMLocal.Tests.E2E.AppTests;

/// <summary>
/// The persisted selection points at a disabled instruction tab (the user unchecked the open tab in
/// "AI Instructions..." and clicked Save). The chat bar must not show one mode while another one is sent.
/// </summary>
[TestFixture]
public class DisabledSelectedInstructionTests : AppTestBase
{
    private const string MockFile = "webview-mock-disabled-selected-instruction.js";

    [Test]
    [Category("Instructions")]
    public async Task DisabledSelectedTab_ChatSendsTheModeTheDropdownShows()
    {
        await GotoWithMockAsync(MockFile);
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // The dropdown cannot show a disabled mode, so it shows the first enabled one.
        await Expect(Page.Locator("#selectedOption")).ToHaveTextAsync("Default", new() { Timeout = 5000 });

        // The status bar describes the effective instruction: it must agree with the dropdown.
        await Expect(Page.Locator("#tools-mode-status")).ToHaveTextAsync("Default", new() { Timeout = 5000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();
        await Page.WaitForFunctionAsync("() => !!window.__lastPromptRequest");

        string request = await Page.EvaluateAsync<string>("window.__lastPromptRequest");
        Assert.That(request, Does.Contain("DEFAULT PROMPT"), "the request must carry the prompt of the mode shown in the dropdown");
        Assert.That(request, Does.Not.Contain("REVIEW PROMPT"));
    }
}
