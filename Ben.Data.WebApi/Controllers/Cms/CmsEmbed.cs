using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Controllers.Public;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ben.Data.WebApi.Controllers.Cms;

/// <summary>
/// What a group's public page is allowed to say about its own cases and investigations.
/// </summary>
/// <remarks>
/// <para>Ben's requirement, and the reason this is the careful part of item #80: both safeguards
/// are enforced <b>server-side, before the data leaves the WebApi</b>. A group publishing its own
/// work must not be able to publish a client's home address or their real name, whatever the
/// browser sends.</para>
///
/// <para><b>References are stored; records are resolved.</b> The section holds ids and switches,
/// never a copy of the data. So redaction runs on every request, and a client who asks for their
/// name to be removed next month is removed from pages published today. A snapshot taken at
/// embed time would freeze whatever happened to be true that afternoon and quietly outlive every
/// later decision.</para>
///
/// <para><b>The projections have no field for the dangerous values.</b> There is no exact latitude,
/// no street address and no real name anywhere in <see cref="EmbeddedInvestigation"/> or
/// <see cref="EmbeddedCase"/> — not nulled, absent. A shape that cannot carry it cannot leak it,
/// and no later edit to a mapping can reintroduce what the record has no room for.</para>
/// </remarks>
public static class CmsEmbed
{
    /// <summary>Section types whose stored content is replaced by a resolved projection.</summary>
    public static bool IsEmbed(CmsSectionType type)
        => type is CmsSectionType.EmbeddedInvestigations
                or CmsSectionType.EmbeddedCases
                or CmsSectionType.CaseMedia;

    // ── What the group stores ────────────────────────────────────────────────

    /// <summary>
    /// The authored side of an embed: which records, and the two questions Ben asked for.
    /// </summary>
    /// <param name="Ids">Records to show. Ones this organization does not own are dropped on read.</param>
    /// <param name="IncludeNonPublic">
    /// Set when the group has been warned that a selection includes work not already public, and
    /// chose to publish it anyway. Without it those records simply do not resolve — so a page saved
    /// by an older editor, or by a hand-made request, cannot publish them by omission.
    /// </param>
    /// <param name="ShowApproximateLocation">
    /// Whether to show the rough area at all. There is no option for the exact place: the projection
    /// carries a grid-snapped point and nothing else, so "on" means an area and "off" means silence.
    /// </param>
    /// <param name="ShowClientName">
    /// Whether to show the client's chosen alias. Never their real name — that is not on the menu,
    /// because <see cref="PublicClientName"/> has no branch that returns one.
    /// </param>
    public sealed record Settings(
        IReadOnlyList<Guid> Ids,
        bool IncludeNonPublic = false,
        bool ShowApproximateLocation = false,
        bool ShowClientName = false)
    {
        public static Settings Empty { get; } = new([]);

        /// <summary>
        /// Never null, whatever the stored JSON omitted or malformed.
        /// </summary>
        /// <remarks>
        /// <c>{}</c> deserializes with <c>Ids</c> null rather than empty, and every read of this
        /// record is about whether to publish something — so the safe empty list is built in here
        /// rather than left for each caller to remember.
        /// </remarks>
        public IReadOnlyList<Guid> Ids { get; init; } = Ids ?? [];
    }

