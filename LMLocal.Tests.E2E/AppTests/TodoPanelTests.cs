namespace LMLocal.Tests.E2E.AppTests;

/// <summary>
/// The live task widget fed by todo_write StreamToolEnd messages (UiData).
/// </summary>
[TestFixture]
public partial class TodoPanelTests : AppTestBase
{
    private const string FirstLine = "Tasks 1/3 \u2014 Fix the parser";

    [GeneratedRegex(@"\bdone\b")]
    private static partial Regex DoneClass();

    [GeneratedRegex(@"\bcurrent\b")]
    private static partial Regex CurrentClass();

    [GeneratedRegex(@"\bpending\b")]
    private static partial Regex PendingClass();

    private async Task StartRequestAsync(string mockFileName, string? scenario = null)
    {
        if (scenario != null)
        {
            await Page.AddInitScriptAsync($"window.__todoScenario = '{scenario}';");
        }

        await GotoWithMockAsync(mockFileName);
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Fix the build");
        await Page.Locator("#mainBtn").ClickAsync();
    }

    [Test]
    [Category("Tools")]
    public async Task TodoWidget_AppearsCollapsedWithProgressLine()
    {
        await StartRequestAsync("webview-mock-todo.js");

        await Expect(Page.Locator("#global-todo-panel"))
            .ToBeVisibleAsync(new() { Timeout = 4000 });

        await Expect(Page.Locator("#todo-summary"))
            .ToHaveTextAsync(FirstLine, new() { Timeout = 4000 });

        await Expect(Page.Locator("#todo-body")).ToBeHiddenAsync();
    }

    [Test]
    [Category("Tools")]
    public async Task TodoWidget_ExpandsToTheStepList()
    {
        await StartRequestAsync("webview-mock-todo.js");

        await Expect(Page.Locator("#global-todo-panel"))
            .ToBeVisibleAsync(new() { Timeout = 4000 });

        await Page.Locator("#todo-header-trigger").ClickAsync();

        await Expect(Page.Locator("#todo-body")).ToBeVisibleAsync(new() { Timeout = 2000 });

        var items = Page.Locator(".todo-item");
        await Expect(items).ToHaveCountAsync(3, new() { Timeout = 2000 });

        await Expect(items.Nth(0)).ToHaveClassAsync(DoneClass());
        await Expect(items.Nth(0).Locator(".task-text")).ToHaveTextAsync("Read the file");

        await Expect(items.Nth(1)).ToHaveClassAsync(CurrentClass());
        await Expect(items.Nth(1).Locator(".task-text")).ToHaveTextAsync("Fix the parser");
        await Expect(items.Nth(1).Locator(".todo-icon")).ToHaveTextAsync("\u25d0");

        await Expect(items.Nth(2)).ToHaveClassAsync(PendingClass());
        await Expect(items.Nth(2).Locator(".task-text")).ToHaveTextAsync("Build");
    }

    [Test]
    [Category("Tools")]
    public async Task TodoWidget_CollapsesAgainOnHeaderClick()
    {
        await StartRequestAsync("webview-mock-todo.js");

        await Expect(Page.Locator("#global-todo-panel"))
            .ToBeVisibleAsync(new() { Timeout = 4000 });

        await Page.Locator("#todo-header-trigger").ClickAsync();
        await Expect(Page.Locator("#todo-body")).ToBeVisibleAsync(new() { Timeout = 2000 });

        await Page.Locator("#todo-header-trigger").ClickAsync();
        await Expect(Page.Locator("#todo-body")).ToBeHiddenAsync(new() { Timeout = 2000 });
    }

    [Test]
    [Category("Tools")]
    public async Task TodoWidget_UpdatesOnTheSecondCall()
    {
        await StartRequestAsync("webview-mock-todo.js");

        await Expect(Page.Locator("#todo-summary"))
            .ToHaveTextAsync("Tasks 2/3 \u2014 Build", new() { Timeout = 6000 });
    }

