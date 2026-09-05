using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Controllers.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Redaction;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// A place as a visitor sees it: only investigations somebody deliberately published.
/// </summary>
/// <remarks>
/// <para>Separate from the signed-in <see cref="PlaceController"/> rather than one endpoint with a
/// branch on whether there is a user, because the two answer different questions and the anonymous
/// one is the dangerous one to get wrong.</para>
///
/// <para><b>It still goes through <see cref="InvestigationVisibilityFilter.VisibleTo"/></b>, passed
/// an empty set of organizations. Writing <c>Where(i => i.Visibility == Public)</c> here would be
/// shorter and would be a second copy of the sharing rules — which is exactly how the rule that
/// holds in one place stops holding in another.</para>
/// </remarks>
[ApiController]
[Route("api/public/places")]
[AllowAnonymous]
public sealed class PublicPlaceController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public PublicPlaceController(IDbContextFactory<BenDataContext> db) => _db = db;

    /// <summary>The place itself, and everything published about it.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PublicPlaceResponse>> GetById(Guid id, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var place = await db.Places.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new PlaceRecord(
                p.Id, p.Name, p.StreetAddress1, p.City, p.State, p.ZipCode, p.Country,
                p.Latitude, p.Longitude, p.GeocodeNote, p.Kind))
            .FirstOrDefaultAsync(ct);

        if (place is null) return NotFound();

        // An anonymous caller belongs to no organizations and has investigated nowhere, so the
        // shared predicate resolves to "public only" on its own. No second rule to keep in step.
        var raw = await db.Investigations.AsNoTracking()
            .Where(i => i.PlaceId == id)
            .Where(InvestigationVisibilityFilter.VisibleTo([], []))
            .OrderByDescending(i => i.ScheduledDateTime)
            .Select(i => new
            {
                i.Id, i.UrlName, i.Title, i.ScheduledDateTime, i.Status, i.CaseId,
                OrganizationName = i.Organization.Name,
                OrganizationUrlName = i.Organization.UrlName,
            })
            .ToListAsync(ct);

        // Item 184: an investigation bound to a private-engagement case must not carry the
        // client's name in its title on the place's public page.
        var rosters = await CaseRedactionRoster.ForCasesAsync(
            db, raw.Where(i => i.CaseId != null).Select(i => i.CaseId!.Value).Distinct().ToList(), ct);

        var rows = raw.Select(i => new PublicPlaceInvestigationRow(
                i.Id,
                i.UrlName,
                i.CaseId is { } caseId ? CaseProseRedactor.RedactFor(rosters, caseId, i.Title)! : i.Title,
                i.ScheduledDateTime,
                i.Status,
                i.OrganizationName,
                i.OrganizationUrlName))
            .ToList();

        return Ok(new PublicPlaceResponse(place, rows, PlaceSummary.From(rows),
            await PublishedSessionsAsync(db, id, ct),
            await ArchiveEvidencePublication.ForPlaceAsync(db, id, ct)));
    }

    /// <summary>
    /// The archive: every field session somebody published here, newest first.
    /// </summary>
    /// <remarks>
    /// <para><b>This is the part no other tool has.</b> A single person's readings at a location
    /// are an anecdote; the same location recorded by eleven people over two years is either a
    /// persistent artifact or a demonstrated absence of one. The rows carry marker and reading
    /// counts precisely so a reader can compare visits rather than take one on faith.</para>
    ///
    /// <para><b>PublishedAtUtc is the only gate.</b> Not the place's kind, not the session's
    /// owner, not a visibility enum — publication is an act somebody performed, and the query
    /// asks whether they performed it. The kind is checked when publishing, which is where a
    /// refusal can still be explained to the person it affects.</para>
    /// </remarks>
    private static async Task<IReadOnlyList<PublicPlaceSessionRow>> PublishedSessionsAsync(
        BenDataContext db, Guid placeId, CancellationToken ct)
        => await db.FieldSessionUploads.AsNoTracking()
            .Where(s => s.PlaceId == placeId && s.PublishedAtUtc != null)
            .OrderByDescending(s => s.StartedAt)
            .Select(s => new PublicPlaceSessionRow(
                s.Id,
                // The recorder's own name when they gave one, otherwise the account that sent
                // it. Attribution is what makes an archive citable — an anonymous pile of
                // numbers is worth less than one reading somebody put their name to.
                s.RecordedByName ?? s.SubmittedByAppUser.DisplayName ?? "A contributor",
                s.SubmittedByAppUserId,
                s.LocationLabel,
                s.StartedAt,
                s.EndedAt,
                s.ReadingCount,
                s.MarkerCount,
                s.DeviceModel,
                s.PublishedAtUtc!.Value,
                s.DocumentUploadFileId,
                // Fail-closed, and on the SAME predicate the serving endpoint uses. Listing media
                // this page cannot serve would draw a gallery of broken frames; the place-kind
                // clause is what stops a place later corrected to a private residence from
                // keeping its pictures up. See ArchiveMediaPublication, which owns the rule.
                s.MediaReviewState == Ben.Data.Common.Enums.FeedMediaReviewState.Approved
                 && s.Place!.Kind == Ben.Data.Common.Enums.PlaceKind.PublicLocation
                    ? s.Files
                        .OrderBy(f => f.RelativePath)
                        .Select(f => new ArchiveMediaItem(
                            f.UploadFileId, f.RelativePath,
                            f.UploadFile.ContentType, f.UploadFile.FileName))
                        .ToList()
                    : new List<ArchiveMediaItem>()))
            .ToListAsync(ct);
}

