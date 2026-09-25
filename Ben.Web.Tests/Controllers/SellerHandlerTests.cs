using Ben.Data.Common.Constants;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Authorization;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The Seller policy (store sellers, backlog 251): the Seller role, by its claim or — for an Entra
/// sign-in, whose token carries no site roles — from the database; and nobody else, not even a
/// SuperAdmin, because every seller endpoint answers about the caller's own items.
/// </summary>
public sealed class SellerHandlerTests
{
    private static AuthorizationHandlerContext Context(params Claim[] claims)
        => new([new SellerRequirement()], new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")), null);

    private static Mock<UserManager<AppUser>> Users()
        => new(Mock.Of<IUserStore<AppUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

    [Fact]
    public async Task The_Seller_role_claim_is_a_seller()
    {
        var context = Context(new Claim(ClaimTypes.Role, RoleNames.Seller), new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));
        await new SellerHandler(Users().Object).HandleAsync(context);
        Assert.True(context.HasSucceeded);
    }

    [Theory]
    [InlineData(RoleNames.SuperAdmin)]
    [InlineData(RoleNames.Admin)]
    [InlineData(RoleNames.Moderator)]
    public async Task No_other_role_is_a_seller(string role)
    {
        var context = Context(new Claim(ClaimTypes.Role, role), new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));
        await new SellerHandler(Users().Object).HandleAsync(context);
        Assert.False(context.HasSucceeded, $"{role} must not reach a seller's workspace by virtue of being {role}");
    }

    [Fact]
    public async Task An_Entra_sign_in_is_a_seller_when_the_database_says_so()
    {
        var person = new AppUser { Id = Guid.NewGuid() };
        var users = Users();
        users.Setup(u => u.FindByIdAsync(person.Id.ToString())).ReturnsAsync(person);
        users.Setup(u => u.IsInRoleAsync(person, RoleNames.Seller)).ReturnsAsync(true);

        var context = Context(new Claim(EntraClaimsTransformation.AppUserIdClaimType, person.Id.ToString()));
        await new SellerHandler(users.Object).HandleAsync(context);
        Assert.True(context.HasSucceeded);

        users.Setup(u => u.IsInRoleAsync(person, RoleNames.Seller)).ReturnsAsync(false);
        var notYet = Context(new Claim(EntraClaimsTransformation.AppUserIdClaimType, person.Id.ToString()));
        await new SellerHandler(users.Object).HandleAsync(notYet);
        Assert.False(notYet.HasSucceeded);
    }
}
