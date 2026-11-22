namespace LMLocal.Tests.E2E.AppTests;

using System.Text.RegularExpressions;

/// <summary>
/// Covers the Usage &amp; Stats dialog: entry points, the Current view (totals, BY SOURCE, BY MODEL), the period views with
/// sessions grouped by day, the session filter and the empty states. The host is mocked by webview-mock-usage-stats.js,
/// which returns the same JSON string as UsageController.
/// </summary>
[TestFixture]
public partial class UsageStatsDialogTests : AppTestBase
{
    private const string Mock = "webview-mock-usage-stats.js";

    private static readonly string[] CurrentCalls = ["current"];
    private static readonly string[] CurrentThen7dCalls = ["current", "7d"];
    private static readonly string[] CurrentTwiceCalls = ["current", "current"];

    [GeneratedRegex(@"\bshow\b")]
    private static partial Regex ShowClassRegex();
    private static Regex ShowClass => ShowClassRegex();

    [GeneratedRegex(@"\bactive\b")]
    private static partial Regex ActiveClassRegex();
    private static Regex ActiveClass => ActiveClassRegex();

    [GeneratedRegex(@"\busage-hidden\b")]
    private static partial Regex HiddenClassRegex();
    private static Regex HiddenClass => HiddenClassRegex();

    private ILocator Dialog => Page.Locator("#usage-stats-dialog");
    private ILocator Container => Page.Locator("#usage-stats-container");