/// <summary>What a visitor gets for one place.</summary>
/// <remarks>
/// <para><c>Sessions</c> and <c>EventEvidence</c> are both defaulted, so every existing caller —
/// the website's place page among them — keeps compiling and simply renders no archive until it
/// asks for one.</para>
///
/// <para><c>EventEvidence</c> is what guests photographed at public events HERE and chose to
/// contribute. It is a separate list from <c>Sessions</c> rather than folded into it, because the
/// two are different things and pretending otherwise would be dishonest: a field session is a
/// document of readings taken over a night, and this is one picture somebody took on a walk.
/// Merging them would put a photograph in a table whose columns are reading counts and
/// magnetometer models.</para>
/// </remarks>
public sealed record PublicPlaceResponse(
    PlaceRecord Place,
    IReadOnlyList<PublicPlaceInvestigationRow> Investigations,
    PlaceSummary Summary,
    IReadOnlyList<PublicPlaceSessionRow>? Sessions = null,
    IReadOnlyList<PlaceEvidenceRow>? EventEvidence = null);

/// <summary>
/// One published field session in a place's archive.
/// </summary>
/// <remarks>
/// <para><b>Readings first, and media now too.</b> The document's numbers are what make visits
/// comparable and they carry no moderation problem, which is why they shipped alone: photos and
/// audio were to wait until the archive had the screening, reporting and blocking the feed
/// already has. Those exist — post-moderation on publish, a flag that hides immediately, and the
/// moderator queue behind it — so <see cref="Media"/> now carries the files themselves rather
/// than a count nobody could open.</para>
/// <para><see cref="DeviceModel"/> is here for an unglamorous but necessary reason: phone
/// magnetometers differ, and a reader comparing a spike across two visits deserves to know
/// whether they are comparing two instruments as well as two nights.</para>
///
/// <para><c>MarkerCount</c> is the moments the recorder flagged — the single most comparable
/// number across visits. "Eleven of twelve people marked something on those stairs" is the
/// archive's whole point.</para>
///
/// <para><c>Media</c> is the photos, video and audio a reviewer has cleared for this page, empty
/// until one has, and null rather than empty for callers that never ask — so an older client
/// renders no gallery rather than an empty one.</para>
/// </remarks>
public sealed record PublicPlaceSessionRow(
    Guid Id,
    string ContributorName,
    Guid ContributorAppUserId,
    string? LocationLabel,
    DateTime StartedAt,
    DateTime? EndedAt,
    int ReadingCount,
    int MarkerCount,
    string DeviceModel,
    DateTime PublishedAtUtc,
    Guid DocumentUploadFileId,
    IReadOnlyList<ArchiveMediaItem>? Media = null)
{
    /// <summary>
    /// How many recordings this row can show.
    /// </summary>
    /// <remarks>
    /// Derived rather than carried. It was a stored count while nothing could serve the bytes,
    /// and a stored count beside a list is two answers to one question that drift the first time
    /// somebody edits one predicate and not the other.
    /// </remarks>
    public int ApprovedMediaCount => Media?.Count ?? 0;
}

/// <summary>
/// One published investigation. Deliberately thinner than the signed-in row: no visibility (every
/// row here is public by definition) and no organization id, since a visitor gets the group's
/// public URL name instead.
/// </summary>
public sealed record PublicPlaceInvestigationRow(
    Guid Id,
    // The readable address of this investigation's own page, or null for one published before
    // slugs existed. Without it the row has nowhere to link, which is how a list of published
    // work becomes a list nobody can read.
    string? UrlName,
    string Title,
    DateTime ScheduledDateTime,
    InvestigationStatus Status,
    string OrganizationName,
    string OrganizationUrlName);

/// <summary>
/// "N investigations by M groups since Y" — the line that makes a place feel like a history rather
/// than a list.
/// </summary>
/// <param name="InvestigationCount">Visits the caller may see — never the raw total.</param>
/// <param name="OrganizationCount">Distinct groups among those visits.</param>
/// <param name="Since">Null when nothing is visible, so the caller can omit the phrase entirely.</param>
public sealed record PlaceSummary(int InvestigationCount, int OrganizationCount, int? Since)
{
    internal static PlaceSummary From(IReadOnlyList<PublicPlaceInvestigationRow> rows) => new(
        rows.Count,
        rows.Select(r => r.OrganizationName).Distinct().Count(),
        rows.Count == 0 ? null : rows.Min(r => r.ScheduledDateTime).Year);
}
