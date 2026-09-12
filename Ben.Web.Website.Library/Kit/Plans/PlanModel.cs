using Ben.Data.Common.Enums;
using Ben.Service.Models.Entities;

namespace Ben.Web.Website.Library.Kit.Plans;

/// <summary>
/// One thing on a plan while it is being edited — a room the event is offering, or a seat.
/// </summary>
/// <remarks>
/// <para>A record, so every edit is a copy and a snapshot is free. That is what makes undo a stack
/// of lists rather than a log of reversible operations, which is the version people get wrong.</para>
/// </remarks>
/// <param name="Id">
/// The server's id once it has one; null while it is only on this screen. Sent back so that
/// renaming a seat is a rename rather than a delete and a create, which the server refuses when a
/// party is confirmed into it.
/// </param>
/// <param name="Key">
/// This screen's own handle on the unit, stable from the moment it appears. Needed because a new
/// unit has no <paramref name="Id"/> and something still has to say which square was clicked.
/// </param>
/// <param name="DisplayName">
/// What to draw in the square. For a room it is the venue's own name for it, which the server
/// resolves; for a seat it is the label.
/// </param>
public sealed record PlanUnit(
    Guid Key,
    Guid? Id = null,
    Guid? PlaceRoomId = null,
    string? Label = null,
    string? Section = null,
    int? Capacity = null,
    decimal? Price = null,
    string? Note = null,
    int? Row = null,
    int? Column = null,
    string DisplayName = "")
{
    /// <summary>Whether it has been given a square on the plan.</summary>
    public bool IsPlaced => Row is not null && Column is not null;

    /// <summary>What a person calls it: its label, else the venue's name for the room behind it.</summary>
    public string Name => Label?.Trim() is { Length: > 0 } l ? l
                        : DisplayName is { Length: > 0 } d ? d
                        : "That space";
}

/// <summary>
/// The plan being edited: what is on it, where, and every way it can change (item 235 phase 2).
/// </summary>
/// <remarks>
/// <para><b>Pure, and separate from anything that draws.</b> Laying out four hundred seats is
/// arithmetic — which square, which label, what happens to the columns right of an inserted aisle
/// — and arithmetic is worth being certain about without a browser in the way. Everything here is
/// testable in milliseconds, and the component above it only turns this into squares.</para>
///
/// <para><b>Undo is a stack of whole snapshots</b>, capped, rather than a log of reversible
/// operations. A list of four hundred small records costs nothing to copy, and the alternative —
/// every operation knowing how to undo itself — is the version that eventually cannot undo an
/// insert-aisle correctly.</para>
/// </remarks>
public sealed class PlanModel
{
    /// <summary>How many steps back a person may go.</summary>
    /// <remarks>Fifty is more than anybody uses in one sitting and small enough to be free.</remarks>
    public const int UndoDepth = 50;

    private List<PlanUnit> _units;
    private readonly List<List<PlanUnit>> _undo = [];
    private readonly List<List<PlanUnit>> _redo = [];
    private readonly List<PlanUnit> _saved;

    public PlanModel(HostedEventLayoutKind kind, IEnumerable<PlanUnit>? units = null)
    {
        Kind = kind;
        _units = [.. units ?? []];
        _saved = [.. _units];
    }

    public HostedEventLayoutKind Kind { get; private set; }

    public IReadOnlyList<PlanUnit> Units => _units;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Whether anything has changed since the last save.</summary>
    /// <remarks>
    /// Compared against the saved snapshot rather than counted from the undo stack, so undoing
    /// back to where you started correctly reports nothing to save — which is what stops the
    /// leaving-the-page warning firing at somebody who has changed nothing.
    /// </remarks>
    public bool IsDirty => !_units.SequenceEqual(_saved);

    /// <summary>The highest row and column anything sits in, or −1 when the plan is empty.</summary>
    public int LastRow => _units.Where(u => u.Row is not null).Select(u => u.Row!.Value).DefaultIfEmpty(-1).Max();

    /// <inheritdoc cref="LastRow"/>
    public int LastColumn => _units.Where(u => u.Column is not null).Select(u => u.Column!.Value).DefaultIfEmpty(-1).Max();

