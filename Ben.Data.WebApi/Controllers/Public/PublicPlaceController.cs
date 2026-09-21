using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Controllers.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Places;
using Ben.Data.WebApi.Services.Redaction;
using Ben.Service.Models.Feed;

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

        // Read the columns, then let PlaceDisclosure decide which of them a stranger may have.
        // Projecting straight into PlaceRecord here is what leaked a client's home address, ZIP
        // and exact map pin to anybody with the URL until the 2026-09-17 audit.
        var stored = await db.Places.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new
            {
                p.Id, p.Name, p.StreetAddress1, p.City, p.State, p.ZipCode, p.Country,
                p.Latitude, p.Longitude, p.GeocodeNote, p.Kind,
            })
            .FirstOrDefaultAsync(ct);

        if (stored is null) return NotFound();

        var place = PlaceDisclosure.Public(
            stored.Id, stored.Name, stored.StreetAddress1, stored.City, stored.State,
            stored.ZipCode, stored.Country, stored.Latitude, stored.Longitude,
            stored.GeocodeNote, stored.Kind);

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
            await ArchiveEvidencePublication.ForPlaceAsync(db, id, ct),
            await PublishedCasesAsync(db, id, ct),
            await PostsAsync(db, id, GetCurrentUserIdOrEmpty(), ct),
            // Always false here, and not because of who is asking. The website calls this endpoint
            // anonymously on purpose, so it cannot know the reader; a signed-in one asks
            // PlaceController.GetPosts instead. Saying so plainly beats a value that looks like an
            // answer and never is.
            CanPost: false,
            // Same set the serving door will hand over, read from the one predicate.
            AddedEvidence: await PlaceEvidencePublication.Showable(db, id)
                .Select(e => new Ben.Service.Models.Entities.PlaceAddedEvidenceRow(
                    e.Id, e.UploadFileId, e.UploadFile!.FileName, e.UploadFile.ContentType, e.Caption,
                    e.AddedByAppUser!.DisplayName ?? "Someone", e.AddedByAppUser.Handle,
                    e.DateCreated))
                .ToListAsync(ct),
            // False for the same reason CanPost is: this endpoint is anonymous and cannot know
            // the reader. The page asks its own signed-in state and the kind of place, which is
            // the whole of the rule — anybody signed in may add, at a public location.
            CanAddEvidence: false,
            Figures: await PlaceEvidenceTally.ForPlaceAsync(db, id, ct)));
    }

    /// <summary>
    /// The signed-in caller's id, or empty for a visitor.
    /// </summary>
    /// <remarks>
    /// This endpoint is anonymous and stays anonymous; the id is read only to answer "may you
    /// post" and to hide what this reader has blocked. Everything else it returns is the same for
    /// everybody, which is what makes it safe to use for the signed-in page as well.
    /// </remarks>
    private Guid GetCurrentUserIdOrEmpty()
        // Null-safe all the way down on purpose. This endpoint is reached with no HttpContext at
        // all in tests, and with no ClaimsPrincipal by a visitor; both are "no reader", and a
        // dereference here would turn an anonymous read into a 500 on the site's most public page.
        => Guid.TryParse(
            HttpContext?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            out var id) ? id : Guid.Empty;

    /// <summary>
    /// The latest posts about this place.
    /// </summary>
    /// <remarks>
    /// <para>Ben, 2026-09-17: a public location should be "actually public for adding files,
    /// messages etc". These are ordinary feed posts carrying a place, which is what gives them the
    /// media screener, the upload pause, reporting, hiding and the moderator queues for the cost of
    /// one column.</para>
    ///
    /// <para><b>Read through the feed's own visibility rule</b>, not a copy of it: hidden posts and
    /// scheduled-but-unreleased ones must disappear from here exactly as they do from the feed, and
    /// a second predicate is how that stops being true. Blocked authors are dropped for a signed-in
    /// reader the same way.</para>
    ///
    /// <para>Newest first, and only the most recent few — the place page is a summary, and "See all"
    /// goes to the feed filtered to this place.</para>
    /// </remarks>
    private static async Task<IReadOnlyList<FeedPostRecord>> PostsAsync(
        BenDataContext db, Guid placeId, Guid readerId, CancellationToken ct)
    {
        // Place posts are feed posts and follow the feed's switch, here as in the signed-in copy.
        if (!await Services.SiteSettingsService.GetBoolAsync(
                db, Services.SiteSettingKeys.FeaturePublicFeed, whenUnset: false, ct))
        {
            return [];
        }

        // Straight into the feed's own reader rather than a query of its own. It owns the
        // visibility predicate, the block list and the record mapper, and a place page with its
        // own copy of any of the three is a page that stops agreeing with the feed.
        return await FeedController.LatestForPlaceAsync(db, placeId, readerId, PlacePostsShown, ct);
    }

    /// <summary>How many of a place's posts the page shows before "See all".</summary>
    private const int PlacePostsShown = 20;

    /// <summary>
    /// The cases published at this place, by any group.
    /// </summary>
    /// <remarks>
    /// <para>Cases learned to name a place on 2026-09-17, which is what makes this possible: until
    /// then a case's address and a place's address were two strings nothing joined. This is the
    /// reading half of that — "who has worked here" now includes the written-up cases, not only the
    /// visits.</para>
    ///
    /// <para><b>Published means the same thing it means everywhere else</b> — the flag AND a status
    /// of Public or Haunted. Two other readers had that wrong until the same day; this one is
    /// written the long way rather than borrowing either of them.</para>
    ///
    /// <para>Real client names never appear: a private-engagement case's prose is redacted through
    /// the same roster the case's own public page uses. A private-lane case can only be here if its
    /// group holds the plan to publish one, which item 184 gates at the publish.</para>
    /// </remarks>
    private static async Task<IReadOnlyList<PublicPlaceCaseRow>> PublishedCasesAsync(
        BenDataContext db, Guid placeId, CancellationToken ct)
    {
        var cases = await db.Cases.AsNoTracking()
            .Where(c => c.PlaceId == placeId
                     && c.IsPublic
                     && (c.Status == CaseStatus.Public || c.Status == CaseStatus.Haunted))
            .OrderByDescending(c => c.DateCaseOpened)
            .Select(c => new
            {
                c.Id, c.Title, c.UrlName, c.CaseYear, c.OrgCaseNumber, c.Status,
                c.DateCaseOpened,
                OrganizationName = c.Organization.Name,
                OrganizationUrlName = c.Organization.UrlName,
            })
            .ToListAsync(ct);

        var rosters = await CaseRedactionRoster.ForCasesAsync(
            db, cases.Select(c => c.Id).ToList(), ct);

        return cases.Select(c => new PublicPlaceCaseRow(
                CaseReference: $"#{c.CaseYear}-{c.OrgCaseNumber:D3}",
                // Falls back to the reference for a case published before slugs existed, so a row
                // always has somewhere to point rather than linking nowhere.
                UrlName: c.UrlName ?? $"{c.CaseYear}-{c.OrgCaseNumber:D3}",
                Title: CaseProseRedactor.RedactFor(rosters, c.Id, c.Title)!,
                Status: c.Status,
                OpenedYear: c.DateCaseOpened.Year,
                OrganizationName: c.OrganizationName,
                OrganizationUrlName: c.OrganizationUrlName))
            .ToList();
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
    {
        var rows = await db.FieldSessionUploads.AsNoTracking()
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
                    // Same rule as ArchiveMediaPublication: a recording sent on its own goes by
                    // its UploadFile's id, one inside the session's .ben by its row's id — the
                    // media route takes either. The name and type are finished below, where
                    // Path.GetFileName can run.
                    ? s.Files
                        .Where(f => f.UploadFileId != null || f.BundleEntryPath != null)
                        .OrderBy(f => f.RelativePath)
                        .Select(f => new ArchiveMediaItem(
                            f.UploadFileId ?? f.Id, f.RelativePath,
                            f.UploadFile != null ? f.UploadFile.ContentType : (f.ContentType ?? ""),
                            f.UploadFile != null ? f.UploadFile.FileName : f.RelativePath))
                        .ToList()
                    : new List<ArchiveMediaItem>()))
            .ToListAsync(ct);
        return rows.Select(r => r with { Media = r.Media!.Select(Finish).ToList() }).ToList();
    }

    /// <summary>A bundle member's row carries a path, not a name, and may carry no type.</summary>
    private static ArchiveMediaItem Finish(ArchiveMediaItem m) => m with
    {
        ContentType = string.IsNullOrEmpty(m.ContentType)
            ? Services.FieldSessionFileGuard.ContentTypeFor(m.RelativePath) : m.ContentType,
        FileName = Path.GetFileName(m.FileName),
    };
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
    IReadOnlyList<PlaceEvidenceRow>? EventEvidence = null,
    /// <summary>Cases any group has published at this place (2026-09-17). Trailing and optional,
    /// so an older client simply does not draw the section.</summary>
    IReadOnlyList<PublicPlaceCaseRow>? Cases = null,
    /// <summary>The latest posts about this place (2026-09-17), newest first.</summary>
    IReadOnlyList<FeedPostRecord>? Posts = null,
    /// <summary>Whether this reader may add one. False for a visitor and at a private residence.</summary>
    bool CanPost = false,
    /// <summary>
    /// Files people added straight to this place, with no investigation behind them (item 250).
    /// </summary>
    /// <remarks>
    /// Trailing and optional, like every addition before it, so a client built before this simply
    /// does not draw the section. Distinct from <c>EventEvidence</c>, which is what a guest
    /// offered at an event — same place, different route, and the page says which.
    /// </remarks>
    IReadOnlyList<Ben.Service.Models.Entities.PlaceAddedEvidenceRow>? AddedEvidence = null,
    /// <summary>Whether this reader may add a file here. False for a visitor and off a public location.</summary>
    bool CanAddEvidence = false,
    /// <summary>
    /// What the evidence here adds up to across every route (item 250). Trailing and optional.
    /// </summary>
    Ben.Service.Models.Entities.PlaceEvidenceFigures? Figures = null);

/// <summary>
/// One published case at a place, as a visitor sees it listed.
/// </summary>
/// <param name="Title">Already redacted — a real client name never reaches here.</param>
/// <param name="OpenedYear">A year rather than a date: when a haunting was reported is the
/// client's business, and the year is what makes a list of them read as a history.</param>
public sealed record PublicPlaceCaseRow(
    string CaseReference,
    string UrlName,
    string Title,
    CaseStatus Status,
    int OpenedYear,
    string OrganizationName,
    string OrganizationUrlName);

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
