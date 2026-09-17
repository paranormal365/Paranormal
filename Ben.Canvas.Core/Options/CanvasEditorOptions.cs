using Ben.Canvas.Core.Model;

namespace Ben.Canvas.Core.Options;

/// <summary>A kind of field on a card.</summary>
public enum CardFieldKind { Text, Date, Select, Checkbox }

/// <summary>A card template: the fields a card of this kind asks for.</summary>
public sealed record CardTemplate(string Id, string Name, List<CardField> Fields);

/// <summary>One field on a card template.</summary>
public sealed record CardField(string Key, string Label, CardFieldKind Kind, List<string>? Options = null, bool Required = false);

/// <summary>
/// What a host tells the canvas editor about where it is running and what it may do.
/// </summary>
/// <remarks>
/// <para>Every server-backed feature defaults to off. A host with no API configured is a complete local
/// editor, not a broken one - the position the video editor reached when an empty API address stopped
/// meaning "half an editor".</para>
///
/// <para>There are deliberately no endpoint URL templates here. Every path lives in the seam that calls it,
/// so a host changes one base address and never a list of routes that can drift.</para>
/// </remarks>
public sealed class CanvasEditorOptions
{
    // ── Host ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Absolute address of the Web API with no trailing slash, e.g. <c>https://ishaunted.com/webapi</c>.
    /// Null means there is no server.
    /// </summary>
    public string? ApiBaseUrl { get; set; }

    /// <summary>The site's own address, used for links back into it. Null when there is none.</summary>
    public string? SiteBaseUrl { get; set; }

    /// <summary>
    /// Where a MapKit token is issued. Null means map boxes show as address cards, because Apple only
    /// honours a token minted for the page's own origin.
    /// </summary>
    public string? MapTokenUrl { get; set; }

    /// <summary>
    /// Where the site signs a still map picture (the website's <c>/auth/mapkit-snapshot</c>). Null means map boxes
    /// show their address and an Open in Apple Maps link.
    /// </summary>
    /// <remarks>
    /// The picture is loaded from Apple each time a box is shown and cached only by the browser, never stored with
    /// the board: Apple's terms allow map data to be kept only temporarily (R35).
    /// </remarks>
    public string? MapSnapshotUrl { get; set; }

    /// <summary>Whether boards may be saved to and opened from the server.</summary>
    public bool ServerSave { get; set; }

    /// <summary>Whether pasted links are sent to the server for a rich preview card.</summary>
    public bool LinkUnfurl { get; set; }

    /// <summary>Whether a board may be published to its case.</summary>
    public bool Publish { get; set; }

    /// <summary>Sent as the <c>X-Ben-Client</c> header. Diagnostic only; it grants nothing.</summary>
    public string ClientName { get; set; } = "canvas";

    // ── Editing ───────────────────────────────────────────────────────────

    /// <summary>The kinds of block the palette offers.</summary>
    public HashSet<CanvasNodeType> EnabledBlocks { get; set; } = [.. Enum.GetValues<CanvasNodeType>()];

