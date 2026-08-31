using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Which archive recordings a stranger may receive.
/// </summary>
/// <remarks>
/// <para>This is the gate on the only anonymous route to field-session media, so each test here
/// removes exactly one condition and asserts the bytes stop being offered. A gate with four
/// clauses and one test proves nothing about the other three.</para>
///
/// <para>The listing is asserted alongside the serving check in every case, because the failure
/// this feature is most likely to ship is the two disagreeing: a page that lists what the endpoint
/// refuses is a gallery of broken frames, and one that refuses what it lists is merely useless.</para>
/// </remarks>
public sealed class ArchiveMediaPublicationTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record World(
        IDbContextFactory<BenDataContext> F, Guid SessionId, Guid FileId, Guid PlaceId);

    /// <summary>A published session at a public place, media approved — everything permitted.</summary>
    private static async Task<World> SeedAsync(
        PlaceKind kind = PlaceKind.PublicLocation,
        FeedMediaReviewState review = FeedMediaReviewState.Approved,
        bool published = true,
        bool attachedToPlace = true)
    {
        var f = CreateFactory();
        Guid userId = Guid.NewGuid(), placeId = Guid.NewGuid(),
             sessionId = Guid.NewGuid(), fileId = Guid.NewGuid();

        await using var db = await f.CreateDbContextAsync();
        var now = DateTime.UtcNow;

        db.AppUsers.Add(new AppUser
        {
            Id = userId, UserName = "r@t.com", Email = "r@t.com",
            DisplayName = "Recorder", DateCreated = now,
        });
        db.Places.Add(new Place
        {
            Id = placeId, Name = "Bell Witch Cave", Kind = kind,
            DateCreated = now, CreatedByAppUserId = userId,
        });
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, FileName = "spike.jpg", ContentType = "image/jpeg",
            StoragePath = $"archive/{fileId}.jpg", FileSize = 1024,
            DateCreated = now, CreatedByAppUserId = userId,
        });
        db.FieldSessionUploads.Add(new FieldSessionUpload
        {
            Id = sessionId,
            SubmittedByAppUserId = userId,
            PlaceId = attachedToPlace ? placeId : null,
            PublishedAtUtc = published ? now : null,
            MediaReviewState = review,
            DocumentUploadFileId = Guid.NewGuid(),
            StartedAt = now.AddHours(-2),
            DeviceModel = "iPhone 17",
            ReadingCount = 900, MarkerCount = 3,
            DateCreated = now, CreatedByAppUserId = userId,
        });
        db.FieldSessionUploadFiles.Add(new FieldSessionUploadFile
        {
            Id = Guid.NewGuid(), FieldSessionUploadId = sessionId, UploadFileId = fileId,
            RelativePath = "media/photo-001.jpg",
            DateCreated = now, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();

        return new World(f, sessionId, fileId, placeId);
    }

    private static async Task<(bool MayServe, int Listed)> AskAsync(World w)
    {
        await using var db = await w.F.CreateDbContextAsync();
        return (await ArchiveMediaPublication.MayServeAsync(db, w.SessionId, w.FileId, default),
                (await ArchiveMediaPublication.ServableFilesAsync(db, w.SessionId, default)).Count);
    }

    [Fact]
    public async Task A_published_approved_session_at_a_public_place_is_served()
    {
        var (mayServe, listed) = await AskAsync(await SeedAsync());

        Assert.True(mayServe);
        Assert.Equal(1, listed);
    }

    // ── one clause removed at a time ─────────────────────────────────────────

    /// <summary>
    /// Publication is an act somebody performed. A session sitting on the server unpublished has
    /// had nothing decided about it, and "not yet decided" is not permission.
    /// </summary>
    [Fact]
    public async Task An_unpublished_session_serves_nothing()
    {
        var (mayServe, listed) = await AskAsync(await SeedAsync(published: false));

        Assert.False(mayServe);
        Assert.Equal(0, listed);
    }

    /// <summary>
    /// The place kind is the whole safety story: publishing forces PublicLocation precisely so a
    /// private residence cannot enter the archive. Re-asking here means a place later corrected to
    /// a residence takes its pictures down with it, with no page to edit and nothing to remember.
    /// </summary>
    [Fact]
    public async Task A_private_residence_serves_nothing_even_when_published_and_approved()
    {
        var (mayServe, listed) = await AskAsync(await SeedAsync(kind: PlaceKind.PrivateResidence));

        Assert.False(mayServe);
        Assert.Equal(0, listed);
    }

    /// <summary>Pending is never served, to anybody — the fail-closed default.</summary>
    [Fact]
    public async Task Unscreened_media_serves_nothing()
    {
        var (mayServe, listed) = await AskAsync(await SeedAsync(review: FeedMediaReviewState.Pending));

        Assert.False(mayServe);
        Assert.Equal(0, listed);
    }

    /// <summary>
    /// The reporting path's whole promise: one flag hides the pictures immediately, before any
    /// moderator has looked. A held session that still served its media would make the flag a
    /// request rather than an act.
    /// </summary>
    [Fact]
    public async Task Held_media_serves_nothing()
    {
        var (mayServe, listed) = await AskAsync(await SeedAsync(review: FeedMediaReviewState.Held));

        Assert.False(mayServe);
        Assert.Equal(0, listed);
    }

    [Fact]
    public async Task A_session_attached_to_no_place_serves_nothing()
    {
        var (mayServe, listed) = await AskAsync(await SeedAsync(attachedToPlace: false));

        Assert.False(mayServe);
        Assert.Equal(0, listed);
    }

    // ── the file must be this session's ──────────────────────────────────────

    /// <summary>
    /// Otherwise the endpoint is a way to read any file in the system by naming it alongside a
    /// session that happens to be published — the session id would authorise the caller rather
    /// than the file.
    /// </summary>
    [Fact]
    public async Task A_file_belonging_to_no_session_is_refused()
    {
        var w = await SeedAsync();

        await using var db = await w.F.CreateDbContextAsync();
        Assert.False(await ArchiveMediaPublication.MayServeAsync(db, w.SessionId, Guid.NewGuid(), default));
    }

    /// <summary>
    /// The pairing is checked, not just each half. A file approved under ONE session must not be
    /// readable by quoting a different published session's id.
    /// </summary>
    [Fact]
    public async Task A_file_from_another_session_is_refused_under_this_one()
    {
        var mine = await SeedAsync();
        var theirs = await SeedAsync();

        await using var db = await mine.F.CreateDbContextAsync();
        Assert.False(await ArchiveMediaPublication.MayServeAsync(db, mine.SessionId, theirs.FileId, default));
    }

    // ── retraction actually retracts ─────────────────────────────────────────

    /// <summary>
    /// The reason the rule is asked per request instead of being cached into UploadFile.IsPublic:
    /// a retraction that leaves the bytes readable is not a retraction.
    /// </summary>
    [Fact]
    public async Task Retracting_a_session_stops_its_media_being_served()
    {
        var w = await SeedAsync();
        Assert.True((await AskAsync(w)).MayServe);

        await using (var db = await w.F.CreateDbContextAsync())
        {
            var session = await db.FieldSessionUploads.SingleAsync(s => s.Id == w.SessionId);
            session.PublishedAtUtc = null;
            await db.SaveChangesAsync();
        }

        var (mayServe, listed) = await AskAsync(w);
        Assert.False(mayServe);
        Assert.Equal(0, listed);
    }

    /// <summary>The same, for the place being corrected after the fact rather than the session.</summary>
    [Fact]
    public async Task Correcting_a_place_to_a_residence_stops_its_media_being_served()
    {
        var w = await SeedAsync();
        Assert.True((await AskAsync(w)).MayServe);

        await using (var db = await w.F.CreateDbContextAsync())
        {
            var place = await db.Places.SingleAsync(p => p.Id == w.PlaceId);
            place.Kind = PlaceKind.PrivateResidence;
            await db.SaveChangesAsync();
        }

        var (mayServe, listed) = await AskAsync(w);
        Assert.False(mayServe);
        Assert.Equal(0, listed);
    }

    // ── what the listing carries ─────────────────────────────────────────────

    /// <summary>
    /// The relative path is what ties a recording to the reading that references it, which is the
    /// only reason audio is worth anything beside the numbers.
    /// </summary>
    [Fact]
    public async Task The_listing_carries_what_the_document_calls_each_file()
    {
        var w = await SeedAsync();

        await using var db = await w.F.CreateDbContextAsync();
        var item = Assert.Single(await ArchiveMediaPublication.ServableFilesAsync(db, w.SessionId, default));

        Assert.Equal(w.FileId, item.UploadFileId);
        Assert.Equal("media/photo-001.jpg", item.RelativePath);
        Assert.Equal("image/jpeg", item.ContentType);
        Assert.Equal("spike.jpg", item.FileName);
    }
}