    /// <summary>Everything nobody has placed yet, in the order it was added.</summary>
    /// <remarks>
    /// Its own list because the designer shows it as a tray. A unit with no square is not a
    /// mistake — a venue may describe a cellar it never draws — so nothing here nags about it.
    /// </remarks>
    public IReadOnlyList<PlanUnit> Unplaced => [.. _units.Where(u => !u.IsPlaced)];

    /// <summary>The unit in a square, or null when it is empty.</summary>
    public PlanUnit? At(int row, int column)
        => _units.FirstOrDefault(u => u.Row == row && u.Column == column);

    public PlanUnit? ByKey(Guid key) => _units.FirstOrDefault(u => u.Key == key);

    /// <summary>Every section named on the plan, in the order the units were arranged.</summary>
    public IReadOnlyList<string> Sections =>
        [.. _units.Where(u => !string.IsNullOrWhiteSpace(u.Section))
                  .Select(u => u.Section!.Trim())
                  .Distinct(StringComparer.OrdinalIgnoreCase)];

    // ── changing it ──────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a block of seats: rows of them, labelled and numbered as the venue names them.
    /// </summary>
    /// <returns>The keys of the seats it made, so the designer can select them at once.</returns>
    /// <remarks>
    /// The whole reason a four-hundred-seat house takes five minutes rather than an afternoon.
    /// Rows land below whatever is already on the plan unless a row is named, so Stalls then
    /// Balcony is two presses of one button.
    /// </remarks>
    public IReadOnlyList<Guid> AddBlock(
        int rows, int seatsPerRow, SeatNumbering numbering = SeatNumbering.LeftToRight,
        int startAt = 1, string? pattern = null, string? section = null, decimal? price = null,
        int? atRow = null, int atColumn = 0)
    {
        if (rows <= 0 || seatsPerRow <= 0) return [];
        if (rows * seatsPerRow > PlanLabels.MaximumUnitsPerBlock) return [];

        Remember();

        var firstRow = atRow ?? LastRow + 1;
        var numbers = PlanLabels.SeatNumbers(seatsPerRow, numbering, startAt);
        var made = new List<Guid>();

        for (var r = 0; r < rows; r++)
        {
            var rowName = PlanLabels.RowName(firstRow + r);
            for (var c = 0; c < seatsPerRow; c++)
            {
                var unit = new PlanUnit(
                    Key: Guid.NewGuid(),
                    Label: PlanLabels.Format(pattern, rowName, numbers[c]),
                    Section: Trimmed(section),
                    // A seat holds one person. The server writes 1 whatever is sent, and saying so
                    // here too keeps the designer's own counts honest before anything is saved.
                    Capacity: 1,
                    Price: price,
                    Row: firstRow + r,
                    Column: atColumn + c);
                _units.Add(unit);
                made.Add(unit.Key);
            }
        }
        return made;
    }

    /// <summary>Puts a room on the plan, or moves one already on it.</summary>
    public void Place(Guid key, int row, int column)
    {
        var moving = ByKey(key);
        if (moving is null) return;

        Remember();

        // Whatever was in that square swaps out to where this one came from, which on a first
        // placement is the tray. Refusing instead would make a full plan unrearrangeable.
        if (At(row, column) is { } occupant && occupant.Key != key)
            Replace(occupant with { Row = moving.Row, Column = moving.Column });

        Replace(moving with { Row = row, Column = column });
    }

    /// <summary>Takes units off the plan and back into the tray, keeping everything else about them.</summary>
    public void Unplace(IEnumerable<Guid> keys)
    {
        var set = keys.ToHashSet();
        if (set.Count == 0) return;

        Remember();
        for (var i = 0; i < _units.Count; i++)
            if (set.Contains(_units[i].Key))
                _units[i] = _units[i] with { Row = null, Column = null };
    }

    /// <summary>Removes units from the plan entirely.</summary>
    public void Remove(IEnumerable<Guid> keys)
    {
        var set = keys.ToHashSet();
        if (set.Count == 0) return;

        Remember();
        _units.RemoveAll(u => set.Contains(u.Key));
    }

    /// <summary>
    /// Opens a gap before a column, shifting everything to its right one square further out.
    /// </summary>
    /// <remarks>
    /// This is the aisle, and it is why the plan is a grid of positions rather than a list in
    /// order. Without it, adding a walkway down the middle of a built house means moving two
    /// hundred seats by hand.
    /// </remarks>
    public void InsertColumn(int before)
    {
        Remember();
        for (var i = 0; i < _units.Count; i++)
            if (_units[i].Column is { } c && c >= before)
                _units[i] = _units[i] with { Column = c + 1 };
    }

