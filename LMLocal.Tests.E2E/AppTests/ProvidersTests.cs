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
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        // Wait for list to be populated
        await Page.WaitForFunctionAsync("() => document.querySelector('#providers-list-container')?.children.length > 0");

        // Verify header and content
        await Expect(dialog.Locator(".modal-header")).ToHaveTextAsync("Providers");
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
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        // Wait for list to be populated
        await Page.WaitForFunctionAsync("() => document.querySelector('#providers-list-container')?.children.length > 0");

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
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        // Wait for list to be populated
        await Page.WaitForFunctionAsync("() => document.querySelector('#providers-list-container')?.children.length > 0");

        // Verify cancel button
        var cancelBtn = dialog.Locator("#providers-modal-cancel");
        await Expect(cancelBtn).ToHaveCountAsync(1);
        await Expect(cancelBtn).ToHaveTextAsync("Cancel");

        // Verify save button
        var saveBtn = dialog.Locator("#providers-modal-confirm");
        await Expect(saveBtn).ToHaveCountAsync(1);
        await Expect(saveBtn).ToHaveTextAsync("Save");
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
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        // Wait for list to be populated
        await Page.WaitForFunctionAsync("() => document.querySelector('#providers-list-container')?.children.length > 0");

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
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        // Wait for list to be populated
        await Page.WaitForFunctionAsync("() => document.querySelector('#providers-list-container')?.children.length > 0");

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

        // Click Apply button (form-save — commits to in-memory list, does NOT close dialog)
        var formSaveBtn = dialog.Locator("#provider-form-save");
        await formSaveBtn.ClickAsync();

        // Verify form closes and list view is shown (dialog still open)
        var listView = dialog.Locator("#providers-list-view");
        await Expect(listView).Not.ToHaveClassAsync("hidden");
        await Expect(formView).ToHaveClassAsync("hidden");

        // Verify the provider was added to the in-memory list
        var listContainer = dialog.Locator("#providers-list-container");
        var providerCards = listContainer.Locator(".provider-card");
        await Expect(providerCards).ToHaveCountAsync(1);

        // Verify provider details in the card
        var providerCard = providerCards.First;
        await Expect(providerCard.Locator(".provider-card-name")).ToHaveTextAsync("Test OpenAI Provider");
        await Expect(providerCard.Locator(".provider-card-type")).ToHaveTextAsync("OpenAI");
        await Expect(providerCard.Locator(".provider-card-meta")).ToHaveTextAsync("https://api.openai.com/v1");

        // Now click the main Save button to persist and close the dialog
        await dialog.Locator("#providers-modal-confirm").ClickAsync();

        // Verify dialog is closed
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("Providers")]
    public async Task CloseDialog_CancelButton_ClosesDialog()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open providers dialog
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-providers']").ClickAsync();

        var dialog = Page.Locator("#providers-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#providers-list-container')?.children.length > 0");

        // Click Cancel
        await dialog.Locator("#providers-modal-cancel").ClickAsync();

        // Verify dialog is closed
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("Providers")]
    public async Task CloseDialog_EscapeKey_ClosesDialog()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open providers dialog
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-providers']").ClickAsync();

        var dialog = Page.Locator("#providers-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#providers-list-container')?.children.length > 0");

        // Press Escape
        await Page.Keyboard.PressAsync("Escape");

        // Verify dialog is closed
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("Providers")]
    public async Task CloseDialog_SaveButton_ClosesDialogWithChanges()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open providers dialog
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-providers']").ClickAsync();

        var dialog = Page.Locator("#providers-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#providers-list-container')?.children.length > 0");

        // Add a provider first (unlike the empty-dialog test above)
        await dialog.Locator("#provider-add-btn").ClickAsync();

        var nameInput = dialog.Locator("[data-setting='name']");
        var typeSelect = dialog.Locator("[data-setting='providerType']");
        var urlInput = dialog.Locator("[data-setting='customBaseUrl']");
        var keyInput = dialog.Locator("[data-setting='customApiKey']");

        await nameInput.FillAsync("My Ollama Provider");
        await typeSelect.SelectOptionAsync("ollama");
        await urlInput.FillAsync("http://localhost:11434");
        await keyInput.FillAsync("ollama-key");

        // Apply in form (commits to in-memory list, returns to list view)
        await dialog.Locator("#provider-form-save").ClickAsync();
        await Expect(dialog.Locator("#providers-list-view")).Not.ToHaveClassAsync("hidden");

        // Click main Save to persist changes and close dialog
        await dialog.Locator("#providers-modal-confirm").ClickAsync();

        // Verify dialog is closed
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    /// <summary>
    /// Overrides the providers bridge mock to return 3 custom providers
    /// in a deliberately unsorted order (Bravo, Charlie, Alpha).
    /// </summary>
    private async Task OverrideProvidersWithThreeAsync()
    {
        await Page.EvaluateAsync(@"() => {
            window.__providersOverride = {
                GetProvidersAsync: async () => JSON.stringify({
                    success: true,
                    data: {
                        providers: [
                            { id: 1, name: 'Bravo', providerType: 'ollama', customBaseUrl: 'http://192.168.1.10:11434', customApiKey: '' },
                            { id: 2, name: 'Charlie', providerType: 'lmstudio', customBaseUrl: 'http://192.168.1.11:1234', customApiKey: '' },
                            { id: 3, name: 'Alpha', providerType: 'openai', customBaseUrl: 'https://10.0.0.3:8443', customApiKey: '' }
                        ],
                        providerTypes: [
                            { key: 'openai', displayName: 'OpenAI' },
                            { key: 'ollama', displayName: 'Ollama' },
                            { key: 'lmstudio', displayName: 'LM Studio' }
                        ]
                    }
                }),
                UpdateProvidersAsync: async (json) => true,
            };
        }");
    }

    private async Task OpenProvidersDialogAsync()
    {
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-providers']").ClickAsync();
        await Expect(Page.Locator("#providers-dialog")).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#providers-list-container')?.children.length > 0");
    }

    [Test]
    [Category("Providers")]
    public async Task ProvidersDialog_Filter_FiltersByNameTypeUrl()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });
        await OverrideProvidersWithThreeAsync();

        await OpenProvidersDialogAsync();

        var cards = Page.Locator("#providers-list-container .provider-card");
        await Expect(cards).ToHaveCountAsync(3);

        // By name
        await Page.Locator("#provider-filter-input").FillAsync("alpha");
        await Expect(cards).ToHaveCountAsync(1);
        await Expect(cards.First.Locator(".provider-card-name")).ToHaveTextAsync("Alpha");

        // By provider type
        await Page.Locator("#provider-filter-input").FillAsync("ollama");
        await Expect(cards).ToHaveCountAsync(1);
        await Expect(cards.First.Locator(".provider-card-name")).ToHaveTextAsync("Bravo");

        // By URL only: "192.168.1.11" appears only in Charlie's customBaseUrl,
        // not in its name or type — proves URL matching.
        await Page.Locator("#provider-filter-input").FillAsync("192.168.1.11");
        await Expect(cards).ToHaveCountAsync(1);
        await Expect(cards.First.Locator(".provider-card-name")).ToHaveTextAsync("Charlie");

        // No match -> empty list
        await Page.Locator("#provider-filter-input").FillAsync("zzz");
        await Expect(cards).ToHaveCountAsync(0);

        // Clear -> all back
        await Page.Locator("#provider-filter-input").FillAsync("");
        await Expect(cards).ToHaveCountAsync(3);
    }

    [Test]
    [Category("Providers")]
    public async Task ProvidersDialog_Sort_CyclesStates()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });
        await OverrideProvidersWithThreeAsync();

        await OpenProvidersDialogAsync();

        var cards = Page.Locator("#providers-list-container .provider-card");
        await Expect(cards).ToHaveCountAsync(3);

        // Default (no sort): backend order Bravo, Charlie, Alpha
        await Expect(cards.Nth(0).Locator(".provider-card-name")).ToHaveTextAsync("Bravo");
        await Expect(cards.Nth(1).Locator(".provider-card-name")).ToHaveTextAsync("Charlie");
        await Expect(cards.Nth(2).Locator(".provider-card-name")).ToHaveTextAsync("Alpha");

        // Asc by name: Alpha, Bravo, Charlie
        await Page.Locator("#providers-sort-btn").ClickAsync();
        await Expect(cards.Nth(0).Locator(".provider-card-name")).ToHaveTextAsync("Alpha");
        await Expect(cards.Nth(1).Locator(".provider-card-name")).ToHaveTextAsync("Bravo");
        await Expect(cards.Nth(2).Locator(".provider-card-name")).ToHaveTextAsync("Charlie");

        // Desc by name: Charlie, Bravo, Alpha
        await Page.Locator("#providers-sort-btn").ClickAsync();
        await Expect(cards.Nth(0).Locator(".provider-card-name")).ToHaveTextAsync("Charlie");
        await Expect(cards.Nth(1).Locator(".provider-card-name")).ToHaveTextAsync("Bravo");
        await Expect(cards.Nth(2).Locator(".provider-card-name")).ToHaveTextAsync("Alpha");

        // Back to null -> backend order restored
        await Page.Locator("#providers-sort-btn").ClickAsync();
        await Expect(cards.Nth(0).Locator(".provider-card-name")).ToHaveTextAsync("Bravo");
        await Expect(cards.Nth(1).Locator(".provider-card-name")).ToHaveTextAsync("Charlie");
        await Expect(cards.Nth(2).Locator(".provider-card-name")).ToHaveTextAsync("Alpha");
    }

    [Test]
    [Category("Providers")]
    public async Task ProvidersDialog_Reopen_ResetsFilterAndSort()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });
        await OverrideProvidersWithThreeAsync();

        await OpenProvidersDialogAsync();

        var dialog = Page.Locator("#providers-dialog");
        await Page.Locator("#provider-filter-input").FillAsync("alpha");
        await Expect(dialog.Locator("#providers-list-container .provider-card")).ToHaveCountAsync(1);

        // Switch sort to 'asc' (Alpha, Bravo, Charlie) — must be reset on reopen
        await Page.Locator("#providers-sort-btn").ClickAsync();
        await Expect(dialog.Locator("#providers-list-container .provider-card").Nth(0).Locator(".provider-card-name")).ToHaveTextAsync("Alpha");

        // Close via Cancel
        await dialog.Locator("#providers-modal-cancel").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });

        // Reopen — filter and sort must be reset: all providers shown in backend order
        await OpenProvidersDialogAsync();
        await Expect(Page.Locator("#provider-filter-input")).ToHaveValueAsync("");
        await Expect(dialog.Locator("#providers-list-container .provider-card")).ToHaveCountAsync(3);

        // Sort reset to null (backend order): Bravo, Charlie, Alpha
        await Expect(dialog.Locator("#providers-list-container .provider-card").Nth(0).Locator(".provider-card-name")).ToHaveTextAsync("Bravo");
        await Expect(dialog.Locator("#providers-list-container .provider-card").Nth(1).Locator(".provider-card-name")).ToHaveTextAsync("Charlie");
        await Expect(dialog.Locator("#providers-list-container .provider-card").Nth(2).Locator(".provider-card-name")).ToHaveTextAsync("Alpha");
    }

    [Test]
    [Category("Providers")]
    public async Task ProvidersDialog_FormCancel_PreservesFilter()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });
        await OverrideProvidersWithThreeAsync();

        await OpenProvidersDialogAsync();

        var dialog = Page.Locator("#providers-dialog");
        await Page.Locator("#provider-filter-input").FillAsync("alpha");
        await Expect(dialog.Locator("#providers-list-container .provider-card")).ToHaveCountAsync(1);

        // Open edit form for Alpha
        await dialog.Locator(".provider-card .provider-card-btn", new() { HasText = "Edit" }).First.ClickAsync();
        await Expect(dialog.Locator("#provider-form-view")).Not.ToHaveClassAsync("hidden");

        // Cancel the form -> back to list, filter preserved
        await dialog.Locator("#provider-form-cancel").ClickAsync();
        await Expect(dialog.Locator("#providers-list-view")).Not.ToHaveClassAsync("hidden");

        await Expect(Page.Locator("#provider-filter-input")).ToHaveValueAsync("alpha");
        await Expect(dialog.Locator("#providers-list-container .provider-card")).ToHaveCountAsync(1);
        await Expect(dialog.Locator("#providers-list-container .provider-card .provider-card-name")).ToHaveTextAsync("Alpha");
    }

    [Test]
    [Category("Providers")]
    public async Task ProvidersDialog_ApplyMatching_PreservesFilter()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });
        await OverrideProvidersWithThreeAsync();

        await OpenProvidersDialogAsync();

        var dialog = Page.Locator("#providers-dialog");
        await Page.Locator("#provider-filter-input").FillAsync("alpha");
        await Expect(dialog.Locator("#providers-list-container .provider-card")).ToHaveCountAsync(1);

        // Add a provider that still matches "alpha"
        await dialog.Locator("#provider-add-btn").ClickAsync();
        await dialog.Locator("[data-setting='name']").FillAsync("Alpha 2");
        await dialog.Locator("[data-setting='providerType']").SelectOptionAsync("openai");
        await dialog.Locator("[data-setting='customBaseUrl']").FillAsync("http://alpha2.local");
        await dialog.Locator("#provider-form-save").ClickAsync();

        // Filter preserved, both Alpha providers visible
        await Expect(Page.Locator("#provider-filter-input")).ToHaveValueAsync("alpha");
        await Expect(dialog.Locator("#providers-list-container .provider-card")).ToHaveCountAsync(2);
    }

    [Test]
    [Category("Providers")]
    public async Task ProvidersDialog_ApplyNonMatching_ResetsFilter()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });
        await OverrideProvidersWithThreeAsync();

        await OpenProvidersDialogAsync();

        var dialog = Page.Locator("#providers-dialog");
        await Page.Locator("#provider-filter-input").FillAsync("alpha");
        await Expect(dialog.Locator("#providers-list-container .provider-card")).ToHaveCountAsync(1);

        // Add a provider that does NOT match "alpha" -> filter must be reset so it's visible
        await dialog.Locator("#provider-add-btn").ClickAsync();
        await dialog.Locator("[data-setting='name']").FillAsync("Zulu");
        await dialog.Locator("[data-setting='providerType']").SelectOptionAsync("openai");
        await dialog.Locator("[data-setting='customBaseUrl']").FillAsync("http://zulu.local");
        await dialog.Locator("#provider-form-save").ClickAsync();

        await Expect(Page.Locator("#provider-filter-input")).ToHaveValueAsync("");
        await Expect(dialog.Locator("#providers-list-container .provider-card")).ToHaveCountAsync(4);
        await Expect(dialog.Locator("#providers-list-container .provider-card", new() { HasText = "Zulu" })).ToHaveCountAsync(1);
    }

    [Test]
    [Category("Providers")]
    public async Task ProvidersDialog_SaveError_KeepsDialogOpenAndShowsToast()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Make UpdateProvidersAsync fail
        await Page.EvaluateAsync(@"() => {
            window.__providersOverride.UpdateProvidersAsync = async (json) => { throw new Error('providers save boom'); };
        }");

        await OpenProvidersDialogAsync();
        var dialog = Page.Locator("#providers-dialog");

        // Add a provider (local change must survive the failed save)
        await dialog.Locator("#provider-add-btn").ClickAsync();
        await dialog.Locator("[data-setting='name']").FillAsync("Persist Me");
        await dialog.Locator("[data-setting='providerType']").SelectOptionAsync("openai");
        await dialog.Locator("[data-setting='customBaseUrl']").FillAsync("http://persist.local");
        await dialog.Locator("#provider-form-save").ClickAsync();
        await Expect(dialog.Locator("#providers-list-container .provider-card")).ToHaveCountAsync(1);

        // Save -> fails -> dialog stays open, toast shows the reason
        await dialog.Locator("#providers-modal-confirm").ClickAsync();

        await Expect(dialog).ToBeVisibleAsync();
        var toast = Page.Locator("#app-toast.show");
        await Expect(toast).ToBeVisibleAsync();
        await Expect(toast).ToContainTextAsync("providers save boom");

        // Provider still in list for retry
        await Expect(dialog.Locator("#providers-list-container .provider-card")).ToHaveCountAsync(1);

        // Now succeed and save again -> dialog closes
        await Page.EvaluateAsync(@"() => {
            window.__providersOverride.UpdateProvidersAsync = async (json) => true;
        }");
        await dialog.Locator("#providers-modal-confirm").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("Providers")]
    public async Task ProvidersDialog_TestConnectionError_ShowsToast()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Make TestConnectionAsync fail with a reason
        await Page.EvaluateAsync(@"() => {
            window.__settingsOverride.TestConnectionAsync = async (json) => JSON.stringify({
                success: false,
                error: { message: 'Connection refused by remote host' }
            });
        }");

        await OpenProvidersDialogAsync();
        var dialog = Page.Locator("#providers-dialog");

        // Open form and fill required fields so the Test button works
        await dialog.Locator("#provider-add-btn").ClickAsync();
        await dialog.Locator("[data-setting='name']").FillAsync("Test Prov");
        await dialog.Locator("[data-setting='providerType']").SelectOptionAsync("openai");
        await dialog.Locator("[data-setting='customBaseUrl']").FillAsync("http://test.local");
        await dialog.Locator("[data-setting='customApiKey']").FillAsync("key");

        // Click Test -> error toast near the button
        await dialog.Locator(".test-connection-btn").ClickAsync();

        var toast = Page.Locator("#app-toast.show");
        await Expect(toast).ToBeVisibleAsync();
        await Expect(toast).ToContainTextAsync("Connection refused by remote host");
    }
}


