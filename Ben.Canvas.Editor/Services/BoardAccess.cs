namespace Ben.Canvas.Editor.Services;

/// <summary>
/// Whether the open board may be changed (R33). People who can read a case but not edit it get a view-only
/// board: pan, zoom, select, read, open links and export, but no editing, pasting, saving or publishing.
/// </summary>
/// <remarks>
/// The server stays the authority - every write is checked there. This only stops the screen from letting
/// somebody spend an hour on changes the server will refuse. A board kept only on this device is always editable.
/// </remarks>
public sealed class BoardAccess
{
    public bool CanEdit { get; private set; } = true;

    /// <summary>Why the board is view-only, in a sentence, or null while it is editable.</summary>
    public string? Reason { get; private set; }

    /// <summary>
    /// True when this person may add to the board but not rework what is already on it (Ben, 2026-09-16). What they
    /// add is theirs; everything that was already there belongs to whoever put it there.
    /// </summary>
    public bool AddOnly { get; private set; }

    private HashSet<Guid> _others = [];

    /// <summary>
    /// The pieces somebody else put on the board. Kept this way round, rather than as "mine", so that anything added
    /// in this session is theirs to move without waiting for the server to say so.
    /// </summary>
    public IReadOnlySet<Guid> OtherPeoplesPieces => _others;

    /// <summary>What to say when somebody tries to change a piece that is not theirs.</summary>
    public const string NotYours =
        "That piece is somebody else's. You can add to this board; changing what is already on it is for whoever put "
        + "it there, a group administrator, or an administrator of the site.";

    public event Action? Changed;

    public void Set(bool canEdit, string? reason = null)
    {
        var why = canEdit ? null : reason ?? Core.Text.CanvasCopy.Sentences.ViewOnly;
        if (CanEdit == canEdit && Reason == why && !AddOnly) return;
        CanEdit = canEdit;
        Reason = why;
        AddOnly = false;
        _others = [];
        Changed?.Invoke();
    }

    /// <summary>The board may be added to; the pieces named here belong to other people and stay as they are.</summary>
    public void SetAddOnly(IEnumerable<Guid> otherPeoplesPieces)
    {
        CanEdit = true;
        Reason = null;
        AddOnly = true;
        _others = [.. otherPeoplesPieces];
        Changed?.Invoke();
    }

    /// <summary>
    /// May this person change this piece? A board they may edit outright answers yes to everything; adding to
    /// somebody else's board answers yes only for what they put there; a piece that is not on the board yet is
    /// theirs to place.
    /// </summary>
    public bool MayChange(Guid pieceId)
        => CanEdit && (!AddOnly || !_others.Contains(pieceId));
}
