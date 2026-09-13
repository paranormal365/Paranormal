using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Events;
using Ben.Data.WebApi.Services.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// An event's files go 90 days after it ends, after two warnings, and the organizer can take away what is
/// theirs first (item 235 phase 12).
/// </summary>
public sealed class HostedEventRetentionTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid HostId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();
    private static readonly Guid GuestId = Guid.NewGuid();
    private static readonly Guid SharerId = Guid.NewGuid();
    private static readonly Guid TypeId = Guid.NewGuid();

    private static readonly DateTime EndsOn = new(2026, 10, 31);

    /// <summary>90 days after the last date.</summary>
    private static readonly DateTime ClearsOn = EndsOn.AddDays(91);

    private static async Task<(SqliteTestDb Db, Ids Ids)> SeedAsync(bool withFiles = true)
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;

        foreach (var (id, name) in new[] { (HostId, "host"), (MemberId, "member"), (GuestId, "guest"), (SharerId, "sharer") })
            db.Users.Add(new AppUser { Id = id, Email = $"{name}@example.test", UserName = $"{name}@example.test", DisplayName = name, DateCreated = now });

        db.Organizations.Add(new Organization { Id = OrgId, Name = "The Thomas House", UrlName = "thomas-house", DateCreated = now, CreatedByAppUserId = HostId });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = MemberId, IsActive = true, DateCreated = now, CreatedByAppUserId = HostId });
        db.Places.Add(new Place { Id = PlaceId, Name = "The Thomas House Hotel", DateCreated = now, CreatedByAppUserId = HostId });
        db.UploadFileTypes.Add(new UploadFileType { Id = TypeId, Name = "Anything", DateCreated = now, CreatedByAppUserId = HostId });

        db.HostedEvents.Add(new HostedEvent
        {
            Id = EventId, OrganizationId = OrgId, PlaceId = PlaceId, Name = "Séance Weekend", UrlName = "seance-weekend",
            StartsOn = EndsOn.AddDays(-1), EndsOn = EndsOn, LifecycleState = HostedEventLifecycleState.Archived,
            DateCreated = now, CreatedByAppUserId = HostId,
        });

        var ids = new Ids();
        if (withFiles)
        {
            ids.File = Upload(db, "guest-pack.pdf", null);
            db.HostedEventFiles.Add(new HostedEventFile { Id = Guid.NewGuid(), HostedEventId = EventId, UploadFileId = ids.File, DateCreated = now, CreatedByAppUserId = HostId });

            ids.Picture = Upload(db, "ballroom.jpg", null);
            db.HostedEventGalleryImages.Add(new HostedEventGalleryImage { Id = Guid.NewGuid(), HostedEventId = EventId, UploadFileId = ids.Picture, DateCreated = now, CreatedByAppUserId = HostId });

            ids.TeamPhoto = Upload(db, "team.jpg", MemberId);
            ids.GuestPhoto = Upload(db, "guest.jpg", GuestId);
            ids.SharedPhoto = Upload(db, "shared.jpg", SharerId);
            foreach (var (file, author) in new[] { (ids.TeamPhoto, MemberId), (ids.GuestPhoto, GuestId), (ids.SharedPhoto, SharerId) })
                db.OrgMessages.Add(new OrgMessage
                {
                    Id = Guid.NewGuid(), HostedEventId = EventId, AuthorAppUserId = author, Body = "Look",
                    ChannelType = OrgMessageChannel.OrgBroadcast, MediaUploadFileId = file, DateCreated = now, CreatedByAppUserId = author,
                });
            db.UploadFileOrganizationShares.Add(new UploadFileOrganizationShare
            {
                Id = Guid.NewGuid(), UploadFileId = ids.SharedPhoto, OrganizationId = OrgId, SharedByAppUserId = SharerId,
                Visibility = FileShareVisibility.OrgMembers, IsActive = true, DateCreated = now, CreatedByAppUserId = SharerId,
            });
        }

        await db.SaveChangesAsync();
        return (sqlite, ids);
    }

    private sealed class Ids
    {
        public Guid File, Picture, TeamPhoto, GuestPhoto, SharedPhoto;
    }

    private static Guid Upload(Ben.Data.Source.Context.BenDataContext db, string name, Guid? owner)
    {
        var id = Guid.NewGuid();
        db.UploadFiles.Add(new UploadFile
        {
            Id = id, UploadFileTypeId = TypeId, AppUserId = owner, FileName = name, StoredFileName = name,
            ContentType = "image/jpeg", FileSize = 1000, StoragePath = $"x/{name}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner ?? HostId,
        });
        return id;
    }

    private static (HostedEventRetentionJob Job, Mock<IMediaIngestService> Ingest) Job(SqliteTestDb sqlite)
    {
        var email = new Mock<IEmailService>();
        email.SetupGet(e => e.IsConfigured).Returns(false);
        var ingest = new Mock<IMediaIngestService>();
        return (new HostedEventRetentionJob(sqlite.Factory, email.Object, new PlatformMessageService(sqlite.Factory), ingest.Object,
            Options.Create(new Ben.Data.Common.SiteIdentity { BaseUrl = "https://example.test" }),
            NullLogger<HostedEventRetentionJob>.Instance), ingest);
    }

    private static async Task<int> LettersAsync(SqliteTestDb sqlite)
    {
        await using var db = await sqlite.NewContextAsync();
        return await db.UserMessages.CountAsync();
    }

    private static async Task<HostedEvent> EventAsync(SqliteTestDb sqlite)
    {
        await using var db = await sqlite.NewContextAsync();
        return await db.HostedEvents.AsNoTracking().SingleAsync(e => e.Id == EventId);
    }

    [Fact]
    public async Task The_organizer_is_warned_a_month_and_a_week_before_and_the_files_go_on_the_day()
    {
        var (sqlite, ids) = await SeedAsync();
        await using var _ = sqlite;
        var (job, ingest) = Job(sqlite);

        await job.RunAtAsync(ClearsOn.AddDays(-31), default);
        Assert.Equal(0, await LettersAsync(sqlite));

        await job.RunAtAsync(ClearsOn.AddDays(-30), default);
        Assert.Equal(1, await LettersAsync(sqlite));
        await job.RunAtAsync(ClearsOn.AddDays(-29), default);
        Assert.Equal(1, await LettersAsync(sqlite));

        await job.RunAtAsync(ClearsOn.AddDays(-7), default);
        Assert.Equal(2, await LettersAsync(sqlite));

        await job.RunAtAsync(ClearsOn.AddDays(-1), default);
        Assert.Null((await EventAsync(sqlite)).MediaClearedUtc);

        await job.RunAtAsync(ClearsOn, default);
        Assert.NotNull((await EventAsync(sqlite)).MediaClearedUtc);

        await using var db = await sqlite.NewContextAsync();
        Assert.Empty(await db.HostedEventFiles.ToListAsync());
        Assert.Empty(await db.HostedEventGalleryImages.ToListAsync());
        Assert.False(await db.UploadFiles.AnyAsync(f => f.Id == ids.File || f.Id == ids.Picture));

        // The room keeps its words and loses its links; the guests keep their photos.
        Assert.Equal(3, await db.OrgMessages.CountAsync(m => m.HostedEventId == EventId && m.MediaUploadFileId == null));
        Assert.True(await db.UploadFiles.AnyAsync(f => f.Id == ids.GuestPhoto));
        Assert.True(await db.UploadFiles.AnyAsync(f => f.Id == ids.SharedPhoto));

        ingest.Verify(i => i.DeleteAllAsync("x/guest-pack.pdf", It.IsAny<CancellationToken>()), Times.Once);
        ingest.Verify(i => i.DeleteAllAsync("x/guest.jpg", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Nothing_is_removed_on_a_letter_nobody_got()
    {
        // The job first sees an event already past its date: it warns, and waits a week.
        var (sqlite, _) = await SeedAsync();
        await using var __ = sqlite;
        var (job, _) = Job(sqlite);

        await job.RunAtAsync(ClearsOn.AddDays(3), default);
        Assert.Null((await EventAsync(sqlite)).MediaClearedUtc);
        Assert.Equal(1, await LettersAsync(sqlite));

        await job.RunAtAsync(ClearsOn.AddDays(9), default);
        Assert.Null((await EventAsync(sqlite)).MediaClearedUtc);

        await job.RunAtAsync(ClearsOn.AddDays(10).AddHours(1), default);
        Assert.NotNull((await EventAsync(sqlite)).MediaClearedUtc);
    }

    [Fact]
    public async Task An_event_with_nothing_to_lose_gets_no_letter()
    {
        var (sqlite, _) = await SeedAsync(withFiles: false);
        await using var __ = sqlite;
        var (job, _) = Job(sqlite);

        await job.RunAtAsync(ClearsOn.AddDays(-30), default);
        await job.RunAtAsync(ClearsOn.AddDays(-7), default);
        await job.RunAtAsync(ClearsOn, default);

        Assert.Equal(0, await LettersAsync(sqlite));
        Assert.NotNull((await EventAsync(sqlite)).MediaClearedUtc);
    }

    [Fact]
    public async Task The_organizer_may_take_the_files_their_own_peoples_photos_and_photos_sent_to_them_but_not_a_guests()
    {
        var (sqlite, ids) = await SeedAsync();
        await using var __ = sqlite;
        await using var db = await sqlite.NewContextAsync();

        var items = await HostedEventAfterController.KeepItemsAsync(db, await EventAsync(sqlite), default);
        var offered = items.Select(i => i.UploadFileId).ToHashSet();

        Assert.Contains(ids.File, offered);
        Assert.Contains(ids.Picture, offered);
        Assert.Contains(ids.TeamPhoto, offered);
        Assert.Contains(ids.SharedPhoto, offered);
        Assert.DoesNotContain(ids.GuestPhoto, offered);
    }

    [Fact]
    public void The_warning_says_what_goes_when_and_where_to_keep_it()
    {
        var ev = new HostedEvent { Name = "Séance Weekend", EndsOn = EndsOn };
        var (subject, body) = HostedEventRetentionJob.Letter(ev, new EventRetention.Holding(2, 1, 4), ClearsOn, "https://example.test/keep");

        Assert.Contains("01/30/2027", subject);
        Assert.Contains("2 files, 1 gallery picture, 4 photos in its room", body);
        Assert.Contains("https://example.test/keep", body);
        Assert.Contains("guests keep their own photos", body);
    }
}
