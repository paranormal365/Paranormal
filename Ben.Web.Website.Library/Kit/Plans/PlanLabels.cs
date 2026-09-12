namespace Ben.Web.Website.Library.Kit.Plans;

/// <summary>
/// How a row of seats is numbered, in the words a box office uses.
/// </summary>
public enum SeatNumbering
{
    /// <summary>1, 2, 3 … from the left as the audience faces the stage.</summary>
    LeftToRight = 0,

    /// <summary>1, 2, 3 … from the right. Some houses number from the prompt side.</summary>
    RightToLeft = 1,

    /// <summary>
    /// Odd numbers one side of the centre aisle and even the other, counting outward from the
    /// middle — the arrangement most proscenium theatres actually use.
    /// </summary>
    /// <remarks>
    /// A twenty-seat row reads 19 17 15 … 3 1 | 2 4 6 … 18 20. Somebody holding seat 1 and
    /// somebody holding seat 2 are sitting next to each other, which is exactly the thing that
    /// surprises anybody meeting it for the first time and exactly why a venue that uses it cannot
    /// be asked to renumber for us.
    /// </remarks>
    OddEvenFromCentre = 2,
}

/// <summary>
/// Naming rows and seats the way a venue already names them (item 235 phase 2).
/// </summary>
/// <remarks>
/// <para>Pure, and separate from everything that draws, because this is the part a venue will
/// check against its own tickets. A plan whose labels do not match the brass numbers on the seats
/// is worse than no plan: it sends people confidently to the wrong chair.</para>
/// </remarks>
public static class PlanLabels
{
    /// <summary>
    /// The letters rows are named with — the alphabet without I and O.
    /// </summary>
    /// <remarks>
    /// Theatre convention, and a good one: on a printed ticket and on a brass rail, <c>I</c> is a
    /// <c>1</c> and <c>O</c> is a <c>0</c>. Dropping both is cheaper than every usher in the
    /// building explaining it for the life of the venue.
    /// </remarks>
    public const string RowAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ";

    /// <summary>The maximum a single block may create, so a typo cannot make a hundred thousand seats.</summary>
    /// <remarks>
    /// Comfortably past the largest room this site will ever describe — the Royal Albert Hall seats
    /// about five thousand — and small enough that the mistake is caught rather than survived.
    /// </remarks>
    public const int MaximumUnitsPerBlock = 2000;

    /// <summary>
    /// The name of a row by its position: 0 is A, 24 is AA, 25 is AB.
    /// </summary>
    /// <remarks>
    /// Bijective base-24 over <see cref="RowAlphabet"/>, not base-24 with a zero: after Z comes AA
    /// rather than BA, because that is how a venue counts and how a spreadsheet column counts.
    /// </remarks>
    public static string RowName(int index)
    {
        if (index < 0) return "?";

        var name = string.Empty;
        var n = index;
        while (true)
        {
            name = RowAlphabet[n % RowAlphabet.Length] + name;
            n = n / RowAlphabet.Length - 1;
            if (n < 0) break;
        }
        return name;
    }

    /// <summary>The position a row name stands for, or null when it is not one of ours.</summary>
    /// <remarks>Needed so a venue can type "start at row C" and mean it.</remarks>
    public static int? RowIndex(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var index = 0;
        foreach (var c in name.Trim().ToUpperInvariant())
        {
            var at = RowAlphabet.IndexOf(c);
            if (at < 0) return null;
            index = index * RowAlphabet.Length + (at + 1);
        }
        return index - 1;
    }

    /// <summary>
    /// The seat number for each position in a row, left to right as the plan is drawn.
    /// </summary>
    /// <remarks>
    /// Returned as a whole row rather than computed per seat, because two of the three schemes
    /// need to know how long the row is before they can name its first seat.
    /// </remarks>
    public static int[] SeatNumbers(int count, SeatNumbering numbering, int startAt = 1)
    {
        if (count <= 0) return [];

        var numbers = new int[count];
        switch (numbering)
        {
            case SeatNumbering.RightToLeft:
                for (var i = 0; i < count; i++) numbers[i] = startAt + (count - 1 - i);
                break;

            case SeatNumbering.OddEvenFromCentre:
                // The centre is between the two middle positions of an even row, and on the middle
                // seat of an odd one — where that seat takes the first odd number and the halves
                // grow outward from either side of it.
                var half = count / 2;
                for (var i = 0; i < count; i++)
                {
                    if (i < half)
                    {
                        // Left of centre: odd, growing as it goes outward (away from the aisle).
                        numbers[i] = startAt + 2 * (half - 1 - i);
                    }
                    else
                    {
                        // Right of centre: even, growing outward. On an odd row the middle seat
                        // falls here and takes the first even number, which is the compromise every
                        // odd-width house makes.
                        numbers[i] = startAt + 1 + 2 * (i - half);
                    }
                }
                break;

            default:
                for (var i = 0; i < count; i++) numbers[i] = startAt + i;
                break;
        }
        return numbers;
    }

    /// <summary>The patterns a venue may name its seats with, in the order a picker offers them.</summary>
    /// <remarks>
    /// A short list rather than a free-text template, because a free one invites <c>{Row}</c> and
    /// <c>{ROW}</c> and a typo that names four hundred seats <c>{row}4</c>.
    /// </remarks>
    public static readonly IReadOnlyList<string> Patterns =
        ["{row}{seat}", "{row}-{seat}", "{row} {seat}", "Row {row}, Seat {seat}"];

    /// <summary>One seat's label — "C4", "C-4", "Row C, Seat 4".</summary>
    public static string Format(string? pattern, string row, int seat)
        => (string.IsNullOrWhiteSpace(pattern) ? Patterns[0] : pattern)
            .Replace("{row}", row, StringComparison.OrdinalIgnoreCase)
            .Replace("{seat}", seat.ToString(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What a block of rows and seats would be called, so the designer can say so before it is made.
    /// </summary>
    /// <remarks>
    /// The live line under the Add-a-block form — "13 rows × 20 = 260 seats, C4 … M20". Somebody
    /// about to create two hundred and sixty things should be shown the first and the last of them
    /// first; it is the cheapest way to catch a wrong starting row or a backwards numbering.
    /// </remarks>
    public static string Describe(
        int firstRowIndex, int rows, int seatsPerRow,
        SeatNumbering numbering, int startAt, string? pattern)
    {
        if (rows <= 0 || seatsPerRow <= 0) return "Nothing yet — say how many rows and seats.";

        var total = rows * seatsPerRow;
        var numbers = SeatNumbers(seatsPerRow, numbering, startAt);
        var first = Format(pattern, RowName(firstRowIndex), numbers[0]);
        var last = Format(pattern, RowName(firstRowIndex + rows - 1), numbers[^1]);

        var overflow = total > MaximumUnitsPerBlock
            ? $" — too many at once; {MaximumUnitsPerBlock} is the most one block may add"
            : "";

        return $"{rows} row{(rows == 1 ? "" : "s")} × {seatsPerRow} = {total} seat"
             + $"{(total == 1 ? "" : "s")}, {first} … {last}{overflow}";
    }
}
