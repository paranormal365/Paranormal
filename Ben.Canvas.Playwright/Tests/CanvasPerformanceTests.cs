namespace Ben.Canvas.Playwright.Tests;

/// <summary>
/// A drag on a large board touches only the dragged block until the pointer lifts.
/// </summary>
/// <remarks>
/// The board does not render while a gesture runs. If that gate breaks, every pointer move re-renders
/// hundreds of blocks and a drag stutters on an iPad; this test sees it as DOM mutations on blocks the
/// drag never touched.
/// </remarks>
[Category("Editing")]
[TestFixture(DeviceKind.Desktop)]
public sealed class CanvasPerformanceTests(DeviceKind device) : CanvasTestBase(device)
{
    [Test]
    public async Task A_drag_on_a_300_node_board_renders_nothing_until_pointerup()
    {
        await StartCleanAsync();
        await Page.ClickAsync(".bc-rail [data-bc-action='add-card']");
        await Board.FocusAsync();
        for (var i = 0; i < 299; i++) await Page.Keyboard.PressAsync("t");
        await Expect(Nodes).ToHaveCountAsync(300, new() { Timeout = 60_000 });

        // The first card: its head row stays uncovered, because the notes after it cascade down and right.
        var dragged = Nodes.First;
        var id = await dragged.GetAttributeAsync("data-bc-node");
        var startRect = await WorldRectAsync(dragged);

        await Page.EvaluateAsync(
            """
            id => {
              window.__bcMutations = 0;
              window.__bcObserver = new MutationObserver(list => {
                for (const m of list) {
                  const node = m.target.closest ? m.target.closest('.bc-node') : m.target.parentElement?.closest('.bc-node');
                  if (node && node.getAttribute('data-bc-node') === id) continue;
                  window.__bcMutations++;
                }
              });
              window.__bcObserver.observe(document.querySelector('.bc-nodes'), { attributes: true, childList: true, subtree: true, characterData: true });
            }
            """, id);

        var box = (await dragged.BoundingBoxAsync())!;
        var x = box.X + box.Width / 2;
        var y = box.Y + 12;
        await Page.Mouse.MoveAsync((float)x, (float)y);
        await Page.Mouse.DownAsync();
        var started = DateTime.UtcNow;
        await Page.Mouse.MoveAsync((float)(x + 160), (float)(y + 90), new() { Steps = 20 });
        var elapsed = DateTime.UtcNow - started;
        var during = await Page.EvaluateAsync<int>("() => window.__bcMutations");
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(500);

        Assert.That(during, Is.EqualTo(0), "Blocks other than the dragged one changed during the drag.");
        Assert.That(elapsed, Is.LessThan(TimeSpan.FromSeconds(3)), "Twenty pointer moves took too long.");

        var after = await WorldRectAsync(Page.Locator($".bc-node[data-bc-node='{id}']"));
        Assert.That(after.X - startRect.X, Is.EqualTo(160).Within(8), "The dragged block did not land where it was dropped.");
    }
}
