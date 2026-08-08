namespace LMLocal.Tests.E2E.AppTests;

using System.Text.RegularExpressions;

[TestFixture]
public partial class InputTests : AppTestBase
{
    [GeneratedRegex(@"\bactive\b")]
    private static partial Regex ActiveClassRegex();
    private static Regex ActiveClass => ActiveClassRegex();

    [GeneratedRegex(@"\bexpanded\b")]
    private static partial Regex ExpandedClassRegex();
    private static Regex ExpandedClass => ExpandedClassRegex();

    [GeneratedRegex(@"\bbtn-stop\b")]
    private static partial Regex BtnStopClassRegex();
    private static Regex BtnStopClass => BtnStopClassRegex();

    [Test]
    [Category("Input")]
    public async Task Input_ContextToggle_ActivatesOnClick()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var contextBtn = Page.Locator("#contextToggleBtn");

        // Should not be active initially
        await Expect(contextBtn).Not.ToHaveClassAsync(ActiveClass);

        // Click to activate
        await contextBtn.ClickAsync();
        await Expect(contextBtn).ToHaveClassAsync(ActiveClass);
    }

    [Test]
    [Category("Input")]
    public async Task Input_ContextToggle_DeactivatesOnSecondClick()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var contextBtn = Page.Locator("#contextToggleBtn");

        // Activate
        await contextBtn.ClickAsync();
        await Expect(contextBtn).ToHaveClassAsync(ActiveClass);

        // Deactivate
        await contextBtn.ClickAsync();
        await Expect(contextBtn).Not.ToHaveClassAsync(ActiveClass);
    }

    [Test]
    [Category("Input")]
    public async Task Input_Dropdown_HiddenWhenNoInstructions()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // With no instructions (mock returns empty tabs), the dropdown is hidden
        var dropdown = Page.Locator("#actionDropdown");
        var display = await dropdown.EvaluateAsync<string>("el => el.style.display");
        Assert.That(display, Is.EqualTo("none"));
    }

    [Test]
    [Category("Input")]
    public async Task Input_Dropdown_OpensAndCloses()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        // Inject instructions so the dropdown becomes visible
        await Page.EvaluateAsync("() => { " +
            "import('/js/components/input.component.js').then(m => { " +
            "  m.inputComponent.updateInstructionsState(" +
            "    { instructions: [{ id: 'tab1', displayName: 'Tab 1', enabled: true }], selectedTabId: 'tab1' }," +
            "    { instructions: [], selectedTabId: null }" +
            "  ); " +
            "}); }");
        await Task.Delay(200);

        var dropdown = Page.Locator("#actionDropdown");

        // Dropdown should no longer be display:none
        var display = await dropdown.EvaluateAsync<string>("el => el.style.display");
        Assert.That(display, Is.EqualTo("block"));

        // Closed by default (no 'active' class)
        await Expect(dropdown).Not.ToHaveClassAsync(ActiveClass);

        // Open via class toggle
        await dropdown.EvaluateAsync("el => el.classList.add('active')");
        await Expect(dropdown).ToHaveClassAsync(ActiveClass);

        // Close via class toggle
        await dropdown.EvaluateAsync("el => el.classList.remove('active')");
        await Expect(dropdown).Not.ToHaveClassAsync(ActiveClass);
    }

    [Test]
    [Category("Input")]
    public async Task Input_TextareaAutoResizesOnInput()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var userInput = Page.Locator("#userInput");
        var inputWrapper = Page.Locator(".input-wrapper");

        // Type long text to trigger auto-resize
        await userInput.FillAsync("Line 1\nLine 2\nLine 3\nLine 4\nLine 5");

        // The input wrapper should have expanded class
        await Expect(inputWrapper).ToHaveClassAsync(ExpandedClass);
    }

    [Test]
    [Category("Input")]
    public async Task Input_ClearInput_ResetsTextareaAndContextToggle()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var userInput = Page.Locator("#userInput");
        var contextBtn = Page.Locator("#contextToggleBtn");
        var inputWrapper = Page.Locator(".input-wrapper");

        // Set up state: text + context active
        await userInput.FillAsync("Hello world");
        await contextBtn.ClickAsync();
        await Expect(contextBtn).ToHaveClassAsync(ActiveClass);

        // Clear input via JS (simulating what happens after send)
        await Page.EvaluateAsync("() => { " +
            "import('/js/components/input.component.js').then(m => { " +
            "  m.inputComponent.clearInput(); " +
            "}); }");
        await Task.Delay(200);

        // Input should be empty
        await Expect(userInput).ToHaveValueAsync("");
        // Context toggle should be reset
        await Expect(contextBtn).Not.ToHaveClassAsync(ActiveClass);
        // Wrapper should not be expanded
        await Expect(inputWrapper).Not.ToHaveClassAsync(ExpandedClass);
    }

    [Test]
    [Category("Input")]
    public async Task Input_MainButtonIsSendByDefault()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var mainBtn = Page.Locator("#mainBtn");
        await Expect(mainBtn).ToHaveTextAsync("Send");
        await Expect(mainBtn).Not.ToHaveClassAsync(BtnStopClass);
    }

    [Test]
    [Category("Input")]
    public async Task Input_UserInputIsEnabledByDefault()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var userInput = Page.Locator("#userInput");
        await Expect(userInput).ToBeEnabledAsync();
    }

    [Test]
    [Category("Input")]
    public async Task Input_ShiftEnterDoesNotClear()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var userInput = Page.Locator("#userInput");
        await userInput.FillAsync("Hello");
        await userInput.PressAsync("Shift+Enter");

        // Content should still be there (Shift+Enter doesn't send)
        await Expect(userInput).ToHaveValueAsync("Hello\n");
    }

    [Test]
    [Category("Input")]
    public async Task Input_AttachFile_AppendsMarkdownToTextarea()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var userInput = Page.Locator("#userInput");
        var attachFileInput = Page.Locator("#attachFileInput");

        // A small text file to attach
        var filePath = Path.Combine(Path.GetTempPath(), "lm-local-e2e-sample.cs");
        await File.WriteAllTextAsync(filePath, "public class Sample { }\n");

        try
        {
            // Move focus away from the textarea first, to simulate the focus loss
            // caused by the native OS file dialog when clicking the browse button.
            await Page.Locator("#mainBtn").FocusAsync();
            await Expect(userInput).Not.ToBeFocusedAsync();

            // Simulate picking a file via the hidden input (same as clicking the browse button)
            await attachFileInput.SetInputFilesAsync(filePath);

            // The textarea should now contain the file wrapped in a markdown code fence
            var value = await userInput.InputValueAsync();
            Assert.That(value, Does.Contain("```csharp"));
            Assert.That(value, Does.Contain("// lm-local-e2e-sample.cs"));
            Assert.That(value, Does.Contain("public class Sample { }"));

            // Focus must be restored to the textarea after inserting the file content
            await Expect(userInput).ToBeFocusedAsync();

            // Caret should be placed at the end, ready for further typing
            var caretAtEnd = await userInput.EvaluateAsync<int>(
                "(el) => el.selectionStart === el.selectionEnd && el.selectionEnd === el.value.length ? 1 : 0");
            Assert.That(caretAtEnd, Is.EqualTo(1));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Test]
    [Category("Input")]
    public async Task Input_AttachFile_DisabledWhenBusy()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var openFileBtn = Page.Locator("#openFileBtn");
        var attachFileInput = Page.Locator("#attachFileInput");

        // Enabled by default (app is idle)
        await Expect(openFileBtn).ToBeEnabledAsync();
        await Expect(attachFileInput).ToBeEnabledAsync();

        // Simulate busy state via store so controls get disabled
        await Page.EvaluateAsync("() => { " +
            "Promise.all([ " +
            "  import('/js/store/app.store.js'), " +
            "  import('/js/store/app.status.js') " +
            "]).then(([storeMod, statusMod]) => { " +
            "  storeMod.default.setState({ status: statusMod.AppStatus.STREAMING }); " +
            "}); }");
        await Task.Delay(200);

        await Expect(openFileBtn).ToBeDisabledAsync();
        await Expect(attachFileInput).ToBeDisabledAsync();
    }
}