    /// <summary>The card templates; the first is used when a card is added without choosing one.</summary>
    /// <remarks>
    /// <para>Ben, 2026-09-17: "there should be other kinds of cards besides evidence. Like historical
    /// notes, articles like news or books, quotes and from people witnesses or clients." Research is
    /// mostly not evidence: it is what somebody read, what somebody said, and what happened here before
    /// anybody was recording it, and all of that was going on evidence cards with the wrong four fields.</para>
    /// <para><b>When is written, not dated</b>, everywhere but evidence. A date control cannot hold
    /// "the 1920s", "before the fire" or "1887" without inventing a day, and a research note that has to
    /// invent one is worse than a sentence.</para>
    /// <para><b>No card asks for a way to contact anybody.</b> A board can be published to the case and
    /// its picture filed there, so a telephone number typed into a person card is a telephone number
    /// published. Who somebody is and what they described is the research; how to reach them lives on
    /// the case, where the rules about that already are.</para>
    /// </remarks>
    public List<CardTemplate> CardTemplates { get; set; } =
    [
        new("evidence", "Evidence",
        [
            new("description", "Description", CardFieldKind.Text),
            new("date", "Date", CardFieldKind.Date),
            new("category", "Category", CardFieldKind.Select, ["Photo", "Audio", "Video", "Witness", "Other"]),
            new("verified", "Verified", CardFieldKind.Checkbox),
        ]),
        new("historical", "Historical note",
        [
            new("what", "What happened", CardFieldKind.Text),
            new("when", "When", CardFieldKind.Text),
            new("where", "Where", CardFieldKind.Text),
            new("source", "Where this comes from", CardFieldKind.Text),
        ]),
        new("article", "Article",
        [
            new("kind", "Kind", CardFieldKind.Select, ["News", "Book", "Journal", "Website", "Broadcast", "Other"]),
            new("author", "Author", CardFieldKind.Text),
            new("publication", "Publication", CardFieldKind.Text),
            new("published", "Published", CardFieldKind.Text),
            new("says", "What it says", CardFieldKind.Text),
        ]),
        new("experience", "Experience",
        [
            new("what", "What was experienced", CardFieldKind.Text),
            new("sense", "Kind", CardFieldKind.Select,
                ["Sound", "Sight", "Touch", "Smell", "Cold spot", "Feeling", "Something moved", "Other"]),
            new("who", "Who experienced it", CardFieldKind.Text),
            new("where", "Where in the place", CardFieldKind.Text),
            new("when", "When", CardFieldKind.Text),
        ]),
        new("quote", "Quote",
        [
            new("words", "The words", CardFieldKind.Text),
            new("who", "Who said it", CardFieldKind.Text),
            new("when", "When", CardFieldKind.Text),
            new("source", "Where it comes from", CardFieldKind.Text),
        ]),
        new("person", "Person",
        [
            new("role", "Role", CardFieldKind.Select,
                ["Witness", "Client", "Resident", "Owner", "Neighbour", "Investigator", "Historian", "Other"]),
            new("connection", "Connection to the place", CardFieldKind.Text),
            new("account", "What they describe", CardFieldKind.Text),
            new("when", "When they were there", CardFieldKind.Text),
        ]),
    ];

    public bool LocalPersistence { get; set; } = true;
    public bool SnapEnabled { get; set; } = true;
    public bool SnapToGrid { get; set; }
    public bool ErrorLog { get; set; } = true;

    /// <summary>Whether the minimap is drawn; the editor also hides it below 768 px wide.</summary>
    public bool ShowMinimap { get; set; } = true;

    // ── Limits ────────────────────────────────────────────────────────────

    public double SnapThresholdPx { get; set; } = 6;
    public double GridSize { get; set; } = 20;
    public int HistoryDepth { get; set; } = 50;
    public int MaxNodes { get; set; } = 2000;

    /// <summary>Above this many blocks only those near the view are rendered.</summary>
    public int VirtualiseAbove { get; set; } = 300;

    public long MaxImageBytes { get; set; } = 25L * 1024 * 1024;
    public long MaxFileBytes { get; set; } = 100L * 1024 * 1024;
    public int MaxTextChars { get; set; } = 20_000;
    public int MaxHtmlChars { get; set; } = 200_000;
    public int MaxPasteItems { get; set; } = 20;

    /// <summary>Refuses a configuration that would misbehave later, naming what is wrong.</summary>
    public void Validate()
    {
        if (EnabledBlocks is null || EnabledBlocks.Count == 0)
            throw new ArgumentException("At least one block type must be enabled.", nameof(EnabledBlocks));
        if (HistoryDepth < 1)
            throw new ArgumentException("HistoryDepth must be at least 1.", nameof(HistoryDepth));

        var templateIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var template in CardTemplates ?? [])
        {
            if (!templateIds.Add(template.Id))
                throw new ArgumentException($"The card template id \"{template.Id}\" is used twice.", nameof(CardTemplates));

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in template.Fields ?? [])
            {
                if (!keys.Add(field.Key))
                    throw new ArgumentException($"The field key \"{field.Key}\" is used twice in template \"{template.Id}\".", nameof(CardTemplates));
                if (field.Kind == CardFieldKind.Select && (field.Options is null || field.Options.Count == 0))
                    throw new ArgumentException($"The select field \"{field.Key}\" in template \"{template.Id}\" has no options.", nameof(CardTemplates));
            }
        }
    }
}
