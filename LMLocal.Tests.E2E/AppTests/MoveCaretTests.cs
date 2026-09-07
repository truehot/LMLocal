namespace LMLocal.Tests.E2E.AppTests;

/// <summary>
/// E2E tests for <c>window.lmApi.moveCaret</c> (LMLocal\Resources\js\lm-api.js).
///
/// Rationale: Home/End/ArrowLeft/ArrowRight (and their Shift/Ctrl variants) never reach
/// the browser as real key events — the VS tool-window host intercepts them in
/// MainWindow.OnControlPreviewKeyDown and forwards them to JS via the moveCaret bridge.
/// The WPF/host layer cannot be driven from Playwright, so, exactly like the
/// ProgressiveBlockRenderer regression tests, we exercise the browser-side contract
/// directly: place the textarea value + selection, invoke moveCaret, and assert the
/// resulting selection/anchor state.
///
/// The layout of the shared test text (30 chars) used below:
///   "alpha beta gamma\ndelta epsilon"
///   0         1         2         3
///   0123456789012345678901234567890
///   alpha beta gamma\ndelta epsilon
///
/// word starts: 0(alpha) 6(beta) 11(gamma) 17(delta) 23(epsilon)
/// word ends (exclusive): alpha=5 beta=10 gamma=16 delta=22 epsilon=30
/// line 1: chars 0..15, line break at 16, line 2: chars 17..29.
/// </summary>
[TestFixture]
public class MoveCaretTests : AppTestBase
{
    private const string MoveCaretJs = @"
(args) => {
    const el = document.getElementById('userInput');
    if (!el) throw new Error('userInput element not found');

    const TEXT = ['alpha beta gamma', 'delta epsilon'].join('\n');
    el.value = TEXT;

    const wrapper = el.closest('.input-wrapper');
    if (wrapper) wrapper.classList.add('expanded');

    el.focus();
    el.setSelectionRange(args.selStart, args.selEnd, args.direction);
    delete el.dataset.selAnchor;

    const ok = window.lmApi.moveCaret(args.keyName, args.shift, args.ctrl);

    return [
        ok ? '1' : '0',
        String(el.selectionStart),
        String(el.selectionEnd),
        el.selectionDirection,
        el.dataset.selAnchor === undefined ? '' : el.dataset.selAnchor
    ].join('|');
}";

    private sealed record CaretState(bool Ok, int Start, int End, string Direction, string? Anchor);

    private async Task<CaretState> MoveAsync(
        string keyName, bool shift, bool ctrl,
        int selStart, int selEnd, string direction = "none")
    {
        var raw = await Page.EvaluateAsync<string>(
            MoveCaretJs,
            new { keyName, shift, ctrl, selStart, selEnd, direction });

        var p = raw.Split('|');
        return new CaretState(
            p[0] == "1",
            int.Parse(p[1]),
            int.Parse(p[2]),
            p[3],
            string.IsNullOrEmpty(p[4]) ? null : p[4]);
    }

