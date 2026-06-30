namespace LMLocal.Tests.E2E.AppTests;
using System.Text.RegularExpressions;
[TestFixture]
public partial class ChangesPanelTests : AppTestBase
{
    [GeneratedRegex(@"\bhidden\b")]
    private static partial Regex HiddenClassRegex();
    private static Regex HiddenClass => HiddenClassRegex();

    [GeneratedRegex(@"\bexpanded\b")]
    private static partial Regex ExpandedClassRegex();
    private static Regex ExpandedClass => ExpandedClassRegex();

    [GeneratedRegex(@"\bview-list\b")]
    private static partial Regex ViewListClassRegex();
    private static Regex ViewListClass => ViewListClassRegex();

    [GeneratedRegex(@"\bview-tree\b")]
    private static partial Regex ViewTreeClassRegex();
    private static Regex ViewTreeClass => ViewTreeClassRegex();

    private const string InjectPanelFiles = @"
        () => {
            const panel = document.getElementById('global-changes-panel');
            if (!panel) return false;
            panel.classList.remove('hidden');
            const comp = window.__changesPanel;
            if (!comp) return false;
            comp._cachedFiles = [
                { relativePath: 'src/Program.cs', status: 'modified' },
                { relativePath: 'src/Models/Customer.cs', status: 'modified' },
                { relativePath: 'src/Services/AuthService.cs', status: 'created' },
                { relativePath: 'tests/old_test.cs', status: 'deleted' }
            ];
            comp._isExpanded = true;
            panel.classList.add('expanded');
            comp._renderFiles();
            comp._updateUiState();
            return true;
        }
    ";

