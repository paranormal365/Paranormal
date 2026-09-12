using Ben.Data.Common.Enums;
using Ben.Web.Website.Library.Kit.Plans;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Naming rows and seats the way a venue already names them (item 235 phase 2).
/// </summary>
/// <remarks>
/// A plan whose labels do not match the brass numbers on the seats is worse than no plan: it sends
/// people confidently to the wrong chair. These are the rules a venue will check against its own
/// tickets on the first afternoon.
/// </remarks>
public sealed class PlanLabelsTests
{
    [Theory]
    [InlineData(0, "A")]
    [InlineData(7, "H")]
    [InlineData(8, "J")]   // I is skipped
    [InlineData(12, "N")]
    [InlineData(13, "P")]  // O is skipped
    [InlineData(23, "Z")]
    [InlineData(24, "AA")]
    [InlineData(25, "AB")]
    [InlineData(47, "AZ")]
    [InlineData(48, "BA")]
    public void Rows_are_named_without_the_two_letters_that_read_as_digits(int index, string expected)
        // On a printed ticket and a brass rail, I is a 1 and O is a 0. Dropping both is cheaper
        // than every usher in the building explaining it for the life of the venue.
        => Assert.Equal(expected, PlanLabels.RowName(index));

    [Fact]
    public void A_row_name_reads_back_to_the_same_position()
    {
        // A venue types "start at row C" and means it.
        for (var i = 0; i < 200; i++)
            Assert.Equal(i, PlanLabels.RowIndex(PlanLabels.RowName(i)));

        Assert.Null(PlanLabels.RowIndex("I"));
        Assert.Null(PlanLabels.RowIndex("4"));
        Assert.Null(PlanLabels.RowIndex(""));
    }

    [Fact]
    public void Seats_count_left_to_right_by_default()
        => Assert.Equal([1, 2, 3, 4, 5], PlanLabels.SeatNumbers(5, SeatNumbering.LeftToRight));

    [Fact]
    public void A_house_that_numbers_from_the_prompt_side_counts_the_other_way()
        => Assert.Equal([5, 4, 3, 2, 1], PlanLabels.SeatNumbers(5, SeatNumbering.RightToLeft));

    [Fact]
    public void Odd_and_even_grow_outward_from_the_centre_aisle()
    {
        // The arrangement most proscenium theatres use, and the one that surprises everybody:
        // seat 1 and seat 2 are next to each other, either side of the middle.
        var row = PlanLabels.SeatNumbers(10, SeatNumbering.OddEvenFromCentre);

        Assert.Equal([9, 7, 5, 3, 1, 2, 4, 6, 8, 10], row);
    }

    [Fact]
    public void An_odd_width_row_gives_its_middle_seat_the_first_even_number()
    {
        // The compromise every odd-width house makes; worth pinning so nobody "fixes" it later.
        Assert.Equal([5, 3, 1, 2, 4, 6, 8], PlanLabels.SeatNumbers(7, SeatNumbering.OddEvenFromCentre));
    }

    [Fact]
    public void A_row_can_start_at_a_number_that_is_not_one()
    {
        // A balcony that continues the stalls' numbering rather than starting again.
        Assert.Equal([101, 102, 103], PlanLabels.SeatNumbers(3, SeatNumbering.LeftToRight, startAt: 101));
    }

    [Theory]
    [InlineData("{row}{seat}", "C4")]
    [InlineData("{row}-{seat}", "C-4")]
    [InlineData("Row {row}, Seat {seat}", "Row C, Seat 4")]
    [InlineData(null, "C4")]
    public void A_seat_is_labelled_the_way_the_venue_writes_it(string? pattern, string expected)
        => Assert.Equal(expected, PlanLabels.Format(pattern, "C", 4));

    [Fact]
    public void The_designer_can_say_what_a_block_will_be_before_it_makes_it()
    {
        // Somebody about to create 260 things should be shown the first and the last of them
        // first; it is the cheapest way to catch a wrong starting row or backwards numbering.
        var line = PlanLabels.Describe(
            firstRowIndex: 2, rows: 13, seatsPerRow: 20,
            SeatNumbering.LeftToRight, startAt: 1, pattern: "{row}{seat}");

        Assert.Equal("13 rows × 20 = 260 seats, C1 … Q20", line);
    }

