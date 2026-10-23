using System.Net;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// A guest writing to the people running an event, and their answers (event-scenario-gaps; Ben's scenario:
/// "contact Jim either on our app").
/// </summary>
/// <remarks>
/// <para><b>One conversation per guest per event.</b> The root message is a container: it carries the event
/// (<c>HostedEventId</c>) and the guest (its author), sits in the group's mail as one row addressed to
/// everybody who may decide the event's bookings, and links to the page where the conversation is read and
/// answered. Every message, the guest's and the organizers', is a reply to it.</para>
///
/// <para><b>Words only, kept as typed.</b> Bodies are plain text and every screen shows them as text, never as
/// markup; the one piece of markup is the root's own link, built here from encoded names.</para>
///
/// <para><b>Who may write.</b> Anybody with a booking at the event, whatever became of it (a guest turned down
/// is the one most likely to ask why), as its lead or named on it. And, until the event starts, anybody signed
/// in who is thinking of coming (Ben, 10/23/2026: "People can write questions to the event organizers before
/// the event"). Once it starts, somebody without a booking can still read what they were told, and cannot
/// write; somebody without a booking who never wrote is told nothing.</para>
///
/// <para><b>Who hears.</b> A guest's message marks the root unread for the group's people, which is what puts
/// it on their bell. An answer marks it unread for the guest, which the bell counts on its own row, never
/// among the group's mail, where the guest has no inbox to open.</para>
/// </remarks>
public static class EventGuestThreads
{
    public const int MaxBody = 2000;

    private static readonly HostedEventLifecycleState[] Open =
        [HostedEventLifecycleState.Published, HostedEventLifecycleState.Live, HostedEventLifecycleState.Ended];

    /// <summary>Whether this person has a booking at the event, as its lead or named on it.</summary>
    public static Task<bool> IsGuestAsync(BenDataContext db, Guid eventId, Guid userId, CancellationToken ct)
        => db.HostedEventBookings.AsNoTracking()
            .AnyAsync(b => b.HostedEventId == eventId
                        && (b.LeadAppUserId == userId || b.Guests.Any(g => g.AppUserId == userId)), ct);

    /// <summary>
    /// Where each person stands at the event, in the organizers' words, so whoever answers knows who they are
    /// answering (Ben, 10/23/2026: "if they are signed up for the event or confirmed ... notate it for the
    /// organizer to see"). A person with several bookings is shown by the one that counts most.
    /// </summary>
    public static async Task<Dictionary<Guid, string>> StandingsAsync(
        BenDataContext db, Guid eventId, IReadOnlyCollection<Guid> people, CancellationToken ct)
    {
        var rows = await db.HostedEventBookings.AsNoTracking()
            .Where(b => b.HostedEventId == eventId)
            .Select(b => new { b.Status, b.LeadAppUserId, Named = b.Guests.Select(g => g.AppUserId) })
            .ToListAsync(ct);

        var best = new Dictionary<Guid, HostedEventBookingStatus>();
        foreach (var row in rows)
            foreach (var person in row.Named.OfType<Guid>().Append(row.LeadAppUserId).Where(people.Contains))
                if (!best.TryGetValue(person, out var held) || Rank(row.Status) < Rank(held)) best[person] = row.Status;

        return people.Distinct().ToDictionary(p => p, p => best.TryGetValue(p, out var s) ? Standing(s) : NotSignedUp);
    }

    public const string NotSignedUp = EventGuestThreadStanding.NotSignedUp;

    /// <summary>A booking's state, as the organizers read it beside a message.</summary>
    public static string Standing(HostedEventBookingStatus status) => status switch
    {
        HostedEventBookingStatus.Confirmed => "Confirmed",
        HostedEventBookingStatus.Held => "Signed up, places held",
        HostedEventBookingStatus.Requested => "Signed up, not confirmed yet",
        HostedEventBookingStatus.Expired => "Signed up, the hold ran out",
        HostedEventBookingStatus.TurnedDown => "Turned down",
        HostedEventBookingStatus.Cancelled => "Booking cancelled",
        _ => NotSignedUp,
    };

    private static int Rank(HostedEventBookingStatus status) => status switch
    {
        HostedEventBookingStatus.Confirmed => 0,
        HostedEventBookingStatus.Held => 1,
        HostedEventBookingStatus.Requested => 2,
        HostedEventBookingStatus.Expired => 3,
        HostedEventBookingStatus.TurnedDown => 4,
        _ => 5,
    };

    /// <summary>When the event's first night starts, by its own window rule.</summary>
    public static async Task<DateTime> StartsAtUtcAsync(BenDataContext db, HostedEvent ev, CancellationToken ct)
    {
        var nights = await db.HostedEventNights.AsNoTracking()
            .Where(n => n.HostedEventId == ev.Id).OrderBy(n => n.Date).ToListAsync(ct);
        return HostedEventCalendarSync.Window(ev, nights).StartUtc;
    }

    /// <summary>
    /// Why this person can't write now, or null when they can: the event's own rule, and for somebody
    /// without a booking, the event having started.
    /// </summary>
    public static string? WhyClosedFor(HostedEvent ev, DateTime now, bool isGuest, DateTime startsAtUtc)
    {
        if (WhyClosed(ev, now) is { } closed) return closed;
        if (!isGuest && now >= startsAtUtc)
            return "Questions from people without a booking close when the event starts.";
        return null;
    }

    /// <summary>Why a message can't be written now, or null when it can.</summary>
    public static string? WhyClosed(HostedEvent ev, DateTime now)
    {
        if (!Open.Contains(ev.LifecycleState)) return "This event isn't on the site any more, so it can't take messages.";
        if (ev.EndsOn.Date.AddDays(30) < now.Date) return "This event ended more than a month ago, so it no longer takes messages.";
        return null;
    }

