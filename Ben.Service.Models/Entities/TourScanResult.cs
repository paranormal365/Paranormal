namespace Ben.Service.Models.Entities;

/// <summary>What a guide is told when they scan a pass (item 247).</summary>
/// <remarks>
/// <see cref="Says"/> is the whole point: the reader is on a dark street with a queue behind them,
/// so the answer is a sentence they can act on rather than fields a page has to assemble into one.
/// </remarks>
public record TourScanResult
{
    /// <summary>Whether this guest may come on the walk.</summary>
    public bool Admitted { get; init; }

    /// <summary>They had already been scanned in. Not a failure, and not a silent success.</summary>
    public bool AlreadyIn { get; init; }

    public string? GuestName { get; init; }

    /// <summary>How many places this pass covers.</summary>
    public int Seats { get; init; }

    /// <summary>What to say or do, in words.</summary>
    public required string Says { get; init; }

    /// <summary>A refused scan, with the reason a guide can read out.</summary>
    public static TourScanResult Refused(string why) => new() { Admitted = false, Says = why };
}
