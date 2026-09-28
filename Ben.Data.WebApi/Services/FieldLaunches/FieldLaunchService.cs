using Ben.Data.Common.Enums;
using Ben.Data.Common.Helpers;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Push;
using Ben.Service.Models.FieldLaunches;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.FieldLaunches;

/// <summary>
/// A lead starting everybody's Field Kit at once (item 252): who may, what they may start, who it
/// reaches, and the card in the feed for anybody who missed the push.
/// </summary>
/// <remarks>
/// <para><b>Who may launch</b> (Ben, 2026-09-28: the lead plus the group's managers):</para>
/// <list type="bullet">
/// <item>an investigation — whoever may manage it (<see cref="InvestigationAccess.CanManageAsync"/>:
/// its lead, its creator, the case manager, the group's owners and administrators);</item>
/// <item>a tour date or public calendar event — its guides, and whoever may edit the group's
/// calendar;</item>
/// <item>a hosted event — whoever may edit it, and staff who run its door.</item>
/// </list>
///
/// <para><b>When:</b> from three hours before it starts until it ends. A launch on the wrong day
/// would push a crowd of people about something that is not happening.</para>
///
/// <para><b>Who it reaches:</b> everybody registered — an investigation's attendees who have not
/// declined, its guest-pass holders, and the accepted attendees of the event it is at; a tour
/// date's or event's accepted attendees; a hosted event's confirmed bookings, lead and named
/// guests. Never the lead who pressed it. Only those signed in on the app get a push; everybody on
/// the list sees the card.</para>
///
/// <para><b>The card</b> is a feed post written as the lead (the site has no robot account). Public
/// when the thing is public; otherwise a <see cref="OrgMessageChannel.FieldLaunchNotice"/> only its
/// people can read. Either way it goes six hours after the thing ends.</para>
/// </remarks>
public sealed class FieldLaunchService
{
    public static readonly TimeSpan OpensBefore = TimeSpan.FromHours(3);
    public static readonly TimeSpan StaysAfter = TimeSpan.FromHours(6);
    public static readonly TimeSpan RelaunchAfter = TimeSpan.FromMinutes(10);

    /// <summary>An investigation with no end time is taken to run this long.</summary>
    public static readonly TimeSpan OpenEndedRuns = TimeSpan.FromHours(6);

    public const string InvestigationTarget = "investigation";
    public const string EventTarget = "event";
    public const string HostedEventTarget = "hosted-event";

    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IOrganizationSecurityService _security;
    private readonly HostedEventAccess _hostedAccess;
    private readonly PushNotifier _push;
    private readonly TimeProvider _clock;

