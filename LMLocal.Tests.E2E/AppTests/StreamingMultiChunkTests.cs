namespace LMLocal.Tests.E2E.AppTests;

[TestFixture]
public class StreamingMultiChunkTests : AppTestBase
{
    [Test]
    [Category("Collapsible")]
    public async Task Collapsible_MultiRoundLongFinalAnswer_PreservesEveryBlock()
    {
        await GotoWithMockAsync("webview-mock-streaming-multi.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        await Page.Locator("#userInput").FillAsync("Where is IChatHistoryManager used");
        await Page.Locator("#mainBtn").ClickAsync();

        // Wait for the session to fully complete (button returns to Send).
        await Expect(Page.Locator("#mainBtn"))
            .ToHaveTextAsync("Send", new() { Timeout = 10000 });

        // In collapsible mode the final answer is moved into the dedicated final-response element.
        var final = Page.Locator("[data-element=\"final-response\"]");
        await Expect(final).ToBeVisibleAsync(new() { Timeout = 5000 });

        await Expect(final).ToContainTextAsync("Open the solution or project");
        await Expect(final).ToContainTextAsync("In the Solution Explorer window");
        await Expect(final).ToContainTextAsync("Then scroll through the file contents");
        await Expect(final).ToContainTextAsync("When creating an instance");
        await Expect(final).ToContainTextAsync("As a method parameter");
        await Expect(final).ToContainTextAsync("As a variable type");
        await Expect(final).ToContainTextAsync("When you find all usage locations");
        await Expect(final).ToContainTextAsync("If the IChatHistoryManager class is not found");
        await Expect(final).ToContainTextAsync("Hope this helps");

        // The first-round tool content must not leak into the final answer.
        await Expect(final).Not.ToContainTextAsync("Searching for files in the project");
    }

    /// <summary>
    /// Regression test for a bug in ProgressiveBlockRenderer (block-tail streaming mode):
    /// streaming markdown parsers can temporarily render a single logical list as two
    /// adjacent &lt;ol&gt; elements on a partial-text prefix (e.g. a tight/loose list
    /// boundary artifact), then re-merge them into one &lt;ol&gt; once more text arrives.
    /// If the renderer commits the first fragment as soon as it appears (i.e. as soon as
    /// a sibling block follows it), that fragment's items get duplicated once the parser
    /// re-merges the list. This exercises ProgressiveBlockRenderer directly with a fixed,
    /// deterministic sequence of HTML snapshots reproducing exactly that shape, bypassing
    /// any markdown-it timing non-determinism.
    /// </summary>
    [Test]
    [Category("Collapsible")]
    public async Task ProgressiveBlockRenderer_ListTemporarilySplitThenReMerged_DoesNotDuplicateItems()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var liCount = await Page.EvaluateAsync<int>(@"async () => {
            const { createStreamingRenderer, StreamingMode } =
                await import('/js/streaming/streaming.renderer.js');

            const container = document.createElement('div');
            document.body.appendChild(container);

            const renderer = createStreamingRenderer({ mode: StreamingMode.BLOCK_TAIL, onUpdate: () => {} });
            renderer.start(container);

            // Step 1: a paragraph followed by a 1-item list (list is still the tail).
            renderer.write('t1',
                '<p>Intro</p><ol><li>Item one</li></ol>');

            // Step 2: parser transiently splits the list into two separate <ol>
            // elements (simulating a tight/loose boundary artifact on partial input).
            renderer.write('t2',
                '<p>Intro</p><ol><li>Item one</li></ol><ol><li>Item two</li></ol>');

            // Step 3: parser self-corrects and re-merges everything into one <ol>
            // with all items, once enough text has streamed in.
            renderer.write('t3',
                '<p>Intro</p><ol><li>Item one</li><li>Item two</li><li>Item three</li></ol>');

            // Flush the tail into the container (mirrors renderer.stop()/finishStreaming()).
            renderer.stop();

            const count = container.querySelectorAll('li').length;
            container.remove();
            return count;
        }");

        Assert.That(liCount, Is.EqualTo(3),
            "ProgressiveBlockRenderer must not permanently commit a list fragment that a later chunk re-merges into a single list — doing so duplicates its items.");
    }

    /// <summary>
    /// Same regression as above, but for &lt;blockquote&gt; grouping, which is subject to
    /// the same tight/loose-style temporary-split-then-re-merge behavior on partial markdown
    /// input as ordered/unordered lists.
    /// </summary>
    [Test]
    [Category("Collapsible")]
    public async Task ProgressiveBlockRenderer_BlockquoteTemporarilySplitThenReMerged_DoesNotDuplicateContent()
    {
        await GotoWithMockAsync("webview-mock.js");
        await Expect(Page.Locator("#conn-status"))
            .ToHaveTextAsync("Connected", new() { Timeout = 3000 });

        var pCount = await Page.EvaluateAsync<int>(@"async () => {
            const { createStreamingRenderer, StreamingMode } =
                await import('/js/streaming/streaming.renderer.js');

            const container = document.createElement('div');
            document.body.appendChild(container);

            const renderer = createStreamingRenderer({ mode: StreamingMode.BLOCK_TAIL, onUpdate: () => {} });
            renderer.start(container);

            renderer.write('t1',
                '<p>Intro</p><blockquote><p>Quote line one</p></blockquote>');

            renderer.write('t2',
                '<p>Intro</p><blockquote><p>Quote line one</p></blockquote><blockquote><p>Quote line two</p></blockquote>');

            renderer.write('t3',
                '<p>Intro</p><blockquote><p>Quote line one</p><p>Quote line two</p><p>Quote line three</p></blockquote>');

            renderer.stop();

            const count = container.querySelectorAll('blockquote p').length;
            container.remove();
            return count;
        }");

        Assert.That(pCount, Is.EqualTo(3),
            "ProgressiveBlockRenderer must not permanently commit a blockquote fragment that a later chunk re-merges into a single blockquote — doing so duplicates its content.");
    }
}