    /// <summary>
    /// The authored side of a case-media section: one case, and which of its files to show.
    /// </summary>
    /// <param name="CaseId">
    /// The case the files are drawn from. Empty means the author has not chosen one yet, and the
    /// section resolves to nothing — a half-filled slot shows an empty page, not somebody's photos.
    /// </param>
    /// <param name="FileIds">
    /// The files, in the order the author arranged them. Ones that are no longer publishable are
    /// dropped on read rather than at save, which is the whole point of storing references.
    /// </param>
    /// <param name="ShowCaptions">
    /// Whether to print the timeline entry each file came from beneath it. Off by default: the
    /// entry title is the group's own working description and may say more than they intend on a
    /// public page, so showing it is a choice rather than a consequence of picking a photo.
    /// </param>
    /// <remarks>
    /// Deliberately <b>not</b> a variant of <see cref="Settings"/>. There is no
    /// <c>IncludeNonPublic</c> here, and its absence is the design: for a case's own files there is
    /// no acknowledgement that makes publishing an investigator's working file acceptable, because
    /// nobody has ever said that file could be shown. The one route to publishing a case file stays
    /// the one <see cref="CaseMediaPublication"/> describes — attach it to a public timeline entry —
    /// and this section can offer no way around it.
    /// </remarks>
    public sealed record CaseMediaSettings(
        Guid CaseId,
        IReadOnlyList<Guid> FileIds,
        bool ShowCaptions = false)
    {
        public static CaseMediaSettings Empty { get; } = new(Guid.Empty, []);

        public IReadOnlyList<Guid> FileIds { get; init; } = FileIds ?? [];
    }