    public FieldLaunchService(
        IDbContextFactory<BenDataContext> db, IOrganizationSecurityService security,
        HostedEventAccess hostedAccess, PushNotifier push, TimeProvider? clock = null)
    {
        _db = db;
        _security = security;
        _hostedAccess = hostedAccess;
        _push = push;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>The link that opens Field Kit on a launch, from a push, a card or anywhere else.</summary>
    public static string AppLink(Guid launchId) => $"ishaunted://field-kit/launch/{launchId}";

    public static string TargetName(FieldLaunchTarget target) => target switch
    {
        FieldLaunchTarget.Investigation => InvestigationTarget,
        FieldLaunchTarget.CalendarEvent => EventTarget,
        _ => HostedEventTarget,
    };

    public static FieldLaunchTarget? ParseTarget(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        InvestigationTarget => FieldLaunchTarget.Investigation,
        EventTarget => FieldLaunchTarget.CalendarEvent,
        HostedEventTarget => FieldLaunchTarget.HostedEvent,
        _ => null,
    };

    /// <summary>One thing that could be launched, resolved to what a launch needs.</summary>
    private sealed record Candidate(
        FieldLaunchTarget Target, Guid Id, Guid OrganizationId, string Title, string? LocationLabel,
        DateTime StartsUtc, DateTime EndsUtc, string? TimeZoneId, bool IsPublic);

    // ── What the caller may launch now ────────────────────────────────────────

    /// <summary>
    /// Everything the caller may launch right now, soonest first — the lead's list in the app.
    /// </summary>
    /// <remarks>
    /// Asked of the groups the caller belongs to and the things they are named on, not of the
    /// whole site: a SuperAdmin may launch anything when asked directly, but their list is their
    /// own, not every hunt in the country.
    /// </remarks>
    public async Task<IReadOnlyList<LaunchableRecord>> LaunchableAsync(Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty) return [];
        var now = _clock.GetUtcNow().UtcDateTime;
        var opensBy = now + OpensBefore;
        await using var db = await _db.CreateDbContextAsync(ct);

        var myOrgs = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Select(m => m.OrganizationId).ToListAsync(ct);
        var leadOf = db.InvestigationAttendees.AsNoTracking()
            .Where(a => a.AppUserId == userId && a.IsLead).Select(a => a.InvestigationId);
        var guideOf = db.OrgCalendarEventGuides.AsNoTracking()
            .Where(g => g.AppUserId == userId).Select(g => g.OrgCalendarEventId);
        var staffOf = db.HostedEventStaff.AsNoTracking()
            .Where(s => s.AppUserId == userId && s.DateConfirmed != null).Select(s => s.HostedEventId);

        var candidates = new List<Candidate>();

        var investigationIds = await db.Investigations.AsNoTracking()
            .Where(i => i.Status != InvestigationStatus.Cancelled && i.Status != InvestigationStatus.Completed
                     && i.ScheduledDateTime <= opensBy
                     && (myOrgs.Contains(i.OrganizationId) || leadOf.Contains(i.Id) || i.CreatedByAppUserId == userId))
            .Select(i => i.Id).ToListAsync(ct);
        foreach (var id in investigationIds)
            if (await ResolveAsync(db, FieldLaunchTarget.Investigation, id, ct) is { } c && Open(c, now)) candidates.Add(c);

        var eventIds = await db.OrgCalendarEvents.AsNoTracking()
            .Where(e => e.HostedEventId == null && (e.TourId != null || e.IsPublic)
                     && e.StartDateTime <= opensBy && e.EndDateTime >= now
                     && (myOrgs.Contains(e.OrganizationId) || guideOf.Contains(e.Id)))
            .Select(e => e.Id).ToListAsync(ct);
        foreach (var id in eventIds)
            if (await ResolveAsync(db, FieldLaunchTarget.CalendarEvent, id, ct) is { } c && Open(c, now)) candidates.Add(c);

        var hostedIds = await db.HostedEvents.AsNoTracking()
            .Where(h => (h.LifecycleState == HostedEventLifecycleState.Published || h.LifecycleState == HostedEventLifecycleState.Live)
                     && (myOrgs.Contains(h.OrganizationId) || staffOf.Contains(h.Id)))
            .Select(h => h.Id).ToListAsync(ct);
        foreach (var id in hostedIds)
            if (await ResolveAsync(db, FieldLaunchTarget.HostedEvent, id, ct) is { } c && Open(c, now)) candidates.Add(c);

        var result = new List<LaunchableRecord>();
        foreach (var c in candidates.OrderBy(c => c.StartsUtc))
        {
            if (!await MayLaunchAsync(db, c, userId, isSuperAdmin: false, ct)) continue;
            var people = (await RecipientsAsync(db, c, ct)).Count(p => p != userId);
            var last = await LastLaunchAsync(db, c, ct);
            result.Add(new LaunchableRecord(TargetName(c.Target), c.Id, c.Title, c.StartsUtc, c.EndsUtc,
                c.TimeZoneId, people, last, c.IsPublic));
        }
        return result;
    }

    // ── Pressing Launch ──────────────────────────────────────────────────────

    /// <summary>What pressing Launch came to.</summary>
    public abstract record LaunchResult
    {
        public sealed record Launched(LaunchOutcomeRecord Outcome) : LaunchResult;
        public sealed record NotFound : LaunchResult;
        public sealed record Forbidden : LaunchResult;
        /// <summary>Refused, with the sentence the lead is shown.</summary>
        public sealed record Refused(string Reason) : LaunchResult;
    }

