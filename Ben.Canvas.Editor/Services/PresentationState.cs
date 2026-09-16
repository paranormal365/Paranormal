using Ben.Canvas.Core.Commands;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Presentation;

namespace Ben.Canvas.Editor.Services;

/// <summary>
/// Walking a board one card at a time, for showing it to somebody.
/// </summary>
/// <remarks>
/// <para>
/// Ben, 2026-09-16: "presentation mode like miro where you can create the cards like slides."
/// Nothing is made to present: the board is the deck, and <see cref="SlideOrder"/> decides the
/// running order from what is already drawn — groups if there are any, otherwise the arrows, then
/// down the page.
/// </para>
/// <para>
/// It changes nothing. Starting, stepping and stopping touch no command, push no undo entry and
/// leave the document byte for byte as it was, so a board can be presented by somebody who may only
/// read it — which is most of the point, since the meeting is where a case gets talked through.
/// </para>
/// <para>
/// The list is taken once, when the walk starts. A board edited underneath a presentation would
/// otherwise renumber itself mid-sentence; and the one case that must not break — a card deleted
/// while it is on screen — is handled by re-reading the rectangle from the store at each step and
/// stopping when it is gone.
/// </para>
/// </remarks>
public sealed class PresentationState(CanvasStore store)
{
    private IReadOnlyList<Slide> _slides = [];

    /// <summary>Raised when the walk starts, moves or stops.</summary>
    public event Action? Changed;

    /// <summary>Whether a walk is running.</summary>
    public bool Active { get; private set; }

    /// <summary>Which stop, from zero. Meaningless while <see cref="Active"/> is false.</summary>
    public int Index { get; private set; }

    /// <summary>How many stops this walk has.</summary>
    public int Count => _slides.Count;

    /// <summary>The stop being shown, or null when no walk is running.</summary>
    public Slide? Current => Active && Index >= 0 && Index < _slides.Count ? _slides[Index] : null;

    /// <summary>Whether there is anything after, or before, the stop being shown.</summary>
    public bool HasNext => Active && Index + 1 < _slides.Count;

    public bool HasPrevious => Active && Index > 0;

    /// <summary>What a board would be walked through as, without starting.</summary>
    public IReadOnlyList<Slide> Preview() => SlideOrder.For(store.Document, NodeWords.Title);

    /// <summary>
    /// Starts a walk, at the stop showing <paramref name="from"/> when it is one of them.
    /// </summary>
    /// <returns>False when the board has nothing to show, in which case nothing changes.</returns>
    /// <remarks>
    /// Starting from the selection is what makes "click a card, then present" do what it looks like
    /// it should. A selected block inside a group starts at that group, because on a board with
    /// groups the groups are the stops.
    /// </remarks>
    public bool Start(Guid? from = null)
    {
        _slides = SlideOrder.For(store.Document, NodeWords.Title);
        if (_slides.Count == 0) return false;

        Active = true;
        Index = IndexOf(from);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Ends the walk. Safe to call when none is running.</summary>
    public void Stop()
    {
        if (!Active) return;
        Active = false;
        _slides = [];
        Index = 0;
        Changed?.Invoke();
    }

    /// <summary>The next stop, or nothing when this is the last. False when nothing moved.</summary>
    public bool Next() => MoveTo(Index + 1);

    public bool Previous() => MoveTo(Index - 1);

    /// <summary>Jumps to a stop by its position, for a counter somebody clicked.</summary>
    public bool MoveTo(int index)
    {
        if (!Active || index < 0 || index >= _slides.Count || index == Index) return false;
        Index = index;
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Where the stop being shown is now, which is not always where it was when the walk began.
    /// </summary>
    /// <remarks>
    /// Read from the store rather than from the slide, so a card moved or resized while the board is
    /// being presented is still framed correctly — and one that has been deleted answers null, which
    /// the editor treats as "stop the walk" rather than fitting the view to a rectangle nothing is in.
    /// </remarks>
    public Core.Geometry.WorldRect? CurrentRect()
    {
        if (Current is not { } slide) return null;

        var rect = slide.IsGroup
            ? store.FindGroup(slide.Id) is { } group ? Core.Geometry.CanvasHitTester.RectOf(group) : (Core.Geometry.WorldRect?)null
            : store.FindNode(slide.Id) is { } node ? Core.Geometry.CanvasHitTester.RectOf(node) : null;

        return rect is { } r
            ? new Core.Geometry.WorldRect(r.X - SlideOrder.Padding, r.Y - SlideOrder.Padding,
                                          r.Width + 2 * SlideOrder.Padding, r.Height + 2 * SlideOrder.Padding)
            : null;
    }

    /// <summary>True when a block is the one being shown, or is inside the group being shown.</summary>
    public bool IsOnStage(CanvasNode node)
    {
        if (Current is not { } slide) return true;
        return slide.IsGroup ? node.GroupId == slide.Id : node.Id == slide.Id;
    }

    private int IndexOf(Guid? from)
    {
        if (from is not { } id) return 0;

        var direct = _slides.ToList().FindIndex(s => s.Id == id);
        if (direct >= 0) return direct;

        // A block was asked for on a board whose stops are groups: start at the group holding it.
        if (store.FindNode(id)?.GroupId is { } groupId)
        {
            var inGroup = _slides.ToList().FindIndex(s => s.Id == groupId);
            if (inGroup >= 0) return inGroup;
        }

        return 0;
    }
}
