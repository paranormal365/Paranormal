using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Investigations;

/// <summary>
/// A guest code, and the passes it minted, are cleared a month after the night (crawl C3).
/// </summary>
/// <remarks>
/// On a real database with its foreign keys on, because the passes point at the code and a sweep
/// that got the order wrong would be refused rather than silently half-done.
/// </remarks>
public sealed class GuestCodeExpiryJobTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid Guide = Guid.NewGuid();
    private static readonly Guid Guest = Guid.NewGuid();

    [Fact]
    public async Task A_code_a_month_past_its_night_goes_with_its_passes_and_a_recent_one_stays()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var now = DateTime.UtcNow;
        Guid investigationId;

        Guid Old, Recent, Live;
        await using (var db = await sqlite.NewContextAsync())
        {
            db.Organizations.Add(new Organization { Id = OrgId, Name = "Night walkers", DateCreated = now, CreatedByAppUserId = Guide });
            db.AppUsers.Add(new AppUser { Id = Guide, DisplayName = "The guide", DateCreated = now });
            db.AppUsers.Add(new AppUser { Id = Guest, DisplayName = "A walk-up", DateCreated = now });
            var placeId = Guid.NewGuid();
            db.Places.Add(new Place { Id = placeId, Name = "The cemetery", Kind = PlaceKind.PublicLocation, DateCreated = now, CreatedByAppUserId = Guide });
            investigationId = Guid.NewGuid();
            db.Investigations.Add(new Investigation
            {
                Id = investigationId, OrganizationId = OrgId, Title = "Tonight", PlaceId = placeId,
                ScheduledDateTime = now, DateCreated = now, CreatedByAppUserId = Guide,
            });

            InvestigationJoinCode Code(DateTime expires, string typed) => new()
            {
                Id = Guid.NewGuid(), InvestigationId = investigationId, OrganizationId = OrgId,
                Token = Guid.NewGuid().ToString("N"), TypedCode = typed, ExpiresUtc = expires,
                DateCreated = expires.AddDays(-1), CreatedByAppUserId = Guide,
            };
            var old = Code(now - GuestCodeExpiryJob.Grace - TimeSpan.FromDays(1), "OLDCODE1");
            var recent = Code(now - TimeSpan.FromDays(10), "RECENT01");
            var live = Code(now + TimeSpan.FromHours(8), "LIVECODE");
            db.InvestigationJoinCodes.AddRange(old, recent, live);
            (Old, Recent, Live) = (old.Id, recent.Id, live.Id);

            foreach (var code in new[] { old, recent })
                db.InvestigationGuestPasses.Add(new InvestigationGuestPass
                {
                    Id = Guid.NewGuid(), InvestigationJoinCodeId = code.Id, InvestigationId = investigationId,
                    AppUserId = Guest, IssuedUtc = code.DateCreated, DateCreated = code.DateCreated,
                    CreatedByAppUserId = Guest,
                });
            await db.SaveChangesAsync();
        }

        await new GuestCodeExpiryJob(sqlite.Factory, NullLogger<GuestCodeExpiryJob>.Instance).RunAsync(default);

        await using (var db = await sqlite.NewContextAsync())
        {
            var left = await db.InvestigationJoinCodes.Select(c => c.Id).ToListAsync();
            Assert.DoesNotContain(Old, left);
            Assert.Contains(Recent, left);
            Assert.Contains(Live, left);

            // The old code's pass went with it; the recent one's is still on the guide's list.
            Assert.Equal(Recent, (await db.InvestigationGuestPasses.SingleAsync()).InvestigationJoinCodeId);

            // Nothing about the people or the night itself was touched.
            Assert.True(await db.AppUsers.AnyAsync(u => u.Id == Guest));
            Assert.True(await db.Investigations.AnyAsync(i => i.Id == investigationId));
        }
    }
}