    [Test]
    [Category("Tools")]
    public async Task TodoWidget_KeepsTheListWhenACallIsRejected()
    {
        await StartRequestAsync("webview-mock-todo.js");

        await Expect(Page.Locator("#todo-summary"))
            .ToHaveTextAsync("Tasks 2/3 \u2014 Build", new() { Timeout = 6000 });

        // The rejected call arrives after the second accepted list: the message flow shows the failure,
        // the widget must keep the list it already has.
        await Expect(Page.Locator("body"))
            .ToContainTextAsync("Task list not updated", new() { Timeout = 8000 });

        await Expect(Page.Locator("#todo-summary"))
            .ToHaveTextAsync("Tasks 2/3 \u2014 Build", new() { Timeout = 2000 });
    }

    [Test]
    [Category("Tools")]
    public async Task TodoWidget_HidesWhenTheResponseCompletes()
    {
        await StartRequestAsync("webview-mock-todo.js");

        await Expect(Page.Locator("#global-todo-panel"))
            .ToBeVisibleAsync(new() { Timeout = 4000 });

        await Expect(Page.Locator("#global-todo-panel"))
            .ToBeHiddenAsync(new() { Timeout = 8000 });
    }

    [Test]
    [Category("Tools")]
    public async Task TodoWidget_HidesOnStop()
    {
        await StartRequestAsync("webview-mock-todo.js");

        await Expect(Page.Locator("#global-todo-panel"))
            .ToBeVisibleAsync(new() { Timeout = 4000 });

        await Page.Locator("#mainBtn").ClickAsync();

        await Expect(Page.Locator("#global-todo-panel"))
            .ToBeHiddenAsync(new() { Timeout = 4000 });

        // The mock clears its timers on Stop, so the normal completion never runs: the hide came from
        // ChatSessionCancelled and not from the answer ending on its own.
        var stopCalls = await Page.EvaluateAsync<int>("() => window.__stopCallCount || 0");
        Assert.That(stopCalls, Is.EqualTo(1), "the widget must hide because of the Stop");
    }

    [Test]
    [Category("Tools")]
    public async Task TodoWidget_HidesOnError()
    {
        await StartRequestAsync("webview-mock-todo-error.js");

        await Expect(Page.Locator("#global-todo-panel"))
            .ToBeVisibleAsync(new() { Timeout = 4000 });

        await Expect(Page.Locator("#global-todo-panel"))
            .ToBeHiddenAsync(new() { Timeout = 8000 });
    }

    [Test]
    [Category("Tools")]
    public async Task TodoWidget_SurvivesCompactionInTheMiddleOfTheAnswer()
    {
        await StartRequestAsync("webview-mock-todo.js", "compaction");

        await Expect(Page.Locator("#todo-summary"))
            .ToHaveTextAsync(FirstLine, new() { Timeout = 4000 });

        // CompactionStart and CompactionEnd{Continues:true} arrive before this content, so the list has
        // already been through COMPACTING and back: it must still be on screen with the same steps.
        await Expect(Page.Locator("body"))
            .ToContainTextAsync("Continued after compaction", new() { Timeout = 8000 });

        await Expect(Page.Locator("#global-todo-panel")).ToBeVisibleAsync(new() { Timeout = 2000 });
        await Expect(Page.Locator("#todo-summary")).ToHaveTextAsync(FirstLine, new() { Timeout = 2000 });

        // And it goes away with the end of the retried answer.
        await Expect(Page.Locator("#global-todo-panel")).ToBeHiddenAsync(new() { Timeout = 6000 });
    }

    [Test]
    [Category("Tools")]
    public async Task TodoWidget_HidesWhenCompactionEndsTheAnswer()
    {
        await StartRequestAsync("webview-mock-todo.js", "compactionFailed");

        await Expect(Page.Locator("#global-todo-panel"))
            .ToBeVisibleAsync(new() { Timeout = 4000 });

        // A compaction that does not continue ends the answer in ERROR instead of retrying.
        await Expect(Page.Locator("#global-todo-panel"))
            .ToBeHiddenAsync(new() { Timeout = 8000 });
    }
}
