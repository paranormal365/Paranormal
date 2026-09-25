using Ben.Canvas.Core.Commands;
using Ben.Canvas.Core.Geometry;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Text;

namespace Ben.Canvas.Editor.Services;

/// <summary>
/// One stop on the way back: a board that was open, and what it was called.
/// </summary>
public readonly record struct BoardCrumb(Guid ServerId, string Title);

/// <summary>
/// Following a link from one board to another, and finding the way back.
/// </summary>
/// <remarks>
/// <para><b>Ben, 2026-09-18:</b> "create a link to open the other page to one of the cards… and a back
/// button to go back to the original page."</para>
///
/// <para><b>Only published boards are linkable, and that is held in three places</b>, because a picker
/// alone only stops the state being created: the picker lists published boards only, the server
/// refuses to publish a board that links to a draft, and following a link opens the target's
/// PUBLISHED copy — for the author too, so what they check is what a reader gets.</para>
///
/// <para><b>The trail is view state and is never stored on the board.</b> Where somebody came from is
/// a fact about this session, not about the document; writing it into the board would put one reader's
/// navigation into everybody's file, and the additive guard would rightly refuse the save.</para>
///
/// <para><b>A missing target is handled twice over</b> — once at rest, so a dead card looks dead, and
/// once on the press, so a card that went stale a moment ago still says why rather than doing nothing.
/// The rule underneath it is that nothing on THIS board is rewritten when a card disappears from
/// another one: the link degrades where it is followed, in front of the person who followed it.</para>
/// </remarks>
public sealed class BoardLinks(
    CanvasStore store,
    CanvasDocumentStore documents,
    CanvasServerSession session,
    ICanvasServerStore server,
    BcToastService toasts,
    SelectionState selection,
    CanvasViewportState viewport)
{
    private readonly BoardTrail _trail = new();
    private readonly HashSet<Guid> _published = [];
    private readonly HashSet<Guid> _missing = [];
    private Guid? _knownForCase;

    /// <summary>Where Back would go, or null when there is nowhere to go back to.</summary>
    public BoardCrumb? Back => _trail.Last;

    /// <summary>
    /// Every board a link was followed from, oldest first — the path to where you are now.
    /// </summary>
    /// <remarks>
    /// <b>Ben, 2026-09-18:</b> "Instead of a back button, what about creating breadcrumbs to navigate."
    /// The trail was always the whole path; only its last stop was ever shown, which is ambiguous the
    /// moment somebody follows two links — one Back leaves them somewhere they cannot name. The path
    /// says where they are and lets them leave from any point on it.
    /// </remarks>
    public IReadOnlyList<BoardCrumb> Trail => _trail.Stops;

    /// <summary>Raised when the trail changes, so the header can redraw its breadcrumbs.</summary>
    public event Action? Changed;

    /// <summary>Asks the picker to open for this card. Set by the editor, which owns the dialog.</summary>
    public Func<Guid, Task>? Picker { get; set; }

    /// <summary>
    /// Whether a board is known to have gone. Unknown reads as present on purpose: a card greyed out
    /// because a request has not come back yet is worse than one that says "gone" a moment late.
    /// </summary>
    public bool IsKnownMissing(Guid documentId) => _missing.Contains(documentId);

    /// <summary>The published boards on this case, refreshed once per case per session.</summary>
    public async Task<IReadOnlyList<CanvasServerSummary>> PublishedAsync(CancellationToken ct = default)
    {
        if (store.Document.CaseId is not { } caseId) return [];

        var (items, _) = await server.ListAsync(caseId, ct);
        if (items is null) return [];

        var published = items.Where(b => b.PublishedAtUtc is not null).ToList();

        _knownForCase = caseId;
        _published.Clear();
        foreach (var board in published) _published.Add(board.Id);

        // Anything a card on this board points at that is not in that list has gone.
        _missing.Clear();
        foreach (var link in Links().Where(l => l.DocumentId != Guid.Empty && !_published.Contains(l.DocumentId)))
            _missing.Add(link.DocumentId);

        Changed?.Invoke();
        return published;
    }

    /// <summary>Opens the picker for one board card.</summary>
    public Task PickAsync(Guid nodeId) => Picker?.Invoke(nodeId) ?? Task.CompletedTask;

    /// <summary>Says why a dead card goes nowhere, without changing anything.</summary>
    public void SayMissing(BoardData link) =>
        toasts.Warning(CanvasCopy.Sentences.LinkedBoardGone(link.Title));

    /// <summary>
    /// Follows a card: saves this board, opens the target's published copy, and remembers the way back.
    /// </summary>
    public async Task FollowAsync(BoardData link, CancellationToken ct = default)
    {
        if (link.DocumentId == Guid.Empty) return;

        if (IsKnownMissing(link.DocumentId))
        {
            SayMissing(link);
            return;
        }

        // Saved first, or following a link would lose whatever was just typed.
        await documents.SaveAsync();

        var here = documents.CurrentServerId;
        var hereTitle = store.Document.Title;

        if (await session.OpenPublishedAsync(link.DocumentId, ct) is { } problem)
        {
            // The one way a target can vanish is deletion, and Delete warns rather than refusing —
            // so this is expected, not exceptional. Say so and leave the trail alone.
            _missing.Add(link.DocumentId);
            Changed?.Invoke();
            toasts.Warning(CanvasCopy.Sentences.LinkedBoardGone(link.Title));
            return;
        }

        _trail.Push(here, hereTitle);
        Changed?.Invoke();

        FocusOn(link);
    }

    /// <summary>Goes back one stop, to the board that was open before.</summary>
    public async Task GoBackAsync(CancellationToken ct = default)
    {
        await GoToAsync(_trail.Stops.Count - 1, ct);
    }

    /// <summary>
    /// Goes back to one stop on the path, dropping everything after it.
    /// </summary>
    /// <param name="index">
    /// Which crumb, counted from the start of the path. Out of range does nothing: the path is view
    /// state, and a stale click is not worth a refusal.
    /// </param>
    public async Task GoToAsync(int index, CancellationToken ct = default)
    {
        if (_trail.GoTo(index) is not { } crumb) return;

        Changed?.Invoke();

        await documents.SaveAsync();

        if (await session.OpenAsync(crumb.ServerId, ct) is { } problem)
            toasts.Warning(problem);
    }

    /// <summary>Forgets the trail, which belongs to one case's worth of reading.</summary>
    public void Clear()
    {
        if (_trail.Stops.Count == 0 && _missing.Count == 0) return;
        _trail.Clear();
        _missing.Clear();
        _knownForCase = null;
        Changed?.Invoke();
    }

    /// <summary>Whether the list has been read for the case now open.</summary>
    public bool KnowsThisCase => _knownForCase is { } id && id == store.Document.CaseId;

    private IEnumerable<BoardData> Links() =>
        store.Document.Nodes.Select(n => n.Data).OfType<BoardData>();

    /// <summary>
    /// Selects and frames the card the link named, or fits the whole board when it named none — and
    /// when the card it named has gone.
    /// </summary>
    /// <remarks>
    /// Ben, 2026-09-18: "if there is a link to a card in a different page, and the card has been
    /// removed, default to opening the other page and not focusing in on that card." A missing card
    /// is not an error; it is a board opened at its full extent with nothing selected.
    /// </remarks>
    private void FocusOn(BoardData link)
    {
        selection.Clear();

        if (link.NodeId is { } wanted && store.FindNode(wanted) is { } node)
        {
            selection.Select(node.Id);
            viewport.Fit(CanvasHitTester.RectOf(node));
            return;
        }

        if (link.NodeId is not null) toasts.Info(CanvasCopy.Sentences.LinkedCardGone);

        var bounds = WorldRect.Bounds(store.Document.Nodes.Select(CanvasHitTester.RectOf)
            .Concat(store.Document.Groups.Select(CanvasHitTester.RectOf)));
        if (!bounds.IsEmpty) viewport.Fit(bounds);
    }
}
