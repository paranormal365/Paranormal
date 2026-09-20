using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// Posts arrivals into one event's staff-room thread (item 238C).
/// </summary>
/// <remarks>
/// <para><b>One thread for the life of the event.</b> The root message carries the event and holds
/// the recipients; every arrival is a reply to it. A reply is not a second inbox row — it marks the
/// root unread again, which is what puts the thread back on the bell without the group's message
/// list filling up with one row per booking.</para>
///
/// <para><b>The audience is the one the board would accept</b>, asked through
/// <see cref="EventBookingRecipients"/>, so the thread can never show a booking to somebody the
/// booking board itself would refuse. Unlike the letters it does NOT skip people who turned alerts
/// off: turning off mail is a statement about mail, and the room existing is most of the point for
/// somebody who did.</para>
///
/// <para><b>Nothing here throws into the job.</b> A venue not hearing about its weekend is the
/// failure this feature exists to prevent; a thread that could not be written must not also stop
/// the letter that would have told them.</para>
/// </remarks>
public sealed class EventStaffRoomWriter
{
    private readonly SiteIdentity _site;
    private readonly ILogger<EventStaffRoomWriter> _logger;

    public EventStaffRoomWriter(IOptions<SiteIdentity> site, ILogger<EventStaffRoomWriter> logger)
    {
        _site = site.Value;
        _logger = logger;
    }

    /// <summary>
    /// Writes one reply about these bookings, creating the thread if it does not exist yet.
    /// </summary>
    /// <param name="postAs">
    /// Who the post is attributed to. <c>OrgMessage.AuthorAppUserId</c> is required and the site
    /// has no robot account, so this is the event's creator — the person whose event it is. A
    /// fabricated author id would break every screen that resolves a name.
    /// </param>
    /// <returns>True when a reply was written.</returns>
    public async Task<bool> PostArrivalsAsync(
        BenDataContext db,
        HostedEvent ev,
        IReadOnlyList<HostedEventBooking> bookings,
        IReadOnlyList<Guid> audience,
        Guid postAs,
        bool summary,
        DateTime now,
        CancellationToken ct)
    {
        var board = _site.AbsoluteUrl(
            $"/organizations/{ev.OrganizationId}/events/{ev.Id}/bookings");

        if (EventStaffRoom.Arrivals(ev, bookings, summary, board) is not { } html) return false;

        try
        {
            var root = await RootAsync(db, ev, postAs, now, ct);

            db.OrgMessages.Add(new OrgMessage
            {
                Id = Guid.NewGuid(),
                OrganizationId = ev.OrganizationId,
                HostedEventId = ev.Id,
                ParentMessageId = root.Id,
                ChannelType = OrgMessageChannel.EventStaffRoom,
                AuthorAppUserId = postAs,
                Body = html,
                IsPublic = false,
                DateCreated = now,
                CreatedByAppUserId = postAs,
            });

            await SyncAudienceAsync(db, root, audience, now, ct);

            // The thread has something new in it, so everybody's marker goes back to unread. This
            // is what reaches a member with no email address — the bell counts exactly these rows.
            foreach (var r in root.Recipients) r.DateRead = null;

            root.DateUpdated = now;

            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogWarning(e, "Could not post arrivals to the staff room for {EventId}.", ev.Id);
            return false;
        }
    }

    /// <summary>The event's thread, created on first need rather than when the event is.</summary>
    /// <remarks>
    /// An event that never takes a booking never gets a thread, which is the difference between a
    /// message list that means something and one with an empty row for every event ever created.
    /// </remarks>
    private async Task<OrgMessage> RootAsync(
        BenDataContext db, HostedEvent ev, Guid postAs, DateTime now, CancellationToken ct)
    {
        var root = await db.OrgMessages
            .Include(m => m.Recipients)
            .FirstOrDefaultAsync(m => m.HostedEventId == ev.Id
                                   && m.ParentMessageId == null
                                   && m.ChannelType == OrgMessageChannel.EventStaffRoom, ct);

        if (root is not null) return root;

        root = new OrgMessage
        {
            Id = Guid.NewGuid(),
            OrganizationId = ev.OrganizationId,
            HostedEventId = ev.Id,
            ChannelType = OrgMessageChannel.EventStaffRoom,
            AuthorAppUserId = postAs,
            Subject = EventStaffRoom.SubjectFor(ev),
            Body = EventStaffRoom.Opening(ev),
            IsPublic = false,
            DateCreated = now,
            CreatedByAppUserId = postAs,
        };

        db.OrgMessages.Add(root);
        return root;
    }

    /// <summary>
    /// Adds anybody who may now decide, and takes nobody away.
    /// </summary>
    /// <remarks>
    /// Staff change during an event's life, so the audience is refreshed on every post. Removal is
    /// deliberately not done here: dropping somebody's recipient row would delete their read state
    /// and, with it, their only record that they were ever told — and the thread carries nothing a
    /// former member of this venue has not already seen.
    /// </remarks>
    private static async Task SyncAudienceAsync(
        BenDataContext db, OrgMessage root, IReadOnlyList<Guid> audience, DateTime now,
        CancellationToken ct)
    {
        var already = root.Recipients.Select(r => r.RecipientAppUserId).ToHashSet();

        foreach (var id in audience.Distinct().Where(id => !already.Contains(id)))
        {
            var row = new OrgMessageRecipient
            {
                Id = Guid.NewGuid(),
                OrgMessageId = root.Id,
                RecipientAppUserId = id,
                DateCreated = now,
            };
            root.Recipients.Add(row);
            db.OrgMessageRecipients.Add(row);
        }

        await Task.CompletedTask;
    }
}
