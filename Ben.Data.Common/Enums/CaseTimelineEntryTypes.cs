namespace Ben.Data.Common.Enums;

/// <summary>
/// What a timeline entry's kind is called on screen (site evaluation 2026-09-06, W-A11).
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Three screens each carried their own <c>switch</c> over
/// <see cref="CaseTimelineEntryType"/>, and every one of them ended in a
/// <c>_ =&gt; t.ToString()</c> fallback. <c>InstrumentReading</c> was added to the enum and to
/// nobody's switch, so the Type list on the case timeline offered a person the raw identifier
/// <c>InstrumentReading</c> to choose from. A fallback to the enum's own name is how that happens
/// silently: it always produces something, so nothing ever fails.</para>
///
/// <para>This one is exhaustive and has no fallback, so a value added to the enum stops the build
/// here rather than surfacing as an identifier in front of somebody.</para>
/// </remarks>
public static class CaseTimelineEntryTypes
{
    /// <summary>
    /// The kinds a person may choose when adding a timeline entry: every kind except
    /// <see cref="CaseTimelineEntryType.ResearchNote"/>.
    /// </summary>
    /// <remarks>
    /// Beta feedback, 2026-09-14: research has its own tab, and a research page carries its own date and
    /// time, so offering "Research" as a timeline entry made two places to write the same thing. The value
    /// stays in the enum — entries written as research before this still exist and still show, under their
    /// own filter chip — it is only no longer offered for new ones.
    /// </remarks>
    public static IReadOnlyList<CaseTimelineEntryType> Pickable { get; } = Enum
        .GetValues<CaseTimelineEntryType>()
        .Where(t => t != CaseTimelineEntryType.ResearchNote)
        .ToArray();

    /// <summary>The full name, for a picker or a heading.</summary>
    public static string DisplayName(CaseTimelineEntryType type) => type switch
    {
        CaseTimelineEntryType.ClientReport      => "Client report",
        CaseTimelineEntryType.InvestigatorNote  => "Investigator note",
        CaseTimelineEntryType.Evidence          => "Evidence",
        CaseTimelineEntryType.ResearchNote      => "Research",
        CaseTimelineEntryType.InstrumentReading => "Instrument reading",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type,
                 "Every timeline entry type needs a name people can read. Add it here."),
    };

    /// <summary>The short form, for a badge on a crowded row.</summary>
    public static string ShortName(CaseTimelineEntryType type) => type switch
    {
        CaseTimelineEntryType.ClientReport      => "Client",
        CaseTimelineEntryType.InvestigatorNote  => "Note",
        CaseTimelineEntryType.Evidence          => "Evidence",
        CaseTimelineEntryType.ResearchNote      => "Research",
        CaseTimelineEntryType.InstrumentReading => "Reading",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type,
                 "Every timeline entry type needs a badge. Add it here."),
    };
}
