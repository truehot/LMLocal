namespace LMLocal.Tests.E2E.AppTests;
using System.Text.Json;

[TestFixture]
public class FileLinkTests : AppTestBase
{
    private const string FileLinkSelector = ".ai-message code.file-link";

    /// <summary>
    /// Streams an inline code span containing <paramref name="link"/> into the open AI answer and installs a
    /// <c>fileLinks</c> host override that records calls and replies with <paramref name="response"/>.
    /// </summary>
    private async Task RenderFileLinkAsync(string link, string response)
    {
        await GotoWithMockAsync("webview-mock-persistent-streaming.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.EvaluateAsync(@"response => {
            window.__fileLinkCalls = [];
            window.__fileLinksOverride = {
                OpenAsync: async (json) => {
                    window.__fileLinkCalls.push(json);
                    return response;
                }
            };
        }", response);

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();
        await Expect(Page.Locator(".ai-response-container")).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Passed as an argument, not interpolated: inside a JS string literal "\a" would become "a".
        await Page.EvaluateAsync(
            "payload => window.__emitBridgeMessage({ Type: 'StreamContent', Payload: payload, Count: 1, TokensPerSecond: 1 })",
            $" see `{link}` for details");

        // ChunkBuffer flushes a chunk only if 100 ms passed since the last flush, otherwise it waits for the next
        // chunk. StreamEnd always flushes the buffer, so the link reaches the DOM regardless of timing.
        await Page.EvaluateAsync("() => window.__emitBridgeMessage({ Type: 'StreamEnd' })");

        await Expect(Page.Locator(FileLinkSelector)).ToHaveCountAsync(1, new() { Timeout = 5000 });
    }

    [Test]
    [Category("Chat")]
    public async Task Click_FileLinkWithLineSuffix_SendsLinkWithoutSuffix()
    {
        await RenderFileLinkAsync(@"services\app.data.service.js:42", "{\"status\":\"opened\",\"path\":\"x\",\"via\":\"suffix\"}");

        await Page.Locator(FileLinkSelector).ClickAsync();
        await Page.WaitForFunctionAsync("() => window.__fileLinkCalls.length === 1");

        var calls = await Page.EvaluateAsync<string[]>("() => window.__fileLinkCalls");

        Assert.That(calls, Has.Length.EqualTo(1));
        using var document = JsonDocument.Parse(calls[0]);
        Assert.That(
            document.RootElement.GetProperty("link").GetString(),
            Is.EqualTo(@"services\app.data.service.js"),
            "LOCATION_SUFFIX must be stripped before the host call");
    }

    [Test]
    [Category("Chat")]
    public async Task Click_FileLink_IsQuietWhenFileIsNotFound()
    {
        await RenderFileLinkAsync(@"wrongdir\app.data.service.js", "{\"status\":\"notFound\"}");

        // A failed resolution is silent by design: console output only, no toast.
        var warning = await Page.RunAndWaitForConsoleMessageAsync(
            () => Page.Locator(FileLinkSelector).ClickAsync(),
            new() { Predicate = m => m.Type == "warning" && m.Text.Contains("was not opened"), Timeout = 5000 });

        Assert.That(warning.Text, Does.Contain("notFound"));
        Assert.That(await Page.EvaluateAsync<int>("() => window.__fileLinkCalls.length"), Is.EqualTo(1));
        await Expect(Page.Locator("#app-toast.show")).ToHaveCountAsync(0);
    }
}
