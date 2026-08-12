namespace LMLocal.Tests.E2E.AppTests;
using System.Text.RegularExpressions;

public partial class AppTestBase : PageTest
{
    // ?? Generated Regex Patterns

    [GeneratedRegex("online")]
    private static partial Regex OnlineRegex();
    // protected wrapper for tests
    protected static Regex Online => OnlineRegex();

    [GeneratedRegex("offline")]
    private static partial Regex OfflineRegex();
    protected static Regex Offline => OfflineRegex();

    [GeneratedRegex("(Thinking|Generating)")]
    private static partial Regex ThinkingOrGeneratingRegex();
    protected static Regex ThinkingOrGenerating => ThinkingOrGeneratingRegex();

    [GeneratedRegex(@"\([\d.]+\s+t/s\)")]
    private static partial Regex TokensPerSecondRegex();
    protected static Regex TokensPerSecond => TokensPerSecondRegex();

    [GeneratedRegex("(Copied|Done)")]
    private static partial Regex CopiedLabelRegex();
    protected static Regex CopiedLabel => CopiedLabelRegex();

    [GeneratedRegex(@"^792 tokens · cached 100 · 42\.0 t/s · \d+\.\d+ s$")]
    private static partial Regex TokenStatsBadgeWithTimeRegex();
    protected static Regex TokenStatsBadgeWithTime => TokenStatsBadgeWithTimeRegex();

    protected static string ReadMock(string fileName) =>
        File.ReadAllText(Path.GetFullPath($"TestAssets/{fileName}"));

    protected const string TestPageUrl = "https://app.local/test-app.html";

    public override BrowserNewContextOptions ContextOptions() => new() { BypassCSP = true };

    protected async Task GotoWithMockAsync(string mockFileName)
    {
        var assetsDir = Path.GetFullPath("TestAssets");
        var resourcesDir = Path.GetFullPath(@"..\..\..\..\LMLocal\Resources");

        await Page.RouteAsync("https://app.local/**", async route =>
        {
            var urlPath = new Uri(route.Request.Url).AbsolutePath.TrimStart('/');

            if (urlPath == "test-app.html")
            {
                var testHtmlPath = Path.Combine(assetsDir, "test-app.html");
                if (!File.Exists(testHtmlPath))
                {
                    testHtmlPath = Path.GetFullPath(@"..\..\..\..\LMLocal\Resources\app.html");
                }
                await route.FulfillAsync(new() { Path = testHtmlPath });
                return;
            }

            var inAssets = Path.Combine(assetsDir, Path.GetFileName(urlPath));
            if (File.Exists(inAssets))
            {
                await route.FulfillAsync(new() { Path = inAssets });
                return;
            }

            var inResources = Path.Combine(resourcesDir, urlPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(inResources))
            {
                await route.FulfillAsync(new() { Path = inResources });
                return;
            }

            await route.AbortAsync();
        });

        await Page.AddInitScriptAsync(ReadMock(mockFileName));
        await Page.GotoAsync(TestPageUrl);
    }
}
