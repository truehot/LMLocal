namespace LMLocal.Tests.E2E.AppTests;

/// <summary>
/// Startup model-selection priority chain (§6.4 of docs/recentmodels.md):
/// active → recent among loaded → first loaded → recent from list → dialog.
/// </summary>
[TestFixture]
public class StartupModelSelectionTests : AppTestBase
{
    [Test]
    [Category("Startup")]
    public async Task Startup_ActiveModel_WinsOverRecent()
    {
        await Page.AddInitScriptAsync(@"window.__mockModels = [
                { id: 'model-recent', name: 'Recent Used', isLoaded: true, supportsMaxTokens: false, maxTokens: 0 },
                { id: 'model-active', name: 'Active Model', isLoaded: true, supportsMaxTokens: false, maxTokens: 0 }
            ];
            window.__mockActiveModel = { id: 'model-active', name: 'Active Model', isLoaded: true, supportsMaxTokens: false, maxTokens: 0 };
            window.__mockRecentEntries = [
                { providerType: 'lmstudio', providerId: null, modelId: 'model-recent', modelName: 'Recent Used', lastUsedUtc: '2026-01-15T12:34:56.1Z' }
            ];");
        await GotoWithMockAsync("webview-mock-recent-models.js");

        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });
        await Expect(Page.Locator("#model-name")).ToHaveTextAsync("Active Model");

        // Priority 1 never goes through SetActiveModelAsync.
        var captured = await Page.EvaluateAsync<string>("() => window.__capturedActiveModelId || null");
        Assert.That(captured, Is.Null, "activeModel must not trigger SetActiveModelAsync");
    }

    [Test]
    [Category("Startup")]
    public async Task Startup_RecentLoadedModel_PreferredOverFirstLoaded()
    {
        await Page.AddInitScriptAsync(@"window.__mockModels = [
                { id: 'model-first', name: 'First Loaded', isLoaded: true, supportsMaxTokens: false, maxTokens: 0 },
                { id: 'model-recent', name: 'Recent Used', isLoaded: true, supportsMaxTokens: false, maxTokens: 0 }
            ];
            window.__mockActiveModel = null;
            window.__mockRecentEntries = [
                { providerType: 'lmstudio', providerId: null, modelId: 'model-recent', modelName: 'Recent Used', lastUsedUtc: '2026-01-15T12:34:56.1Z' }
            ];");
        await GotoWithMockAsync("webview-mock-recent-models.js");

        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var captured = await Page.EvaluateAsync<string>("() => window.__capturedActiveModelId || null");
        Assert.That(captured, Is.EqualTo("model-recent"),
            "Recent loaded model must win over the first loaded model");
    }

    [Test]
    [Category("Startup")]
    public async Task Startup_NoRecent_FallsBackToFirstLoaded()
    {
        await Page.AddInitScriptAsync(@"window.__mockModels = [
                { id: 'model-first', name: 'First Loaded', isLoaded: true, supportsMaxTokens: false, maxTokens: 0 },
                { id: 'model-second', name: 'Second Loaded', isLoaded: true, supportsMaxTokens: false, maxTokens: 0 }
            ];
            window.__mockActiveModel = null;
            window.__mockRecentEntries = [];");
        await GotoWithMockAsync("webview-mock-recent-models.js");

        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var captured = await Page.EvaluateAsync<string>("() => window.__capturedActiveModelId || null");
        Assert.That(captured, Is.EqualTo("model-first"),
            "Without recent entries the first loaded model must be selected");
    }

    [Test]
    [Category("Startup")]
    public async Task Startup_RecentNotLoaded_UsedWhenNoLoadedModels()
    {
        await Page.AddInitScriptAsync(@"window.__mockModels = [
                { id: 'model-other', name: 'Other', isLoaded: false, supportsMaxTokens: false, maxTokens: 0 },
                { id: 'model-recent', name: 'Recent Used', isLoaded: false, supportsMaxTokens: false, maxTokens: 0 }
            ];
            window.__mockActiveModel = null;
            window.__mockRecentEntries = [
                { providerType: 'lmstudio', providerId: null, modelId: 'model-recent', modelName: 'Recent Used', lastUsedUtc: '2026-01-15T12:34:56.1Z' }
            ];");
        await GotoWithMockAsync("webview-mock-recent-models.js");

        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var captured = await Page.EvaluateAsync<string>("() => window.__capturedActiveModelId || null");
        Assert.That(captured, Is.EqualTo("model-recent"),
            "Recent not-loaded model must be selected when no model is loaded");
    }

    [Test]
    [Category("Startup")]
    public async Task Startup_NoRecentNoLoaded_ShowsDialog()
    {
        await Page.AddInitScriptAsync(@"window.__mockModels = [
                { id: 'model-solo', name: 'Solo', isLoaded: false, supportsMaxTokens: false, maxTokens: 0 }
            ];
            window.__mockActiveModel = null;
            window.__mockRecentEntries = [];");
        await GotoWithMockAsync("webview-mock-recent-models.js");

        var dialog = Page.Locator("#model-selector-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 3000 });

        var captured = await Page.EvaluateAsync<string>("() => window.__capturedActiveModelId || null");
        Assert.That(captured, Is.Null, "No model must be activated when the dialog is shown");
    }
}
