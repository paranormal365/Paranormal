using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Places;

/// <summary>
/// Merging two records of one building keeps what people contributed to both (C6).
/// </summary>
/// <remarks>
/// <para><b>What this was.</b> `PlaceEvidence`'s key to `Place` is CASCADE, and the merge
/// repointed seven kinds of row and then deleted the loser — so every photograph, recording and
/// description anybody had added to the losing record was destroyed, silently, by a tool called
/// "merge". The duplicate-places screen exists precisely because two records of one building
/// happen, which is to say this would have fired on the cases it was built for.</para>
///
/// <para>The second test is the one that needed thought: the unique <c>(PlaceId, UploadFileId)</c>
/// index means a file present at BOTH records cannot simply be repointed. A collision is the two
/// records agreeing, so the loser's row is dropped and the survivor's kept — with its own caption
/// and its own votes.</para>
/// </remarks>
public sealed class PlaceMergeKeepsEvidenceTests
{
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly Guid FileTypeId = new("70000000-0000-0000-0000-000000000001");

    private sealed record Two(SqliteTestDb Db, Guid Losing, Guid Surviving);

    private static async Task<Two> SeedAsync()
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.Factory.CreateDbContextAsync();

        db.AppUsers.Add(new AppUser
        { Id = Admin, DisplayName = "A site admin", DateCreated = DateTime.UtcNow });
        db.UploadFileTypes.Add(new UploadFileType
        { Id = FileTypeId, Name = "Feed media", DateCreated = DateTime.UtcNow, CreatedByAppUserId = Admin });

        var losing = Guid.NewGuid();
        var surviving = Guid.NewGuid();
        foreach (var (id, name) in new[] { (losing, "Cragfont"), (surviving, "Cragfont House") })
            db.Places.Add(new Place
            {
                Id = id, Name = name, City = "Castalian Springs", State = "TN",
                Kind = PlaceKind.PublicLocation,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Admin,
            });

        await db.SaveChangesAsync();
        return new Two(sqlite, losing, surviving);
    }

    private static Guid AddFile(BenDataContext db)
    {
        var id = Guid.NewGuid();
        db.UploadFiles.Add(new UploadFile
        {
            Id = id, UploadFileTypeId = FileTypeId, AppUserId = Admin,
            FileName = "a.jpg", StoredFileName = $"{id}.jpg", ContentType = "image/jpeg",
            FileSize = 10, StoragePath = $"x/{id}.jpg",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Admin,
        });
        return id;
    }

    private static void Contribute(BenDataContext db, Guid placeId, Guid fileId, string caption)
        => db.PlaceEvidence.Add(new PlaceEvidence
        {
            Id = Guid.NewGuid(), PlaceId = placeId, UploadFileId = fileId,
            AddedByAppUserId = Admin, Caption = caption,
            ReviewState = FeedMediaReviewState.Approved,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Admin,
        });

    private static AdminPlaceMergeController Controller(SqliteTestDb sqlite)
    {
        var audit = new Mock<IAuditLogService>();
        audit.Setup(a => a.LogDeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(),
                                          It.IsAny<Guid>(), It.IsAny<string>()))
             .Returns(Task.CompletedTask);

        return new AdminPlaceMergeController(
            sqlite.Factory, audit.Object, NullLogger<AdminPlaceMergeController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, Admin.ToString())], "Bearer")),
                },
            },
        };
    }

    [Fact]
    public async Task What_people_added_to_the_losing_record_moves_with_it()
    {
        var two = await SeedAsync();
        await using var _ = two.Db;

        await using (var db = await two.Db.Factory.CreateDbContextAsync())
        {
            Contribute(db, two.Losing, AddFile(db), "The upstairs corridor");
            Contribute(db, two.Losing, AddFile(db), "The frontage");
            Contribute(db, two.Surviving, AddFile(db), "The cellar steps");
            await db.SaveChangesAsync();
        }

        var result = await Controller(two.Db).Merge(
            two.Losing, new AdminPlaceMergeController.MergeRequest(two.Surviving), default);

        var merged = Assert.IsType<AdminPlaceMergeController.MergeResult>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(2, merged.Contributions);

        await using (var db = await two.Db.Factory.CreateDbContextAsync())
        {
            // Three contributions existed; three survive, all at the surviving record, and
            // nothing was left pointing at a place that no longer exists.
            Assert.Equal(3, await db.PlaceEvidence.CountAsync());
            Assert.Equal(3, await db.PlaceEvidence.CountAsync(e => e.PlaceId == two.Surviving));
            Assert.Equal(0, await db.PlaceEvidence.CountAsync(e => e.PlaceId == two.Losing));
        }
    }

    [Fact]
    public async Task The_same_file_at_both_records_collapses_to_one()
    {
        var two = await SeedAsync();
        await using var _ = two.Db;

        Guid shared;
        await using (var db = await two.Db.Factory.CreateDbContextAsync())
        {
            // Somebody added one photograph to both records of the building — exactly what a
            // merge screen exists to tidy, and what the unique index refuses to let through.
            shared = AddFile(db);
            Contribute(db, two.Losing, shared, "from the drive");
            Contribute(db, two.Surviving, shared, "the frontage");
            Contribute(db, two.Losing, AddFile(db), "the cellar");
            await db.SaveChangesAsync();
        }

        var result = await Controller(two.Db).Merge(
            two.Losing, new AdminPlaceMergeController.MergeRequest(two.Surviving), default);

        var merged = Assert.IsType<AdminPlaceMergeController.MergeResult>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        // One moved; the duplicate was already home and is not counted as moved.
        Assert.Equal(1, merged.Contributions);

        await using (var db = await two.Db.Factory.CreateDbContextAsync())
        {
            Assert.Equal(2, await db.PlaceEvidence.CountAsync());
            Assert.Equal(1, await db.PlaceEvidence.CountAsync(e => e.UploadFileId == shared));

            // The survivor's own row is the one kept, with its own caption.
            Assert.Equal("the frontage",
                await db.PlaceEvidence.Where(e => e.UploadFileId == shared)
                    .Select(e => e.Caption).FirstAsync());
        }
    }
}