    private async Task GotoReadyAsync()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });
        await Page.WaitForFunctionAsync("() => typeof window.lmApi?.moveCaret === 'function'");
    }

    // ---------------------------------------------------------------------
    // Plain caret movement (no modifiers) — existing behavior
    // ---------------------------------------------------------------------

    [Test]
    [Category("Input")]
    public async Task MoveCaret_ArrowLeft_MovesCaretBackOneChar()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("ArrowLeft", false, false, selStart: 5, selEnd: 5);

        // Note: Chromium may report a collapsed selection's direction as "forward"
        // even when moveCaret explicitly collapses with 'none', so only the caret
        // position and the absence of a stored anchor are asserted here.
        Assert.That(s.Ok, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(s.Start, Is.EqualTo(4));
            Assert.That(s.End, Is.EqualTo(4));
            Assert.That(s.Anchor, Is.Null);
        });
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_ArrowLeft_AtTextStart_StaysPut()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("ArrowLeft", false, false, selStart: 0, selEnd: 0);

        Assert.That(s.Start, Is.EqualTo(0));
        Assert.That(s.End, Is.EqualTo(0));
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_ArrowRight_MovesCaretForwardOneChar()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("ArrowRight", false, false, selStart: 5, selEnd: 5);

        Assert.That(s.Start, Is.EqualTo(6));
        Assert.That(s.End, Is.EqualTo(6));
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_ArrowRight_AtTextEnd_StaysPut()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("ArrowRight", false, false, selStart: 30, selEnd: 30);

        Assert.That(s.Start, Is.EqualTo(30));
        Assert.That(s.End, Is.EqualTo(30));
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_Home_FromFirstLine_MovesToTextStart()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("Home", false, false, selStart: 10, selEnd: 10);

        Assert.That(s.Start, Is.EqualTo(0));
        Assert.That(s.End, Is.EqualTo(0));
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_Home_FromSecondLine_MovesToSecondLineStart()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("Home", false, false, selStart: 20, selEnd: 20);

        Assert.That(s.Start, Is.EqualTo(17));
        Assert.That(s.End, Is.EqualTo(17));
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_End_FromFirstLine_MovesBeforeLineBreak()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("End", false, false, selStart: 3, selEnd: 3);

        Assert.That(s.Start, Is.EqualTo(16));
        Assert.That(s.End, Is.EqualTo(16));
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_End_FromSecondLine_MovesToTextEnd()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("End", false, false, selStart: 18, selEnd: 18);

        Assert.That(s.Start, Is.EqualTo(30));
        Assert.That(s.End, Is.EqualTo(30));
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_UnsupportedKey_ReturnsFalseAndDoesNotMove()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("ArrowUp", false, false, selStart: 5, selEnd: 5);

        Assert.That(s.Ok, Is.False);
        Assert.That(s.Start, Is.EqualTo(5));
        Assert.That(s.End, Is.EqualTo(5));
    }

    // ---------------------------------------------------------------------
    // Shift extension — existing behavior (anchor via el.dataset.selAnchor)
    // ---------------------------------------------------------------------

    [Test]
    [Category("Input")]
    public async Task MoveCaret_ShiftArrowLeft_SelectsPreviousChar()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("ArrowLeft", shift: true, ctrl: false, selStart: 6, selEnd: 6);

        Assert.Multiple(() =>
        {
            Assert.That(s.Start, Is.EqualTo(5));
            Assert.That(s.End, Is.EqualTo(6));
            Assert.That(s.Direction, Is.EqualTo("backward"));
            Assert.That(s.Anchor, Is.EqualTo("6"));
        });
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_ShiftArrowRight_SelectsNextChar()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("ArrowRight", shift: true, ctrl: false, selStart: 5, selEnd: 5);

        Assert.Multiple(() =>
        {
            Assert.That(s.Start, Is.EqualTo(5));
            Assert.That(s.End, Is.EqualTo(6));
            Assert.That(s.Direction, Is.EqualTo("forward"));
            Assert.That(s.Anchor, Is.EqualTo("5"));
        });
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_ShiftHome_FromSecondLine_SelectsToLineStart()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("Home", shift: true, ctrl: false, selStart: 20, selEnd: 20);

        Assert.Multiple(() =>
        {
            Assert.That(s.Start, Is.EqualTo(17));
            Assert.That(s.End, Is.EqualTo(20));
            Assert.That(s.Direction, Is.EqualTo("backward"));
            Assert.That(s.Anchor, Is.EqualTo("20"));
        });
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_ShiftEnd_FromSecondLine_SelectsToTextEnd()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("End", shift: true, ctrl: false, selStart: 20, selEnd: 20);

        Assert.Multiple(() =>
        {
            Assert.That(s.Start, Is.EqualTo(20));
            Assert.That(s.End, Is.EqualTo(30));
            Assert.That(s.Direction, Is.EqualTo("forward"));
            Assert.That(s.Anchor, Is.EqualTo("20"));
        });
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_RepeatedShiftArrowLeft_KeepsOriginalAnchor()
    {
        await GotoReadyAsync();
        var raw = await Page.EvaluateAsync<string[]>(@"() => {
            const el = document.getElementById('userInput');
            const TEXT = ['alpha beta gamma', 'delta epsilon'].join('\n');
            el.value = TEXT;
            el.focus();
            el.setSelectionRange(6, 6, 'none');

            // First press: creates anchor at 6, selection 5..6 backward.
            window.lmApi.moveCaret('ArrowLeft', true);
            // Second press: anchor must stay 6, selection extends to 4..6.
            window.lmApi.moveCaret('ArrowLeft', true);

            return [
                String(el.selectionStart),
                String(el.selectionEnd),
                el.selectionDirection,
                el.dataset.selAnchor === undefined ? '' : el.dataset.selAnchor
            ];
        }");

        Assert.Multiple(() =>
        {
            Assert.That(int.Parse(raw[0]), Is.EqualTo(4));
            Assert.That(int.Parse(raw[1]), Is.EqualTo(6));
            Assert.That(raw[2], Is.EqualTo("backward"));
            Assert.That(raw[3], Is.EqualTo("6"));
        });
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_ShiftThenPlainMove_CollapsesSelectionAndClearsAnchor()
    {
        await GotoReadyAsync();
        var raw = await Page.EvaluateAsync<string[]>(@"() => {
            const el = document.getElementById('userInput');
            const TEXT = ['alpha beta gamma', 'delta epsilon'].join('\n');
            el.value = TEXT;
            el.focus();
            el.setSelectionRange(5, 6, 'backward');
            el.dataset.selAnchor = '6';

            // Plain (non-shift) press: collapse to caret 4 and drop the anchor.
            window.lmApi.moveCaret('ArrowLeft', false);

            return [
                String(el.selectionStart),
                String(el.selectionEnd),
                el.selectionDirection,
                el.dataset.selAnchor === undefined ? '' : el.dataset.selAnchor
            ];
        }");

        // Collapsing leaves no selection; Chromium may report the collapsed selection's
        // direction as "forward" rather than "none", so we assert position + cleared anchor.
        Assert.Multiple(() =>
        {
            Assert.That(int.Parse(raw[0]), Is.EqualTo(4));
            Assert.That(int.Parse(raw[1]), Is.EqualTo(4));
            Assert.That(raw[3], Is.EqualTo(""));
        });
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_TwoArgumentInvocation_MatchesThreeArgumentFalse()
    {
        // The current host bridge (SendKeyToWebView) calls moveCaret with only two
        // arguments. Ensure a literal two-argument call keeps behaving identically
        // once the third (isCtrl) parameter is introduced.
        await GotoReadyAsync();
        var raw = await Page.EvaluateAsync<string[]>(@"() => {
            const el = document.getElementById('userInput');
            const TEXT = ['alpha beta gamma', 'delta epsilon'].join('\n');
            el.value = TEXT;
            el.focus();

            el.setSelectionRange(20, 20, 'none');
            const twoArgHome = window.lmApi.moveCaret('Home', true);

            return [
                twoArgHome ? '1' : '0',
                String(el.selectionStart),
                String(el.selectionEnd),
                el.selectionDirection,
                el.dataset.selAnchor === undefined ? '' : el.dataset.selAnchor
            ];
        }");

        Assert.Multiple(() =>
        {
            Assert.That(raw[0], Is.EqualTo("1"));
            Assert.That(int.Parse(raw[1]), Is.EqualTo(17));
            Assert.That(int.Parse(raw[2]), Is.EqualTo(20));
            Assert.That(raw[3], Is.EqualTo("backward"));
            Assert.That(raw[4], Is.EqualTo("20"));
        });
    }

    // ---------------------------------------------------------------------
    // Ctrl navigation — new behavior (third argument isCtrl = true)
    // ---------------------------------------------------------------------

    [Test]
    [Category("Input")]
    public async Task MoveCaret_CtrlHome_MovesToDocumentStart()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("Home", false, ctrl: true, selStart: 20, selEnd: 20);

        Assert.That(s.Start, Is.EqualTo(0));
        Assert.That(s.End, Is.EqualTo(0));
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_CtrlEnd_MovesToDocumentEnd()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("End", false, ctrl: true, selStart: 10, selEnd: 10);

        Assert.That(s.Start, Is.EqualTo(30));
        Assert.That(s.End, Is.EqualTo(30));
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_CtrlArrowLeft_MiddleOfWord_JumpsToWordStart()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("ArrowLeft", false, ctrl: true, selStart: 27, selEnd: 27);

        Assert.That(s.Start, Is.EqualTo(23));
        Assert.That(s.End, Is.EqualTo(23));
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_CtrlArrowLeft_AtWordStart_JumpsToPreviousWordStart()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("ArrowLeft", false, ctrl: true, selStart: 23, selEnd: 23);

        Assert.That(s.Start, Is.EqualTo(17));
        Assert.That(s.End, Is.EqualTo(17));
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_CtrlArrowLeft_FromFirstWordStart_StaysPut()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("ArrowLeft", false, ctrl: true, selStart: 0, selEnd: 0);

        Assert.That(s.Start, Is.EqualTo(0));
        Assert.That(s.End, Is.EqualTo(0));
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_CtrlArrowRight_FromTextStart_ToEndOfFirstWord()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("ArrowRight", false, ctrl: true, selStart: 0, selEnd: 0);

        Assert.That(s.Start, Is.EqualTo(5));
        Assert.That(s.End, Is.EqualTo(5));
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_CtrlArrowRight_AfterWord_SkipsSpacesToNextWordEnd()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("ArrowRight", false, ctrl: true, selStart: 5, selEnd: 5);

        Assert.That(s.Start, Is.EqualTo(10));
        Assert.That(s.End, Is.EqualTo(10));
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_CtrlArrowRight_MiddleOfWord_JumpsToWordEnd()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("ArrowRight", false, ctrl: true, selStart: 13, selEnd: 13);

        Assert.That(s.Start, Is.EqualTo(16));
        Assert.That(s.End, Is.EqualTo(16));
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_CtrlArrowRight_FromLastLineSpace_ToTextEnd()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("ArrowRight", false, ctrl: true, selStart: 22, selEnd: 22);

        Assert.That(s.Start, Is.EqualTo(30));
        Assert.That(s.End, Is.EqualTo(30));
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_CtrlShiftHome_SelectsToDocumentStart()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("Home", shift: true, ctrl: true, selStart: 20, selEnd: 20);

        Assert.Multiple(() =>
        {
            Assert.That(s.Start, Is.EqualTo(0));
            Assert.That(s.End, Is.EqualTo(20));
            Assert.That(s.Direction, Is.EqualTo("backward"));
            Assert.That(s.Anchor, Is.EqualTo("20"));
        });
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_CtrlShiftEnd_SelectsToDocumentEnd()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("End", shift: true, ctrl: true, selStart: 10, selEnd: 10);

        Assert.Multiple(() =>
        {
            Assert.That(s.Start, Is.EqualTo(10));
            Assert.That(s.End, Is.EqualTo(30));
            Assert.That(s.Direction, Is.EqualTo("forward"));
            Assert.That(s.Anchor, Is.EqualTo("10"));
        });
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_CtrlShiftArrowLeft_SelectsWordBackward()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("ArrowLeft", shift: true, ctrl: true, selStart: 27, selEnd: 27);

        Assert.Multiple(() =>
        {
            Assert.That(s.Start, Is.EqualTo(23));
            Assert.That(s.End, Is.EqualTo(27));
            Assert.That(s.Direction, Is.EqualTo("backward"));
            Assert.That(s.Anchor, Is.EqualTo("27"));
        });
    }

    [Test]
    [Category("Input")]
    public async Task MoveCaret_CtrlShiftArrowRight_SelectsWordForward()
    {
        await GotoReadyAsync();
        var s = await MoveAsync("ArrowRight", shift: true, ctrl: true, selStart: 13, selEnd: 13);

        Assert.Multiple(() =>
        {
            Assert.That(s.Start, Is.EqualTo(13));
            Assert.That(s.End, Is.EqualTo(16));
            Assert.That(s.Direction, Is.EqualTo("forward"));
            Assert.That(s.Anchor, Is.EqualTo("13"));
        });
    }
}
