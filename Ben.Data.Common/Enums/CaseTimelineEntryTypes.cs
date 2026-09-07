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
