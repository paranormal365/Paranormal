using Ben.Data.WebApi.SeedData;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A role row whose lookup name is missing breaks every role change on that role, silently.
/// </summary>
/// <remarks>
/// Identity resolves a role by <c>NormalizedName</c>. A row written by hand has it empty, and then
/// <c>AddToRoleAsync</c> and <c>RemoveFromRoleAsync</c> throw instead of failing — a 500 with no body, which the Site
/// Roles page could only report as "the server did not say why" (Ben, 2026-09-15).
/// </remarks>
public sealed class RoleLookupNamesTests
{
    [Fact]
    public async Task A_row_with_no_lookup_name_is_repaired_and_named()
    {
        await using var test = await SqliteTestDb.CreateAsync();
        await using (var seed = await test.Factory.CreateDbContextAsync())
        {
            seed.Roles.Add(new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = "SuperAdmin", NormalizedName = null });
            seed.Roles.Add(new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = "Admin", NormalizedName = "admin" });
            seed.Roles.Add(new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = "Moderator", NormalizedName = "MODERATOR" });
            await seed.SaveChangesAsync();
        }

        await using var db = await test.Factory.CreateDbContextAsync();
        var repaired = await RoleLookupNames.RepairAsync(db);

        Assert.Equal(["SuperAdmin", "Admin"], repaired.OrderByDescending(r => r).ToList());
        var rows = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(db.Roles);
        Assert.All(rows, r => Assert.Equal(r.Name!.ToUpperInvariant(), r.NormalizedName));
    }

    [Fact]
    public async Task Rows_that_are_already_right_are_left_alone()
    {
        await using var test = await SqliteTestDb.CreateAsync();
        await using (var seed = await test.Factory.CreateDbContextAsync())
        {
            seed.Roles.Add(new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = "Admin", NormalizedName = "ADMIN" });
            await seed.SaveChangesAsync();
        }

        await using var db = await test.Factory.CreateDbContextAsync();
        Assert.Empty(await RoleLookupNames.RepairAsync(db));
    }
}
