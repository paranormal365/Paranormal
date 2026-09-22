using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using AutoMapper;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Where a closed account still belongs, and where it does not (Ben, 2026-09-22).
/// </summary>
/// <remarks>
/// <para>Closing an account anonymises it and deliberately keeps the row — otherwise a group's
/// case history would leave with the person who wrote it. The cost is that a former member is
/// still a row in every list that reads the people table, named "A former member".</para>
///
/// <para>Ben drew the line in two different places, and the difference is the point. A group's
/// Members page never shows them: nobody there can give a closed account a role, invite it, or
/// ask it to do anything, so a row for it is pure noise on a working screen. The site-wide Users
/// list keeps them behind a checkbox that is off by default, because a SuperAdmin sometimes IS
/// looking for a closed account.</para>
/// </remarks>
public sealed class FormerMembersTests
{
    private static readonly Guid Admin = new("A0000000-0000-0000-0000-000000000001");
    private static readonly Guid Org = new("B0000000-0000-0000-0000-000000000001");

    private static async Task<SqliteTestDb> SeedAsync()
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.Factory.CreateDbContextAsync();

        db.AppUsers.Add(new AppUser
        { Id = Admin, DisplayName = "A site admin", DateCreated = DateTime.UtcNow });

        db.Organizations.Add(new Organization
        {
            Id = Org, Name = "Nashville Paranormal", UrlName = "nashville",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Admin,
        });

        // One live member and one whose account has been closed.
        var live = Guid.NewGuid();
        var closed = Guid.NewGuid();

        db.AppUsers.Add(new AppUser
        { Id = live, DisplayName = "Sarah Mitchell", DateCreated = DateTime.UtcNow });
        db.AppUsers.Add(new AppUser
        {
            Id = closed, DisplayName = AccountClosure.FormerMemberName,
            DateClosed = DateTime.UtcNow, DateCreated = DateTime.UtcNow,
        });

        foreach (var userId in new[] { live, closed })
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = Org, AppUserId = userId,
                Role = OrganizationMemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Admin,
            });

        await db.SaveChangesAsync();
        return sqlite;
    }

    /// <summary>
    /// Asks the roster endpoint itself, not a copy of its query.
    /// </summary>
    /// <remarks>
    /// The first draft of this test rebuilt the join by hand and asserted on that, which would
    /// have gone on passing with the filter deleted from the controller — a test naming a rule it
    /// does not touch. It calls <see cref="OrganizationController.GetRoster"/> now.
    /// </remarks>
    private static OrganizationController Roster(SqliteTestDb sqlite)
    {
        var mapper = new MapperConfiguration(
            cfg => { }, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
            .CreateMapper();

        var controller = new OrganizationController(
            sqlite.Factory, mapper,
            new Mock<IOrganizationSecurityService>().Object,
            new Mock<IAuditLogService>().Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, Admin.ToString()),
                        new Claim(ClaimTypes.Role, RoleNames.SuperAdmin),
                    ], "Bearer")),
                },
            },
        };
        return controller;
    }

    /// <summary>
    /// A group's roster leaves former members out, and does not remove their membership to do it.
    /// </summary>
    /// <remarks>
    /// Both halves matter. Dropping the membership row would be a much simpler way to clear the
    /// list and would take the person's attribution inside the group with it, which is the one
    /// thing account closure was built to preserve.
    /// </remarks>
    [Fact]
    public async Task AGroupsRosterLeavesFormerMembersOut()
    {
        await using var sqlite = await SeedAsync();

        var result = await Roster(sqlite).GetRoster(Org, default);
        var rows = (IEnumerable<OrgRosterEntry>)((ObjectResult)result.Result!).Value!;

        Assert.Equal(["Sarah Mitchell"], rows.Select(r => r.DisplayName).ToArray());

        // The membership row is still there — the person is off the list, not out of the history.
        await using var db = await sqlite.Factory.CreateDbContextAsync();
        Assert.Equal(2, await db.OrganizationUserMemberships.CountAsync(m => m.OrganizationId == Org));
    }

    /// <summary>The site-wide people table still holds them, for the checkbox to reveal.</summary>
    [Fact]
    public async Task TheSiteWidePeopleTableKeepsThem()
    {
        await using var sqlite = await SeedAsync();
        await using var db = await sqlite.Factory.CreateDbContextAsync();

        Assert.Equal(1, await db.AppUsers.CountAsync(u => u.DateClosed != null));
        Assert.Equal(AccountClosure.FormerMemberName,
            await db.AppUsers.Where(u => u.DateClosed != null).Select(u => u.DisplayName).SingleAsync());
    }
}
