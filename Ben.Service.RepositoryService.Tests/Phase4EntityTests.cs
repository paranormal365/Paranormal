using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Service.RepositoryService.Tests;

/// <summary>Tests for Phase 4 entities: OrgMessage threading/views and calendar events/attendees.</summary>
public class Phase4EntityTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory() => TestDbFactory.Create();

    private static async Task<(AppUser user, Organization org)> SeedAsync(BenDataContext db)
    {
        var user = new AppUser { Id = Guid.NewGuid(), UserName = "u@o.com", Email = "u@o.com", DisplayName = "User", DateCreated = DateTime.UtcNow };
        var org  = new Organization { Id = Guid.NewGuid(), Name = "Org", UrlName = "org", DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id };
        db.AppUsers.Add(user);
        db.Organizations.Add(org);
        await db.SaveChangesAsync();
        return (user, org);
    }

    // ── OrgMessage ────────────────────────────────────────────────────────────

    [Fact]
    public async Task OrgMessage_CanBeSavedAndRetrieved()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        var msg = new OrgMessage
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id, AuthorAppUserId = user.Id,
            ChannelType = OrgMessageChannel.OrgBroadcast, Subject = "Hello",
            Body = "<p>Welcome everyone!</p>", DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.OrgMessages.Add(msg);
        await db.SaveChangesAsync();

        var loaded = await db.OrgMessages.AsNoTracking().FirstAsync(m => m.Id == msg.Id);
        Assert.Equal("Hello", loaded.Subject);
        Assert.Equal(OrgMessageChannel.OrgBroadcast, loaded.ChannelType);
        Assert.Equal(0, loaded.ViewCount);
    }

    [Fact]
    public async Task OrgMessage_SupportsThreadedReplies()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        var parent = new OrgMessage
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id, AuthorAppUserId = user.Id,
            ChannelType = OrgMessageChannel.OrgBroadcast, Body = "Parent message",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.OrgMessages.Add(parent);
        await db.SaveChangesAsync();

        var reply = new OrgMessage
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id, AuthorAppUserId = user.Id,
            ParentMessageId = parent.Id, ChannelType = OrgMessageChannel.OrgBroadcast,
            Body = "Reply message", DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.OrgMessages.Add(reply);
        await db.SaveChangesAsync();

        var loadedParent = await db.OrgMessages.AsNoTracking()
            .Include(m => m.Replies)
            .FirstAsync(m => m.Id == parent.Id);
        Assert.Single(loadedParent.Replies);
        Assert.Equal(reply.Id, loadedParent.Replies.First().Id);
    }

    [Fact]
    public async Task OrgMessageRecipient_TracksReadStatus()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        var msg = new OrgMessage
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id, AuthorAppUserId = user.Id,
            ChannelType = OrgMessageChannel.DirectMessage, Body = "Private message",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.OrgMessages.Add(msg);
        await db.SaveChangesAsync();

        var recipient = new OrgMessageRecipient
        {
            Id = Guid.NewGuid(), OrgMessageId = msg.Id, RecipientAppUserId = user.Id,
            DateCreated = DateTime.UtcNow,
        };
        db.OrgMessageRecipients.Add(recipient);
        await db.SaveChangesAsync();

        // Initially unread
        var loaded = await db.OrgMessageRecipients.AsNoTracking().FirstAsync(r => r.Id == recipient.Id);
        Assert.Null(loaded.DateRead);

        // Mark as read
        recipient.DateRead = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var read = await db.OrgMessageRecipients.AsNoTracking().FirstAsync(r => r.Id == recipient.Id);
        Assert.NotNull(read.DateRead);
    }

    [Fact]
    public async Task OrgMessageView_CompositeKey_TracksPerViewer()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        var msg = new OrgMessage
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id, AuthorAppUserId = user.Id,
            ChannelType = OrgMessageChannel.OrgBroadcast, Body = "Viewed message",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.OrgMessages.Add(msg);
        await db.SaveChangesAsync();

        db.OrgMessageViews.Add(new OrgMessageView { OrgMessageId = msg.Id, ViewerAppUserId = user.Id, DateViewed = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var viewCount = await db.OrgMessageViews.AsNoTracking().CountAsync(v => v.OrgMessageId == msg.Id);
        Assert.Equal(1, viewCount);
    }

    [Fact]
    public async Task OrgMessageRecipient_UniqueIndex_IsConfiguredOnModel()
    {
        using var db = new BenDataContext(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<BenDataContext>().UseInMemoryDatabase("msg-model").Options);
        var entityType = db.Model.FindEntityType(typeof(OrgMessageRecipient));
        Assert.NotNull(entityType);
        var idx = entityType!.GetIndexes().FirstOrDefault(i => i.IsUnique &&
            i.Properties.Any(p => p.Name == nameof(OrgMessageRecipient.OrgMessageId)) &&
            i.Properties.Any(p => p.Name == nameof(OrgMessageRecipient.RecipientAppUserId)));
        Assert.NotNull(idx);
    }

    // ── OrgCalendarEventType ──────────────────────────────────────────────────

    [Fact]
    public async Task OrgCalendarEventType_CanBeSavedAndRetrieved()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        var t = new OrgCalendarEventType
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id, Name = "Investigation",
            ColorClass = "text-danger", SortOrder = 1, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.OrgCalendarEventTypes.Add(t);
        await db.SaveChangesAsync();

        var loaded = await db.OrgCalendarEventTypes.AsNoTracking().FirstAsync(x => x.Id == t.Id);
        Assert.Equal("Investigation", loaded.Name);
        Assert.Equal("text-danger", loaded.ColorClass);
    }

    // ── OrgCalendarEvent ──────────────────────────────────────────────────────

    [Fact]
    public async Task OrgCalendarEvent_CanBeSavedAndRetrieved()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        var start = DateTime.UtcNow.AddDays(7);
        var ev = new OrgCalendarEvent
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id,
            Title = "Monthly Meeting", Description = "<p>All hands.</p>",
            StartDateTime = start, EndDateTime = start.AddHours(2),
            IsAllDay = false, IsPublic = false,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.OrgCalendarEvents.Add(ev);
        await db.SaveChangesAsync();

        var loaded = await db.OrgCalendarEvents.AsNoTracking().FirstAsync(x => x.Id == ev.Id);
        Assert.Equal("Monthly Meeting", loaded.Title);
        Assert.False(loaded.IsAllDay);
        Assert.Null(loaded.RecurrenceRule);
    }

    [Fact]
    public async Task OrgCalendarEvent_SupportsRecurrenceRule()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        var ev = new OrgCalendarEvent
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id, Title = "Weekly Check-in",
            StartDateTime = DateTime.UtcNow, EndDateTime = DateTime.UtcNow.AddHours(1),
            IsAllDay = false, IsPublic = false,
            RecurrenceRule = "FREQ=WEEKLY;BYDAY=TU",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.OrgCalendarEvents.Add(ev);
        await db.SaveChangesAsync();

        var loaded = await db.OrgCalendarEvents.AsNoTracking().FirstAsync(x => x.Id == ev.Id);
        Assert.Equal("FREQ=WEEKLY;BYDAY=TU", loaded.RecurrenceRule);
    }

    [Fact]
    public async Task OrgCalendarEventAttendee_TracksRsvpStatus()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        var ev = new OrgCalendarEvent
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id, Title = "Investigation Night",
            StartDateTime = DateTime.UtcNow.AddDays(3), EndDateTime = DateTime.UtcNow.AddDays(3).AddHours(5),
            IsAllDay = false, IsPublic = false,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.OrgCalendarEvents.Add(ev);
        await db.SaveChangesAsync();

        var attendee = new OrgCalendarEventAttendee
        {
            Id = Guid.NewGuid(), OrgCalendarEventId = ev.Id, AppUserId = user.Id,
            RsvpStatus = RsvpStatus.Invited, AssignedTask = "Lead investigator",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.OrgCalendarEventAttendees.Add(attendee);
        await db.SaveChangesAsync();

        var loaded = await db.OrgCalendarEventAttendees.AsNoTracking().FirstAsync(a => a.Id == attendee.Id);
        Assert.Equal(RsvpStatus.Invited, loaded.RsvpStatus);
        Assert.Equal("Lead investigator", loaded.AssignedTask);

        // RSVP accepted
        attendee.RsvpStatus = RsvpStatus.Accepted;
        attendee.DateRsvp   = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var accepted = await db.OrgCalendarEventAttendees.AsNoTracking().FirstAsync(a => a.Id == attendee.Id);
        Assert.Equal(RsvpStatus.Accepted, accepted.RsvpStatus);
        Assert.NotNull(accepted.DateRsvp);
    }

    [Fact]
    public async Task OrgCalendarEventAttendee_CascadeDeletesWithEvent()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        var ev = new OrgCalendarEvent
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id, Title = "Meeting",
            StartDateTime = DateTime.UtcNow, EndDateTime = DateTime.UtcNow.AddHours(1),
            IsAllDay = false, IsPublic = false,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.OrgCalendarEvents.Add(ev);
        await db.SaveChangesAsync();

        db.OrgCalendarEventAttendees.Add(new OrgCalendarEventAttendee
        {
            Id = Guid.NewGuid(), OrgCalendarEventId = ev.Id, AppUserId = user.Id,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        });
        await db.SaveChangesAsync();

        db.OrgCalendarEvents.Remove(ev);
        await db.SaveChangesAsync();

        var remaining = await db.OrgCalendarEventAttendees.AsNoTracking()
            .Where(a => a.OrgCalendarEventId == ev.Id).ToListAsync();
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task OrgCalendarEventAttendee_AllRsvpValues_CanBeStored()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        var ev = new OrgCalendarEvent
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id, Title = "Event",
            StartDateTime = DateTime.UtcNow, EndDateTime = DateTime.UtcNow.AddHours(1),
            IsAllDay = false, IsPublic = false,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.OrgCalendarEvents.Add(ev);
        await db.SaveChangesAsync();

        // Add one attendee, update through all statuses
        var attendee = new OrgCalendarEventAttendee
        {
            Id = Guid.NewGuid(), OrgCalendarEventId = ev.Id, AppUserId = user.Id,
            RsvpStatus = RsvpStatus.Invited,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.OrgCalendarEventAttendees.Add(attendee);
        await db.SaveChangesAsync();

        foreach (var status in Enum.GetValues<RsvpStatus>())
        {
            attendee.RsvpStatus = status;
            await db.SaveChangesAsync();
            var loaded = await db.OrgCalendarEventAttendees.AsNoTracking()
                .FirstAsync(a => a.Id == attendee.Id);
            Assert.Equal(status, loaded.RsvpStatus);
        }
    }
}
