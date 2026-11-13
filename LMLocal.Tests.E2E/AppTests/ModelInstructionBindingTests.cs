namespace LMLocal.Tests.E2E.AppTests;
using System.Text.RegularExpressions;

/// <summary>
/// Covers binding a model to a single instruction (models.config.json -> instructionTabId):
/// the auto-apply on model activation and the "Auto-apply instruction" field in the models dialog.
/// </summary>
[TestFixture]
public partial class ModelInstructionBindingTests : AppTestBase
{
    private const string MockFile = "webview-mock-model-instruction.js";

    [GeneratedRegex("^Provider type: openai")]
    private static partial Regex ProviderTooltipRegex();

    private async Task GotoAndWaitConnectedAsync()
    {
        await GotoWithMockAsync(MockFile);
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });
    }

    private async Task OpenModelsDialogAsync()
    {
        await GotoAndWaitConnectedAsync();

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-models']").ClickAsync();

        var dialog = Page.Locator("#models-config-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#models-config-list-container')?.children.length > 0");
    }

    [Test]
    [Category("ModelInstruction")]
    public async Task Startup_ModelWithBoundInstruction_AutoSelectsInstructionTab()
    {
        await GotoAndWaitConnectedAsync();

        // The active model ("test-model-instance") is bound to instruction tab 4 ("Review"),
        // so the instruction dropdown must auto-select it instead of the persisted "Default" (id 1).
        var selected = Page.Locator("#selectedOption");
        await Expect(selected).ToHaveTextAsync("Review", new() { Timeout = 5000 });
    }

    [Test]
    [Category("ModelInstruction")]
    public async Task Startup_ModelWithBoundInstruction_StatusBarShowsInstruction()
    {
        await GotoAndWaitConnectedAsync();

        // The active model is bound to instruction tab 4 ("Review"); the status bar shows the
        // effective instruction. Tools are off by default, so only the instruction segment appears.
        var status = Page.Locator("#tools-mode-status");
        await Expect(status).ToHaveTextAsync("Review", new() { Timeout = 5000 });

        // The tooltip always exposes the full three-line state through the shared popover
        // (a native title is dismissed by Chromium whenever the attribute changes).
        await Expect(status).ToHaveAttributeAsync(
            "data-tooltip", "Instructions: Review (t=0.5)\nTools: Disabled\nSubAgents: Disabled");
    }

    [Test]
    [Category("ModelInstruction")]
    public async Task StatusBar_ProviderLabelPublishesTooltipThroughPopover()
    {
        await GotoAndWaitConnectedAsync();

        var connStatus = Page.Locator("#conn-status");

        // The provider label used to carry a native title. It now publishes the same text through
        // data-tooltip and the shared popover, like the token bar and the model name next to it.
        Assert.That(await connStatus.GetAttributeAsync("title"), Is.Null,
            "#conn-status must not use the native title");

        await Expect(connStatus).ToHaveAttributeAsync(
            "data-tooltip", ProviderTooltipRegex(), new() { Timeout = 3000 });

        await connStatus.HoverAsync();
        var tooltip = Page.Locator("#context-tooltip");
        await Expect(tooltip).ToBeVisibleAsync(new() { Timeout = 3000 });
        await Expect(tooltip).ToContainTextAsync("Provider type: openai");

        await Page.Locator("#userInput").HoverAsync();
        await Expect(tooltip).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("ModelInstruction")]
    public async Task ModelsDialog_EditBoundModel_ShowsInstructionInForm()
    {
        await OpenModelsDialogAsync();

        var dialog = Page.Locator("#models-config-dialog");
        var card = dialog.Locator("#models-config-list-container .provider-card")
            .Filter(new() { HasText = "Test Model" });

        await card.Locator("button:has-text('Edit')").ClickAsync();

        var instructionSelect = dialog.Locator("[data-setting='instructionTabId']");
        await Expect(instructionSelect).ToHaveValueAsync("4");

        await Expect(dialog.Locator("[data-setting='instructionTabId'] option[value='4']"))
            .ToHaveTextAsync("Review");
    }

    [Test]
    [Category("ModelInstruction")]
    public async Task ModelsDialog_SaveNewModelWithBinding_PersistsInstructionTabId()
    {
        await OpenModelsDialogAsync();

        var dialog = Page.Locator("#models-config-dialog");
        await dialog.Locator("#model-add-btn").ClickAsync();

        await dialog.Locator("[data-setting='modelId']").FillAsync("manual-with-binding");
        await dialog.Locator("[data-setting='displayName']").FillAsync("Manual With Binding");
        await dialog.Locator("[data-setting='instructionTabId']").SelectOptionAsync("4");
        await dialog.Locator("#model-form-save").ClickAsync();

        await dialog.Locator("#models-config-modal-confirm").ClickAsync();
        await Expect(dialog).Not.ToBeVisibleAsync();

        var saved = await Page.EvaluateAsync<string>("window.__lastSavedModelsConfig || 'null'");
        Assert.That(saved, Does.Contain("manual-with-binding"));
        Assert.That(saved, Does.Contain("\"instructionTabId\":4"));
    }

    [Test]
    [Category("ModelInstruction")]
    public async Task ModelsDialog_ClearBinding_OmitsInstructionTabIdFromPayload()
    {
        await OpenModelsDialogAsync();

        var dialog = Page.Locator("#models-config-dialog");
        var card = dialog.Locator("#models-config-list-container .provider-card")
            .Filter(new() { HasText = "Test Model" });

        await card.Locator("button:has-text('Edit')").ClickAsync();

        // Clear the binding ("Not set"): the field must not be persisted at all.
        await dialog.Locator("[data-setting='instructionTabId']").SelectOptionAsync("");
        await dialog.Locator("#model-form-save").ClickAsync();

        await dialog.Locator("#models-config-modal-confirm").ClickAsync();
        await Expect(dialog).Not.ToBeVisibleAsync();

        var saved = await Page.EvaluateAsync<string>("window.__lastSavedModelsConfig || 'null'");
        Assert.That(saved, Does.Not.Contain("instructionTabId"));
    }
}
