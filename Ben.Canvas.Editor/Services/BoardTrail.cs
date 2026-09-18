namespace Ben.Canvas.Editor.Services;

/// <summary>
/// The path somebody took across boards: where they have been, in order, and where a crumb leads.
/// </summary>
/// <remarks>
/// <para><b>Ben, 2026-09-18:</b> "Instead of a back button, what about creating breadcrumbs to
/// navigate." One Back button was ambiguous the moment two links were followed — it left somebody
/// somewhere they could not name. The path says where they are and lets them leave from any point
/// on it.</para>
///
/// <para><b>Pure on purpose.</b> <see cref="BoardLinks"/> needs six collaborators to do its job, so
/// the arithmetic of the path lived somewhere it could not be tested and had no tests at all. This
/// holds the list and nothing else; opening a board stays with the thing that can.</para>
///
/// <para>A path is a WALK, not a hierarchy: following A to B and back to A records all three, because
/// that is what happened and Back has to undo it stop by stop.</para>
/// </remarks>
public sealed class BoardTrail
{
    private readonly List<BoardCrumb> _stops = [];

    /// <summary>Every stop, oldest first. Empty before any link has been followed.</summary>
    public IReadOnlyList<BoardCrumb> Stops => _stops;

    /// <summary>The stop Back would return to, or null when there is nowhere to go back to.</summary>
    public BoardCrumb? Last => _stops.Count == 0 ? null : _stops[^1];

    /// <summary>Records leaving a board. False when there was nothing to record.</summary>
    public bool Push(Guid? serverId, string title)
    {
        if (serverId is not { } id || id == Guid.Empty) return false;

        _stops.Add(new BoardCrumb(id, title));
        return true;
    }

    /// <summary>
    /// Returns to one stop, and answers which board to open.
    /// </summary>
    /// <param name="index">Counted from the start of the path.</param>
    /// <returns>
    /// The stop to open, or null when the index names none — the path is view state, and a stale
    /// click is not worth a refusal.
    /// </returns>
    /// <remarks>
    /// Everything from <paramref name="index"/> onwards is dropped, the named stop included: it
    /// becomes the board now open, so it is no longer somewhere to go back TO.
    /// </remarks>
    public BoardCrumb? GoTo(int index)
    {
        if (index < 0 || index >= _stops.Count) return null;

        var stop = _stops[index];
        _stops.RemoveRange(index, _stops.Count - index);
        return stop;
    }

    /// <summary>Forgets the path. False when there was nothing to forget.</summary>
    public bool Clear()
    {
        if (_stops.Count == 0) return false;

        _stops.Clear();
        return true;
    }
}
