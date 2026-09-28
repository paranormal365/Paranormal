using Ben.Data.Common.Enums;
using Ben.Data.Common.Helpers;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Investigations;
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
            // Any of the group's calendar events — a tour date, a public night, or the group's own
            // hunt ("a specific event"). A hosted event's umbrella row is launched as the hosted event.
            .Where(e => e.HostedEventId == null
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
            JoinToken = await MayBeJoinedByCodeAsync(db, candidate, ct) ? NewJoinToken() : null,
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
            CreatedByAppUserId = userId,
        };
        launch.FeedPostId = post.Id;

        db.FieldLaunches.Add(launch);
        db.OrgMessages.Add(post);
        await db.SaveChangesAsync(ct);

        var launcherName = await NameAsync(db, userId, ct);
        var fanOut = await _push.SendAsync(recipients, new PushMessage(
            Title: $"{candidate.Title} is starting",
            Body: $"{launcherName} started the group's session. Tap to join it in Field Kit.",
            Data: new Dictionary<string, string> { ["link"] = AppLink(launch.Id), ["launchId"] = launch.Id.ToString() },
            CollapseId: $"launch-{launch.Id:N}",
            ExpiresAt: new DateTimeOffset(DateTime.SpecifyKind(expires, DateTimeKind.Utc))), ct);

        launch.PeopleWithTheApp = fanOut.PeopleWithTheApp;
        launch.PhonesReached = fanOut.Delivered;
        await db.SaveChangesAsync(ct);

        var record = await ToRecordAsync(db, launch, forManager: true, ct);
        return new LaunchResult.Launched(new LaunchOutcomeRecord(
            record, fanOut.People, fanOut.PeopleWithTheApp, fanOut.Delivered, fanOut.Configured));
    }

    /// <summary>What the card says. Plain text, like every feed post.</summary>
    public static string CardText(string title) =>
        Clip($"{title} is starting now. Tap Join to open the group's session in Field Kit — nothing records until you press Start, and every session you take stays on your phone until you send it.", 1000);

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
        // Whoever may manage it opens it too — another guide, the group's owner — or nobody but the
        // launcher could see who is asking to join.
        var manages = await MayManageAsync(db, launch, readerId, isSuperAdmin, ct);
        if (!launch.IsPublic && !manages
            && (readerId == Guid.Empty
                || !await db.FieldLaunchRecipients.AsNoTracking()
                        .AnyAsync(r => r.FieldLaunchId == launchId && r.AppUserId == readerId, ct)))
            return null;
        return await ToRecordAsync(db, launch, manages, ct);
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
        foreach (var launch in launches) records.Add(await ToRecordAsync(db, launch, forManager: launch.LaunchedByAppUserId == userId, ct));
        return records;
    }

    // ── Joining by the lead's code ───────────────────────────────────────────

    /// <summary>The link that opens the lead's list of who is asking, from the lead's notification.</summary>
    public static string RequestsLink(Guid launchId) => $"ishaunted://field-kit/launch/{launchId}/requests";

    /// <summary>
    /// Where somebody who scanned the lead's code stands — or null for a code that is unknown,
    /// ended or never allowed, which the endpoint answers as 404.
    /// </summary>
    public async Task<JoinStandingRecord?> StandingAsync(string token, Guid readerId, bool isSuperAdmin, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        return await StandingAsync(db, token, readerId, isSuperAdmin, ct);
    }

    private async Task<JoinStandingRecord?> StandingAsync(
        BenDataContext db, string token, Guid readerId, bool isSuperAdmin, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        if (string.IsNullOrWhiteSpace(token)) return null;
        var launch = await db.FieldLaunches.AsNoTracking().FirstOrDefaultAsync(l => l.JoinToken == token, ct);
        if (launch is null || launch.ExpiresUtc <= now) return null;

        var orgName = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == launch.OrganizationId).Select(o => o.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        var lead = await NameAsync(db, launch.LaunchedByAppUserId, ct);
        JoinStandingRecord Standing(string standing, FieldLaunchRecord? record = null)
            => new(launch.Id, launch.Title, orgName, lead, standing, record);

        var isIn = launch.IsPublic || isSuperAdmin || launch.LaunchedByAppUserId == readerId
            || (readerId != Guid.Empty && await db.FieldLaunchRecipients.AsNoTracking()
                    .AnyAsync(r => r.FieldLaunchId == launch.Id && r.AppUserId == readerId, ct));
        if (isIn)
            return Standing("in", await ToRecordAsync(db, launch, await MayManageAsync(db, launch, readerId, isSuperAdmin, ct), ct));
        if (readerId == Guid.Empty) return Standing("sign-in");

        var asked = await db.FieldLaunchJoinRequests.AsNoTracking()
            .Where(r => r.FieldLaunchId == launch.Id && r.AppUserId == readerId)
            .Select(r => (FieldLaunchJoinStatus?)r.Status).FirstOrDefaultAsync(ct);
        return asked switch
        {
            FieldLaunchJoinStatus.Pending => Standing("pending"),
            FieldLaunchJoinStatus.Declined => Standing("declined"),
            _ => Standing("ask"),
        };
    }

    /// <summary>
    /// Asks the lead to be let in. Idempotent; a decline stands (asking again is asking the lead
    /// in person). The lead is told on their phone.
    /// </summary>
    public async Task<JoinStandingRecord?> AskAsync(string token, Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty) return null;
        await using var db = await _db.CreateDbContextAsync(ct);
        var standing = await StandingAsync(db, token, userId, isSuperAdmin: false, ct);
        if (standing is null || standing.Standing != "ask") return standing;

        var now = _clock.GetUtcNow().UtcDateTime;
        db.FieldLaunchJoinRequests.Add(new FieldLaunchJoinRequest
        {
            Id = Guid.NewGuid(), FieldLaunchId = standing.LaunchId, AppUserId = userId,
            Status = FieldLaunchJoinStatus.Pending, RequestedUtc = now,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Asked twice at once; the other one stands.
        }

        var launcher = await db.FieldLaunches.AsNoTracking()
            .Where(l => l.Id == standing.LaunchId).Select(l => l.LaunchedByAppUserId).FirstAsync(ct);
        var asker = await NameAsync(db, userId, ct, fallback: "Somebody");
        await _push.SendAsync([launcher], new PushMessage(
            Title: $"{asker} wants to join",
            Body: $"{standing.Title}: tap to let them in.",
            Data: new Dictionary<string, string> { ["link"] = RequestsLink(standing.LaunchId) },
            CollapseId: $"asks-{standing.LaunchId:N}"), ct);

        return standing with { Standing = "pending" };
    }

    /// <summary>Who has asked to join, for whoever may manage the launch — null for anybody else.</summary>
    public async Task<IReadOnlyList<JoinRequestRecord>?> RequestsAsync(
        Guid launchId, Guid userId, bool isSuperAdmin, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var launch = await db.FieldLaunches.AsNoTracking().FirstOrDefaultAsync(l => l.Id == launchId, ct);
        if (launch is null || !await MayManageAsync(db, launch, userId, isSuperAdmin, ct)) return null;

        var requests = await db.FieldLaunchJoinRequests.AsNoTracking()
            .Where(r => r.FieldLaunchId == launchId)
            .OrderBy(r => r.Status).ThenBy(r => r.RequestedUtc)
            .ToListAsync(ct);
        var people = requests.Select(r => r.AppUserId).ToList();
        var names = await db.AppUsers.AsNoTracking().Where(u => people.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.DisplayName ?? u.Handle })
            .ToDictionaryAsync(u => u.Id, u => u.Name ?? "Somebody", ct);
        return requests.Select(r => new JoinRequestRecord(
            r.Id, r.AppUserId, names.GetValueOrDefault(r.AppUserId, "Somebody"), StatusName(r.Status),
            AsUtc(r.RequestedUtc), r.DecidedUtc is { } d ? AsUtc(d) : null)).ToList();
    }

    /// <summary>What deciding a request came to.</summary>
    public abstract record DecideResult
    {
        public sealed record Decided(JoinRequestRecord Request) : DecideResult;
        public sealed record NotFound : DecideResult;
        public sealed record Forbidden : DecideResult;
        public sealed record Refused(string Reason) : DecideResult;
    }

    /// <summary>
    /// The lead's yes or no. A yes registers the person for the thing — a reserved seat on a tour
    /// date, an accepted place at an event, a guest pass for an investigation, and for a paid hosted
    /// event only this launch, never a booking — adds them to the launch, and tells their phone.
    /// </summary>
    public async Task<DecideResult> DecideAsync(
        Guid launchId, Guid requestId, Guid userId, bool isSuperAdmin, bool approve, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        await using var db = await _db.CreateDbContextAsync(ct);
        var launch = await db.FieldLaunches.FirstOrDefaultAsync(l => l.Id == launchId, ct);
        var request = await db.FieldLaunchJoinRequests.FirstOrDefaultAsync(r => r.Id == requestId && r.FieldLaunchId == launchId, ct);
        if (launch is null || request is null) return new DecideResult.NotFound();
        if (!await MayManageAsync(db, launch, userId, isSuperAdmin, ct)) return new DecideResult.Forbidden();

        if (request.Status != FieldLaunchJoinStatus.Pending)
            return new DecideResult.Decided(await RequestRecordAsync(db, request, ct));
        if (launch.ExpiresUtc <= now) return new DecideResult.Refused("It has ended, so there is nothing to join.");

        if (approve)
        {
            if (await RegisterAsync(db, launch, request.AppUserId, userId, now, ct) is { } refused)
                return new DecideResult.Refused(refused);
            if (!await db.FieldLaunchRecipients.AnyAsync(r => r.FieldLaunchId == launchId && r.AppUserId == request.AppUserId, ct))
                db.FieldLaunchRecipients.Add(new FieldLaunchRecipient { Id = Guid.NewGuid(), FieldLaunchId = launchId, AppUserId = request.AppUserId });
        }
        request.Status = approve ? FieldLaunchJoinStatus.Approved : FieldLaunchJoinStatus.Declined;
        request.DecidedUtc = now;
        request.DecidedByAppUserId = userId;
        await db.SaveChangesAsync(ct);

        if (approve)
            await _push.SendAsync([request.AppUserId], new PushMessage(
                Title: "You're in",
                Body: $"{launch.Title}: tap to join the group's session.",
                Data: new Dictionary<string, string> { ["link"] = AppLink(launch.Id), ["launchId"] = launch.Id.ToString() },
                CollapseId: $"in-{launch.Id:N}",
                ExpiresAt: new DateTimeOffset(DateTime.SpecifyKind(launch.ExpiresUtc, DateTimeKind.Utc))), ct);

        return new DecideResult.Decided(await RequestRecordAsync(db, request, ct));
    }

    /// <summary>Registers somebody the lead let in, as the thing itself registers people. Null, or why not.</summary>
    private static async Task<string?> RegisterAsync(
        BenDataContext db, FieldLaunch launch, Guid personId, Guid leadId, DateTime now, CancellationToken ct)
    {
        switch (launch.Target)
        {
            case FieldLaunchTarget.CalendarEvent when launch.OrgCalendarEventId is { } eventId:
            {
                var isTour = await db.OrgCalendarEvents.AsNoTracking()
                    .Where(e => e.Id == eventId).Select(e => e.TourId != null).FirstOrDefaultAsync(ct);
                var attendee = await db.OrgCalendarEventAttendees
                    .FirstOrDefaultAsync(a => a.OrgCalendarEventId == eventId && a.AppUserId == personId, ct);
                if (attendee is null)
                {
                    attendee = new OrgCalendarEventAttendee
                    {
                        Id = Guid.NewGuid(), OrgCalendarEventId = eventId, AppUserId = personId,
                        Seats = 1, DateCreated = now, CreatedByAppUserId = leadId,
                    };
                    db.OrgCalendarEventAttendees.Add(attendee);
                }
                attendee.RsvpStatus = RsvpStatus.Accepted;
                attendee.DateRsvp ??= now;
                if (isTour)
                {
                    attendee.SeatStatus = TourSeatStatus.Reserved;
                    attendee.Seats = Math.Max(1, attendee.Seats);
                    attendee.SeatDecidedUtc = now;
                    attendee.SeatDecidedByAppUserId = leadId;
                }
                return null;
            }
            case FieldLaunchTarget.Investigation when launch.InvestigationId is { } investigationId:
            {
                // The guest pass item 248's codes mint — the one credential an outsider holds for a
                // visit. A code the guide already holds up is reused: issuing one would take theirs away.
                var code = (await GuestCodes.LiveForAsync(db, investigationId, ct)).FirstOrDefault();
                if (code is null)
                {
                    var investigation = await db.Investigations.FirstAsync(i => i.Id == investigationId, ct);
                    code = await GuestCodes.IssueAsync(db, investigation, leadId, launch.ExpiresUtc, ct);
                }
                var name = await db.AppUsers.AsNoTracking().Where(u => u.Id == personId)
                    .Select(u => u.DisplayName ?? u.Handle).FirstOrDefaultAsync(ct);
                var (_, refused) = await GuestCodes.RedeemAsync(db, code, personId, name, ct);
                return refused;
            }
            default:
                // A hosted event is paid for: a yes lets them into this launch, and makes no booking.
                return null;
        }
    }

    private static async Task<JoinRequestRecord> RequestRecordAsync(BenDataContext db, FieldLaunchJoinRequest r, CancellationToken ct)
        => new(r.Id, r.AppUserId, await NameAsync(db, r.AppUserId, ct, fallback: "Somebody"), StatusName(r.Status),
               AsUtc(r.RequestedUtc), r.DecidedUtc is { } d ? AsUtc(d) : null);

    private static string StatusName(FieldLaunchJoinStatus status) => status switch
    {
        FieldLaunchJoinStatus.Approved => "approved",
        FieldLaunchJoinStatus.Declined => "declined",
        _ => "pending",
    };

    /// <summary>The launcher, or anybody who may launch the thing now — the lead's managers.</summary>
    private async Task<bool> MayManageAsync(BenDataContext db, FieldLaunch launch, Guid userId, bool isSuperAdmin, CancellationToken ct)
    {
        if (isSuperAdmin || (userId != Guid.Empty && launch.LaunchedByAppUserId == userId)) return true;
        if (userId == Guid.Empty) return false;
        var id = launch.InvestigationId ?? launch.OrgCalendarEventId ?? launch.HostedEventId;
        return id is { } targetId && await ResolveAsync(db, launch.Target, targetId, ct) is { } c
            && await MayLaunchAsync(db, c, userId, isSuperAdmin: false, ct);
    }

    /// <summary>
    /// Whether a stranger may ask to join by the lead's code. Not for a visit to somebody's home
    /// or a private client case — item 248's rule, because even the request screen names the visit.
    /// </summary>
    private static async Task<bool> MayBeJoinedByCodeAsync(BenDataContext db, Candidate c, CancellationToken ct)
    {
        if (c.Target != FieldLaunchTarget.Investigation) return true;
        var investigation = await db.Investigations.AsNoTracking().FirstOrDefaultAsync(i => i.Id == c.Id, ct);
        return investigation is not null && await GuestCodes.WhyACodeMayNotBeIssuedAsync(db, investigation, ct) is null;
    }

    private static string NewJoinToken()
        => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

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

    private static async Task<FieldLaunchRecord> ToRecordAsync(BenDataContext db, FieldLaunch launch, bool forManager, CancellationToken ct)
    {
        var orgName = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == launch.OrganizationId).Select(o => o.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        return new FieldLaunchRecord(
            launch.Id, TargetName(launch.Target), launch.InvestigationId, launch.OrgCalendarEventId, launch.HostedEventId,
            launch.Title, launch.LocationLabel, orgName, await NameAsync(db, launch.LaunchedByAppUserId, ct),
            AsUtc(launch.LaunchedUtc), AsUtc(launch.EndsUtc), AsUtc(launch.ExpiresUtc), launch.IsPublic, AppLink(launch.Id),
            forManager ? launch.JoinToken : null);
    }

    private static async Task<string> NameAsync(BenDataContext db, Guid userId, CancellationToken ct, string fallback = "Your lead")
        => await db.AppUsers.AsNoTracking().Where(u => u.Id == userId)
               .Select(u => u.DisplayName ?? u.Handle).FirstOrDefaultAsync(ct) is { Length: > 0 } name
            ? name : fallback;

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