    private async Task OpenPageAsync()
    {
        await GotoWithMockAsync(Mock);
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });
    }

    private async Task OpenFromMenuAsync()
    {
        await Page.Locator("#menu-btn").ClickAsync();
        await Expect(Page.Locator("#dropdown-menu")).ToHaveClassAsync(ShowClass);
        await Page.Locator("button[data-action=\"open-usage-stats\"]").ClickAsync();
        await Expect(Dialog).ToBeVisibleAsync(new() { Timeout = 3000 });
    }

    private async Task SelectPeriodAsync(string period)
    {
        await Dialog.Locator($".usage-period-btn[data-period=\"{period}\"]").ClickAsync();
        await Expect(Dialog.Locator($".usage-period-btn[data-period=\"{period}\"]")).ToHaveClassAsync(ActiveClass);
    }

    private ILocator Row(string label) =>
        Container.Locator(".usage-row").Filter(new() { Has = Page.Locator(".usage-row-label", new() { HasTextString = label }) });

    [Test]
    [Category("UsageStats")]
    public async Task UsageStats_DialogOpens_FromMenu()
    {
        await OpenPageAsync();
        await OpenFromMenuAsync();

        await Expect(Page.Locator("#dropdown-menu")).Not.ToHaveClassAsync(ShowClass);
        await Expect(Dialog.Locator(".usage-period-btn[data-period=\"current\"]")).ToHaveClassAsync(ActiveClass);
    }

    [Test]
    [Category("UsageStats")]
    public async Task UsageStats_DialogOpens_FromContextBarClick()
    {
        await OpenPageAsync();

        await Page.Locator("#info-tooltip").ClickAsync();

        await Expect(Dialog).ToBeVisibleAsync(new() { Timeout = 3000 });
        await Expect(Container.Locator(".usage-summary")).ToBeVisibleAsync(new() { Timeout = 3000 });
    }

    /// <summary>
    /// The host answer is wrapped by Callback.emitResult; the dialog must render the stats, not an empty state.
    /// </summary>
    [Test]
    [Category("UsageStats")]
    public async Task UsageStats_Current_ShowsTotals()
    {
        await OpenPageAsync();
        await OpenFromMenuAsync();

        // Activity only: no token sum, it would add local tokens to cloud ones. Current is one session: no session count.
        await Expect(Container.Locator(".usage-summary-meta"))
            .ToHaveTextAsync("3 prompts · 12 subagents · 2 failed", new() { Timeout = 3000 });
        await Expect(Container.Locator(".usage-summary-meta .usage-failed")).ToHaveTextAsync("2 failed");
        await Expect(Container.Locator(".usage-summary-peak")).ToHaveTextAsync("peak ctx 65%");
        await Expect(Container.Locator(".empty-placeholder")).ToHaveCountAsync(0);

        var calls = await Page.EvaluateAsync<string[]>("() => window.__usageCalls");
        Assert.That(calls, Is.EqualTo(CurrentCalls), "Opening the dialog asks the host for the current session.");
    }

    [Test]
    [Category("UsageStats")]
    public async Task UsageStats_Current_ShowsBySourceRows()
    {
        await OpenPageAsync();
        await OpenFromMenuAsync();

        var title = Container.Locator(".usage-block-title", new() { HasTextString = "BY SOURCE" });
        await Expect(title).ToBeVisibleAsync(new() { Timeout = 3000 });

        await Expect(Row("Main").Locator(".usage-in")).ToHaveTextAsync("122k");
        await Expect(Row("Main").Locator(".usage-out")).ToHaveTextAsync("9k");
        // Send-to-answer time of the prompts, shown here and not repeated in the summary.
        await Expect(Row("Main").Locator(".usage-extra")).ToHaveTextAsync("18m");
        await Expect(Row("Subagents").Locator(".usage-badge")).ToHaveTextAsync("×12");
        await Expect(Row("Subagents").Locator(".usage-in")).ToHaveTextAsync("71k");
        await Expect(Row("Subagents").Locator(".usage-out")).ToHaveTextAsync("4k");
        // Wall time of the subagent runs, a part of the Main time.
        await Expect(Row("Subagents").Locator(".usage-extra")).ToHaveTextAsync("10m");
        // No time recorded: an em dash.
        await Expect(Row("Compaction").Locator(".usage-extra")).ToHaveTextAsync("—");

        // The failure caption sits under the Subagents row only.
        var captions = Container.Locator(".usage-row-caption.usage-failed");
        await Expect(captions).ToHaveCountAsync(1);
        await Expect(captions).ToHaveTextAsync("2 failed");
    }

    [Test]
    [Category("UsageStats")]
    public async Task UsageStats_Current_ShowsByModelWithSpeedAndProvider()
    {
        await OpenPageAsync();
        await OpenFromMenuAsync();

        var title = Container.Locator(".usage-block-title", new() { HasTextString = "BY MODEL" });
        await Expect(title).ToBeVisibleAsync(new() { Timeout = 3000 });

        await Expect(Row("qwen3-coder-30b").Locator(".usage-extra")).ToHaveTextAsync("42 t/s");
        // No measured speed: an em dash instead of 0 t/s.
        await Expect(Row("deepseek-chat").Locator(".usage-extra")).ToHaveTextAsync("—");

        // Two providers in the selection: every model carries its provider badge.
        await Expect(Row("qwen3-coder-30b").Locator(".usage-badge")).ToHaveTextAsync("LM Studio");
        await Expect(Row("deepseek-chat").Locator(".usage-badge")).ToHaveTextAsync("DeepSeek");

        // The cache line appears only for a provider that reports it; LM Studio gets none rather than 0%.
        var cacheLines = Container.Locator(".usage-row-caption.usage-cache");
        await Expect(cacheLines).ToHaveCountAsync(1);
        await Expect(cacheLines).ToHaveTextAsync("39k cached · 80%");
    }

    [Test]
    [Category("UsageStats")]
    public async Task UsageStats_Current_HasNoSessionList()
    {
        await OpenPageAsync();
        await OpenFromMenuAsync();

        await Expect(Container.Locator(".usage-summary")).ToBeVisibleAsync(new() { Timeout = 3000 });
        await Expect(Container.Locator(".usage-sessions")).ToHaveCountAsync(0);
    }

    [Test]
    [Category("UsageStats")]
    public async Task UsageStats_Period_AsksHostAndShowsSessionsByDay()
    {
        await OpenPageAsync();
        await OpenFromMenuAsync();
        await Expect(Container.Locator(".usage-summary")).ToBeVisibleAsync(new() { Timeout = 3000 });

        await SelectPeriodAsync("7d");

        await Expect(Container.Locator(".usage-sessions-title")).ToContainTextAsync("SESSIONS (3)", new() { Timeout = 3000 });
        await Expect(Container.Locator(".usage-summary-meta")).ToHaveTextAsync("4 prompts · 3 sessions");
        // Peak context mixes sessions and context windows over a period: shown on Current only.
        await Expect(Container.Locator(".usage-summary-peak")).ToHaveCountAsync(0);

        // The newest day starts expanded, older days start collapsed with their session count.
        await Expect(Container.Locator(".usage-day-label").First).ToHaveTextAsync("TODAY");
        await Expect(Container.Locator(".usage-day-label").Nth(1)).ToHaveTextAsync("YESTERDAY (1)");
        await Expect(Container.Locator(".usage-session-card")).ToHaveCountAsync(2);

        var calls = await Page.EvaluateAsync<string[]>("() => window.__usageCalls");
        Assert.That(calls, Is.EqualTo(CurrentThen7dCalls));
    }

    [Test]
    [Category("UsageStats")]
    public async Task UsageStats_Period_WithoutSubagents_ShowsNoSubagentRow()
    {
        await OpenPageAsync();
        await OpenFromMenuAsync();
        await SelectPeriodAsync("today");

        await Expect(Row("Main")).ToHaveCountAsync(1, new() { Timeout = 3000 });
        await Expect(Container.Locator(".usage-row-label", new() { HasTextString = "Subagents" })).ToHaveCountAsync(0);
        await Expect(Container.Locator(".usage-row-label", new() { HasTextString = "Compaction" })).ToHaveCountAsync(0);

        // One provider in the selection: no provider badge next to the model.
        await Expect(Row("qwen3-coder-30b").Locator(".usage-badge")).ToHaveCountAsync(0);

        // Today is a single day: its sessions come without a day header.
        await Expect(Container.Locator(".usage-day-header")).ToHaveCountAsync(0);
    }

    [Test]
    [Category("UsageStats")]
    public async Task UsageStats_SessionWithoutTitle_ShowsUntitled()
    {
        await OpenPageAsync();
        await OpenFromMenuAsync();
        await SelectPeriodAsync("today");

        var card = Container.Locator(".usage-session-card[data-session=\"usage-session-2\"]");
        await Expect(card.Locator(".usage-session-title")).ToHaveTextAsync("Untitled", new() { Timeout = 3000 });
    }

    [Test]
    [Category("UsageStats")]
    public async Task UsageStats_SessionCard_ShowsFailedBadgeAndExpands()
    {
        await OpenPageAsync();
        await OpenFromMenuAsync();
        await SelectPeriodAsync("today");

        var card = Container.Locator(".usage-session-card[data-session=\"usage-session-1\"]");
        await Expect(card.Locator(".usage-session-meta .usage-failed")).ToHaveTextAsync("⚠ 1 failed", new() { Timeout = 3000 });
        await Expect(card.Locator(".usage-session-detail")).ToHaveCountAsync(0);

        await card.Locator(".usage-session-title").ClickAsync();

        var expanded = Container.Locator(".usage-session-card[data-session=\"usage-session-1\"] .usage-session-detail");
        await Expect(expanded).ToBeVisibleAsync(new() { Timeout = 3000 });
        await Expect(expanded.Locator(".usage-block-title", new() { HasTextString = "BY SOURCE" })).ToHaveCountAsync(1);
        await Expect(expanded.Locator(".usage-block-title", new() { HasTextString = "BY MODEL" })).ToHaveCountAsync(1);
    }

    [Test]
    [Category("UsageStats")]
    public async Task UsageStats_CollapsedDay_ExpandsOnClick()
    {
        await OpenPageAsync();
        await OpenFromMenuAsync();
        await SelectPeriodAsync("7d");

        var yesterday = Container.Locator(".usage-day-header").Nth(1);
        await Expect(yesterday.Locator(".usage-day-label")).ToHaveTextAsync("YESTERDAY (1)", new() { Timeout = 3000 });

        await yesterday.ClickAsync();

        await Expect(Container.Locator(".usage-session-card[data-session=\"usage-session-3\"] .usage-session-title"))
            .ToHaveTextAsync("Fix the button color", new() { Timeout = 3000 });
        await Expect(Container.Locator(".usage-session-card")).ToHaveCountAsync(3);
    }

    [Test]
    [Category("UsageStats")]
    public async Task UsageStats_Filter_HidesNonMatchingSessions()
    {
        await OpenPageAsync();
        await OpenFromMenuAsync();
        await SelectPeriodAsync("today");

        await Expect(Container.Locator(".usage-session-card")).ToHaveCountAsync(2, new() { Timeout = 3000 });

        await Container.Locator("#usage-session-filter").FillAsync("stream");

        await Expect(Container.Locator(".usage-session-card[data-session=\"usage-session-1\"]")).Not.ToHaveClassAsync(HiddenClass);
        await Expect(Container.Locator(".usage-session-card[data-session=\"usage-session-2\"]")).ToHaveClassAsync(HiddenClass);

        // The model of a card matches too.
        await Container.Locator("#usage-session-filter").FillAsync("gemma");

        await Expect(Container.Locator(".usage-session-card[data-session=\"usage-session-1\"]")).ToHaveClassAsync(HiddenClass);
        await Expect(Container.Locator(".usage-session-card[data-session=\"usage-session-2\"]")).Not.ToHaveClassAsync(HiddenClass);
    }

    [Test]
    [Category("UsageStats")]
    public async Task UsageStats_NoData_ShowsEmptyState()
    {
        await OpenPageAsync();
        await Page.EvaluateAsync("() => { window.__usageMode = 'empty'; }");
        await OpenFromMenuAsync();

        await Expect(Container.Locator(".empty-placeholder"))
            .ToHaveTextAsync("No usage recorded for this period.", new() { Timeout = 3000 });
    }

    [Test]
    [Category("UsageStats")]
    public async Task UsageStats_LoggingDisabled_ShowsSettingsHint()
    {
        await OpenPageAsync();
        await Page.EvaluateAsync("() => { window.__usageMode = 'loggingOff'; }");
        await OpenFromMenuAsync();

        await Expect(Container.Locator(".empty-placeholder"))
            .ToContainTextAsync("Save chat history & stats", new() { Timeout = 3000 });
    }

    [Test]
    [Category("UsageStats")]
    public async Task UsageStats_Refresh_AsksHostAgain()
    {
        await OpenPageAsync();
        await OpenFromMenuAsync();
        await Expect(Container.Locator(".usage-summary")).ToBeVisibleAsync(new() { Timeout = 3000 });

        await Page.Locator("#usage-refresh-btn").ClickAsync();

        await Page.WaitForFunctionAsync("() => window.__usageCalls.length === 2", null, new() { Timeout = 3000 });

        var calls = await Page.EvaluateAsync<string[]>("() => window.__usageCalls");
        Assert.That(calls, Is.EqualTo(CurrentTwiceCalls), "Refresh reloads the same selection.");
    }

    [Test]
    [Category("UsageStats")]
    public async Task UsageStats_Close_ClosesDialog()
    {
        await OpenPageAsync();
        await OpenFromMenuAsync();

        await Page.Locator("#usage-stats-close").ClickAsync();

        await Expect(Dialog).Not.ToBeVisibleAsync(new() { Timeout = 3000 });
    }

    [Test]
    [Category("UsageStats")]
    public async Task UsageStats_Reopen_StartsFromCurrent()
    {
        await OpenPageAsync();
        await OpenFromMenuAsync();
        await SelectPeriodAsync("7d");
        await Expect(Container.Locator(".usage-sessions")).ToHaveCountAsync(1, new() { Timeout = 3000 });

        await Page.Locator("#usage-stats-close").ClickAsync();
        await Expect(Dialog).Not.ToBeVisibleAsync(new() { Timeout = 3000 });

        await OpenFromMenuAsync();

        await Expect(Dialog.Locator(".usage-period-btn[data-period=\"current\"]")).ToHaveClassAsync(ActiveClass);
        await Expect(Container.Locator(".usage-sessions")).ToHaveCountAsync(0, new() { Timeout = 3000 });
    }

    /// <summary>
    /// Yesterday is a single local day, so its sessions come without a day header, exactly like Today.
    /// </summary>
    [Test]
    [Category("UsageStats")]
    public async Task UsageStats_Yesterday_IsASingleDayWithoutDayHeader()
    {
        await OpenPageAsync();
        await OpenFromMenuAsync();
        await SelectPeriodAsync("yesterday");

        await Expect(Container.Locator(".usage-summary")).ToBeVisibleAsync(new() { Timeout = 3000 });
        // Both sessions of the day are listed, not only the newest one; only then is the missing header meaningful.
        await Expect(Container.Locator(".usage-session-card")).ToHaveCountAsync(2);
        await Expect(Container.Locator(".usage-day-header")).ToHaveCountAsync(0);

        var calls = await Page.EvaluateAsync<string[]>("() => window.__usageCalls");
        Assert.That(calls, Is.EqualTo(["current", "yesterday"]));
    }

    /// <summary>
    /// 3d is today plus the two days before it: three day groups, only the newest one expanded.
    /// </summary>
    [Test]
    [Category("UsageStats")]
    public async Task UsageStats_ThreeDays_GroupsSessionsByDay()
    {
        await OpenPageAsync();
        await OpenFromMenuAsync();
        await SelectPeriodAsync("3d");

        await Expect(Container.Locator(".usage-sessions-title")).ToContainTextAsync("SESSIONS (4)", new() { Timeout = 3000 });
        await Expect(Container.Locator(".usage-day-header")).ToHaveCountAsync(3);
        await Expect(Container.Locator(".usage-day-label").First).ToHaveTextAsync("TODAY");
        await Expect(Container.Locator(".usage-day-label").Nth(1)).ToHaveTextAsync("YESTERDAY (1)");
        // The collapsed days render no cards until they are opened.
        await Expect(Container.Locator(".usage-session-card")).ToHaveCountAsync(2);
    }

    /// <summary>
    /// Six periods in one row of the narrow panel: the toolbar must neither wrap nor overflow.
    /// </summary>
    [Test]
    [Category("UsageStats")]
    public async Task UsageStats_PeriodButtons_StayOnOneRow()
    {
        await OpenPageAsync();
        await OpenFromMenuAsync();
        await Expect(Container.Locator(".usage-summary")).ToBeVisibleAsync(new() { Timeout = 3000 });

        var tops = await Page.EvaluateAsync<double[]>(
            "() => Array.from(document.querySelectorAll('#usage-period-nav .usage-period-btn')).map(b => Math.round(b.getBoundingClientRect().top))");

        Assert.That(tops, Has.Length.EqualTo(6), "Current | Today | Yday | 3d | 7d | 30d.");
        Assert.That(tops.Distinct().Count(), Is.EqualTo(1), "The period buttons must not wrap.");

        var widths = await Page.EvaluateAsync<double[]>(
            "() => { const nav = document.getElementById('usage-period-nav'); return [nav.scrollWidth, nav.clientWidth]; }");

        Assert.That(widths[0], Is.LessThanOrEqualTo(widths[1] + 1), "The row must fit the toolbar of the panel.");
    }
}
