namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public class ProvidersTests : AppTestBase
{
    [Test]
    [Category("Providers")]
    public async Task Open_ProvidersDialog_IsVisible()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open providers dialog from menu
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-providers']").ClickAsync();

        var dialog = Page.Locator("#providers-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Verify header and content
        await Expect(dialog.Locator(".modal-header")).ToHaveTextAsync("Configure providers");
        await Expect(dialog.Locator("#provider-add-btn")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("Providers")]
    public async Task ProvidersDialog_HasAddProfileButton()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open providers dialog
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-providers']").ClickAsync();

        var dialog = Page.Locator("#providers-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Verify "Add Profile" button exists and is visible
        var addBtn = dialog.Locator("#provider-add-btn");
        await Expect(addBtn).ToHaveCountAsync(1);
        await Expect(addBtn).ToHaveTextAsync("+ Add Profile");
    }

    [Test]
    [Category("Providers")]
    public async Task ProvidersDialog_HasCancelAndSaveButtons()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open providers dialog
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-providers']").ClickAsync();

        var dialog = Page.Locator("#providers-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Verify cancel button
        var cancelBtn = dialog.Locator("#providers-modal-cancel");
        await Expect(cancelBtn).ToHaveCountAsync(1);
        await Expect(cancelBtn).ToHaveTextAsync("Cancel");

        // Verify save button
        var saveBtn = dialog.Locator("#providers-modal-confirm");
        await Expect(saveBtn).ToHaveCountAsync(1);
        await Expect(saveBtn).ToHaveTextAsync("Save Changes");
    }

    [Test]
    [Category("Providers")]
    public async Task AddProfile_ClicksAddButton_ShowsFormAndReturnsToList()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open providers dialog
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-providers']").ClickAsync();

        var dialog = Page.Locator("#providers-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Verify list view is visible
        var listView = dialog.Locator("#providers-list-view");
        var formView = dialog.Locator("#provider-form-view");

        // List should be visible, form should be hidden
        await Expect(listView).Not.ToHaveClassAsync("hidden");
        await Expect(formView).ToHaveClassAsync("hidden");

        // Click "Add Profile" button
        var addBtn = dialog.Locator("#provider-add-btn");
        await addBtn.ClickAsync();

        // Verify form view is now visible
        await Expect(formView).Not.ToHaveClassAsync("hidden");
        await Expect(listView).ToHaveClassAsync("hidden");

        // Verify form fields are empty for new provider
        var nameInput = dialog.Locator("[data-setting='name']");
        var typeSelect = dialog.Locator("[data-setting='providerType']");
        var urlInput = dialog.Locator("[data-setting='customBaseUrl']");
        var keyInput = dialog.Locator("[data-setting='customApiKey']");

        await Expect(nameInput).ToHaveValueAsync("");
        await Expect(typeSelect).ToHaveValueAsync("openai");
        await Expect(urlInput).ToHaveValueAsync("");
        await Expect(keyInput).ToHaveValueAsync("");

        // Fill in the form
        await nameInput.FillAsync("My Test Provider");
        await typeSelect.SelectOptionAsync("ollama");
        await urlInput.FillAsync("http://localhost:11434");
        await keyInput.FillAsync("test-api-key");

        // Verify form fields have values
        await Expect(nameInput).ToHaveValueAsync("My Test Provider");
        await Expect(typeSelect).ToHaveValueAsync("ollama");
        await Expect(urlInput).ToHaveValueAsync("http://localhost:11434");
        await Expect(keyInput).ToHaveValueAsync("test-api-key");

        // Click cancel to go back to list without saving
        var formCancelBtn = dialog.Locator("#provider-form-cancel");
        await formCancelBtn.ClickAsync();

        // Verify list view is visible again
        await Expect(listView).Not.ToHaveClassAsync("hidden");
        await Expect(formView).ToHaveClassAsync("hidden");
    }

    [Test]
    [Category("Providers")]
    public async Task AddProfile_FillsFormAndApplies_AddsProviderToList()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open providers dialog
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-providers']").ClickAsync();

        var dialog = Page.Locator("#providers-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Click "Add Profile" button
        var addBtn = dialog.Locator("#provider-add-btn");
        await addBtn.ClickAsync();

        var formView = dialog.Locator("#provider-form-view");
        await Expect(formView).Not.ToHaveClassAsync("hidden");

        // Fill in the form
        var nameInput = dialog.Locator("[data-setting='name']");
        var typeSelect = dialog.Locator("[data-setting='providerType']");
        var urlInput = dialog.Locator("[data-setting='customBaseUrl']");
        var keyInput = dialog.Locator("[data-setting='customApiKey']");

        await nameInput.FillAsync("Test OpenAI Provider");
        await typeSelect.SelectOptionAsync("openai");
        await urlInput.FillAsync("https://api.openai.com/v1");
        await keyInput.FillAsync("sk-test-key-123");

        // Click Apply/Save button
        var formSaveBtn = dialog.Locator("#provider-form-save");
        await formSaveBtn.ClickAsync();

        // Verify form closes and list view is shown
        var listView = dialog.Locator("#providers-list-view");
        await Expect(listView).Not.ToHaveClassAsync("hidden");
        await Expect(formView).ToHaveClassAsync("hidden");

        // Verify the provider was added to the list
        var listContainer = dialog.Locator("#providers-list-container");
        var providerCards = listContainer.Locator(".provider-card");
        await Expect(providerCards).ToHaveCountAsync(1);

        // Verify provider details in the card
        var providerCard = providerCards.First;
        await Expect(providerCard.Locator(".provider-card-name")).ToHaveTextAsync("Test OpenAI Provider");
        await Expect(providerCard.Locator(".provider-card-type")).ToHaveTextAsync("openai");
        await Expect(providerCard.Locator(".provider-card-meta")).ToHaveTextAsync("https://api.openai.com/v1");

        // Verify card has Edit and Remove buttons
        var editBtn = providerCard.Locator("button").Filter(new() { HasText = "Edit" });
        var removeBtn = providerCard.Locator("button").Filter(new() { HasText = "Remove" });

        await Expect(editBtn).ToHaveCountAsync(1);
        await Expect(removeBtn).ToHaveCountAsync(1);
    }
}
