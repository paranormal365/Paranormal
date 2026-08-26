using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// A title may SUGGEST roles. It may never confer them (item 156 step 5).
/// </summary>
/// <remarks>
/// <para><c>OrganizationMemberLevel</c>'s own remarks set the rule these tests defend: "a title is
/// seniority, never permission: it grants nothing, and no code may ever read it to decide access."
/// Step 5 makes titles useful without breaking that, by copying at the moment of assignment rather
/// than inheriting continuously.</para>
///
/// <para>The distinction is invisible at rest — a member with the title and the roles looks
/// identical either way. It shows up on the second and third events: promoting somebody, and
/// editing the ladder afterwards. Those are what is tested here.</para>
/// </remarks>
public class TitleSuggestedRolesTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record World(
        IDbContextFactory<BenDataContext> F, Guid OrgId, Guid AdminId,
        Guid MembershipId, Guid LevelId, Guid RoleId);

    private static async Task<World> SeedAsync()
    {
        var f = CreateFactory();
        var orgId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var levelId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        await using var db = await f.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = adminId, UserName = "a@t.com", DateCreated = DateTime.UtcNow });
        db.Users.Add(new AppUser { Id = memberId, UserName = "m@t.com", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "G", UrlName = $"g-{orgId:N}",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = adminId,
            Role = OrganizationMemberRole.Owner, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = membershipId, OrganizationId = orgId, AppUserId = memberId,
            Role = OrganizationMemberRole.Member, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
        });
        db.OrganizationMemberLevels.Add(new OrganizationMemberLevel
        {
            Id = levelId, OrganizationId = orgId, Name = "Senior Investigator",
            SortOrder = 200, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
        });
        db.OrganizationRoles.Add(new OrganizationRole
        {
            Id = roleId, OrganizationId = orgId, Name = "Case Lead", IsActive = true, SortOrder = 1,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
        });
        db.OrganizationMemberLevelRoles.Add(new OrganizationMemberLevelRole
        {
            Id = Guid.NewGuid(), OrganizationMemberLevelId = levelId, OrganizationRoleId = roleId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
        });
        await db.SaveChangesAsync();
        return new World(f, orgId, adminId, membershipId, levelId, roleId);
    }

    private static OrganizationMemberLevelController Build(World w, Guid userId)
    {
        var ctrl = new OrganizationMemberLevelController(w.F, new Mock<IMapper>().Object);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
            },
        };
        return ctrl;
    }

    private static async Task<int> RolesHeldAsync(World w)
    {
        await using var db = await w.F.CreateDbContextAsync();
        return await db.OrganizationRoleMemberships
            .CountAsync(rm => rm.OrganizationUserMembershipId == w.MembershipId);
    }

    /// <summary>The default: assigning a title changes the title and nothing else.</summary>
    [Fact]
    public async Task Assigning_a_title_alone_grants_nothing()
    {
        var w = await SeedAsync();

        await Build(w, w.AdminId).Assign(
            w.OrgId, w.MembershipId,
            new OrganizationMemberLevelController.AssignMemberLevelRequest(w.LevelId), default);

        Assert.Equal(0, await RolesHeldAsync(w));

        await using var db = await w.F.CreateDbContextAsync();
        var membership = await db.OrganizationUserMemberships.SingleAsync(m => m.Id == w.MembershipId);
        Assert.Equal(w.LevelId, membership.MemberLevelId);   // the title itself did land
    }

    /// <summary>Asking for the suggestions copies them in as ordinary role memberships.</summary>
    [Fact]
    public async Task Asking_for_the_suggested_roles_grants_them()
    {
        var w = await SeedAsync();

        await Build(w, w.AdminId).Assign(
            w.OrgId, w.MembershipId,
            new OrganizationMemberLevelController.AssignMemberLevelRequest(w.LevelId, ApplySuggestedRoles: true),
            default);

        await using var db = await w.F.CreateDbContextAsync();
        var held = await db.OrganizationRoleMemberships
            .SingleAsync(rm => rm.OrganizationUserMembershipId == w.MembershipId);
        Assert.Equal(w.RoleId, held.OrganizationRoleId);
    }

    /// <summary>
    /// Editing a title's suggestions afterwards changes nobody's access.
    /// </summary>
    /// <remarks>
    /// This is the whole reason for copying instead of inheriting. Under live inheritance, adding
    /// a role to a rung would silently grant it to everyone standing on that rung — a permission
    /// change made from a screen labelled "titles", to people nobody was looking at.
    /// </remarks>
    [Fact]
    public async Task Editing_the_ladder_afterwards_changes_nobodys_access()
    {
        var w = await SeedAsync();
        await Build(w, w.AdminId).Assign(
            w.OrgId, w.MembershipId,
            new OrganizationMemberLevelController.AssignMemberLevelRequest(w.LevelId, ApplySuggestedRoles: true),
            default);

        // A second role joins the rung's suggestions, long after the promotion.
        var laterRoleId = Guid.NewGuid();
        await using (var seed = await w.F.CreateDbContextAsync())
        {
            seed.OrganizationRoles.Add(new OrganizationRole
            {
                Id = laterRoleId, OrganizationId = w.OrgId, Name = "Evidence Keeper",
                IsActive = true, SortOrder = 2,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.AdminId,
            });
            await seed.SaveChangesAsync();
        }

        await Build(w, w.AdminId).SetSuggestedRoles(
            w.OrgId, w.LevelId,
            new OrganizationMemberLevelController.SetSuggestedRolesRequest([w.RoleId, laterRoleId]),
            default);

        // Still exactly the one role they were actually given.
        Assert.Equal(1, await RolesHeldAsync(w));
    }

    /// <summary>Clearing a title takes nothing away — access is removed on the roles screen.</summary>
    [Fact]
    public async Task Clearing_the_title_leaves_the_roles_alone()
    {
        var w = await SeedAsync();
        await Build(w, w.AdminId).Assign(
            w.OrgId, w.MembershipId,
            new OrganizationMemberLevelController.AssignMemberLevelRequest(w.LevelId, ApplySuggestedRoles: true),
            default);

        await Build(w, w.AdminId).Assign(
            w.OrgId, w.MembershipId,
            new OrganizationMemberLevelController.AssignMemberLevelRequest(null), default);

        Assert.Equal(1, await RolesHeldAsync(w));
    }

    /// <summary>Granting twice does not double up.</summary>
    [Fact]
    public async Task Applying_the_same_suggestions_twice_adds_one_role_once()
    {
        var w = await SeedAsync();
        var request = new OrganizationMemberLevelController.AssignMemberLevelRequest(
            w.LevelId, ApplySuggestedRoles: true);

        await Build(w, w.AdminId).Assign(w.OrgId, w.MembershipId, request, default);
        await Build(w, w.AdminId).Assign(w.OrgId, w.MembershipId, request, default);

        Assert.Equal(1, await RolesHeldAsync(w));
    }

    /// <summary>A role belonging to another group can never be suggested, or granted.</summary>
    [Fact]
    public async Task A_role_from_another_group_cannot_be_suggested()
    {
        var w = await SeedAsync();
        var otherOrgId = Guid.NewGuid();
        var foreignRoleId = Guid.NewGuid();
        await using (var seed = await w.F.CreateDbContextAsync())
        {
            seed.Organizations.Add(new Organization
            {
                Id = otherOrgId, Name = "Other", UrlName = $"o-{otherOrgId:N}",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.AdminId,
            });
            seed.OrganizationRoles.Add(new OrganizationRole
            {
                Id = foreignRoleId, OrganizationId = otherOrgId, Name = "Theirs",
                IsActive = true, SortOrder = 1,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.AdminId,
            });
            await seed.SaveChangesAsync();
        }

        var result = await Build(w, w.AdminId).SetSuggestedRoles(
            w.OrgId, w.LevelId,
            new OrganizationMemberLevelController.SetSuggestedRolesRequest([foreignRoleId]), default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    /// <summary>A plain member cannot rewrite what a title suggests.</summary>
    [Fact]
    public async Task A_plain_member_cannot_edit_the_suggestions()
    {
        var w = await SeedAsync();
        var plainId = Guid.NewGuid();
        await using (var seed = await w.F.CreateDbContextAsync())
        {
            seed.Users.Add(new AppUser { Id = plainId, UserName = "p@t.com", DateCreated = DateTime.UtcNow });
            seed.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = w.OrgId, AppUserId = plainId,
                Role = OrganizationMemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.AdminId,
            });
            await seed.SaveChangesAsync();
        }

        var result = await Build(w, plainId).SetSuggestedRoles(
            w.OrgId, w.LevelId,
            new OrganizationMemberLevelController.SetSuggestedRolesRequest([]), default);

        Assert.IsType<ForbidResult>(result);
    }

    /// <summary>
    /// Both halves of the feature are reachable from the UI.
    /// </summary>
    /// <remarks>
    /// <para>Written because I built <c>SetSuggestedRoles</c> and shipped no way to call it —
    /// caught only by asking, before committing, which screen an administrator would use. A group
    /// could have been given the endpoint and still never able to say what any title suggests,
    /// leaving the whole step inert. That is the write-only-feature shape this codebase has hit
    /// eight times now, and it always looks finished from the server side.</para>
    ///
    /// <para>Deliberately checks the CLIENT methods rather than the routes: an endpoint reached by
    /// a client method that no component calls is just as unreachable as one with no client method
    /// at all.</para>
    /// </remarks>
    [Fact]
    public void Both_halves_are_reachable_from_a_screen()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var razor = Directory
            .EnumerateFiles(dir!.FullName, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/") && !f.Contains("/worktrees/"))
            .Select(File.ReadAllText)
            .ToList();

        string[] mustBeCalled =
        [
            "SetSuggestedRolesAsync",   // an admin can say what a title suggests
            "GetSuggestedRolesAsync",   // …and see what it currently suggests
            "applySuggestedRoles",      // …and the offer can actually be accepted
        ];

        var unreachable = mustBeCalled
            .Where(name => !razor.Any(text => text.Contains(name)))
            .ToList();

        Assert.True(unreachable.Count == 0,
            "these exist on the server and no screen calls them, so the feature is inert:\n  "
            + string.Join("\n  ", unreachable));
    }

    /// <summary>
    /// Nothing anywhere reads the suggestions to decide access.
    /// </summary>
    /// <remarks>
    /// The ratchet on the rule. A future convenience — "just check the title's roles here" — would
    /// turn copy-on-assign into live inheritance without anybody deciding to, and every behavioural
    /// test above would still pass. Only the security service and this one controller may name the
    /// table at all.
    /// </remarks>
    [Fact]
    public void The_suggestion_table_is_read_only_where_titles_are_assigned()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        string[] allowed =
        [
            "OrganizationMemberLevelController.cs",   // assigns titles, and offers the copy
            "BenDataContext.cs",                      // declares the table
            "TitleSuggestedRolesTests.cs",            // this file
        ];

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(dir!.FullName, "*.cs", SearchOption.AllDirectories))
        {
            // Normalized before matching: EnumerateFiles returns BACKSLASH paths on Windows, so
            // "/obj/" matched nothing there and the scan swept generated and migration files,
            // reporting offenders that were never source. A guard defeated by the paths it reads
            // is the same trap the source-scan guards keep falling into — the exclusion list was
            // right, the separator was not. Found by a Windows run, not by this machine.
            var slashed = file.Replace('\\', '/');
            if (slashed.Contains("/obj/") || slashed.Contains("/bin/") || slashed.Contains("/worktrees/")
                || slashed.Contains("/Migrations/") || slashed.Contains("/Entities/"))
                continue;
            if (allowed.Contains(Path.GetFileName(file))) continue;

            // Comments stripped: a remark explaining the rule must not trip the rule.
            var text = string.Join('\n', File.ReadLines(file).Select(l => l.Split("//")[0]));
            if (text.Contains("OrganizationMemberLevelRoles"))
                offenders.Add(Path.GetRelativePath(dir.FullName, file));
        }

        Assert.True(offenders.Count == 0,
            "a title's suggested roles are being read outside the assignment path — that is live "
            + "inheritance, and a title must never decide access:\n  " + string.Join("\n  ", offenders));
    }
}
