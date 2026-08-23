using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Web.Website.Library.Manage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Item 166 W2: onboarding's two invariants — the stamp is set by finishing AND by skipping
/// (both call the same endpoint, and the gate must never nag twice), and the routing map sends
/// each first-run answer to the door it names.
/// </summary>
public sealed class OnboardingTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    private static MyOnboardingController Build(IDbContextFactory<BenDataContext> factory, Guid userId)
        => new(factory)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
                },
            },
        };

    [Fact]
    public async Task A_fresh_account_reads_unonboarded_and_complete_stamps_it_idempotently()
    {
        var factory = CreateFactory();
        var userId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = userId, UserName = "new", Email = "new@test.com" });
            await db.SaveChangesAsync();
        }
        var ctrl = Build(factory, userId);

        var before = Assert.IsType<OnboardingStateResponse>(
            Assert.IsType<OkObjectResult>((await ctrl.Get(default)).Result).Value);
        Assert.False(before.Onboarded);

        Assert.IsType<NoContentResult>(await ctrl.Complete(default));
        Assert.IsType<NoContentResult>(await ctrl.Complete(default));   // skipping after finishing, or vice versa

        var after = Assert.IsType<OnboardingStateResponse>(
            Assert.IsType<OkObjectResult>((await ctrl.Get(default)).Result).Value);
        Assert.True(after.Onboarded);

        await using var verify = await factory.CreateDbContextAsync();
        Assert.NotNull((await verify.AppUsers.SingleAsync(u => u.Id == userId)).DateOnboarded);
    }

    [Fact]
    public async Task A_second_complete_keeps_the_first_stamp()
    {
        var factory = CreateFactory();
        var userId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser
            {
                Id = userId, UserName = "old", Email = "old@test.com",
                DateOnboarded = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            await db.SaveChangesAsync();
        }

        Assert.IsType<NoContentResult>(await Build(factory, userId).Complete(default));

        await using var verify = await factory.CreateDbContextAsync();
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            (await verify.AppUsers.SingleAsync(u => u.Id == userId)).DateOnboarded);
    }

    [Theory]
    [InlineData(OnboardingRouting.Intent.RequestInvestigation, "/my-requests/new")]
    [InlineData(OnboardingRouting.Intent.JoinGroup, "/find")]
    [InlineData(OnboardingRouting.Intent.RunGroup, "/organizations/new")]
    [InlineData(OnboardingRouting.Intent.JustLooking, "/")]
    public void Each_first_run_answer_lands_at_the_door_it_names(
        OnboardingRouting.Intent intent, string expected)
        => Assert.Equal(expected, OnboardingRouting.DestinationFor(intent));

    [Fact]
    public void An_unknown_intent_falls_back_to_the_front_door()
        => Assert.Equal("/", OnboardingRouting.DestinationFor((OnboardingRouting.Intent)99));
}
