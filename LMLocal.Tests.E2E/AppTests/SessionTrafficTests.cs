namespace LMLocal.Tests.E2E.AppTests;
using System.Globalization;

/// <summary>
/// Covers the two independent metrics exposed by the toolbar tooltip (#info-tooltip):
///
///  * context occupancy — how full the model window is right now (also drawn as the bar).
///    It is a snapshot: it grows by roughly one exchange per request, and it survives a model
///    or provider switch because the conversation itself is not cleared.
///  * session traffic   — the cumulative volume sent to (in) and received from (out) the
///    server for the currently active model. Because the model is stateless, every request
///    re-sends the whole conversation, so this keeps growing even when the occupancy
///    stays flat (10k in / 1k out on turn 1, 11k in / 1k out on turn 2 -> 21k / 2k total).
///    It is dropped when the model or the endpoint changes (see sessionStore.resetTraffic).
///
/// The text is published through data-tooltip (the native title attribute is unusable: the
/// toolbar re-renders on every chunk flush and Chromium dismisses a visible native tooltip as
/// soon as the attribute changes). The popover over it is opened by hover only.
///
/// The mock ("webview-mock-context-bar.js") schedules nothing by itself, so every protocol
/// step is emitted explicitly and the assertions are deterministic.
/// </summary>
[TestFixture]
public class SessionTrafficTests : AppTestBase
{
    private const int TokenMax = 16384;

    /// <summary>
    /// Tooltip as written by toolbar.component.js while the session has no reported traffic:
    /// two lines, compared exactly. A plain string is both stricter than a pattern and free of
    /// runtime regex compilation. Note the space between the percent sign and the line break —
    /// that is what the current production format emits.
    ///
    /// The UI formats numbers with toLocaleString('en-US'), so the expected text must use the
    /// invariant comma separator regardless of the machine culture (ru-RU would emit NBSP).
    /// </summary>
    private static string ContextOnlyTitle(string percent, string used, int tokenMax = TokenMax) =>
        $"Context usage: {percent}% \n{used} / {tokenMax.ToString("N0", CultureInfo.InvariantCulture)} tokens";

    /// <summary>
    /// Tooltip form once the provider reported usage for at least one request. Cached input tokens
    /// are a subset of the prompt, so they are rendered inside the `in` term; the default (empty)
    /// value keeps the historical two-part traffic line, which is what the backend produces when
    /// it reports no cache hit.
    /// </summary>
    private static string SessionTitle(string percent, string used, string inTokens, string outTokens, string cached = "", int tokenMax = TokenMax) =>
        $"{ContextOnlyTitle(percent, used, tokenMax)}\nSession: {inTokens} in{(cached.Length == 0 ? string.Empty : $" ({cached} cached)")} · {outTokens} out";

    /// <summary>
    /// Asserts the tooltip text published by the toolbar.
    /// </summary>
    private async Task ExpectTooltipAsync(string expected)
    {
        await Expect(Page.Locator("#info-tooltip"))
            .ToHaveAttributeAsync("data-tooltip", expected, new() { Timeout = 5000 });
    }

    private async Task StartSessionAsync()
    {
        await GotoWithMockAsync("webview-mock-context-bar.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Hello");
        await Page.Locator("#mainBtn").ClickAsync();
    }

    private async Task EmitAsync(object message)
    {
        await Page.EvaluateAsync<object>("msg => window.__emitBridgeMessage(msg)", message);
    }

    /// <summary>
    /// Emits a content chunk and then forces a ChunkBuffer flush, so the UI state is updated
    /// without depending on the 100 ms flush interval timing.
    /// </summary>
    private async Task EmitContentAsync(string text, int count)
    {
        await EmitAsync(new { Type = "StreamContent", Payload = text, Count = count, TokensPerSecond = 15.5 });
        await Page.WaitForTimeoutAsync(140);
        await EmitAsync(new { Type = "StreamContent", Payload = " ", Count = count, TokensPerSecond = 15.5 });
    }

    /// <summary>Completes the current request with the usage the real backend reports.</summary>
    private async Task CompleteRequestAsync(int totalTokens, int promptTokens, int completionTokens)
    {
        await EmitAsync(new
        {
            Type = "ChatSessionComplete",
            TotalTokens = totalTokens,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            ReasoningTokens = 0,
            CachedTokens = 0,
            TokensPerSecond = 12.0
        });
    }

