using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for the help-audience ceiling — the single value that decides how much of the help
/// documentation a reader sees.
/// </summary>
public class HelpAudienceControllerTests
{
    private static HelpAudienceController BuildController(
        IDbContextFactory<BenDataContext> factory, Guid userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        return new HelpAudienceController(factory)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
                }
            }
        };
    }

    private static async Task<(IDbContextFactory<BenDataContext> Factory, Guid UserId)> SeedAsync(
        params (OrganizationMemberRole Role, bool IsActive)[] memberships)
    {
        var factory = TestDbFactory.Create();
        var userId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        foreach (var (role, isActive) in memberships)
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(),
                AppUserId = userId,
                OrganizationId = Guid.NewGuid(),
                Role = role,
                IsActive = isActive,
                DateCreated = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();

        return (factory, userId);
    }

    private static HelpAudience Result(ActionResult<HelpAudience> result)
        => Assert.IsType<HelpAudience>(Assert.IsType<OkObjectResult>(result.Result).Value);

    [Fact]
    public async Task Signed_in_user_with_no_memberships_gets_SignedIn()
    {
        var (factory, userId) = await SeedAsync();

        var result = await BuildController(factory, userId).Get(default);

        Assert.Equal(HelpAudience.SignedIn, Result(result));
    }

    [Theory]
    [InlineData(OrganizationMemberRole.Owner, HelpAudience.OrganizationAdministrator)]
    [InlineData(OrganizationMemberRole.Administrator, HelpAudience.OrganizationAdministrator)]
    // A manager runs cases but does not configure the group, so the administration documents
    // are not theirs — this is the line the user drew as "create/own/administer".
    [InlineData(OrganizationMemberRole.Manager, HelpAudience.OrganizationMember)]
    [InlineData(OrganizationMemberRole.Member, HelpAudience.OrganizationMember)]
    [InlineData(OrganizationMemberRole.Viewer, HelpAudience.OrganizationMember)]
    public async Task Organization_role_maps_to_its_audience(OrganizationMemberRole role, HelpAudience expected)
    {
        var (factory, userId) = await SeedAsync((role, true));

        var result = await BuildController(factory, userId).Get(default);

        Assert.Equal(expected, Result(result));
    }

    [Fact]
    public async Task Highest_membership_wins_across_several_groups()
    {
        var (factory, userId) = await SeedAsync(
            (OrganizationMemberRole.Member, true),
            (OrganizationMemberRole.Owner, true));

        var result = await BuildController(factory, userId).Get(default);

        Assert.Equal(HelpAudience.OrganizationAdministrator, Result(result));
    }

    [Fact]
    public async Task Inactive_membership_does_not_count()
    {
        // Someone removed from the group they used to own keeps no documentation access.
        var (factory, userId) = await SeedAsync((OrganizationMemberRole.Owner, false));

        var result = await BuildController(factory, userId).Get(default);

        Assert.Equal(HelpAudience.SignedIn, Result(result));
    }

    [Theory]
    [InlineData(RoleNames.SuperAdmin)]
    [InlineData(RoleNames.Admin)]
    public async Task App_roles_see_everything_without_any_membership(string role)
    {
        var (factory, userId) = await SeedAsync();

        var result = await BuildController(factory, userId, role).Get(default);

        Assert.Equal(HelpAudience.AppAdministrator, Result(result));
    }

    [Fact]
    public async Task Caller_with_no_identifiable_user_gets_the_public_floor()
    {
        var factory = TestDbFactory.Create();
        var ctrl = new HelpAudienceController(factory)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([], "Bearer"))
                }
            }
        };

        var result = await ctrl.Get(default);

        Assert.Equal(HelpAudience.Everyone, Result(result));
    }
}