    [Fact]
    public void A_block_too_big_to_be_meant_says_so_in_the_same_line()
    {
        var line = PlanLabels.Describe(0, 500, 50, SeatNumbering.LeftToRight, 1, null);

        Assert.Contains("25000 seats", line);
        Assert.Contains("too many at once", line);
    }
}

/// <summary>
/// The plan being edited: what a block makes, what an aisle moves, and what undo puts back.
/// </summary>
/// <remarks>
/// <para>Laying out four hundred seats is arithmetic, and arithmetic is worth being certain about
/// without a browser in the way. Every rule here is one the designer above it only draws.</para>
/// </remarks>
public sealed class PlanModelTests
{
    private static PlanModel Seats() => new(HostedEventLayoutKind.Seats);
    private static PlanModel Rooms() => new(HostedEventLayoutKind.Rooms);

    // ── building a house ─────────────────────────────────────────────────────

    [Fact]
    public void A_block_makes_a_labelled_seat_in_every_square()
    {
        var plan = Seats();

        var made = plan.AddBlock(rows: 13, seatsPerRow: 20, section: "Stalls", price: 24m);

        Assert.Equal(260, made.Count);
        Assert.Equal(260, plan.Units.Count);
        Assert.Equal("A1", plan.At(0, 0)!.Name);
        Assert.Equal("N20", plan.At(12, 19)!.Name);
        // A seat holds one person, said here as well as by the server, so the designer's own
        // counts are honest before anything is saved.
        Assert.All(plan.Units, u => Assert.Equal(1, u.Capacity));
        Assert.All(plan.Units, u => Assert.Equal("Stalls", u.Section));
        Assert.All(plan.Units, u => Assert.Equal(24m, u.Price));
    }

    [Fact]
    public void A_second_block_lands_below_the_first_so_two_presses_build_two_sections()
    {
        var plan = Seats();
        plan.AddBlock(2, 4, section: "Stalls");

        plan.AddBlock(2, 4, section: "Balcony", price: 18m);

        Assert.Equal(4, plan.LastRow + 1);
        Assert.Equal("Stalls", plan.At(0, 0)!.Section);
        Assert.Equal("Balcony", plan.At(2, 0)!.Section);
        Assert.Equal(["Stalls", "Balcony"], plan.Sections);
    }

    [Fact]
    public void A_block_nobody_could_have_meant_is_refused_rather_than_made()
    {
        var plan = Seats();

        Assert.Empty(plan.AddBlock(500, 50));
        Assert.Empty(plan.Units);
        Assert.False(plan.CanUndo);   // and it left no step to undo
    }

    // ── the aisle ────────────────────────────────────────────────────────────

    [Fact]
    public void An_aisle_shifts_only_what_is_to_its_right()
    {
        // This is why the plan is a grid of positions rather than a list in order: without it,
        // adding a walkway down the middle of a built house means moving two hundred seats.
        var plan = Seats();
        plan.AddBlock(1, 6);
        var left = plan.At(0, 2)!.Key;
        var right = plan.At(0, 3)!.Key;

        plan.InsertColumn(before: 3);

        Assert.Equal(2, plan.ByKey(left)!.Column);
        Assert.Equal(4, plan.ByKey(right)!.Column);
        Assert.Null(plan.At(0, 3));   // the gap is genuinely empty
    }

    [Fact]
    public void A_gap_can_be_closed_but_never_one_with_seats_in_it()
    {
        var plan = Seats();
        plan.AddBlock(1, 4);
        plan.InsertColumn(before: 2);

        Assert.False(plan.RemoveColumn(1));   // occupied: closing it would stack two in one square
        Assert.True(plan.RemoveColumn(2));
        Assert.Equal(4, plan.Units.Count(u => u.IsPlaced));
        Assert.Equal([0, 1, 2, 3], plan.Units.Select(u => u.Column!.Value).Order());
    }

    // ── moving a selection ───────────────────────────────────────────────────

    [Fact]
    public void A_selection_moves_whole_or_not_at_all()
    {
        var plan = Seats();
        plan.AddBlock(1, 4);
        var first = plan.At(0, 0)!.Key;
        var second = plan.At(0, 1)!.Key;

        // Onto seats that are not moving: refused, because half a moved selection is a plan
        // somebody has to unpick square by square.
        Assert.False(plan.Move([first, second], rowDelta: 0, columnDelta: 2));
        Assert.Equal(0, plan.ByKey(first)!.Column);

        Assert.True(plan.Move([first, second], rowDelta: 1, columnDelta: 0));
        Assert.Equal(1, plan.ByKey(first)!.Row);
    }