    /// <summary>The words as they will be kept, or the reason they can't be.</summary>
    public static (string? Body, string? Refusal) Clean(string? body)
    {
        var text = body?.Trim();
        if (string.IsNullOrEmpty(text)) return (null, "Write something first.");
        if (text.Length > MaxBody) return (null, $"Keep it under {MaxBody:N0} characters.");
        return (text, null);
    }

    /// <summary>This guest's conversation about this event, or null before their first message.</summary>
    public static Task<OrgMessage?> RootAsync(BenDataContext db, Guid eventId, Guid guestId, CancellationToken ct)
        => db.OrgMessages.Include(m => m.Recipients)
            .FirstOrDefaultAsync(m => m.HostedEventId == eventId && m.ParentMessageId == null
                                   && m.ChannelType == OrgMessageChannel.EventGuestThread
                                   && m.AuthorAppUserId == guestId, ct);

    /// <summary>Writes one message, starting the conversation on the guest's first.</summary>
    /// <param name="audience">Everybody who may decide the event's bookings: the root's addressees.</param>
    public static async Task<OrgMessage> WriteAsync(
        BenDataContext db, HostedEvent ev, Guid guestId, string guestName, OrgMessage? root, Guid authorId,
        string body, IReadOnlyList<Guid> audience, DateTime now, CancellationToken ct)
    {
        if (root is null)
        {
            var page = $"/organizations/{ev.OrganizationId}/events/{ev.Id}/guest-messages?guest={guestId}";
            root = new OrgMessage
            {
                Id = Guid.NewGuid(),
                OrganizationId = ev.OrganizationId,
                HostedEventId = ev.Id,
                ChannelType = OrgMessageChannel.EventGuestThread,
                AuthorAppUserId = guestId,
                Subject = $"{guestName} about {ev.Name}",
                Body = $"<p>{WebUtility.HtmlEncode(guestName)} wrote to you about {WebUtility.HtmlEncode(ev.Name)}.</p>"
                     + $"<p><a href=\"{page}\">Read and answer it</a></p>",
                IsPublic = false,
                DateCreated = now,
                CreatedByAppUserId = guestId,
            };
            db.OrgMessages.Add(root);
        }

        // Anybody who may now decide is added; nobody is taken away (their read state is their record).
        var already = root.Recipients.Select(r => r.RecipientAppUserId).ToHashSet();
        foreach (var id in audience.Append(guestId).Distinct().Where(id => !already.Contains(id)))
        {
            var row = new OrgMessageRecipient { Id = Guid.NewGuid(), OrgMessageId = root.Id, RecipientAppUserId = id, DateCreated = now };
            root.Recipients.Add(row);
            db.OrgMessageRecipients.Add(row);
        }

        var message = new OrgMessage
        {
            Id = Guid.NewGuid(),
            OrganizationId = ev.OrganizationId,
            HostedEventId = ev.Id,
            ParentMessageId = root.Id,
            ChannelType = OrgMessageChannel.EventGuestThread,
            AuthorAppUserId = authorId,
            Body = body,
            IsPublic = false,
            DateCreated = now,
            CreatedByAppUserId = authorId,
        };
        db.OrgMessages.Add(message);

        // The guest wrote: the group's people have something new. The group answered: the guest does.
        var fromGuest = authorId == guestId;
        foreach (var r in root.Recipients)
        {
            if (r.RecipientAppUserId == authorId) r.DateRead = now;
            else if (fromGuest ? r.RecipientAppUserId != guestId : r.RecipientAppUserId == guestId) r.DateRead = null;
        }
        root.DateUpdated = now;

        await db.SaveChangesAsync(ct);
        return root;
    }

    /// <summary>Marks the conversation read for this person.</summary>
    public static async Task MarkReadAsync(BenDataContext db, OrgMessage root, Guid userId, DateTime now, CancellationToken ct)
    {
        var mine = root.Recipients.FirstOrDefault(r => r.RecipientAppUserId == userId);
        if (mine is null || mine.DateRead is not null) return;
        mine.DateRead = now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The conversation, oldest first, as either side reads it.</summary>
    public static async Task<EventGuestThreadRecord> RecordAsync(
        BenDataContext db, HostedEvent ev, Guid guestId, OrgMessage? root, Guid viewerId, string? note, CancellationToken ct,
        string? whyClosed = null, string? standing = null)
    {
        var guestName = await db.Users.AsNoTracking().Where(u => u.Id == guestId)
            .Select(u => u.DisplayName).FirstOrDefaultAsync(ct) ?? "A guest";
        var organizer = await db.Organizations.AsNoTracking().Where(o => o.Id == ev.OrganizationId)
            .Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "The organizers";

        var messages = root is null
            ? []
            : await db.OrgMessages.AsNoTracking()
                .Where(m => m.ParentMessageId == root.Id && m.ChannelType == OrgMessageChannel.EventGuestThread)
                .OrderBy(m => m.DateCreated)
                .Select(m => new { m.Id, m.AuthorAppUserId, Name = m.AuthorAppUser.DisplayName, m.Body, m.DateCreated })
                .ToListAsync(ct);

        var why = whyClosed ?? WhyClosed(ev, DateTime.UtcNow);
        return new EventGuestThreadRecord(
            ev.Id, root?.Id, guestId, guestName, ev.Name, organizer, why is null, why,
            [.. messages.Select(m => new EventGuestThreadMessageRecord(
                m.Id,
                m.AuthorAppUserId == guestId ? guestName : $"{m.Name ?? "Somebody"} for {organizer}",
                m.Body, m.DateCreated, m.AuthorAppUserId != guestId, m.AuthorAppUserId == viewerId))],
            note, standing);
    }
}