    public async Task<LaunchResult> LaunchAsync(
        Guid userId, bool isSuperAdmin, FieldLaunchTarget target, Guid id, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        await using var db = await _db.CreateDbContextAsync(ct);

        var candidate = await ResolveAsync(db, target, id, ct);
        if (candidate is null) return new LaunchResult.NotFound();
        if (!await MayLaunchAsync(db, candidate, userId, isSuperAdmin, ct)) return new LaunchResult.Forbidden();
        if (!Open(candidate, now))
            return new LaunchResult.Refused(now < candidate.StartsUtc - OpensBefore
                ? "It can be launched from three hours before it starts."
                : "It has ended, so there is nothing to launch.");
        if (await LastLaunchAsync(db, candidate, ct) is { } last && now - last < RelaunchAfter)
            return new LaunchResult.Refused(
                "It was launched a few minutes ago. Everybody has the card in their feed if they missed the push.");

        var recipients = (await RecipientsAsync(db, candidate, ct)).Where(p => p != userId).Distinct().ToList();
        var endsUtc = candidate.EndsUtc;
        var expires = endsUtc + StaysAfter;

        var launch = new FieldLaunch
        {
            Id = Guid.NewGuid(),
            OrganizationId = candidate.OrganizationId,
            Target = candidate.Target,
            InvestigationId = candidate.Target == FieldLaunchTarget.Investigation ? candidate.Id : null,
            OrgCalendarEventId = candidate.Target == FieldLaunchTarget.CalendarEvent ? candidate.Id : null,
            HostedEventId = candidate.Target == FieldLaunchTarget.HostedEvent ? candidate.Id : null,
            Title = Clip(candidate.Title, 300),
            LocationLabel = candidate.LocationLabel is null ? null : Clip(candidate.LocationLabel, 300),
            LaunchedByAppUserId = userId,
            LaunchedUtc = now,
            EndsUtc = endsUtc,
            ExpiresUtc = expires,
            IsPublic = candidate.IsPublic,
            PeopleCount = recipients.Count,
        };
        foreach (var person in recipients)
            launch.Recipients.Add(new FieldLaunchRecipient { Id = Guid.NewGuid(), FieldLaunchId = launch.Id, AppUserId = person });

        var post = new OrgMessage
        {
            Id = Guid.NewGuid(),
            ChannelType = candidate.IsPublic ? OrgMessageChannel.PublicFeed : OrgMessageChannel.FieldLaunchNotice,
            AuthorAppUserId = userId,
            Body = CardText(candidate.Title),
            IsPublic = candidate.IsPublic,
            DateCreated = now,
            ExpiresUtc = expires,
            FieldLaunchId = launch.Id,
        };
        launch.FeedPostId = post.Id;

        db.FieldLaunches.Add(launch);
        db.OrgMessages.Add(post);
        await db.SaveChangesAsync(ct);

        var launcherName = await NameAsync(db, userId, ct);
        var fanOut = await _push.SendAsync(recipients, new PushMessage(
            Title: $"{candidate.Title} is starting",
            Body: $"{launcherName} started the hunt. Tap to open Field Kit.",
            Data: new Dictionary<string, string> { ["link"] = AppLink(launch.Id), ["launchId"] = launch.Id.ToString() },
            CollapseId: $"launch-{launch.Id:N}",
            ExpiresAt: new DateTimeOffset(DateTime.SpecifyKind(expires, DateTimeKind.Utc))), ct);

        launch.PeopleWithTheApp = fanOut.PeopleWithTheApp;
        launch.PhonesReached = fanOut.Delivered;
        await db.SaveChangesAsync(ct);

        var record = await ToRecordAsync(db, launch, ct);
        return new LaunchResult.Launched(new LaunchOutcomeRecord(
            record, fanOut.People, fanOut.PeopleWithTheApp, fanOut.Delivered, fanOut.Configured));
    }

    /// <summary>What the card says. Plain text, like every feed post.</summary>
    public static string CardText(string title) =>
        Clip($"{title} is starting now. Open Field Kit to record it — every session you take is kept on your phone until you send it.", 1000);

    // ── Reading launches ─────────────────────────────────────────────────────

    /// <summary>
    /// One launch, for the app to open Field Kit on — or null, which the endpoint answers as 404:
    /// gone, expired, or private and not the reader's. The three are not told apart.
    /// </summary>
    public async Task<FieldLaunchRecord?> ReadAsync(Guid launchId, Guid readerId, bool isSuperAdmin, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        await using var db = await _db.CreateDbContextAsync(ct);
        var launch = await db.FieldLaunches.AsNoTracking().FirstOrDefaultAsync(l => l.Id == launchId, ct);
        if (launch is null || launch.ExpiresUtc <= now) return null;
        if (!launch.IsPublic && !isSuperAdmin
            && launch.LaunchedByAppUserId != readerId
            && (readerId == Guid.Empty
                || !await db.FieldLaunchRecipients.AsNoTracking()
                        .AnyAsync(r => r.FieldLaunchId == launchId && r.AppUserId == readerId, ct)))
            return null;
        return await ToRecordAsync(db, launch, ct);
    }