    [Fact]
    public void A_selection_never_moves_off_the_top_or_the_left_edge()
    {
        var plan = Seats();
        plan.AddBlock(1, 2);
        var key = plan.At(0, 0)!.Key;

        Assert.False(plan.Move([key], -1, 0));
        Assert.False(plan.Move([key], 0, -1));
    }

    // ── relabelling ──────────────────────────────────────────────────────────

    [Fact]
    public void Relabelling_reads_where_seats_now_sit_not_what_they_used_to_be_called()
    {
        // A block that has been moved down, then relabelled, gets the labels the venue would now
        // paint on the chairs.
        var plan = Seats();
        plan.AddBlock(1, 3);
        var keys = plan.Units.Select(u => u.Key).ToList();
        plan.Move(keys, rowDelta: 2, columnDelta: 0);

        plan.Relabel(keys, "{row}{seat}", SeatNumbering.LeftToRight);

        Assert.Equal("C1", plan.At(2, 0)!.Name);
        Assert.Equal("C3", plan.At(2, 2)!.Name);
    }

    [Fact]
    public void Relabelling_after_an_aisle_numbers_the_row_as_it_now_reads()
    {
        var plan = Seats();
        plan.AddBlock(1, 4);
        plan.InsertColumn(before: 2);
        var keys = plan.Units.Select(u => u.Key).ToList();

        plan.Relabel(keys, "{row}{seat}", SeatNumbering.LeftToRight);

        // Four seats either side of a gap are still seats 1 to 4; the gap is not a seat.
        Assert.Equal("A1", plan.At(0, 0)!.Name);
        Assert.Equal("A2", plan.At(0, 1)!.Name);
        Assert.Equal("A3", plan.At(0, 3)!.Name);
        Assert.Equal("A4", plan.At(0, 4)!.Name);
    }

    // ── rooms ────────────────────────────────────────────────────────────────

    [Fact]
    public void A_room_arrives_in_the_tray_and_only_once()
    {
        var plan = Rooms();
        var room = Guid.NewGuid();

        var key = plan.AddRoom(room, "Blue Room", capacity: 2);

        Assert.NotNull(key);
        Assert.Single(plan.Unplaced);
        Assert.Equal("Blue Room", plan.Unplaced[0].Name);
        // Offering the same room twice would make "is the Blue Room free" a question with two
        // answers, and the server refuses it too.
        Assert.Null(plan.AddRoom(room, "Blue Room", 2));
        Assert.Single(plan.Units);
    }

    [Fact]
    public void Placing_onto_an_occupied_square_swaps_rather_than_refuses()
    {
        // Refusing would make a full plan unrearrangeable; the displaced unit goes where the
        // moving one came from, which on a first placement is the tray.
        var plan = Rooms();
        var a = plan.AddRoom(Guid.NewGuid(), "Blue Room", 2)!.Value;
        var b = plan.AddRoom(Guid.NewGuid(), "The Suite", 4)!.Value;
        plan.Place(a, 0, 0);
        plan.Place(b, 0, 1);

        plan.Place(a, 0, 1);

        Assert.Equal("Blue Room", plan.At(0, 1)!.Name);
        Assert.Equal("The Suite", plan.At(0, 0)!.Name);
    }

    [Fact]
    public void Auto_arrange_lays_the_tray_out_one_section_per_row()
    {
        var plan = Rooms();
        var ids = new Dictionary<Guid, string?>();
        foreach (var (name, floor) in new[]
                 { ("Blue Room", "First"), ("The Suite", "First"), ("Attic", "Second") })
        {
            var id = Guid.NewGuid();
            ids[plan.AddRoom(id, name, 2)!.Value] = floor;
        }

        plan.AutoArrange(u => ids[u.Key]);

        Assert.Empty(plan.Unplaced);
        Assert.Equal(2, plan.Units.Select(u => u.Row).Distinct().Count());
        // "First" sorts before "Second", so the first floor is the first row.
        Assert.Equal(0, plan.Units.First(u => u.Name == "Blue Room").Row);
        Assert.Equal(1, plan.Units.First(u => u.Name == "Attic").Row);
    }