    /// <summary>
    /// camelCase, matching every other section type's stored content and what the renderer reads.
    /// </summary>
    /// <remarks>
    /// Not cosmetic. This JSON is a string carried inside the response, so the outer serializer never
    /// touches it — the casing here is the only casing there is. The first version left it at the
    /// default and the renderer, which looks for <c>title</c>, found nothing: every embedded card
    /// would have rendered blank on a real page. Caught by a test asserting the title was published,
    /// not by one asserting an address was not.
    /// </remarks>
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Reads the stored settings, treating anything unparseable as "show nothing".
    /// </summary>
    /// <remarks>
    /// Failing closed matters more here than anywhere else in the CMS. Every other section type that
    /// cannot be parsed renders as an empty box; this one would be deciding whether somebody's
    /// address is published, so malformed content must never fall through to a permissive default.
    /// </remarks>
    public static Settings ParseSettings(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson)) return Settings.Empty;

        try
        {
            return JsonSerializer.Deserialize<Settings>(contentJson, Json) ?? Settings.Empty;
        }
        catch (JsonException)
        {
            return Settings.Empty;
        }
    }

    public static string WriteSettings(Settings settings)
        => JsonSerializer.Serialize(settings, Json);

    /// <summary>Reads a case-media section's stored settings, failing closed like the above.</summary>
    public static CaseMediaSettings ParseCaseMediaSettings(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson)) return CaseMediaSettings.Empty;

        try
        {
            return JsonSerializer.Deserialize<CaseMediaSettings>(contentJson, Json) ?? CaseMediaSettings.Empty;
        }
        catch (JsonException)
        {
            return CaseMediaSettings.Empty;
        }
    }

    public static string WriteCaseMediaSettings(CaseMediaSettings settings)
        => JsonSerializer.Serialize(settings, Json);

    // ── What a visitor receives ──────────────────────────────────────────────

    /// <summary>
    /// One investigation as published on a group's own page.
    /// </summary>
    /// <remarks>
    /// <see cref="Latitude"/> and <see cref="Longitude"/> are the centre of a grid cell roughly
    /// seven miles across, never the real point — see <see cref="PublicCoordinates"/>. They are null
    /// unless the group asked for the area to be shown at all.
    /// </remarks>
    public sealed record EmbeddedInvestigation(
        Guid Id,
        string Title,
        string? Summary,
        DateTime? ScheduledDateTime,
        string? UrlName,
        string? PlaceName,
        string? City,
        string? State,
        decimal? Latitude,
        decimal? Longitude,
        bool LocationIsApproximate);

    /// <summary>One case as published on a group's own page.</summary>
    /// <remarks><see cref="ClientName"/> is an alias or nothing. There is no field for a real name.</remarks>
    public sealed record EmbeddedCase(
        Guid Id,
        string Title,
        string? Summary,
        DateTime DateCreated,
        string? UrlName,
        string? ClientName,
        string? City,
        string? State,
        decimal? Latitude,
        decimal? Longitude,
        bool LocationIsApproximate);

    /// <summary>
    /// One file from a case, as published on a group's own page.
    /// </summary>
    /// <param name="CaseId">
    /// Carried so the renderer can build the URL that serves the bytes. The public media endpoint
    /// is case-scoped on purpose — it asks "may this case publish this file", which is the only
    /// question with a safe answer, and a bare file id could not have posed it.
    /// </param>
    /// <param name="UploadFileId">The file itself.</param>
    /// <param name="Caption">
    /// The timeline entry's title, and null unless the group asked for captions. Absent rather than
    /// withheld, the same discipline as the records above.
    /// </param>
    /// <param name="When">When the entry happened, on the same switch as the caption.</param>
    /// <param name="EntryType">What kind of entry it came from — evidence, a note, a visit.</param>
    public sealed record PublishedCaseFile(
        Guid CaseId,
        Guid UploadFileId,
        string? Caption,
        DateTime? When,
        CaseTimelineEntryType EntryType);

    // ── Resolution ───────────────────────────────────────────────────────────

    /// <summary>
    /// Turns the stored references into records a visitor may see, or an empty list.
    /// </summary>
    /// <remarks>
    /// <para><b>Ownership is re-checked here, on read.</b> The editor only offers the group's own
    /// work, but that is a convenience, not a control — a request can say anything. Filtering on
    /// <c>OrganizationId</c> in the query is what actually stops a group embedding somebody else's
    /// investigation, and doing it at read time means it stays true if the record changes hands.</para>
    ///
    /// <para>Ids that resolve to nothing are dropped silently rather than reported. The page is
    /// public, and "that investigation exists but you may not see it" is itself a disclosure.</para>
    /// </remarks>
    public static async Task<string> ResolveAsync(
        BenDataContext db, Guid organizationId, CmsSectionType type, string? contentJson,
        CancellationToken ct)
    {
        // Case media stores a different shape — one case and its files, not a list of records — so
        // it branches before the shared parse rather than being bent into it. Reading it with the
        // wrong parser would yield an empty selection and silently render nothing.
        if (type == CmsSectionType.CaseMedia)
            return JsonSerializer.Serialize(
                await ResolveCaseMediaAsync(db, organizationId, ParseCaseMediaSettings(contentJson), ct), Json);

        var settings = ParseSettings(contentJson);

        if (settings.Ids.Count == 0)
            return JsonSerializer.Serialize(Array.Empty<object>(), Json);

        var ids = settings.Ids.Distinct().ToList();

        return type switch
        {
            CmsSectionType.EmbeddedInvestigations =>
                JsonSerializer.Serialize(await ResolveInvestigationsAsync(db, organizationId, ids, settings, ct), Json),
            CmsSectionType.EmbeddedCases =>
                JsonSerializer.Serialize(await ResolveCasesAsync(db, organizationId, ids, settings, ct), Json),
            _ => JsonSerializer.Serialize(Array.Empty<object>(), Json),
        };
    }

    private static async Task<List<EmbeddedInvestigation>> ResolveInvestigationsAsync(
        BenDataContext db, Guid organizationId, List<Guid> ids, Settings settings, CancellationToken ct)
    {
        var rows = await db.Investigations.AsNoTracking()
            .Include(i => i.Place)
            .Where(i => ids.Contains(i.Id) && i.OrganizationId == organizationId)
            // Work that is not already public needs the group to have said so deliberately. The
            // warning in the editor is what makes that a decision; this is what makes it a rule.
            .Where(i => settings.IncludeNonPublic || i.Visibility == InvestigationVisibility.Public)
            .ToListAsync(ct);

        // Ordered by the group's own arrangement, not by date — they chose the sequence.
        return [.. ids
            .Select(id => rows.FirstOrDefault(r => r.Id == id))
            .Where(r => r is not null)
            .Select(r =>
            {
                var (lat, lon) = settings.ShowApproximateLocation
                    ? PublicCoordinates.Approximate(r!.Place?.Latitude, r.Place?.Longitude)
                    : (null, null);

                return new EmbeddedInvestigation(
                    r!.Id, r.Title, r.Notes, r.ScheduledDateTime, r.UrlName,
                    settings.ShowApproximateLocation ? r.Place?.Name : null,
                    settings.ShowApproximateLocation ? r.Place?.City : null,
                    settings.ShowApproximateLocation ? r.Place?.State : null,
                    lat, lon,
                    LocationIsApproximate: true);
            })];
    }

    private static async Task<List<EmbeddedCase>> ResolveCasesAsync(
        BenDataContext db, Guid organizationId, List<Guid> ids, Settings settings, CancellationToken ct)
    {
        var rows = await db.Cases.AsNoTracking()
            .Where(c => ids.Contains(c.Id) && c.OrganizationId == organizationId)
            // The same two conditions the case's own public page uses. Restated rather than
            // shared because there is no helper for it yet; if one appears, this must adopt it.
            .Where(c => settings.IncludeNonPublic
                     || (c.IsPublic && (c.Status == CaseStatus.Public || c.Status == CaseStatus.Haunted)))
            .ToListAsync(ct);

        return [.. ids
            .Select(id => rows.FirstOrDefault(r => r.Id == id))
            .Where(r => r is not null)
            .Select(r =>
            {
                var (lat, lon) = settings.ShowApproximateLocation
                    ? PublicCoordinates.Approximate(r!.Latitude, r.Longitude)
                    : (null, null);

                return new EmbeddedCase(
                    r!.Id, r.Title, r.Description, r.DateCreated, r.UrlName,
                    // The alias or nothing, through the one helper that decides this — so an embed
                    // and the case's own public page can never disagree about who somebody is.
                    settings.ShowClientName ? PublicClientName.For(r) : null,
                    settings.ShowApproximateLocation ? r.City : null,
                    settings.ShowApproximateLocation ? r.State : null,
                    lat, lon,
                    LocationIsApproximate: true);
            })];
    }

    /// <summary>
    /// Turns a case-media section's stored ids into the files a visitor may actually see.
    /// </summary>
    /// <remarks>
    /// <para><b>Two independent gates, and both are re-asked here.</b> The case must belong to this
    /// organization — a group may not decorate its page with another group's work, even work that
    /// is public — and each file must still be publishable under
    /// <see cref="CaseMediaPublication"/>. The picker in the editor offers only files that pass
    /// both, but that is a convenience; a saved section carries ids, and ids can be edited.</para>
    ///
    /// <para><b>Silence, not a gap.</b> A file that no longer qualifies is dropped, not replaced
    /// with a placeholder. "There was a photo here that you may not see" is itself a disclosure,
    /// and the page reads as though it was always this length.</para>
    ///
    /// <para>Captions come from the same query that decides publishability, so a caption can never
    /// describe a file the rule went on to drop.</para>
    /// </remarks>
    private static async Task<List<PublishedCaseFile>> ResolveCaseMediaAsync(
        BenDataContext db, Guid organizationId, CaseMediaSettings settings, CancellationToken ct)
    {
        if (settings.CaseId == Guid.Empty || settings.FileIds.Count == 0) return [];

        var ownsCase = await db.Cases.AsNoTracking()
            .AnyAsync(c => c.Id == settings.CaseId && c.OrganizationId == organizationId, ct);

        if (!ownsCase) return [];

        // One query for the whole selection: a write-up with a dozen photos should not cost a dozen
        // round trips, and this is the same list the picker was built from.
        var publishable = (await CaseMediaPublication.PublishableAsync(db, settings.CaseId, ct))
            .ToDictionary(f => f.UploadFileId);

        return [.. settings.FileIds
            .Distinct()
            .Where(publishable.ContainsKey)
            .Select(id => publishable[id])
            .Select(f => new PublishedCaseFile(
                settings.CaseId,
                f.UploadFileId,
                settings.ShowCaptions ? f.Context : null,
                settings.ShowCaptions ? f.When : null,
                f.EntryType))];
    }
}
