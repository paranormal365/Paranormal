using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.SeedData;

namespace Ben.Web.Tests.Services.DesignedMenus;

/// <summary>
/// An event at a venue, with two nights and a dinner on each, for the designed-menu tests
/// (10/13/2026). Every id is fresh per fixture, so tests sharing one database can't collide.
/// </summary>
internal sealed class MenuFixture
{
    public Guid HostId { get; } = Guid.NewGuid();
    public Guid OrgId { get; } = Guid.NewGuid();
    public Guid OtherOrgId { get; } = Guid.NewGuid();
    public Guid PlaceId { get; } = Guid.NewGuid();
    public Guid EventId { get; } = Guid.NewGuid();
    public Guid FridayId { get; } = Guid.NewGuid();
    public Guid SaturdayId { get; } = Guid.NewGuid();
    public Guid FridayDinnerId { get; } = Guid.NewGuid();
    public Guid SaturdayDinnerId { get; } = Guid.NewGuid();

    public static readonly DateTime Friday = new(2026, 10, 30);
    public static readonly DateTime Saturday = new(2026, 10, 31);

    public static async Task<MenuFixture> SeedAsync(SqliteTestDb sqlite)
    {
        var f = new MenuFixture();
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;

        db.Users.Add(new AppUser
        {
            Id = f.HostId, Email = $"{f.HostId:N}@example.com", UserName = $"{f.HostId:N}@example.com",
            DisplayName = "The Host", DateCreated = now,
        });
        db.Organizations.Add(new Organization
        {
            Id = f.OrgId, Name = "Paranormal 365", UrlName = $"p365-{f.OrgId:N}"[..20],
            DateCreated = now, CreatedByAppUserId = f.HostId,
        });
        db.Organizations.Add(new Organization
        {
            Id = f.OtherOrgId, Name = "Another Group", UrlName = $"other-{f.OtherOrgId:N}"[..20],
            DateCreated = now, CreatedByAppUserId = f.HostId,
        });
        db.Places.Add(new Place
        {
            Id = f.PlaceId, Name = "The Thomas House Hotel", DateCreated = now, CreatedByAppUserId = f.HostId,
        });
        db.HostedEvents.Add(new HostedEvent
        {
            Id = f.EventId, OrganizationId = f.OrgId, PlaceId = f.PlaceId,
            Name = "Halloween Lock-In", UrlName = $"lock-in-{f.EventId:N}"[..20],
            StartsOn = Friday, EndsOn = Saturday, LifecycleState = HostedEventLifecycleState.Published,
            DateCreated = now, CreatedByAppUserId = f.HostId,
        });
        db.HostedEventNights.AddRange(
            new HostedEventNight { Id = f.FridayId, HostedEventId = f.EventId, Date = Friday, DateCreated = now, CreatedByAppUserId = f.HostId },
            new HostedEventNight { Id = f.SaturdayId, HostedEventId = f.EventId, Date = Saturday, DateCreated = now, CreatedByAppUserId = f.HostId });
        db.HostedEventMenus.AddRange(
            new HostedEventMenu { Id = f.FridayDinnerId, HostedEventNightId = f.FridayId, Title = "Dinner", ServedAtLocal = new TimeSpan(19, 0, 0), DateCreated = now, CreatedByAppUserId = f.HostId },
            new HostedEventMenu { Id = f.SaturdayDinnerId, HostedEventNightId = f.SaturdayId, Title = "Dinner", ServedAtLocal = new TimeSpan(19, 0, 0), DateCreated = now, CreatedByAppUserId = f.HostId });

        await db.SaveChangesAsync();
        await MenuCategorySeeder.SeedAsync(db, default);
        return f;
    }

    public static Guid CategoryId(BenDataContext db, string name)
        => db.MenuCategories.Single(c => c.NameKey == MenuCategory.KeyFor(name)).Id;
}
