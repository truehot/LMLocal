namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public class InstructionsTests : AppTestBase
{
    [Test]
    [Category("Instructions")]
    public async Task Open_InstructionsDialog_IsVisible()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open instructions dialog from menu
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-instructions']").ClickAsync();
        // Wait for dialog to open
        await Task.Delay(200);

        var dialog = Page.Locator("#instructions-dialog");
        // Wait for dialog to be visible in DOM
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        // Wait for modal body to be populated
        await Page.WaitForFunctionAsync("() => document.querySelector('#instructions-dialog .modal-body')?.children.length > 0");

        // Verify header
        await Expect(dialog.Locator(".modal-header")).ToHaveTextAsync("AI Instructions");
    }

    [Test]
    [Category("Instructions")]
    public async Task InstructionsDialog_HasModalContainer()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open instructions dialog
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-instructions']").ClickAsync();
        // Wait for dialog to open
        await Task.Delay(200);

        var dialog = Page.Locator("#instructions-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        // Wait for modal body to be populated
        await Page.WaitForFunctionAsync("() => document.querySelector('#instructions-dialog .modal-body')?.children.length > 0");

        // Verify modal container exists (where content is populated)
        var container = dialog.Locator(".modal-container");
        await Expect(container).ToHaveCountAsync(1);
    }

    [Test]
    [Category("Instructions")]
    public async Task InstructionsDialog_HasButtons()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open instructions dialog
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-instructions']").ClickAsync();
        // Wait for dialog to open
        await Task.Delay(200);

        var dialog = Page.Locator("#instructions-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        // Wait for modal body to be populated
        await Page.WaitForFunctionAsync("() => document.querySelector('#instructions-dialog .modal-body')?.children.length > 0");

        // Verify cancel button exists
        var cancelBtn = dialog.Locator("#instructions-dialog-cancel");
        await Expect(cancelBtn).ToHaveCountAsync(1);
        await Expect(cancelBtn).ToHaveTextAsync("Cancel");

        // Verify save button exists
        var saveBtn = dialog.Locator("#instructions-dialog-confirm");
        await Expect(saveBtn).ToHaveCountAsync(1);
        await Expect(saveBtn).ToHaveTextAsync("Save");
    }

    [Test]
    [Category("Instructions")]
    public async Task InstructionsDialog_HasModalFooter()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Open instructions dialog
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-instructions']").ClickAsync();
        // Wait for dialog to open
        await Task.Delay(200);

        var dialog = Page.Locator("#instructions-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        // Wait for modal body to be populated
        await Page.WaitForFunctionAsync("() => document.querySelector('#instructions-dialog .modal-body')?.children.length > 0");

        // Verify modal footer exists
        var footer = dialog.Locator(".modal-footer");
        await Expect(footer).ToHaveCountAsync(1);
    }

    [Test]
    [Category("Instructions")]
    public async Task CloseDialog_CancelButton_ClosesDialog()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-instructions']").ClickAsync();
        await Task.Delay(200);

        var dialog = Page.Locator("#instructions-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#instructions-dialog .modal-body')?.children.length > 0");

        // Click Cancel
        await dialog.Locator("#instructions-dialog-cancel").ClickAsync();

        // Verify dialog is closed
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("Instructions")]
    public async Task CloseDialog_EscapeKey_ClosesDialog()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-instructions']").ClickAsync();
        await Task.Delay(200);

        var dialog = Page.Locator("#instructions-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#instructions-dialog .modal-body')?.children.length > 0");

        // Press Escape
        await Page.Keyboard.PressAsync("Escape");

        // Verify dialog is closed
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("Instructions")]
    public async Task CloseDialog_SaveButton_ClosesDialog()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-instructions']").ClickAsync();
        await Task.Delay(200);

        var dialog = Page.Locator("#instructions-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#instructions-dialog .modal-body')?.children.length > 0");

        // Click Save
        await dialog.Locator("#instructions-dialog-confirm").ClickAsync();

        // Verify dialog is closed
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("Instructions")]
    public async Task InstructionsDialog_ShowsBuiltInTabs()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-instructions']").ClickAsync();
        await Task.Delay(200);

        var dialog = Page.Locator("#instructions-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#instructions-dialog .modal-body')?.children.length > 0");

        // With no saved file, the draft is built from the built-in defaults.
        await Expect(dialog.Locator(".settings-sidebar button:has-text('Default')")).ToHaveCountAsync(1);
        await Expect(dialog.Locator(".settings-sidebar button:has-text('Plan & Execute')")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("Instructions")]
    public async Task InstructionsDialog_DefaultTabNameIsReadOnly()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-instructions']").ClickAsync();
        await Task.Delay(200);

        var dialog = Page.Locator("#instructions-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#instructions-dialog .modal-body')?.children.length > 0");

        // Built-in tab: no editable name field, but prompt and temperature are present.
        await dialog.Locator(".settings-sidebar button:has-text('Improve')").ClickAsync();
        await Expect(dialog.Locator("input[data-field='displayName']")).ToHaveCountAsync(0);
        await Expect(dialog.Locator("textarea[data-field='prompt']")).ToHaveCountAsync(1);
        await Expect(dialog.Locator("input[data-field='temperature']")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("Instructions")]
    public async Task InstructionsDialog_AddAndDeleteCustomTab()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-instructions']").ClickAsync();
        await Task.Delay(200);

        var dialog = Page.Locator("#instructions-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#instructions-dialog .modal-body')?.children.length > 0");

        // A default tab is active: no delete affordance (only custom instructions are removable).
        await Expect(dialog.Locator(".tab-delete-btn")).ToHaveCountAsync(0);

        // Add a custom instruction: the add button advertises itself as "Add instruction" and the
        // new tab becomes active with an editable name field.
        await Expect(dialog.Locator(".settings-sidebar button:has-text('+ Add')"))
            .ToHaveAttributeAsync("title", "Add instruction");
        await dialog.Locator(".settings-sidebar button:has-text('+ Add')").ClickAsync();
        await Expect(dialog.Locator(".settings-sidebar button:has-text('New Tab')")).ToHaveCountAsync(1);
        await Expect(dialog.Locator("input[data-field='displayName']")).ToHaveCountAsync(1);

        // The remove control lives in the tab header, immediately after the enable checkbox, and is icon-only.
        var removeBtn = dialog.Locator(".group-header-row .header-actions > input[type='checkbox'] + .tab-delete-btn");
        await Expect(removeBtn).ToHaveCountAsync(1);
        await Expect(removeBtn).ToHaveAttributeAsync("title", "Remove instruction");
        await Expect(removeBtn.Locator("svg")).ToHaveCountAsync(1);

        // Remove it: the custom instruction disappears and the name field is gone (first default is active).
        await removeBtn.ClickAsync();
        await Expect(dialog.Locator(".settings-sidebar button:has-text('New Tab')")).ToHaveCountAsync(0);
        await Expect(dialog.Locator("input[data-field='displayName']")).ToHaveCountAsync(0);
        await Expect(dialog.Locator(".tab-delete-btn")).ToHaveCountAsync(0);
    }

    [Test]
    [Category("Instructions")]
    public async Task InstructionsDialog_ShowsSavedCustomTab()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // A saved custom tab that is NOT part of the built-in defaults.
        await Page.EvaluateAsync(
            "() => { window.__instructionsOverride.GetInstructionsAsync = async () => " +
            "JSON.stringify({ selectedTabId: 50, tabs: [ { id: 50, displayName: 'Mine', enabled: true, temperature: 0.2, prompt: 'hi' } ] }); }");

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-instructions']").ClickAsync();
        await Task.Delay(200);

        var dialog = Page.Locator("#instructions-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#instructions-dialog .modal-body')?.children.length > 0");

        // The saved user tab is present and deletable alongside the reconciled defaults.
        await Expect(dialog.Locator(".settings-sidebar button:has-text('Mine')")).ToHaveCountAsync(1);
        await Expect(dialog.Locator(".settings-sidebar button:has-text('Default')")).ToHaveCountAsync(1);
        await Expect(dialog.Locator(".tab-delete-btn")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("Instructions")]
    public async Task InstructionsDialog_SaveFailure_KeepsDialogOpenAndShowsError()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Backend rejects the save.
        await Page.EvaluateAsync(
            "() => { window.__instructionsOverride.UpdateInstructionsAsync = async () => " +
            "JSON.stringify({ success: false, error: 'Validation failed: nope' }); }");

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-instructions']").ClickAsync();
        await Task.Delay(200);

        var dialog = Page.Locator("#instructions-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#instructions-dialog .modal-body')?.children.length > 0");

        await dialog.Locator("#instructions-dialog-confirm").ClickAsync();

        // Dialog stays open and the reason is surfaced to the user.
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 3000 });
        var error = dialog.Locator(".instructions-error");
        await Expect(error).ToBeVisibleAsync(new() { Timeout = 3000 });
        await Expect(error).ToContainTextAsync("nope");
    }

    [Test]
    [Category("Instructions")]
    public async Task InstructionsDialog_TabStateMatrix()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Deterministic tabs: id 1 selected + enabled, id 2 enabled but not selected, id 4 disabled.
        await Page.EvaluateAsync(
            "() => { window.__instructionsOverride.GetInstructionsAsync = async () => " +
            "JSON.stringify({ selectedTabId: 1, tabs: [ " +
            "{ id: 1, displayName: 'Default', enabled: true, temperature: 0.2, prompt: 'p1' }, " +
            "{ id: 2, displayName: 'Improve', enabled: true, temperature: 0.1, prompt: 'p2' }, " +
            "{ id: 4, displayName: 'Review', enabled: false, temperature: 0.1, prompt: 'p4' } ] }); }");

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-instructions']").ClickAsync();
        await Task.Delay(200);

        var dialog = Page.Locator("#instructions-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#instructions-dialog .modal-body')?.children.length > 0");

        var tab1 = dialog.Locator(".settings-sidebar button[data-target='1']");
        var tab2 = dialog.Locator(".settings-sidebar button[data-target='2']");
        var tab4 = dialog.Locator(".settings-sidebar button[data-target='4']");

        // 1) Selected + enabled: highlighted, full opacity.
        await Expect(tab1).ToHaveClassAsync("tab-btn active");
        await Expect(tab1).ToHaveCSSAsync("opacity", "1");

        // 2) Enabled but not selected: no highlight, full opacity.
        await Expect(tab2).ToHaveClassAsync("tab-btn");
        await Expect(tab2).ToHaveCSSAsync("opacity", "1");

        // 3) Disabled and not selected: dimmed.
        await Expect(tab4).ToHaveClassAsync("tab-btn inactive");
        await Expect(tab4).ToHaveCSSAsync("opacity", "0.5");

        // 4) Selecting the disabled tab keeps the accent highlight but stays dimmed.
        await tab4.ClickAsync();
        await Expect(tab4).ToHaveClassAsync("tab-btn active inactive");
        await Expect(tab4).ToHaveCSSAsync("opacity", "0.5");
        await Expect(tab1).ToHaveClassAsync("tab-btn");
        await Expect(tab1).ToHaveCSSAsync("opacity", "1");

        // 5) So does re-selecting the enabled tab: highlight, full opacity.
        await tab1.ClickAsync();
        await Expect(tab1).ToHaveClassAsync("tab-btn active");
        await Expect(tab1).ToHaveCSSAsync("opacity", "1");
        await Expect(tab4).ToHaveClassAsync("tab-btn inactive");

        // 6) Live toggle: disabling the selected tab dims it without losing the highlight.
        await dialog.Locator("input[data-field='enabled']").UncheckAsync();
        await Expect(tab1).ToHaveClassAsync("tab-btn active inactive");
        await Expect(tab1).ToHaveCSSAsync("opacity", "0.5");

        // 7) Re-enabling restores full opacity.
        await dialog.Locator("input[data-field='enabled']").CheckAsync();
        await Expect(tab1).ToHaveClassAsync("tab-btn active");
        await Expect(tab1).ToHaveCSSAsync("opacity", "1");
    }

    [Test]
    [Category("Instructions")]
    public async Task InstructionsDialog_DeleteSelectsNearestTabAbove()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-instructions']").ClickAsync();
        await Task.Delay(200);

        var dialog = Page.Locator("#instructions-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#instructions-dialog .modal-body')?.children.length > 0");

        var sidebar = dialog.Locator(".settings-sidebar");

        // Two custom instructions; they take the next free ids (50, 51) and the newest is active.
        await sidebar.Locator("button:has-text('+ Add')").ClickAsync();
        await sidebar.Locator("button:has-text('+ Add')").ClickAsync();

        var firstCustom = sidebar.Locator("button[data-target='50']");
        var secondCustom = sidebar.Locator("button[data-target='51']");
        await Expect(firstCustom).ToHaveCountAsync(1);
        await Expect(secondCustom).ToHaveCountAsync(1);
        await Expect(secondCustom).ToHaveClassAsync("tab-btn active");

        // Removing the active (last) tab selects the nearest tab above, not the first built-in tab.
        await dialog.Locator(".tab-delete-btn").ClickAsync();
        await Expect(secondCustom).ToHaveCountAsync(0);
        await Expect(firstCustom).ToHaveClassAsync("tab-btn active");
        // The active tab is the custom one above (it exposes a name field), not a built-in tab.
        await Expect(dialog.Locator("input[data-field='displayName']")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("Instructions")]
    public async Task InstructionsDialog_BuiltInTabHasResetButton_UserTabHasNot()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-instructions']").ClickAsync();
        await Task.Delay(200);

        var dialog = Page.Locator("#instructions-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#instructions-dialog .modal-body')?.children.length > 0");

        // No saved file: every built-in tab still carries the shipped text, so the reset control exists
        // (right after the enable checkbox) but is disabled.
        var resetBtn = dialog.Locator(".group-header-row .header-actions > input[type='checkbox'] + .tab-reset-btn");
        await Expect(resetBtn).ToHaveCountAsync(1);
        await Expect(resetBtn).ToHaveAttributeAsync("title", "Reset to default");
        await Expect(resetBtn.Locator("svg")).ToHaveCountAsync(1);
        await Expect(resetBtn).ToBeDisabledAsync();

        // A custom instruction has nothing to be reset to.
        await dialog.Locator(".settings-sidebar button:has-text('+ Add')").ClickAsync();
        await Expect(dialog.Locator(".tab-reset-btn")).ToHaveCountAsync(0);
    }

    [Test]
    [Category("Instructions")]
    public async Task InstructionsDialog_ResetRestoresShippedTextAndKeepsEnabledState()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-instructions']").ClickAsync();
        await Task.Delay(200);

        var dialog = Page.Locator("#instructions-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#instructions-dialog .modal-body')?.children.length > 0");

        var promptBox = dialog.Locator("textarea[data-field='prompt']");
        var enabled = dialog.Locator("input[data-field='enabled']");
        var resetBtn = dialog.Locator(".tab-reset-btn");

        string shippedPrompt = await promptBox.InputValueAsync();
        Assert.That(shippedPrompt, Is.Not.Empty);
        await Expect(resetBtn).ToBeDisabledAsync();

        // An edit enables the reset button.
        await promptBox.FillAsync("my custom text");
        await Expect(resetBtn).ToBeEnabledAsync();

        // 'enabled' is the user's choice, not shipped content: the reset must leave it alone.
        await enabled.UncheckAsync();

        await resetBtn.ClickAsync();

        await Expect(promptBox).ToHaveValueAsync(shippedPrompt);
        await Expect(enabled).Not.ToBeCheckedAsync();
        await Expect(resetBtn).ToBeDisabledAsync();

        
    }

    [Test]
    [Category("Instructions")]
    public async Task InstructionsDialog_EditedTab_IsNotMarkedInSidebar_AndKeepsResetAfterSwitching()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-instructions']").ClickAsync();
        await Task.Delay(200);

        var dialog = Page.Locator("#instructions-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#instructions-dialog .modal-body')?.children.length > 0");

        var sidebar = dialog.Locator(".settings-sidebar");
        string? editedId = await sidebar.Locator(".tab-btn.active").GetAttributeAsync("data-target");
        Assert.That(editedId, Is.Not.Null.And.Not.Empty, "active instructions tab has no data-target");

        await dialog.Locator("textarea[data-field='prompt']").FillAsync("my custom text");

        // A text that differs from the shipped one may be the user's own, better prompt: the sidebar does not flag it.
        await Expect(sidebar.Locator("button[data-modified]")).ToHaveCountAsync(0);

        // Switching away and back keeps the draft, so the reset button of the edited tab is enabled again.
        await sidebar.Locator($".tab-btn[data-target]:not([data-target='{editedId}'])").First.ClickAsync();
        await Expect(dialog.Locator(".tab-reset-btn")).ToBeDisabledAsync();
        await sidebar.Locator($".tab-btn[data-target='{editedId}']").ClickAsync();
        await Expect(dialog.Locator("textarea[data-field='prompt']")).ToHaveValueAsync("my custom text");
        await Expect(dialog.Locator(".tab-reset-btn")).ToBeEnabledAsync();
        await Expect(sidebar.Locator("button[data-modified]")).ToHaveCountAsync(0);
    }

    [Test]
    [Category("Instructions")]
    public async Task InstructionsDialog_ResetThenSave_SendsShippedText()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-instructions']").ClickAsync();
        await Task.Delay(200);

        var dialog = Page.Locator("#instructions-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#instructions-dialog .modal-body')?.children.length > 0");

        var promptBox = dialog.Locator("textarea[data-field='prompt']");
        string shippedPrompt = await promptBox.InputValueAsync();

        await promptBox.FillAsync("my custom text");
        await dialog.Locator(".tab-reset-btn").ClickAsync();
        await Expect(promptBox).ToHaveValueAsync(shippedPrompt);

        await dialog.Locator("#instructions-dialog-confirm").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });

        // The dialog sends the shipped text again; the backend is what drops it from the file. 
        var payload = await Page.EvaluateAsync<string>("() => window.__lastSavedInstructions ?? null");
        Assert.That(payload, Is.Not.Null, "Save must reach UpdateInstructionsAsync");

        var sent = System.Text.Json.JsonDocument.Parse(payload!).RootElement;
        System.Text.Json.JsonElement defaultTab = default;
        foreach (var tab in sent.GetProperty("tabs").EnumerateArray())
        {
            if (tab.GetProperty("id").GetInt32() == 1)
            {
                defaultTab = tab;
                break;
            }
        }

        Assert.That(defaultTab.GetProperty("prompt").GetString(), Is.EqualTo(shippedPrompt));
    }

    [Test]
    [Category("Instructions")]
    public async Task InstructionsDialog_ResetThenCancel_SavesNothing()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-instructions']").ClickAsync();
        await Task.Delay(200);

        var dialog = Page.Locator("#instructions-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#instructions-dialog .modal-body')?.children.length > 0");

        await dialog.Locator("textarea[data-field='prompt']").FillAsync("my custom text");
        await dialog.Locator(".tab-reset-btn").ClickAsync();

        await dialog.Locator("#instructions-dialog-cancel").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });

        var payload = await Page.EvaluateAsync<string>("() => window.__lastSavedInstructions ?? null");
        Assert.That(payload, Is.Null, "Cancel must not persist the reset");
    }
}
