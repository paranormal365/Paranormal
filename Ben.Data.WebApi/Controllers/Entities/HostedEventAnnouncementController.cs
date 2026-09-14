using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// A host writing to everybody coming to an event (item 235 phase 17a, audit finding A4).
/// </summary>
/// <remarks>
/// <para><b>Who may write.</b> Whoever may decide the bookings — the people already trusted with the guest list and its
/// addresses. A steward handed the door is not, and the venue's lent staff are not: a letter to every guest is the
/// organizer's voice.</para>
///
/// <para><b>Two doors to each guest.</b> An email where the site has outgoing mail, and a message in the guest's bell
/// on this site either way, so a letter still reaches somebody whose mail filter ate it.</para>
/// </remarks>
[Route("api/organizations/{orgId:guid}/events/{eventId:guid}/announcements")]
public sealed class HostedEventAnnouncementController : OrgCmsControllerBase
{
    private readonly Services.Access.HostedEventAccess _access;
    private readonly EventGuestMailer _mail;
    private readonly PlatformMessageService _messages;

    public HostedEventAnnouncementController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper, IOrganizationSecurityService security,
        Services.Access.HostedEventAccess access, EventGuestMailer mail, PlatformMessageService messages)
        : base(dbFactory, mapper, security)
    {
        _access = access;
        _mail = mail;
        _messages = messages;
    }

    /// <summary>Everything written to this event's guests, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<HostedEventAnnouncementsRecord>> Get(Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanDecideBookingsAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();
        if (!await db.HostedEvents.AnyAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct)) return NotFound();

        return Ok(new HostedEventAnnouncementsRecord(await ListAsync(db, eventId, ct)));
    }

    /// <summary>How many parties, and people, a letter with these choices would reach — shown before it is sent.</summary>
    [HttpGet("audience")]
    public async Task<ActionResult<HostedEventAnnouncementAudienceRecord>> Audience(
        Guid orgId, Guid eventId, [FromQuery] Guid? night, [FromQuery] bool includeUnconfirmed, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanDecideBookingsAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();
        if (!await db.HostedEvents.AnyAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct)) return NotFound();

        var recipients = await HostedEventAnnouncements.RecipientsAsync(db, eventId, night, includeUnconfirmed, ct);
        return Ok(new HostedEventAnnouncementAudienceRecord(recipients.Count, recipients.Sum(r => r.People)));
    }

    [HttpPost]
    public async Task<ActionResult<HostedEventAnnouncementsRecord>> Send(
        Guid orgId, Guid eventId, [FromBody] SendHostedEventAnnouncementRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanDecideBookingsAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();

        var ev = await db.HostedEvents
            .Include(e => e.Organization)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (ev is null) return NotFound();

        if (request.HostedEventNightId is { } nightId
            && !await db.HostedEventNights.AnyAsync(n => n.Id == nightId && n.HostedEventId == eventId, ct))
            return BadRequest("That isn't a date of this event.");

        var now = DateTime.UtcNow;
        if (await HostedEventAnnouncements.WhyNotAsync(db, eventId,
                new HostedEventAnnouncements.SendHostedEventAnnouncementLimits(request.Subject, request.Body), now, ct) is { } why)
            return BadRequest(why);

        var recipients = await HostedEventAnnouncements.RecipientsAsync(
            db, eventId, request.HostedEventNightId, request.IncludeUnconfirmed, ct);
        if (recipients.Count == 0)
            return Conflict(request.HostedEventNightId is null
                ? "Nobody has a place yet, so there's nobody to write to."
                : "Nobody has a place on that date, so there's nobody to write to.");

        var subject = request.Subject.Trim();
        var body = request.Body.Trim();

        var letter = new HostedEventAnnouncement
        {
            Id = Guid.NewGuid(), HostedEventId = eventId, Subject = subject, Body = body,
            HostedEventNightId = request.HostedEventNightId, IncludeUnconfirmed = request.IncludeUnconfirmed,
            Recipients = recipients.Count, SentByAppUserId = userId.Value, SentUtc = now,
            DateCreated = now, CreatedByAppUserId = userId.Value,
        };
        db.HostedEventAnnouncements.Add(letter);
        await db.SaveChangesAsync(ct);
        if (HttpContext?.RequestServices?.GetService<IAuditLogService>() is { } audit)
            await TryAuditAsync(audit.LogCreateAsync(nameof(HostedEventAnnouncement), letter.Id, letter, userId.Value,
                Ben.Data.Common.Constants.AppSources.WebApi));

        // After the record, so a letter that went is never missing from the history; the count is corrected once known.
        letter.Emailed = await _mail.SendAnnouncementAsync(ev, ev.Organization.Name, ev.Organization.PublicEmail,
            subject, body, recipients, ct);
        await _messages.SendAsync($"{ev.Name}: {subject}", $"{body}\n\n— {ev.Organization.Name}",
            [.. recipients.Select(r => r.LeadAppUserId)], userId.Value, ct);
        await db.SaveChangesAsync(ct);

        var note = letter.Emailed == recipients.Count
            ? $"Sent to {Parties(recipients.Count)} — by email and in their messages here."
            : letter.Emailed == 0
                ? $"Put in the messages of {Parties(recipients.Count)} here. No email went out, so they'll see it when they next visit."
                : $"Put in the messages of {Parties(recipients.Count)} here, and emailed {letter.Emailed} of them.";

        return Ok(new HostedEventAnnouncementsRecord(await ListAsync(db, eventId, ct), note));
    }

    private static string Parties(int n) => n == 1 ? "1 party" : $"{n} parties";

    private static async Task<IReadOnlyList<HostedEventAnnouncementRecord>> ListAsync(BenDataContext db, Guid eventId, CancellationToken ct)
        => await db.HostedEventAnnouncements.AsNoTracking()
            .Where(a => a.HostedEventId == eventId)
            .OrderByDescending(a => a.SentUtc)
            .Take(100)
            .Select(a => new HostedEventAnnouncementRecord(
                a.Id, a.Subject, a.Body, a.HostedEventNightId,
                a.HostedEventNight != null ? a.HostedEventNight.Date : null,
                a.IncludeUnconfirmed, a.Recipients, a.Emailed,
                a.SentByAppUser.DisplayName ?? a.SentByAppUser.Email ?? "Somebody", a.SentUtc))
            .ToListAsync(ct);
}
