using System.Security.Claims;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Places;

/// <summary>
/// Merging two records of one building keeps every room scan (LiDAR plan, phase 1).
/// </summary>
/// <remarks>
/// A scan's key to its place is CASCADE, so a merge that forgot them would not fail — it would
/// quietly delete every scan of the losing record. And two records of one building can each have a
/// current scan of the same room, which the one-current-scan index refuses to put side by side.
/// </remarks>
public sealed class PlaceScanMergeTests
{
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly DateTime Start = new(2026, 10, 28, 21, 0, 0, DateTimeKind.Utc);

    private static PlaceScan Scan(Guid placeId, string room, int version, PlaceScanState state) => new()
    {
        Id = Guid.NewGuid(), PlaceId = placeId, RoomKey = room.ToLowerInvariant(), RoomName = room,
        Version = version, State = state, Retention = PlaceScanRetention.ModelOnly, ScannedUtc = Start,
        ScannedByAppUserId = Admin, OwnerAppUserId = Admin, StoragePath = $"places/{placeId}/scans/{Guid.NewGuid():N}/room.scan",
        FileSize = 10, Sha256 = new string('0', 64), ScannerVersion = 1,
        DateCreated = Start, CreatedByAppUserId = Admin,
    };

    [Fact]
    public async Task Merging_two_records_of_one_building_keeps_every_scan()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        Guid losing = Guid.NewGuid(), surviving = Guid.NewGuid();
        PlaceScan losingBedroom, losingKitchen, survivingBedroom;

        await using (var db = await sqlite.NewContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = Admin, DisplayName = "A site admin", DateCreated = Start });
            foreach (var (id, name) in new[] { (losing, "Cragfont"), (surviving, "Cragfont House") })
                db.Places.Add(new Place
                {
                    Id = id, Name = name, City = "Castalian Springs", State = "TN", Kind = PlaceKind.PublicLocation,
                    DateCreated = Start, CreatedByAppUserId = Admin,
                });

            db.PlaceScans.AddRange(
                losingBedroom = Scan(losing, "Bedroom", 1, PlaceScanState.Current),
                losingKitchen = Scan(losing, "Kitchen", 1, PlaceScanState.Current),
                survivingBedroom = Scan(surviving, "Bedroom", 1, PlaceScanState.Current));
            db.PlaceScanHolds.Add(new PlaceScanHold
            {
                Id = Guid.NewGuid(), PlaceId = losing, RoomKey = "hall", RoomName = "Hall", AppUserId = Admin,
                TakenUtc = Start, ExpiresUtc = Start.AddMinutes(20),
            });
            await db.SaveChangesAsync();
        }

        var audit = new Mock<IAuditLogService>();
        audit.Setup(a => a.LogDeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<Guid>(), It.IsAny<string>()))
             .Returns(Task.CompletedTask);
        var controller = new AdminPlaceMergeController(sqlite.Factory, audit.Object, NullLogger<AdminPlaceMergeController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Admin.ToString())], "Bearer")),
                },
            },
        };

        Assert.IsType<OkObjectResult>((await controller.Merge(losing, new AdminPlaceMergeController.MergeRequest(surviving), default)).Result);

        await using var after = await sqlite.NewContextAsync();
        var scans = await after.PlaceScans.AsNoTracking().ToDictionaryAsync(s => s.Id);
        Assert.Equal(3, scans.Count);
        Assert.All(scans.Values, s => Assert.Equal(surviving, s.PlaceId));

        // The survivor's bedroom stays the room's scan; the loser's is kept after it.
        Assert.Equal(PlaceScanState.Current, scans[survivingBedroom.Id].State);
        Assert.Equal(PlaceScanState.Previous, scans[losingBedroom.Id].State);
        Assert.Equal(2, scans[losingBedroom.Id].Version);
        // A room only the loser had keeps its scan as it was, file and all.
        Assert.Equal(PlaceScanState.Current, scans[losingKitchen.Id].State);
        Assert.Equal(1, scans[losingKitchen.Id].Version);
        Assert.Equal(losingKitchen.StoragePath, scans[losingKitchen.Id].StoragePath);

        Assert.Empty(after.PlaceScanHolds);
    }
}
