namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public class ModelsConfigDialogTests : AppTestBase
{
    private async Task OpenModelsDialogAsync()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-models']").ClickAsync();

        var dialog = Page.Locator("#models-config-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#models-config-list-container')?.children.length > 0");
    }

    [Test]
    [Category("ModelsConfig")]
    public async Task Open_ModelsConfigDialog_IsVisible()
    {
        await OpenModelsDialogAsync();

        var dialog = Page.Locator("#models-config-dialog");
        await Expect(dialog.Locator(".modal-header")).ToHaveTextAsync("Models");

        // Mock returns two models -> two cards rendered.
        var cards = dialog.Locator("#models-config-list-container .provider-card");
        await Expect(cards).ToHaveCountAsync(2);

        await Expect(dialog.Locator("#model-add-btn")).ToHaveTextAsync("+ Add Model");
    }

    [Test]
    [Category("ModelsConfig")]
    public async Task AddModel_ClicksAddButton_ShowsFormWithGeneratedId()
    {
        await OpenModelsDialogAsync();

        var dialog = Page.Locator("#models-config-dialog");
        var listView = dialog.Locator("#models-config-list-view");
        var formView = dialog.Locator("#model-form-view");

        await dialog.Locator("#model-add-btn").ClickAsync();

        await Expect(formView).Not.ToHaveClassAsync("hidden");
        await Expect(listView).ToHaveClassAsync("hidden");

        // New model: hidden id = next id (mock has ids 1,2 -> 3), modelId empty, custom checked by default.
        var idInput = dialog.Locator("[data-setting='id']");
        await Expect(idInput).ToHaveValueAsync("3");
        await Expect(dialog.Locator("[data-setting='modelId']")).ToHaveValueAsync("");
        await Expect(dialog.Locator("[data-setting='isCustom']")).ToBeCheckedAsync();

        await dialog.Locator("#model-form-cancel").ClickAsync();
        await Expect(listView).Not.ToHaveClassAsync("hidden");
        await Expect(formView).ToHaveClassAsync("hidden");
    }

    [Test]
    [Category("ModelsConfig")]
    public async Task AddModel_ApplyWithoutModelId_DoesNotLeaveForm()
    {
        await OpenModelsDialogAsync();

        var dialog = Page.Locator("#models-config-dialog");
        var formView = dialog.Locator("#model-form-view");

        await dialog.Locator("#model-add-btn").ClickAsync();
        await Expect(formView).Not.ToHaveClassAsync("hidden");

        // modelId is required; leave empty and submit.
        await dialog.Locator("#model-form-save").ClickAsync();

        // Form stays open because native validation blocks submit.
        await Expect(formView).Not.ToHaveClassAsync("hidden");
    }

    [Test]
    [Category("ModelsConfig")]
    public async Task AddModel_ApplyCustomWithoutDisplayName_ShowsErrorAndStaysInForm()
    {
        await OpenModelsDialogAsync();

        var dialog = Page.Locator("#models-config-dialog");
        var formView = dialog.Locator("#model-form-view");

        await dialog.Locator("#model-add-btn").ClickAsync();
        await Expect(formView).Not.ToHaveClassAsync("hidden");

        // Custom model is default; fill modelId but leave displayName empty.
        await dialog.Locator("[data-setting='modelId']").FillAsync("manual-test");
        await dialog.Locator("#model-form-save").ClickAsync();

        // Validation in the dialog keeps the form open.
        await Expect(formView).Not.ToHaveClassAsync("hidden");
    }

    [Test]
    [Category("ModelsConfig")]
    public async Task AddModel_FillsFormAndApplies_AddsModelToCardList()
    {
        await OpenModelsDialogAsync();

        var dialog = Page.Locator("#models-config-dialog");
        var formView = dialog.Locator("#model-form-view");
        var listView = dialog.Locator("#models-config-list-view");

        await dialog.Locator("#model-add-btn").ClickAsync();
        await Expect(formView).Not.ToHaveClassAsync("hidden");

        // Custom model needs a display name; provide both modelId and displayName.
        await dialog.Locator("[data-setting='modelId']").FillAsync("my-custom-model");
        await dialog.Locator("[data-setting='displayName']").FillAsync("My Custom Model");
        await dialog.Locator("#model-form-save").ClickAsync();

        await Expect(listView).Not.ToHaveClassAsync("hidden");
        await Expect(formView).ToHaveClassAsync("hidden");

        var cards = dialog.Locator("#models-config-list-container .provider-card");
        await Expect(cards).ToHaveCountAsync(3);

        var newCard = dialog.Locator("#models-config-list-container .provider-card")
            .Filter(new() { HasText = "My Custom Model" });
        await Expect(newCard).ToHaveCountAsync(1);
    }

    [Test]
    [Category("ModelsConfig")]
    public async Task Save_AfterAddingModel_CallsUpdateWithPersistedConfig()
    {
        await OpenModelsDialogAsync();

        var dialog = Page.Locator("#models-config-dialog");
        await dialog.Locator("#model-add-btn").ClickAsync();
        await dialog.Locator("[data-setting='modelId']").FillAsync("persisted-model");
        await dialog.Locator("[data-setting='displayName']").FillAsync("Persisted Model");
        await dialog.Locator("#model-form-save").ClickAsync();

        await dialog.Locator("#models-config-modal-confirm").ClickAsync();

        await Expect(dialog).Not.ToBeVisibleAsync();

        var saved = await Page.EvaluateAsync<string>("window.__lastSavedModelsConfig || 'null'");
        Assert.That(saved, Does.Contain("persisted-model"));
        Assert.That(saved, Does.Contain("Persisted Model"));
    }

    [Test]
    [Category("ModelsConfig")]
    public async Task Remove_DeletesCardFromList()
    {
        await OpenModelsDialogAsync();

        var dialog = Page.Locator("#models-config-dialog");
        var cards = dialog.Locator("#models-config-list-container .provider-card");
        await Expect(cards).ToHaveCountAsync(2);

        // First card has a Remove button.
        await cards.First.Locator("button.btn-danger-text").ClickAsync();

        await Expect(cards).ToHaveCountAsync(1);
    }

    [Test]
    [Category("ModelsConfig")]
    public async Task Remove_AndSave_DoesNotPersistRemovedModel()
    {
        await OpenModelsDialogAsync();

        var dialog = Page.Locator("#models-config-dialog");
        var cards = dialog.Locator("#models-config-list-container .provider-card");
        await Expect(cards).ToHaveCountAsync(2);

        // Remove the custom model card (contains "My Custom").
        var customCard = cards.Filter(new() { HasText = "My Custom" });
        await customCard.Locator("button.btn-danger-text").ClickAsync();

        await Expect(cards).ToHaveCountAsync(1);

        await dialog.Locator("#models-config-modal-confirm").ClickAsync();
        await Expect(dialog).Not.ToBeVisibleAsync();

        var saved = await Page.EvaluateAsync<string>("window.__lastSavedModelsConfig || 'null'");
        Assert.That(saved, Does.Not.Contain("manual/custom-model"));
        Assert.That(saved, Does.Contain("qwen2.5-coder-7b-instruct"));
    }
}