    /// <summary>The launches still open for this person, newest first — "happening now" in Field Kit.</summary>
    public async Task<IReadOnlyList<FieldLaunchRecord>> MineAsync(Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty) return [];
        var now = _clock.GetUtcNow().UtcDateTime;
        await using var db = await _db.CreateDbContextAsync(ct);
        var mine = db.FieldLaunchRecipients.AsNoTracking().Where(r => r.AppUserId == userId).Select(r => r.FieldLaunchId);
        var launches = await db.FieldLaunches.AsNoTracking()
            .Where(l => l.ExpiresUtc > now && (l.LaunchedByAppUserId == userId || mine.Contains(l.Id)))
            .OrderByDescending(l => l.LaunchedUtc)
            .ToListAsync(ct);
        var records = new List<FieldLaunchRecord>();
        foreach (var launch in launches) records.Add(await ToRecordAsync(db, launch, ct));
        return records;
    }

    // ── The rules ────────────────────────────────────────────────────────────

    private static bool Open(Candidate c, DateTime now) => now >= c.StartsUtc - OpensBefore && now <= c.EndsUtc;

    private async Task<Candidate?> ResolveAsync(BenDataContext db, FieldLaunchTarget target, Guid id, CancellationToken ct)
    {
        switch (target)
        {
            case FieldLaunchTarget.Investigation:
            {
                var i = await db.Investigations.AsNoTracking()
                    .Where(x => x.Id == id)
                    .Select(x => new
                    {
                        x.Id, x.OrganizationId, x.Title, x.Location, x.ScheduledDateTime, x.EndDateTime, x.Status,
                        x.Visibility, x.TimeZoneId, OrgZone = x.Organization.TimeZoneId,
                    })
                    .FirstOrDefaultAsync(ct);
                if (i is null || i.Status is InvestigationStatus.Cancelled) return null;
                var starts = AsUtc(i.ScheduledDateTime);
                var ends = i.EndDateTime is { } end && AsUtc(end) > starts ? AsUtc(end) : starts + OpenEndedRuns;
                return new Candidate(target, i.Id, i.OrganizationId, i.Title, i.Location, starts, ends,
                    i.TimeZoneId ?? i.OrgZone, i.Visibility == InvestigationVisibility.Public);
            }
            case FieldLaunchTarget.CalendarEvent:
            {
                var e = await db.OrgCalendarEvents.AsNoTracking()
                    .Where(x => x.Id == id && x.HostedEventId == null)
                    .Select(x => new
                    {
                        x.Id, x.OrganizationId, x.Title, x.Location, x.HideExactLocation, x.StartDateTime,
                        x.EndDateTime, x.TimeZoneId, x.IsPublic, x.TourId, TourName = x.Tour != null ? x.Tour.Name : null,
                    })
                    .FirstOrDefaultAsync(ct);
                if (e is null) return null;
                var title = e.TourName is { Length: > 0 } tour && !e.Title.Contains(tour, StringComparison.OrdinalIgnoreCase)
                    ? $"{tour}: {e.Title}" : e.Title;
                return new Candidate(target, e.Id, e.OrganizationId, title, e.HideExactLocation ? null : e.Location,
                    AsUtc(e.StartDateTime), AsUtc(e.EndDateTime), e.TimeZoneId, e.IsPublic);
            }
            default:
            {
                var h = await db.HostedEvents.AsNoTracking()
                    .Where(x => x.Id == id)
                    .Select(x => new
                    {
                        x.Id, x.OrganizationId, x.Name, x.StartsOn, x.EndsOn, x.TimeZoneId, x.LifecycleState,
                        x.HideExactLocation, PlaceName = x.Place.Name,
                    })
                    .FirstOrDefaultAsync(ct);
                if (h is null || h.LifecycleState is not (HostedEventLifecycleState.Published or HostedEventLifecycleState.Live))
                    return null;
                // Its nights are local dates on its own clock: open from the first morning to the
                // last midnight there.
                var zone = Zones.Find(h.TimeZoneId);
                var starts = Zones.ToUtc(h.StartsOn.Date, zone);
                var ends = Zones.ToUtc(h.EndsOn.Date.AddDays(1), zone);
                return new Candidate(target, h.Id, h.OrganizationId, h.Name, h.HideExactLocation ? null : h.PlaceName,
                    starts, ends, h.TimeZoneId, IsPublic: true);
            }
        }
    }

    private async Task<bool> MayLaunchAsync(BenDataContext db, Candidate c, Guid userId, bool isSuperAdmin, CancellationToken ct)
    {
        if (isSuperAdmin) return true;
        if (userId == Guid.Empty) return false;
        switch (c.Target)
        {
            case FieldLaunchTarget.Investigation:
                return await InvestigationAccess.CanManageAsync(db, c.Id, userId, isSuperAdmin: false, ct);
            case FieldLaunchTarget.CalendarEvent:
                if (await db.OrgCalendarEventGuides.AsNoTracking().AnyAsync(g => g.OrgCalendarEventId == c.Id && g.AppUserId == userId, ct))
                    return true;
                return await IsOwnerOrAdministratorAsync(db, c.OrganizationId, userId, ct)
                    || await _security.HasAccessAsync(userId, c.OrganizationId,
                           OrganizationSecurityTable.OrgCalendar, OrganizationSecurityAction.Update, ct);
            default:
                return await _hostedAccess.CanEditEventAsync(userId, c.OrganizationId, ct)
                    || await _hostedAccess.CanRunTheDoorAsync(userId, c.OrganizationId, c.Id, db, ct);
        }
    }

    private static Task<bool> IsOwnerOrAdministratorAsync(BenDataContext db, Guid orgId, Guid userId, CancellationToken ct)
        => db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive
                        && (m.Role == OrganizationMemberRole.Owner || m.Role == OrganizationMemberRole.Administrator), ct);

    /// <summary>Everybody registered for it — who a launch goes to.</summary>
    private static async Task<List<Guid>> RecipientsAsync(BenDataContext db, Candidate c, CancellationToken ct)
    {
        switch (c.Target)
        {
            case FieldLaunchTarget.Investigation:
            {
                var people = await db.InvestigationAttendees.AsNoTracking()
                    .Where(a => a.InvestigationId == c.Id && a.Rsvp != RsvpStatus.Declined)
                    .Select(a => a.AppUserId).ToListAsync(ct);
                people.AddRange(await db.InvestigationGuestPasses.AsNoTracking()
                    .Where(p => p.InvestigationId == c.Id && p.RevokedUtc == null)
                    .Select(p => p.AppUserId).ToListAsync(ct));
                // "A public investigation at a public event": the event's people are at the hunt too.
                var eventId = await db.Investigations.AsNoTracking()
                    .Where(i => i.Id == c.Id).Select(i => i.OrgCalendarEventId).FirstOrDefaultAsync(ct);
                if (eventId is { } onEvent)
                    people.AddRange(await db.OrgCalendarEventAttendees.AsNoTracking()
                        .Where(a => a.OrgCalendarEventId == onEvent && a.RsvpStatus == RsvpStatus.Accepted)
                        .Select(a => a.AppUserId).ToListAsync(ct));
                return people.Distinct().ToList();
            }
            case FieldLaunchTarget.CalendarEvent:
                return await db.OrgCalendarEventAttendees.AsNoTracking()
                    .Where(a => a.OrgCalendarEventId == c.Id && a.RsvpStatus == RsvpStatus.Accepted)
                    .Select(a => a.AppUserId).Distinct().ToListAsync(ct);
            default:
            {
                var confirmed = db.HostedEventBookings.AsNoTracking()
                    .Where(b => b.HostedEventId == c.Id && b.Status == HostedEventBookingStatus.Confirmed);
                var people = await confirmed.Select(b => b.LeadAppUserId).ToListAsync(ct);
                people.AddRange(await db.HostedEventBookingGuests.AsNoTracking()
                    .Where(g => g.AppUserId != null && confirmed.Any(b => b.Id == g.HostedEventBookingId))
                    .Select(g => g.AppUserId!.Value).ToListAsync(ct));
                return people.Distinct().ToList();
            }
        }
    }

    private static Task<DateTime?> LastLaunchAsync(BenDataContext db, Candidate c, CancellationToken ct)
        => db.FieldLaunches.AsNoTracking()
            .Where(l => l.Target == c.Target
                     && (l.InvestigationId == c.Id || l.OrgCalendarEventId == c.Id || l.HostedEventId == c.Id))
            .OrderByDescending(l => l.LaunchedUtc)
            .Select(l => (DateTime?)l.LaunchedUtc)
            .FirstOrDefaultAsync(ct);

    private static async Task<FieldLaunchRecord> ToRecordAsync(BenDataContext db, FieldLaunch launch, CancellationToken ct)
    {
        var orgName = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == launch.OrganizationId).Select(o => o.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        return new FieldLaunchRecord(
            launch.Id, TargetName(launch.Target), launch.InvestigationId, launch.OrgCalendarEventId, launch.HostedEventId,
            launch.Title, launch.LocationLabel, orgName, await NameAsync(db, launch.LaunchedByAppUserId, ct),
            AsUtc(launch.LaunchedUtc), AsUtc(launch.EndsUtc), AsUtc(launch.ExpiresUtc), launch.IsPublic, AppLink(launch.Id));
    }

    private static async Task<string> NameAsync(BenDataContext db, Guid userId, CancellationToken ct)
        => await db.AppUsers.AsNoTracking().Where(u => u.Id == userId)
               .Select(u => u.DisplayName ?? u.Handle).FirstOrDefaultAsync(ct) is { Length: > 0 } name
            ? name : "Your lead";

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
