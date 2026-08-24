using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Item 110: merging one group into another. The load-bearing assertion is the last one in the
/// big test — after the merge, a walk over EVERY foreign key in the EF model finds nothing that
/// still points at the merged group. On SQL Server the husk delete enforces that physically; the
/// InMemory provider does not enforce FKs, so the test enforces it itself, which also makes it
/// the guard that catches a future table the sweep somehow misses.
/// </summary>
public sealed class OrganizationMergeTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    private static IDbContextFactory<BenDataContext> Factory() =>
        new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static OrganizationMergeService Service(IDbContextFactory<BenDataContext> factory)
        => new(factory, new Ben.Data.WebApi.Services.PlatformMessageService(factory));

    private sealed record Seeded(
        IDbContextFactory<BenDataContext> Factory, Guid BaseId, Guid MergedId,
        Guid AdminId, Guid SharedUserId, Guid MergedOnlyUserId, Guid ClientId, Guid MergedCaseId);

    /// <summary>Two groups that collide everywhere collisions are possible.</summary>
    private static async Task<Seeded> SeedAsync()
    {
        var factory = Factory();
        Guid baseId = Guid.NewGuid(), mergedId = Guid.NewGuid(), adminId = Guid.NewGuid();
        Guid sharedUserId = Guid.NewGuid(), mergedOnlyUserId = Guid.NewGuid(), clientId = Guid.NewGuid();
        Guid mergedCaseId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.AddRange(
            new AppUser { Id = adminId, UserName = "a@t", Email = "a@t", DateCreated = DateTime.UtcNow },
            new AppUser { Id = sharedUserId, UserName = "s@t", Email = "s@t", DateCreated = DateTime.UtcNow },
            new AppUser { Id = mergedOnlyUserId, UserName = "m@t", Email = "m@t", DateCreated = DateTime.UtcNow },
            new AppUser { Id = clientId, UserName = "c@t", Email = "c@t", DateCreated = DateTime.UtcNow });
        db.Organizations.AddRange(
            new Organization { Id = baseId, Name = "Base Group", UrlName = "base-group", DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId },
            new Organization { Id = mergedId, Name = "Merged Group", UrlName = "merged-group", DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId });

        // The shared person: Member in the base, OWNER of the merged group — higher role must win.
        db.OrganizationUserMemberships.AddRange(
            new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = baseId, AppUserId = sharedUserId, Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId },
            new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = mergedId, AppUserId = sharedUserId, Role = OrganizationMemberRole.Owner, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId },
            new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = mergedId, AppUserId = mergedOnlyUserId, Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId });

        // Cases that collide on number AND slug.
        db.ClientRequests.Add(new ClientRequest
        {
            Id = Guid.NewGuid(), AppUserId = clientId, Status = ClientRequestStatus.Assigned,
            StreetAddress1 = "1 Elm", City = "Nashville", State = "TN", ZipCode = "37201",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
        });
        var requestId = db.ClientRequests.Local.First().Id;
        db.Cases.AddRange(
            new Case
            {
                Id = Guid.NewGuid(), OrganizationId = baseId, Title = "Base case", UrlName = "the-house",
                CaseYear = 2026, OrgCaseNumber = 1, Status = CaseStatus.Active,
                StreetAddress1 = "1 A St", City = "N", State = "TN", ZipCode = "1", Country = "US",
                DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
            },
            new Case
            {
                Id = mergedCaseId, OrganizationId = mergedId, Title = "Merged case", UrlName = "the-house",
                CaseYear = 2026, OrgCaseNumber = 1, Status = CaseStatus.Active, ClientRequestId = requestId,
                StreetAddress1 = "2 B St", City = "N", State = "TN", ZipCode = "2", Country = "US",
                DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
            });

        // A subscription on the merged group, with contract terms — both must be dropped.
        var subId = Guid.NewGuid();
        db.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            Id = subId, OrganizationId = mergedId, Status = SubscriptionStatus.Active,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
        });
        db.SubscriptionContractTerms.Add(new SubscriptionContractTerms
        {
            Id = Guid.NewGuid(), OrganizationSubscriptionId = subId, TierName = "Small group",
            SubscriptionTierId = Guid.NewGuid(), Price = 15m,
            PeriodStartUtc = DateTime.UtcNow, PeriodEndUtc = DateTime.UtcNow.AddMonths(1),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
        });

        // The same file shared with BOTH groups — the merged copy must be dropped, not reparented.
        var fileId = Guid.NewGuid();
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, AppUserId = adminId, UploadFileTypeId = Guid.NewGuid(),
            FileName = "f.png", StoredFileName = "f.png", ContentType = "image/png", FileSize = 1,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
        });
        db.UploadFileOrganizationShares.AddRange(
            new UploadFileOrganizationShare { Id = Guid.NewGuid(), UploadFileId = fileId, OrganizationId = baseId, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId },
            new UploadFileOrganizationShare { Id = Guid.NewGuid(), UploadFileId = fileId, OrganizationId = mergedId, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId });

        await db.SaveChangesAsync();
        return new Seeded(factory, baseId, mergedId, adminId, sharedUserId, mergedOnlyUserId, clientId, mergedCaseId);
    }

    [Fact]
    public async Task The_merge_moves_everything_and_leaves_no_reference_to_the_merged_group()
    {
        var s = await SeedAsync();
        var error = await Service(s.Factory).MergeAsync(s.BaseId, s.MergedId, "United Watch", s.AdminId, default);
        Assert.Null(error);

        await using var db = await s.Factory.CreateDbContextAsync();

        // The husk is gone; the base survives under the chosen name.
        Assert.Null(await db.Organizations.FirstOrDefaultAsync(o => o.Id == s.MergedId));
        Assert.Equal("United Watch", (await db.Organizations.SingleAsync(o => o.Id == s.BaseId)).Name);

        // The merged group's URL is a permanent alias of the base (item 89).
        var alias = await db.OrganizationUrlNameAliases.SingleAsync(a => a.UrlName == "merged-group");
        Assert.Equal(s.BaseId, alias.OrganizationId);

        // The shared person holds ONE membership, upgraded to their higher role.
        var membership = Assert.Single(await db.OrganizationUserMemberships
            .Where(m => m.AppUserId == s.SharedUserId).ToListAsync());
        Assert.Equal(s.BaseId, membership.OrganizationId);
        Assert.Equal(OrganizationMemberRole.Owner, membership.Role);

        // The merged-only person simply belongs to the base now.
        Assert.Equal(s.BaseId, (await db.OrganizationUserMemberships
            .SingleAsync(m => m.AppUserId == s.MergedOnlyUserId)).OrganizationId);

        // The colliding case was renumbered into the base's sequence and its slug suffixed.
        var mergedCase = await db.Cases.SingleAsync(c => c.Id == s.MergedCaseId);
        Assert.Equal(s.BaseId, mergedCase.OrganizationId);
        Assert.Equal(2, mergedCase.OrgCaseNumber);
        Assert.Equal("the-house-2", mergedCase.UrlName);

        // Subscription and its contract terms are gone; the duplicate file share kept one row.
        Assert.Empty(await db.OrganizationSubscriptions.Where(x => x.OrganizationId == s.MergedId).ToListAsync());
        Assert.Empty(await db.SubscriptionContractTerms.ToListAsync());
        Assert.Single(await db.UploadFileOrganizationShares.ToListAsync());

        // Former members and the client were told.
        Assert.True(await db.UserMessageTos.AnyAsync(t => t.ToAppUserId == s.MergedOnlyUserId));
        Assert.True(await db.UserMessageTos.AnyAsync(t => t.ToAppUserId == s.ClientId));

        // THE invariant: walk every FK in the model — nothing may still point at the merged id.
        foreach (var entityType in db.Model.GetEntityTypes())
        {
            foreach (var fk in entityType.GetForeignKeys()
                .Where(fk => fk.PrincipalEntityType.ClrType == typeof(Organization) && fk.Properties.Count == 1))
            {
                var stale = 0;
                foreach (var row in (System.Collections.IEnumerable)db.GetType()
                    .GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!
                    .MakeGenericMethod(entityType.ClrType).Invoke(db, null)!)
                {
                    if (Equals(db.Entry(row).Property(fk.Properties[0].Name).CurrentValue, s.MergedId)) stale++;
                }
                Assert.True(stale == 0,
                    $"{entityType.ClrType.Name}.{fk.Properties[0].Name} still references the merged group in {stale} row(s)");
            }
        }
    }

    [Fact]
    public async Task Preview_reports_the_collisions_and_mutates_nothing()
    {
        var s = await SeedAsync();
        var (preview, error) = await Service(s.Factory).PreviewAsync(s.BaseId, s.MergedId, default);
        Assert.Null(error);
        Assert.NotNull(preview);

        Assert.Contains(preview!.Notes, n => n.Contains("both groups"));
        Assert.Contains(preview.Notes, n => n.Contains("subscription is dropped"));
        Assert.Contains(preview.Notes, n => n.Contains("renumbered"));
        Assert.Contains(preview.Notes, n => n.Contains("alias"));
        Assert.Contains(preview.Reparented, c => c.Table == nameof(Case) && c.Rows == 1);

        await using var db = await s.Factory.CreateDbContextAsync();
        Assert.NotNull(await db.Organizations.FirstOrDefaultAsync(o => o.Id == s.MergedId));
        Assert.Equal(2, await db.OrganizationUserMemberships.CountAsync(m => m.AppUserId != s.MergedOnlyUserId));
    }

    [Fact]
    public async Task A_group_cannot_merge_into_itself_and_missing_groups_are_refused()
    {
        var s = await SeedAsync();
        var service = Service(s.Factory);
        Assert.NotNull(await service.MergeAsync(s.BaseId, s.BaseId, null, s.AdminId, default));
        Assert.NotNull(await service.MergeAsync(s.BaseId, Guid.NewGuid(), null, s.AdminId, default));
        Assert.NotNull(await service.MergeAsync(Guid.NewGuid(), s.MergedId, null, s.AdminId, default));
    }
}
