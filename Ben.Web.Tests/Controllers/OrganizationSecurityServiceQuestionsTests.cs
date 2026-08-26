using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The three questions the security service answers, and the cache that makes asking them cheap.
/// </summary>
/// <remarks>
/// These replaced twenty-six per-controller helpers whose names disagreed with what they did —
/// an <c>IsOrgMember</c> that returned "holds a Case.Read grant" among them. Each question is
/// tested for the thing its NAME claims, because that mismatch is the defect being designed out.
/// </remarks>
public class OrganizationSecurityServiceQuestionsTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory, Guid OrgId,
        Guid OwnerId, Guid MemberId, Guid GrantedId, Guid StrangerId);

    private static async Task<World> SeedAsync()
    {
        var factory = CreateFactory();
        var orgId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var member = Guid.NewGuid();
        var granted = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Test Org", UrlName = "test",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
        });

        foreach (var (userId, role) in new[]
                 {
                     (owner,   OrganizationMemberRole.Owner),
                     (member,  OrganizationMemberRole.Member),
                     (granted, OrganizationMemberRole.Member),
                 })
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
                Role = role, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
            });
        }

        // One person holds a direct grant to CREATE cases — a plain member otherwise.
        db.OrganizationAccessGrants.Add(new OrganizationAccessGrant
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = granted,
            TableName = OrganizationSecurityTable.Case,
            Actions = OrganizationSecurityAction.Create,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
        });

        await db.SaveChangesAsync();
        await TestSeeds.BridgeAsync(factory, orgId);
        return new World(factory, orgId, owner, member, granted, Guid.NewGuid());
    }

    private static Ben.Service.RepositoryService.Services.OrganizationSecurityService Build(
        IDbContextFactory<BenDataContext> factory)
        => new(factory);

    // ── MayAsync: the GRANT question ─────────────────────────────────────────

    [Fact]
    public async Task MayAsync_AGrantedAction_IsAllowed()
    {
        var w = await SeedAsync();
        Assert.True(await Build(w.Factory).MayAsync(
            w.GrantedId, w.OrgId, OrganizationPermissionArea.Cases, OrganizationSecurityAction.Create));
    }

    /// <summary>The half that matters now roles are authoritative: no grant means no.</summary>
    [Fact]
    public async Task MayAsync_APlainMemberWithNoGrant_IsRefused()
    {
        var w = await SeedAsync();
        Assert.False(await Build(w.Factory).MayAsync(
            w.MemberId, w.OrgId, OrganizationPermissionArea.Cases, OrganizationSecurityAction.Create));
    }

    /// <summary>A grant is for the action it names, not for everything in the area.</summary>
    [Fact]
    public async Task MayAsync_ADifferentAction_IsNotImplied()
    {
        var w = await SeedAsync();
        Assert.False(await Build(w.Factory).MayAsync(
            w.GrantedId, w.OrgId, OrganizationPermissionArea.Cases, OrganizationSecurityAction.Delete));
    }

    /// <summary>And a grant in one area says nothing about another.</summary>
    [Fact]
    public async Task MayAsync_ADifferentArea_IsNotImplied()
    {
        var w = await SeedAsync();
        Assert.False(await Build(w.Factory).MayAsync(
            w.GrantedId, w.OrgId, OrganizationPermissionArea.Equipment, OrganizationSecurityAction.Create));
    }

    [Fact]
    public async Task MayAsync_TheOwner_PassesWithoutAnyGrant()
    {
        var w = await SeedAsync();
        Assert.True(await Build(w.Factory).MayAsync(
            w.OwnerId, w.OrgId, OrganizationPermissionArea.Cases, OrganizationSecurityAction.Delete));
    }

    // ── IsOwnerOrAdminAsync: the TIER question ───────────────────────────────

    [Fact]
    public async Task IsOwnerOrAdmin_SeparatesTheOwnerFromAMember()
    {
        var w = await SeedAsync();
        var service = Build(w.Factory);
        Assert.True(await service.IsOwnerOrAdminAsync(w.OwnerId, w.OrgId));
        Assert.False(await service.IsOwnerOrAdminAsync(w.MemberId, w.OrgId));
    }

    // ── BelongsToAsync: the MEMBERSHIP question ──────────────────────────────

    /// <summary>
    /// Belonging is not permission — a plain member with no grant still BELONGS.
    /// </summary>
    /// <remarks>
    /// This is the distinction the old helper names blurred: the same member is "yes" to
    /// belonging and "no" to may-create-a-case, and a controller has to be able to say which one
    /// it meant.
    /// </remarks>
    [Fact]
    public async Task BelongsTo_IsTrueForAMemberWhoMayDoNothing()
    {
        var w = await SeedAsync();
        var service = Build(w.Factory);
        Assert.True(await service.BelongsToAsync(w.MemberId, w.OrgId));
        Assert.False(await service.MayAsync(
            w.MemberId, w.OrgId, OrganizationPermissionArea.Cases, OrganizationSecurityAction.Create));
    }

    [Fact]
    public async Task BelongsTo_IsFalseForAStranger()
    {
        var w = await SeedAsync();
        Assert.False(await Build(w.Factory).BelongsToAsync(w.StrangerId, w.OrgId));
    }

    // ── The cache ────────────────────────────────────────────────────────────

    /// <summary>
    /// Asking the same question twice gives the same answer, and asking a different one still
    /// works — a cache keyed too loosely would answer the second from the first.
    /// </summary>
    [Fact]
    public async Task TheCache_DoesNotAnswerOneQuestionWithAnothersVerdict()
    {
        var w = await SeedAsync();
        var service = Build(w.Factory);

        Assert.True(await service.MayAsync(
            w.GrantedId, w.OrgId, OrganizationPermissionArea.Cases, OrganizationSecurityAction.Create));
        Assert.True(await service.MayAsync(
            w.GrantedId, w.OrgId, OrganizationPermissionArea.Cases, OrganizationSecurityAction.Create));

        // Different action, different area, different person — none may borrow the "true" above.
        Assert.False(await service.MayAsync(
            w.GrantedId, w.OrgId, OrganizationPermissionArea.Cases, OrganizationSecurityAction.Delete));
        Assert.False(await service.MayAsync(
            w.GrantedId, w.OrgId, OrganizationPermissionArea.Equipment, OrganizationSecurityAction.Create));
        Assert.False(await service.MayAsync(
            w.MemberId, w.OrgId, OrganizationPermissionArea.Cases, OrganizationSecurityAction.Create));
    }

    // ── Agreement with the existing authority ────────────────────────────────

    /// <summary>
    /// <c>MayAsync</c> must agree with <c>HasAccessAsync</c> for every area and every action.
    /// </summary>
    /// <remarks>
    /// <para>This is the test that makes the consolidation safe to spread across the application.
    /// Twenty-six controllers are going to stop calling <c>HasAccessAsync</c> directly and start
    /// calling <c>MayAsync</c>; if the two ever disagree, the change silently widens or narrows
    /// access somewhere nobody is looking. So rather than trusting the mapping by eye, every
    /// area × action pair is computed BOTH ways and compared — the new answer against the old
    /// authority, over the tables that area actually owns.</para>
    ///
    /// <para>Run for four different people, because the interesting disagreements are at the
    /// edges: the owner who bypasses, the member who holds nothing, the member who holds exactly
    /// one action, and the stranger who is not in the organization at all.</para>
    /// </remarks>
    [Fact]
    public async Task MayAsync_AgreesWithHasAccessAsync_ForEveryAreaAndAction()
    {
        var w = await SeedAsync();
        var service = Build(w.Factory);

        var actions = new[]
        {
            OrganizationSecurityAction.Create, OrganizationSecurityAction.Read,
            OrganizationSecurityAction.Update, OrganizationSecurityAction.Delete,
        };
        var people = new[]
        {
            ("owner", w.OwnerId), ("plain member", w.MemberId),
            ("granted member", w.GrantedId), ("stranger", w.StrangerId),
        };

        var disagreements = new List<string>();

        foreach (var (who, userId) in people)
        foreach (var area in Enum.GetValues<OrganizationPermissionArea>())
        foreach (var action in actions)
        {
            var viaArea = await service.MayAsync(userId, w.OrgId, area, action);

            // The old authority, asked directly: holding the action on ANY table of the area.
            var tables = Ben.Data.Common.Constants.PermissionAreas.Map
                .Where(kv => kv.Value == area).Select(kv => kv.Key);
            var viaTables = false;
            foreach (var table in tables)
                if (await service.HasAccessAsync(userId, w.OrgId, table, action))
                { viaTables = true; break; }

            if (viaArea != viaTables)
                disagreements.Add($"{who} / {area} / {action}: MayAsync={viaArea} HasAccessAsync={viaTables}");
        }

        Assert.True(disagreements.Count == 0,
            "the consolidated question disagrees with the authority it replaces:\n  "
            + string.Join("\n  ", disagreements));
    }

    /// <summary>
    /// Every permission area owns at least one table, or <c>MayAsync</c> would answer "no" for it
    /// forever and nobody would know why.
    /// </summary>
    [Fact]
    public void EveryArea_OwnsAtLeastOneTable()
    {
        var orphans = Enum.GetValues<OrganizationPermissionArea>()
            .Where(area => !Ben.Data.Common.Constants.PermissionAreas.Map.Values.Contains(area))
            .ToList();

        Assert.True(orphans.Count == 0,
            "these areas map to no table, so no grant in them can ever be true: "
            + string.Join(", ", orphans));
    }

    /// <summary>
    /// The table-based check keeps table, action, person and organization apart.
    /// </summary>
    /// <remarks>
    /// <para>Written while <c>HasAccessAsync</c> was briefly cached, and kept after that was
    /// reverted — the assertions are about the ANSWERS, not the mechanism, and they are worth
    /// having either way.</para>
    ///
    /// <para>Why the caching went: <c>PhaseDFlipTests</c> grants a role and re-asks within one
    /// scope, and got the cached "no" from before the grant. See the remarks on
    /// <c>HasAccessAsync</c> — a request that changes access and then acts on it is a normal
    /// shape, so this is the one that must stay uncached until invalidation exists.</para>
    /// </remarks>
    [Fact]
    public async Task TheTableCheck_KeepsTableAndActionApart()
    {
        var w = await SeedAsync();
        var service = Build(w.Factory);

        // Granted: Case + Create. Everything else about this person is false.
        Assert.True(await service.HasAccessAsync(
            w.GrantedId, w.OrgId, OrganizationSecurityTable.Case, OrganizationSecurityAction.Create));
        Assert.True(await service.HasAccessAsync(
            w.GrantedId, w.OrgId, OrganizationSecurityTable.Case, OrganizationSecurityAction.Create));

        Assert.False(await service.HasAccessAsync(
            w.GrantedId, w.OrgId, OrganizationSecurityTable.Case, OrganizationSecurityAction.Delete));
        Assert.False(await service.HasAccessAsync(
            w.GrantedId, w.OrgId, OrganizationSecurityTable.Investigation, OrganizationSecurityAction.Create));
        Assert.False(await service.HasAccessAsync(
            w.MemberId, w.OrgId, OrganizationSecurityTable.Case, OrganizationSecurityAction.Create));
        Assert.False(await service.HasAccessAsync(
            w.GrantedId, Guid.NewGuid(), OrganizationSecurityTable.Case, OrganizationSecurityAction.Create));
    }
}