    /// <summary>Closes a gap, pulling everything to its right one square back.</summary>
    /// <remarks>Refused when the column is occupied: that would put two units in one square.</remarks>
    public bool RemoveColumn(int column)
    {
        if (_units.Any(u => u.Column == column)) return false;

        Remember();
        for (var i = 0; i < _units.Count; i++)
            if (_units[i].Column is { } c && c > column)
                _units[i] = _units[i] with { Column = c - 1 };
        return true;
    }

    /// <summary>Moves a selection by a number of rows and columns, if the whole of it can go.</summary>
    /// <remarks>
    /// All or nothing, and never onto anything: half a moved selection is a plan somebody has to
    /// unpick square by square.
    /// </remarks>
    public bool Move(IEnumerable<Guid> keys, int rowDelta, int columnDelta)
    {
        var moving = keys.ToHashSet();
        var placed = _units.Where(u => moving.Contains(u.Key) && u.IsPlaced).ToList();
        if (placed.Count == 0) return false;

        if (placed.Any(u => u.Row!.Value + rowDelta < 0 || u.Column!.Value + columnDelta < 0))
            return false;

        var destinations = placed
            .Select(u => (Row: u.Row!.Value + rowDelta, Column: u.Column!.Value + columnDelta))
            .ToHashSet();

        var blocked = _units.Any(u => u.IsPlaced
                                   && !moving.Contains(u.Key)
                                   && destinations.Contains((u.Row!.Value, u.Column!.Value)));
        if (blocked) return false;

        Remember();
        for (var i = 0; i < _units.Count; i++)
            if (moving.Contains(_units[i].Key) && _units[i].IsPlaced)
                _units[i] = _units[i] with
                {
                    Row = _units[i].Row!.Value + rowDelta,
                    Column = _units[i].Column!.Value + columnDelta,
                };
        return true;
    }

    /// <summary>Changes one thing about a selection, leaving everything else alone.</summary>
    /// <remarks>
    /// One method with optional parts rather than five, because the selection bar sets one at a
    /// time and every one of them is the same shape: remember, map the chosen units, keep the rest.
    /// </remarks>
    public void Apply(
        IEnumerable<Guid> keys,
        string? section = null, bool clearSection = false,
        decimal? price = null, bool clearPrice = false,
        string? note = null, bool clearNote = false,
        int? capacity = null, bool clearCapacity = false)
    {
        var set = keys.ToHashSet();
        if (set.Count == 0) return;

        Remember();
        for (var i = 0; i < _units.Count; i++)
        {
            if (!set.Contains(_units[i].Key)) continue;
            var u = _units[i];

            if (clearSection) u = u with { Section = null };
            else if (section is not null) u = u with { Section = Trimmed(section) };

            if (clearPrice) u = u with { Price = null };
            else if (price is not null) u = u with { Price = price };

            if (clearNote) u = u with { Note = null };
            else if (note is not null) u = u with { Note = Trimmed(note) };

            // A seat is always one, whatever a selection bar was told.
            if (Kind == HostedEventLayoutKind.Seats) u = u with { Capacity = 1 };
            else if (clearCapacity) u = u with { Capacity = null };
            else if (capacity is not null) u = u with { Capacity = capacity };

            _units[i] = u;
        }
    }

    /// <summary>Renames a selection of seats by pattern, in the order they are drawn.</summary>
    /// <remarks>
    /// Row and seat come from where each one SITS, not from what it was called, so relabelling a
    /// block that has been moved or had an aisle pushed through it produces the labels the venue
    /// would now paint on the chairs.
    /// </remarks>
    public void Relabel(IEnumerable<Guid> keys, string? pattern, SeatNumbering numbering, int startAt = 1)
    {
        var set = keys.ToHashSet();
        var chosen = _units.Where(u => set.Contains(u.Key) && u.IsPlaced).ToList();
        if (chosen.Count == 0) return;

        Remember();

        foreach (var row in chosen.GroupBy(u => u.Row!.Value).OrderBy(g => g.Key))
        {
            var inRow = row.OrderBy(u => u.Column!.Value).ToList();
            var numbers = PlanLabels.SeatNumbers(inRow.Count, numbering, startAt);
            var rowName = PlanLabels.RowName(row.Key);

            for (var i = 0; i < inRow.Count; i++)
                Replace(inRow[i] with { Label = PlanLabels.Format(pattern, rowName, numbers[i]) });
        }
    }

