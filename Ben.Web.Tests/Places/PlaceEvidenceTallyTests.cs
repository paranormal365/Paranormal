using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Places;

/// <summary>
/// What the evidence at one place adds up to (item 250).
/// </summary>
/// <remarks>
/// <para>The one that carries the feature is
/// <see cref="A_file_on_two_routes_is_counted_once"/>. Ben's decision was "one file counted once,
/// at the place, whatever route it arrived by", and three counts added together would report a
/// place as holding more evidence than it does — the figure nobody can check is the one nobody
/// trusts.</para>
/// </remarks>
public sealed class PlaceEvidenceTallyTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    private sealed record Fixture(SqliteTestDb Sqlite, Guid PlaceId);

    private static async Task<Fixture> SeedAsync(PlaceKind kind = PlaceKind.PublicLocation)
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.Factory.CreateDbContextAsync();

        db.AppUsers.Add(new AppUser
        { Id = Owner, DisplayName = "A contributor", DateCreated = DateTime.UtcNow });

        var placeId = Guid.NewGuid();
        db.Places.Add(new Place
        {
            Id = placeId, Name = "Cragfont", Kind = kind,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Owner,
        });
        db.UploadFileTypes.Add(new UploadFileType
        {
            Id = new Guid("70000000-0000-0000-0000-000000000001"),
            Name = "Feed media", DateCreated = DateTime.UtcNow, CreatedByAppUserId = Owner,
        });
        await db.SaveChangesAsync();
        return new Fixture(sqlite, placeId);
    }

    private static Guid AddFile(BenDataContext db)
    {
        var id = Guid.NewGuid();
        db.UploadFiles.Add(new UploadFile
        {
            Id = id,
            UploadFileTypeId = new Guid("70000000-0000-0000-0000-000000000001"),
            AppUserId = Owner, FileName = "a.jpg", StoredFileName = $"{id}.jpg",
            ContentType = "image/jpeg", FileSize = 10, StoragePath = $"x/{id}.jpg",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Owner,
        });
        return id;
    }

    private static void AddDirectly(BenDataContext db, Guid placeId, Guid fileId,
                                    FeedMediaReviewState state = FeedMediaReviewState.Approved)
        => db.PlaceEvidence.Add(new PlaceEvidence
        {
            Id = Guid.NewGuid(), PlaceId = placeId, UploadFileId = fileId,
            AddedByAppUserId = Owner, ReviewState = state,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Owner,
        });

    private static void AddInASession(BenDataContext db, Guid placeId, Guid fileId)
    {
        var sessionId = Guid.NewGuid();
        var documentId = AddFile(db);
        db.FieldSessionUploads.Add(new FieldSessionUpload
        {
            Id = sessionId, SubmittedByAppUserId = Owner, DeviceSessionId = Guid.NewGuid(),
            DocumentUploadFileId = documentId, DeviceModel = "iPhone", PlaceId = placeId,
            PublishedAtUtc = DateTime.UtcNow, MediaReviewState = FeedMediaReviewState.Approved,
            StartedAt = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Owner,
        });
        db.FieldSessionUploadFiles.Add(new FieldSessionUploadFile
        {
            Id = Guid.NewGuid(), FieldSessionUploadId = sessionId, UploadFileId = fileId,
            RelativePath = "media/a.jpg",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Owner,
        });
    }

    /// <summary>One vote by a real account — the voter FK is enforced here, as in production.</summary>
    private static void Vote(BenDataContext db, Guid fileId, EvidenceVoteType how)
    {
        var voter = Guid.NewGuid();
        db.AppUsers.Add(new AppUser
        { Id = voter, DisplayName = "A voter", DateCreated = DateTime.UtcNow });
        db.EvidenceVotes.Add(new EvidenceVote
        {
            Id = Guid.NewGuid(), UploadFileId = fileId, VoterAppUserId = voter,
            VoteType = how, IsPublicVoter = true, DateVoted = DateTime.UtcNow,
        });
    }

    [Fact]
    public async Task A_file_on_two_routes_is_counted_once()
    {
        var f = await SeedAsync();
        await using var _ = f.Sqlite;

        await using (var db = await f.Sqlite.Factory.CreateDbContextAsync())
        {
            // The same photograph, added straight to the place AND carried by a published session
            // recorded there. Adding three route counts together would call this two.
            var shared = AddFile(db);
            AddDirectly(db, f.PlaceId, shared);
            AddInASession(db, f.PlaceId, shared);

            var onlyDirect = AddFile(db);
            AddDirectly(db, f.PlaceId, onlyDirect);

            await db.SaveChangesAsync();
        }

        await using (var db = await f.Sqlite.Factory.CreateDbContextAsync())
        {
            var figures = await PlaceEvidenceTally.ForPlaceAsync(db, f.PlaceId, default);
            Assert.Equal(2, figures.EvidenceCount);
        }
    }

    [Fact]
    public async Task Held_evidence_is_in_no_total()
    {
        var f = await SeedAsync();
        await using var _ = f.Sqlite;

        await using (var db = await f.Sqlite.Factory.CreateDbContextAsync())
        {
            AddDirectly(db, f.PlaceId, AddFile(db));
            AddDirectly(db, f.PlaceId, AddFile(db), FeedMediaReviewState.Held);
            AddDirectly(db, f.PlaceId, AddFile(db), FeedMediaReviewState.Pending);
            await db.SaveChangesAsync();
        }

        await using (var db = await f.Sqlite.Factory.CreateDbContextAsync())
            Assert.Equal(1, (await PlaceEvidenceTally.ForPlaceAsync(db, f.PlaceId, default)).EvidenceCount);
    }

    [Fact]
    public async Task The_split_and_the_score_come_from_the_sites_own_arithmetic()
    {
        var f = await SeedAsync();
        await using var _ = f.Sqlite;

        await using (var db = await f.Sqlite.Factory.CreateDbContextAsync())
        {
            var file = AddFile(db);
            AddDirectly(db, f.PlaceId, file);
            Vote(db, file, EvidenceVoteType.Confirms);
            Vote(db, file, EvidenceVoteType.Confirms);
            Vote(db, file, EvidenceVoteType.Disputes);
            Vote(db, file, EvidenceVoteType.Inconclusive);
            await db.SaveChangesAsync();
        }

        await using (var db = await f.Sqlite.Factory.CreateDbContextAsync())
        {
            var figures = await PlaceEvidenceTally.ForPlaceAsync(db, f.PlaceId, default);

            Assert.Equal(4, figures.VoteCount);
            Assert.Equal(1, figures.VotedOnCount);
            Assert.Equal(2, figures.Confirms);
            Assert.Equal(1, figures.Disputes);
            Assert.Equal(1, figures.Inconclusive);

            // +1 confirms, 0 inconclusive, −1 disputes — Ben's own mapping, from the one place
            // that defines it rather than re-derived here.
            Assert.Equal(1, figures.Score);

            Assert.Equal(0.5, figures.ConfirmsShare);
            Assert.Equal(0.25, figures.DisputesShare);
            Assert.Equal(0.25, figures.InconclusiveShare);
        }
    }

    [Fact]
    public async Task A_place_nobody_has_voted_on_reports_nothing_rather_than_zero()
    {
        var f = await SeedAsync();
        await using var _ = f.Sqlite;

        await using (var db = await f.Sqlite.Factory.CreateDbContextAsync())
        {
            AddDirectly(db, f.PlaceId, AddFile(db));
            await db.SaveChangesAsync();
        }

        await using (var db = await f.Sqlite.Factory.CreateDbContextAsync())
        {
            var figures = await PlaceEvidenceTally.ForPlaceAsync(db, f.PlaceId, default);

            Assert.Equal(1, figures.EvidenceCount);
            Assert.Equal(0, figures.VoteCount);

            // Null, not zero. "Nobody has said" and "everybody said no" are different facts, and a
            // bar that draws them the same way lies about an empty place.
            Assert.Null(figures.ConfirmsShare);
            Assert.Null(figures.DisputesShare);
            Assert.Null(figures.InconclusiveShare);
        }
    }

    [Fact]
    public async Task A_place_corrected_to_a_residence_counts_nothing()
    {
        var f = await SeedAsync();
        await using var _ = f.Sqlite;

        await using (var db = await f.Sqlite.Factory.CreateDbContextAsync())
        {
            var file = AddFile(db);
            AddDirectly(db, f.PlaceId, file);
            AddInASession(db, f.PlaceId, AddFile(db));
            await db.SaveChangesAsync();
        }

        await using (var db = await f.Sqlite.Factory.CreateDbContextAsync())
        {
            Assert.Equal(2, (await PlaceEvidenceTally.ForPlaceAsync(db, f.PlaceId, default)).EvidenceCount);

            (await db.Places.FirstAsync(p => p.Id == f.PlaceId)).Kind = PlaceKind.PrivateResidence;
            await db.SaveChangesAsync();
        }

        // Every route re-asks the place's kind, so a correction empties the total on the next read
        // with nothing to migrate — the same discipline the archive is built on.
        await using (var db = await f.Sqlite.Factory.CreateDbContextAsync())
            Assert.Equal(0, (await PlaceEvidenceTally.ForPlaceAsync(db, f.PlaceId, default)).EvidenceCount);
    }
}