    [Test]
    [Category("TokenBar")]
    public async Task SessionTraffic_IsOmittedBeforeAnyUsage()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);

        // Nothing was completed yet, so the tooltip keeps the short two-line form and does not
        // claim "0 in / 0 out" traffic.
        await ExpectTooltipAsync(ContextOnlyTitle("5", "900"));
    }

    [Test]
    [Category("TokenBar")]
    public async Task SessionTraffic_AccumulatesInputAndOutputAcrossRequests()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);
        await EmitAsync(new { Type = "StreamEnd" });
        await CompleteRequestAsync(totalTokens: 11000, promptTokens: 10000, completionTokens: 1000);

        // Occupancy: 11000 / 16384 = 67.1% -> 67%. Traffic: 10k in / 1k out.
        await ExpectTooltipAsync(SessionTitle("67", "11,000", "10k", "1k"));

        // Second request: the whole history (11k) is sent again and another 1k comes back.
        await Page.Locator("#userInput").FillAsync("Again");
        await Page.Locator("#mainBtn").ClickAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Second answer", 500);
        await EmitAsync(new { Type = "StreamEnd" });
        await CompleteRequestAsync(totalTokens: 12000, promptTokens: 11000, completionTokens: 1000);

        // Occupancy: 12000 / 16384 = 73.2% -> 73%. Traffic accumulated: 21k in / 2k out.
        await ExpectTooltipAsync(SessionTitle("73", "12,000", "21k", "2k"));
    }

    [Test]
    [Category("TokenBar")]
    public async Task SessionTraffic_IsResetByClearChat()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);
        await EmitAsync(new { Type = "StreamEnd" });
        await CompleteRequestAsync(totalTokens: 11000, promptTokens: 10000, completionTokens: 1000);

        await ExpectTooltipAsync(SessionTitle("67", "11,000", "10k", "1k"));

        await Page.Locator("#clear-chat-btn").ClickAsync();
        await Expect(Page.Locator("#confirm-dialog")).ToBeVisibleAsync(new() { Timeout = 3000 });
        await Page.Locator("#confirm-dialog #confirm-dialog-confirm").ClickAsync();
        await Expect(Page.Locator("#confirm-dialog")).ToBeHiddenAsync(new() { Timeout = 3000 });

        // The conversation is replaced and the host reports no token count for the new one,
        // so the context occupancy and the session traffic both drop to zero.
        await ExpectTooltipAsync(ContextOnlyTitle("0", "0"));
    }

    [Test]
    [Category("TokenBar")]
    public async Task SessionTraffic_KeepsValuesWhenClearFails()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);
        await EmitAsync(new { Type = "StreamEnd" });
        await CompleteRequestAsync(totalTokens: 11000, promptTokens: 10000, completionTokens: 1000);

        await ExpectTooltipAsync(SessionTitle("67", "11,000", "10k", "1k"));

        // The host rejects the reset, so the conversation is NOT cleared: the metrics must stay.
        await Page.EvaluateAsync(@"() => {
            window.__bridgeOverride.ResetHistoryWithActionAsync = async () => {
                throw new Error('host rejected the reset');
            };
        }");

        await Page.Locator("#clear-chat-btn").ClickAsync();
        await Expect(Page.Locator("#confirm-dialog")).ToBeVisibleAsync(new() { Timeout = 3000 });
        await Page.Locator("#confirm-dialog #confirm-dialog-confirm").ClickAsync();
        await Expect(Page.Locator("#confirm-dialog")).ToBeHiddenAsync(new() { Timeout = 3000 });

        // performClearChat lands in ERROR; the live counters are dropped but the authoritative
        // context and the accumulated traffic survive, because the history still exists on the host.
        await Expect(Page.Locator("#status-text")).ToHaveTextAsync("Error", new() { Timeout = 5000 });

        await ExpectTooltipAsync(SessionTitle("67", "11,000", "10k", "1k"));
    }

    [Test]
    [Category("TokenBar")]
    public async Task SessionTraffic_IsResetBySessionLoad()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);
        await EmitAsync(new { Type = "StreamEnd" });
        await CompleteRequestAsync(totalTokens: 11000, promptTokens: 10000, completionTokens: 1000);

        await ExpectTooltipAsync(SessionTitle("67", "11,000", "10k", "1k"));

        // Loading another session replaces the conversation: the host reports no token count
        // for it, so both the context occupancy and the accumulated traffic must drop to zero.
        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-chat-history']").ClickAsync();

        var dialog = Page.Locator("#chat-history-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 3000 });
        await Expect(dialog.Locator(".chat-history-load")).ToHaveCountAsync(1, new() { Timeout = 3000 });
        await dialog.Locator(".chat-history-load").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });

        await ExpectTooltipAsync(ContextOnlyTitle("0", "0"));
    }

    [Test]
    [Category("TokenBar")]
    public async Task SessionTraffic_SumsAllRoundsOfOneRequest()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);
        await EmitAsync(new { Type = "StreamEnd" });

        // A tool loop makes several LLM calls. The host sends the cumulative sums of the whole
        // request (RequestPromptTokens/RequestCompletionTokens) next to the last-round fields.
        await EmitAsync(new
        {
            Type = "ChatSessionComplete",
            TotalTokens = 4400,
            PromptTokens = 4200,
            CompletionTokens = 200,
            RequestPromptTokens = 7200,
            RequestCompletionTokens = 300,
            ReasoningTokens = 0,
            CachedTokens = 0,
            TokensPerSecond = 12.0
        });

        // Occupancy stays the last-round snapshot (4400 / 16384 = 26.8% -> 27%),
        // while the traffic shows the sum of every round: 7.2k in / 300 out.
        await ExpectTooltipAsync(SessionTitle("27", "4,400", "7.2k", "300"));
    }

    [Test]
    [Category("TokenBar")]
    public async Task SessionTraffic_ResetsTrafficButKeepsContext_WhenProviderChanges()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);
        await EmitAsync(new { Type = "StreamEnd" });
        await CompleteRequestAsync(totalTokens: 11000, promptTokens: 10000, completionTokens: 1000);

        await ExpectTooltipAsync(SessionTitle("67", "11,000", "10k", "1k"));

        // Swap the provider in the settings dialog. The host does not clear the conversation, so
        // the occupied context must survive — while the traffic is a per-endpoint metric and must
        // not count what was already paid to the previous provider.
        await Page.EvaluateAsync(@"() => {
            window.__settingsOverride.GetSettingsAsync = async () => JSON.stringify({
                AutoLoadOnStartup: true,
                Provider: 'openai',
                ProviderId: 0,
                LmStudioBaseUrl: 'http://first.local',
                ApiKey: 'key-1'
            });
            window.__providersOverride.GetProvidersAsync = async () => JSON.stringify({
                defaultProviders: [
                    { id: 0, providerType: 'openai', name: 'OpenAI', customBaseUrl: 'http://first.local', customApiKey: 'key-1' },
                    { id: 1, providerType: 'ollama', name: 'Ollama', customBaseUrl: 'http://second.local', customApiKey: 'key-2' }
                ],
                providers: [],
                providerTypes: []
            });
        }");

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-settings']").ClickAsync();

        var dialog = Page.Locator("#settings-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#settings-dialog form')?.children.length > 0");
        await Expect(dialog.Locator("[data-setting='Provider'] option")).ToHaveCountAsync(2);

        await dialog.Locator("[data-setting='Provider']").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await dialog.Locator(".modal-footer button.btn-primary").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });

        // A different endpoint is a new interaction session: the traffic of the previous provider
        // is dropped, while the occupied context survives (the conversation was not cleared).
        await ExpectTooltipAsync("Context usage: 11,000");
    }

    [Test]
    [Category("TokenBar")]
    public async Task SessionTraffic_IsResetByModelChange__KeepsOccupancy()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);
        await EmitAsync(new { Type = "StreamEnd" });
        await CompleteRequestAsync(totalTokens: 5000, promptTokens: 10000, completionTokens: 1000);

        // Occupancy: 5000 / 16384 = 30.5% -> 31%. Traffic: 10k in / 1k out.
        await ExpectTooltipAsync(SessionTitle("31", "5,000", "10k", "1k"));

        // Switch to the model with the smaller window (8192).
        await Page.Locator("#model-name").ClickAsync();
        var dialog = Page.Locator("#model-selector-dialog");
        await Expect(dialog).ToBeVisibleAsync();
        await Page.Locator("#models-list-container .model-card[data-model-id='test-model-2']").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });

        // The very same 5000 tokens now fill the smaller window (5000 / 8192 = 61.03% -> 61%),
        // because the next request sends the whole history again. The traffic, however, belongs
        // to the previous model and is gone: the tooltip drops back to the two-line form.
        await ExpectTooltipAsync(ContextOnlyTitle("61", "5,000", tokenMax: 8192));
    }

    [Test]
    [Category("TokenBar")]
    public async Task SessionTraffic_IsNotResetBySameModelReselection()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);
        await EmitAsync(new { Type = "StreamEnd" });
        await CompleteRequestAsync(totalTokens: 5000, promptTokens: 10000, completionTokens: 1000);

        await ExpectTooltipAsync(SessionTitle("31", "5,000", "10k", "1k"));

        // The card of the model that is already active can be clicked again; that is not a switch,
        // so the traffic accumulated for it must survive. Guards the identity check in
        // appDataService.setActiveModel.
        await Page.Locator("#model-name").ClickAsync();
        var dialog = Page.Locator("#model-selector-dialog");
        await Expect(dialog).ToBeVisibleAsync();
        await Page.Locator("#models-list-container .model-card[data-model-id='test-model-1']").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });

        await ExpectTooltipAsync(SessionTitle("31", "5,000", "10k", "1k"));
    }

    [Test]
    [Category("TokenBar")]
    public async Task SessionTraffic_IsKeptWhenHostRejectsModelSwitch()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);
        await EmitAsync(new { Type = "StreamEnd" });
        await CompleteRequestAsync(totalTokens: 5000, promptTokens: 10000, completionTokens: 1000);

        await ExpectTooltipAsync(SessionTitle("31", "5,000", "10k", "1k"));

        // The host refuses the switch: nothing was activated, so nothing may be forgotten either
        // (decision 4 of §11.2: the reset happens only after a successful host answer).
        await Page.EvaluateAsync(@"() => {
            window.__modelsOverride.SetActiveModelAsync = async () => false;
        }");

        await Page.Locator("#model-name").ClickAsync();
        var dialog = Page.Locator("#model-selector-dialog");
        await Expect(dialog).ToBeVisibleAsync();
        await Page.Locator("#models-list-container .model-card[data-model-id='test-model-2']").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });

        await ExpectTooltipAsync(SessionTitle("31", "5,000", "10k", "1k"));
    }

    [Test]
    [Category("TokenBar")]
    public async Task SessionTraffic_IsKeptWhenSettingsSaveIsRejected()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);
        await EmitAsync(new { Type = "StreamEnd" });
        await CompleteRequestAsync(totalTokens: 11000, promptTokens: 10000, completionTokens: 1000);

        await ExpectTooltipAsync(SessionTitle("67", "11,000", "10k", "1k"));

        // The host rejects the new settings, so the endpoint did not change on its side either and
        // the traffic of the still-active endpoint must survive.
        await Page.EvaluateAsync(@"() => {
            window.__settingsOverride.GetSettingsAsync = async () => JSON.stringify({
                AutoLoadOnStartup: true,
                Provider: 'openai',
                ProviderId: 0,
                LmStudioBaseUrl: 'http://first.local',
                ApiKey: 'key-1'
            });
            window.__settingsOverride.UpdateSettingsAsync = async () => false;
            window.__providersOverride.GetProvidersAsync = async () => JSON.stringify({
                defaultProviders: [
                    { id: 0, providerType: 'openai', name: 'OpenAI', customBaseUrl: 'http://first.local', customApiKey: 'key-1' },
                    { id: 1, providerType: 'ollama', name: 'Ollama', customBaseUrl: 'http://second.local', customApiKey: 'key-2' }
                ],
                providers: [],
                providerTypes: []
            });
        }");

        await Page.Locator("#menu-btn").ClickAsync();
        await Page.Locator("button[data-action='open-settings']").ClickAsync();

        var dialog = Page.Locator("#settings-dialog");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Page.WaitForFunctionAsync("() => document.querySelector('#settings-dialog form')?.children.length > 0");
        await Expect(dialog.Locator("[data-setting='Provider'] option")).ToHaveCountAsync(2);

        await dialog.Locator("[data-setting='Provider']").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await dialog.Locator(".modal-footer button.btn-primary").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });

        await ExpectTooltipAsync(SessionTitle("67", "11,000", "10k", "1k"));
    }

    [Test]
    [Category("TokenBar")]
    public async Task SessionTraffic_ShowsCachedShareOfInput()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);
        await EmitAsync(new { Type = "StreamEnd" });

        // A tool loop: the request sent 7.2k prompt tokens in total, of which 4.2k were served
        // from the provider cache. The last-round CachedTokens (1200) must be ignored in favour
        // of RequestCachedTokens, and the cache share must not inflate `in`.
        await EmitAsync(new
        {
            Type = "ChatSessionComplete",
            TotalTokens = 4400,
            PromptTokens = 4200,
            CompletionTokens = 200,
            RequestPromptTokens = 7200,
            RequestCompletionTokens = 300,
            RequestCachedTokens = 4200,
            ReasoningTokens = 0,
            CachedTokens = 1200,
            TokensPerSecond = 12.0
        });

        await ExpectTooltipAsync(SessionTitle("27", "4,400", "7.2k", "300", cached: "4.2k"));
    }

    [Test]
    [Category("TokenBar")]
    public async Task SessionTraffic_OmitsCachedWhenBackendReportsNone()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);
        await EmitAsync(new { Type = "StreamEnd" });

        // An explicit zero must not fall through to another field and must not print an empty
        // "( cached)" suffix: the traffic line keeps its historical two-part form.
        await EmitAsync(new
        {
            Type = "ChatSessionComplete",
            TotalTokens = 11000,
            PromptTokens = 10000,
            CompletionTokens = 1000,
            RequestPromptTokens = 10000,
            RequestCompletionTokens = 1000,
            RequestCachedTokens = 0,
            ReasoningTokens = 0,
            CachedTokens = 0,
            TokensPerSecond = 12.0
        });

        await ExpectTooltipAsync(SessionTitle("67", "11,000", "10k", "1k"));
    }

    [Test]
    [Category("TokenBar")]
    public async Task SessionTraffic_ClampsCachedToPrompt()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);
        await EmitAsync(new { Type = "StreamEnd" });

        // A malformed usage (or a transitional backend that sums the prompt over all rounds but
        // reports the cache for the last round only) may claim more cached tokens than prompt
        // ones. The cache share is a subset of `in`, so it is clamped instead of printed as is.
        await EmitAsync(new
        {
            Type = "ChatSessionComplete",
            TotalTokens = 4400,
            PromptTokens = 4200,
            CompletionTokens = 200,
            RequestPromptTokens = 5000,
            RequestCompletionTokens = 200,
            RequestCachedTokens = 9000,
            ReasoningTokens = 0,
            CachedTokens = 0,
            TokensPerSecond = 12.0
        });

        await ExpectTooltipAsync(SessionTitle("27", "4,400", "5k", "200", cached: "5k"));
    }

    [Test]
    [Category("TokenBar")]
    public async Task SessionTraffic_ResetsCachedOnModelChange()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);
        await EmitAsync(new { Type = "StreamEnd" });

        await EmitAsync(new
        {
            Type = "ChatSessionComplete",
            TotalTokens = 5000,
            PromptTokens = 4200,
            CompletionTokens = 1000,
            RequestPromptTokens = 10000,
            RequestCompletionTokens = 1000,
            RequestCachedTokens = 4000,
            ReasoningTokens = 0,
            CachedTokens = 0,
            TokensPerSecond = 12.0
        });

        // Occupancy 5000 / 16384 = 30.5% -> 31%; traffic 10k in (4k cached) / 1k out.
        await ExpectTooltipAsync(SessionTitle("31", "5,000", "10k", "1k", cached: "4k"));

        // A cache hit belongs to the provider that produced it: switching the model drops the
        // cache share together with the traffic, so nothing of the previous model is shown.
        await Page.Locator("#model-name").ClickAsync();
        var dialog = Page.Locator("#model-selector-dialog");
        await Expect(dialog).ToBeVisibleAsync();
        await Page.Locator("#models-list-container .model-card[data-model-id='test-model-2']").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 3000 });

        await ExpectTooltipAsync(ContextOnlyTitle("61", "5,000", tokenMax: 8192));
    }

    [Test]
    [Category("TokenBar")]
    public async Task SessionTraffic_AddsEstimatedToolResultTokens()
    {
        await StartSessionAsync();

        await EmitAsync(new { Type = "ChatSessionStart" });
        await EmitContentAsync("Hello", 900);
        await EmitAsync(new { Type = "StreamEnd" });

        // The host reports an estimate of the tool results that will be prepended to the next
        // round's prompt. The live number must include it: 900 + 700 = 1600 -> 9.77% -> 10%.
        await EmitAsync(new { Type = "ChatSessionIterating", RoundNumber = 1, ToolCount = 1, IsFinalRound = false, EstimatedToolResultTokens = 700 });
        await ExpectTooltipAsync(ContextOnlyTitle("10", "1,600"));

        // The authoritative total replaces the estimate together with the live counters.
        await CompleteRequestAsync(totalTokens: 5000, promptTokens: 10000, completionTokens: 1000);
        await ExpectTooltipAsync(SessionTitle("31", "5,000", "10k", "1k"));
    }
}