    [Test]
    [Category("ChangesPanel")]
    public async Task Panel_IsHiddenByDefault()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var panel = Page.Locator("#global-changes-panel");
        await Expect(panel).ToHaveClassAsync(HiddenClass);
    }

    [Test]
    [Category("ChangesPanel")]
    public async Task Panel_BecomesVisibleWithFiles()
    {
        await GotoWithMockAsync("webview-mock.js");

        await Page.EvaluateAsync("() => { import('/js/components/changes.panel.component.js').then(m => { window.__changesPanel = m.changesPanelComponent; window.__changesPanel.setup(); }); }");
        await Task.Delay(300);

        await Page.EvaluateAsync(InjectPanelFiles);

        var panel = Page.Locator("#global-changes-panel");
        await Expect(panel).Not.ToHaveClassAsync(HiddenClass);
    }

    [Test]
    [Category("ChangesPanel")]
    public async Task Panel_ShowsHeaderAndToolbar()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Page.EvaluateAsync("() => { import('/js/components/changes.panel.component.js').then(m => { window.__changesPanel = m.changesPanelComponent; window.__changesPanel.setup(); }); }");
        await Task.Delay(300);
        await Page.EvaluateAsync(InjectPanelFiles);

        var panel = Page.Locator("#global-changes-panel");

        // Header
        await Expect(panel.Locator(".changes-title")).ToHaveTextAsync("Changes");
        await Expect(panel.Locator("#global-changes-count")).ToHaveTextAsync("(4 files)");

        // Toolbar buttons
        await Expect(panel.Locator("#review-all-btn")).ToHaveCountAsync(1);
        await Expect(panel.Locator("#open-all-btn")).ToHaveCountAsync(1);
        await Expect(panel.Locator("#discard-all-btn")).ToHaveCountAsync(1);
        await Expect(panel.Locator("#accept-all-btn")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("ChangesPanel")]
    public async Task Panel_RendersFileListInListView()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Page.EvaluateAsync("() => { import('/js/components/changes.panel.component.js').then(m => { window.__changesPanel = m.changesPanelComponent; window.__changesPanel.setup(); }); }");
        await Task.Delay(300);
        await Page.EvaluateAsync(InjectPanelFiles);

        var panel = Page.Locator("#global-changes-panel");
        var filesList = panel.Locator("#global-files-list");

        // Should be in list view by default
        await Expect(filesList).ToHaveClassAsync(ViewListClass);

        // Should have 4 file items
        var fileItems = filesList.Locator(".file-item");
        await Expect(fileItems).ToHaveCountAsync(4);

        // Verify first file
        await Expect(fileItems.Nth(0).Locator(".file-name")).ToHaveTextAsync("Program.cs");
        await Expect(fileItems.Nth(0).Locator(".file-path-dir")).ToHaveTextAsync("src");
        await Expect(fileItems.Nth(0).Locator(".file-status")).ToHaveTextAsync("Modified");

        // Verify created file
        await Expect(fileItems.Nth(2).Locator(".file-status")).ToHaveTextAsync("New");

        // Verify deleted file
        await Expect(fileItems.Nth(3).Locator(".file-status")).ToHaveTextAsync("Deleted");
    }

    [Test]
    [Category("ChangesPanel")]
    public async Task Panel_CollapseToggle_HidesBody()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Page.EvaluateAsync("() => { import('/js/components/changes.panel.component.js').then(m => { window.__changesPanel = m.changesPanelComponent; window.__changesPanel.setup(); }); }");
        await Task.Delay(300);
        await Page.EvaluateAsync(InjectPanelFiles);

        var panel = Page.Locator("#global-changes-panel");

        // Expanded by our inject
        await Expect(panel).ToHaveClassAsync(ExpandedClass);

        // Click header to collapse
        await panel.Locator("#changes-header-trigger").ClickAsync();

        // Should be collapsed
        await Expect(panel).Not.ToHaveClassAsync(ExpandedClass);
    }

    [Test]
    [Category("ChangesPanel")]
    public async Task Panel_CollapseToggle_ReExpandBody()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Page.EvaluateAsync("() => { import('/js/components/changes.panel.component.js').then(m => { window.__changesPanel = m.changesPanelComponent; window.__changesPanel.setup(); }); }");
        await Task.Delay(300);
        // Start expanded
        await Page.EvaluateAsync(InjectPanelFiles);

        var panel = Page.Locator("#global-changes-panel");

        // Collapse
        await panel.Locator("#changes-header-trigger").ClickAsync();
        await Expect(panel).Not.ToHaveClassAsync(ExpandedClass);

        // Re-expand
        await panel.Locator("#changes-header-trigger").ClickAsync();
        await Expect(panel).ToHaveClassAsync(ExpandedClass);
    }

    [Test]
    [Category("ChangesPanel")]
    public async Task Panel_ToggleViewMode_SwitchesToTree()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Page.EvaluateAsync("() => { import('/js/components/changes.panel.component.js').then(m => { window.__changesPanel = m.changesPanelComponent; window.__changesPanel.setup(); }); }");
        await Task.Delay(300);
        await Page.EvaluateAsync(InjectPanelFiles);

        var panel = Page.Locator("#global-changes-panel");
        var filesList = panel.Locator("#global-files-list");

        // Default is list
        await Expect(filesList).ToHaveClassAsync(ViewListClass);

        // Click toggle view mode
        await panel.Locator("#toggle-view-mode-btn").ClickAsync();

        // Should switch to tree
        await Expect(filesList).ToHaveClassAsync(ViewTreeClass);

        // Tree should have at least one folder row
        await Expect(filesList.Locator(".tree-folder-row")).ToHaveCountAsync(4);

        // Click again to switch back
        await panel.Locator("#toggle-view-mode-btn").ClickAsync();
        await Expect(filesList).ToHaveClassAsync(ViewListClass);
    }

    [Test]
    [Category("ChangesPanel")]
    public async Task Panel_DiscardAllButton_HidesPanel()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Page.EvaluateAsync("() => { import('/js/components/changes.panel.component.js').then(m => { window.__changesPanel = m.changesPanelComponent; window.__changesPanel.setup(); const comp = window.__changesPanel; comp.onDiscardAll.on(async () => { comp._cachedFiles = []; comp.updateChangesState({ visible: false, changedFiles: [] }, {}); return true; }); comp.setup(); }); }");
        await Task.Delay(300);
        await Page.EvaluateAsync(InjectPanelFiles);

        var panel = Page.Locator("#global-changes-panel");
        await Expect(panel).Not.ToHaveClassAsync(HiddenClass);

        // Click Discard all
        await panel.Locator("#discard-all-btn").ClickAsync();

        // Panel should hide after discarding
        await Expect(panel).ToHaveClassAsync(HiddenClass);
    }
}