    /// <summary>Adds a room the venue has described, unplaced, unless it is already here.</summary>
    public Guid? AddRoom(Guid placeRoomId, string displayName, int? capacity)
    {
        if (_units.Any(u => u.PlaceRoomId == placeRoomId)) return null;

        Remember();
        var unit = new PlanUnit(
            Key: Guid.NewGuid(), PlaceRoomId: placeRoomId, DisplayName: displayName,
            Capacity: capacity);
        _units.Add(unit);
        return unit.Key;
    }

    /// <summary>
    /// Lays every unplaced room out, one section per row, left to right.
    /// </summary>
    /// <remarks>
    /// The button that turns "I have described twenty rooms" into a plan without twenty clicks.
    /// Rooms with no floor named share the last row, because a venue that has not said is not
    /// asking for a row of its own.
    /// </remarks>
    public void AutoArrange(Func<PlanUnit, string?> sectionOf)
    {
        var loose = _units.Where(u => !u.IsPlaced).ToList();
        if (loose.Count == 0) return;

        Remember();

        var row = LastRow + 1;
        foreach (var group in loose.GroupBy(u => sectionOf(u) ?? "").OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var column = 0;
            foreach (var unit in group)
                Replace(unit with { Row = row, Column = column++ });
            row++;
        }
    }

    /// <summary>Changes what the plan allocates, which is only allowed while it is empty.</summary>
    /// <remarks>
    /// The server refuses a kind change once anything is confirmed into the plan, and refusing an
    /// occupied one here as well means the designer never offers a change it knows will bounce.
    /// </remarks>
    public bool ChangeKind(HostedEventLayoutKind kind)
    {
        if (kind == Kind) return true;
        if (_units.Count > 0) return false;

        Remember();
        Kind = kind;
        return true;
    }

    // ── undo, redo, saving ───────────────────────────────────────────────────

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Add([.. _units]);
        _units = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Add([.. _units]);
        _units = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
    }

    /// <summary>What the server is sent: the whole plan, in the order it is drawn.</summary>
    /// <remarks>
    /// <para>Position in the list is the sort order, which is the contract the endpoint documents.
    /// Placed units come first in reading order — top row left to right — and the tray follows, so
    /// a plan reads down the page the way a person reads it.</para>
    ///
    /// <para><b>A position is sent as a pair or not at all.</b> Half of one is not a place on a
    /// plan, and the server would store a unit its own designer could never find again.</para>
    /// </remarks>
    public IReadOnlyList<HostedEventLayoutUnitChoice> ToChoices()
        => [.. _units
            .OrderBy(u => u.IsPlaced ? 0 : 1)
            .ThenBy(u => u.Row ?? 0)
            .ThenBy(u => u.Column ?? 0)
            .Select(u => new HostedEventLayoutUnitChoice(
                Id: u.Id,
                PlaceRoomId: Kind == HostedEventLayoutKind.Rooms ? u.PlaceRoomId : null,
                Label: Kind == HostedEventLayoutKind.Seats ? u.Label : null,
                Section: u.Section,
                Capacity: u.Capacity,
                Price: u.Price,
                Note: u.Note,
                LayoutRow: u.IsPlaced ? u.Row : null,
                LayoutColumn: u.IsPlaced ? u.Column : null))];

    /// <summary>Takes what the server sent back as the new starting point, so nothing reads dirty.</summary>
    public void Saved(IEnumerable<PlanUnit> units)
    {
        _units = [.. units];
        _saved.Clear();
        _saved.AddRange(_units);
        _undo.Clear();
        _redo.Clear();
    }

    // ── the work ─────────────────────────────────────────────────────────────

    private void Remember()
    {
        _undo.Add([.. _units]);
        if (_undo.Count > UndoDepth) _undo.RemoveAt(0);
        // A new edit ends the redo road, which is what everybody expects and nobody says.
        _redo.Clear();
    }

    private void Replace(PlanUnit unit)
    {
        var at = _units.FindIndex(u => u.Key == unit.Key);
        if (at >= 0) _units[at] = unit;
    }

    private static string? Trimmed(string? value)
        => value?.Trim() is { Length: > 0 } v ? v : null;
}