    // ── undo, dirt and what is sent ──────────────────────────────────────────

    [Fact]
    public void Undo_puts_back_exactly_what_was_there_and_redo_takes_it_away_again()
    {
        var plan = Seats();
        plan.AddBlock(1, 3);
        var before = plan.Units.ToList();

        plan.Remove([plan.At(0, 1)!.Key]);
        Assert.Equal(2, plan.Units.Count);

        plan.Undo();
        Assert.Equal(before, plan.Units);

        plan.Redo();
        Assert.Equal(2, plan.Units.Count);
    }

    [Fact]
    public void A_new_edit_ends_the_redo_road()
    {
        var plan = Seats();
        plan.AddBlock(1, 2);
        plan.Undo();
        Assert.True(plan.CanRedo);

        plan.AddBlock(1, 1);

        Assert.False(plan.CanRedo);
    }

    [Fact]
    public void Undoing_back_to_the_start_is_not_dirty()
    {
        // Otherwise the leaving-the-page warning fires at somebody who has changed nothing, and a
        // warning that cries wolf is one people click through.
        var plan = Seats();
        Assert.False(plan.IsDirty);

        plan.AddBlock(1, 2);
        Assert.True(plan.IsDirty);

        plan.Undo();
        Assert.False(plan.IsDirty);
    }

    [Fact]
    public void The_undo_stack_stops_growing()
    {
        var plan = Seats();
        for (var i = 0; i < PlanModel.UndoDepth + 20; i++) plan.AddBlock(1, 1);

        for (var i = 0; i < PlanModel.UndoDepth; i++) plan.Undo();

        Assert.False(plan.CanUndo);
    }

    [Fact]
    public void What_is_sent_is_the_plan_in_reading_order_with_the_tray_last()
    {
        var plan = Rooms();
        var placed = plan.AddRoom(Guid.NewGuid(), "Blue Room", 2)!.Value;
        plan.AddRoom(Guid.NewGuid(), "Attic", null);
        plan.Place(placed, 0, 0);

        var choices = plan.ToChoices();

        // Position in the list is the sort order the endpoint documents.
        Assert.Equal(2, choices.Count);
        Assert.Equal(0, choices[0].LayoutRow);
        Assert.Null(choices[1].LayoutRow);
        // A position is a pair or nothing: half of one is not a place on a plan.
        Assert.Null(choices[1].LayoutColumn);
    }

    [Fact]
    public void A_rooms_plan_sends_rooms_and_a_seats_plan_sends_labels()
    {
        var room = Guid.NewGuid();
        var rooms = Rooms();
        rooms.AddRoom(room, "Blue Room", 2);
        var sent = rooms.ToChoices().Single();
        Assert.Equal(room, sent.PlaceRoomId);
        Assert.Null(sent.Label);

        var seats = Seats();
        seats.AddBlock(1, 1);
        var seat = seats.ToChoices().Single();
        Assert.Null(seat.PlaceRoomId);
        Assert.Equal("A1", seat.Label);
    }

    [Fact]
    public void A_seats_plan_will_not_let_a_selection_bar_change_a_capacity()
    {
        var plan = Seats();
        plan.AddBlock(1, 2);

        plan.Apply(plan.Units.Select(u => u.Key), capacity: 4);

        Assert.All(plan.Units, u => Assert.Equal(1, u.Capacity));
    }

    [Fact]
    public void The_kind_can_change_only_while_the_plan_is_empty()
    {
        // The server refuses a kind change once anything is confirmed into the plan, so the
        // designer never offers a change it knows will bounce.
        var plan = Rooms();
        Assert.True(plan.ChangeKind(HostedEventLayoutKind.Seats));

        plan.AddBlock(1, 1);
        Assert.False(plan.ChangeKind(HostedEventLayoutKind.Rooms));
        Assert.Equal(HostedEventLayoutKind.Seats, plan.Kind);
    }

    [Fact]
    public void What_the_server_returns_becomes_the_new_starting_point()
    {
        var plan = Seats();
        plan.AddBlock(1, 2);
        Assert.True(plan.IsDirty);

        plan.Saved(plan.Units);

        Assert.False(plan.IsDirty);
        Assert.False(plan.CanUndo);
    }
}
