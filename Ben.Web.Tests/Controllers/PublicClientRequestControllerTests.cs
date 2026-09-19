using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Asking for an investigation without an account (site evaluation 2026-09-06, phase 1).
/// </summary>
/// <remarks>
/// <para>Two properties carry the weight, and both are the kind that erode quietly.</para>
///
/// <para><b>The answer never varies.</b> The endpoint is anonymous and takes an email address. If
/// its reply differed when the address already had an account — "that email is taken", a different
/// message, even a different validation order — the request form would become a way of testing who
/// has an account on a site about people's homes, one guess at a time.</para>
///
/// <para><b>Nothing is written to somebody else's account.</b> An anonymous caller who types a
/// stranger's address must not be able to put a request, an address or a story on that stranger's
/// <i>My Requests</i>. It is parked instead, and only the account holder — signed in, holding the
/// secret from their own email — can claim it.</para>
/// </remarks>
public sealed class PublicClientRequestControllerTests
{
    private const string StrangerEmail = "stranger@elsewhere.test";
    private const string TakenEmail    = "already@here.test";

    /// <summary>
    /// Generated per run rather than written down. The repository is public and development shares
    /// production's database, so a literal here is a live credential — <c>NoCredentialsInTheRepoTests</c>
    /// says so, and the answer it asks for is this one. Nothing outside the run needs the value.
    /// </summary>
    private static readonly string GoodPassword =
        "T!" + System.Security.Cryptography.RandomNumberGenerator.GetString(
            "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789", 16) + "9";

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// <summary>
    /// A UserManager over the same in-memory store, so an account created by the endpoint is
    /// really there afterwards — and with Identity's real password rules, which one test relies on.
    /// </summary>
    private static UserManager<AppUser> UserManagerFor(IDbContextFactory<BenDataContext> factory)
    {
        var db    = factory.CreateDbContext();
        var store = new UserStore<AppUser, IdentityRole<Guid>, BenDataContext, Guid>(db);
        var options = Options.Create(new IdentityOptions());

        var users = new UserManager<AppUser>(
            store,
            options,
            new PasswordHasher<AppUser>(),
            [],
            [new PasswordValidator<AppUser>()],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<AppUser>>.Instance);

        // The confirmation token needs a provider. Registered rather than stubbed, so the send
        // path these tests exercise is the one the site runs.
        users.RegisterTokenProvider(TokenOptions.DefaultProvider, new StubTokenProvider());
        return users;
    }

    /// <summary>A token provider for the in-memory manager. Round-trips, which is all that is asked of it.</summary>
    private sealed class StubTokenProvider : IUserTwoFactorTokenProvider<AppUser>
    {
        public Task<string> GenerateAsync(string purpose, UserManager<AppUser> manager, AppUser user)
            => Task.FromResult($"{purpose}:{user.Id}");

        public Task<bool> ValidateAsync(string purpose, string token, UserManager<AppUser> manager, AppUser user)
            => Task.FromResult(token == $"{purpose}:{user.Id}");

        public Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<AppUser> manager, AppUser user)
            => Task.FromResult(false);
    }

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory,
        PublicClientRequestController Controller,
        UserManager<AppUser> Users,
        Guid OrgId,
        List<(string To, string Subject, string Body)> Sent);

    private static async Task<World> BuildAsync(bool seedTakenAccount = false)
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var ownerId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new AppUser
            {
                Id = ownerId, Email = "owner@test", UserName = "owner@test",
                NormalizedEmail = "OWNER@TEST", NormalizedUserName = "OWNER@TEST",
                DateCreated = DateTime.UtcNow,
            });
            db.Organizations.Add(new Organization
            {
                Id = orgId, Name = "Nashville Paranormal", UrlName = "nashville",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId, IsAcceptingClients = true,
            });
            await db.SaveChangesAsync();
        }

        var users = UserManagerFor(factory);

        if (seedTakenAccount)
        {
            var taken = new AppUser
            {
                Id = Guid.NewGuid(), Email = TakenEmail, UserName = TakenEmail,
                NormalizedEmail = TakenEmail.ToUpperInvariant(),
                NormalizedUserName = TakenEmail.ToUpperInvariant(),
                DisplayName = "Existing Person", EmailConfirmed = true,
                DateCreated = DateTime.UtcNow,
            };
            var created = await users.CreateAsync(taken, GoodPassword);
            Assert.True(created.Succeeded);
        }

        var sent = new List<(string, string, string)>();
        var email = new Mock<IEmailService>();
        email.SetupGet(e => e.IsConfigured).Returns(true);
        email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Callback<string, string, string, CancellationToken>((to, subject, body, _) => sent.Add((to, subject, body)))
             .Returns(Task.CompletedTask);

        var site = Options.Create(new SiteIdentity { Name = "IsHaunted", BaseUrl = "https://example.test" });

        var accounts = new AccountCreationService(
            users,
            new UserHandleService(factory),
            new Mock<IConfirmationMailer>().Object,
            email.Object,
            site,
            new ConfigurationBuilder().Build(),
            NullLogger<AccountCreationService>.Instance);

        var ctrl = new PublicClientRequestController(
            factory, users, accounts, site, new ConfigurationBuilder().Build(),
            NullLogger<PublicClientRequestController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
            }
        };

        return new World(factory, ctrl, users, orgId, sent);
    }

    private static AnonymousClientRequestSubmission Submission(
        Guid orgId, string email, string? password = null, string name = "Casey Miller",
        decimal? lat = 36.1627m, decimal? lon = -86.7816m, string? description = "<p>Three knocks, 2am.</p>")
        => new("2500 West End Ave", null, "Nashville", "TN", "37203", "US",
               lat, lon, ClientGender.NotProvided, null, description, [orgId], name, email,
               password ?? GoodPassword);

    private static AnonymousSubmitResponse Body(ActionResult<AnonymousSubmitResponse> result)
        => result.Result switch
        {
            OkObjectResult ok         => Assert.IsType<AnonymousSubmitResponse>(ok.Value),
            BadRequestObjectResult br => Assert.IsType<AnonymousSubmitResponse>(br.Value),
            _ => throw new Xunit.Sdk.XunitException($"Unexpected result {result.Result?.GetType().Name}."),
        };

    // ── A stranger with no account ───────────────────────────────────────────

    [Fact]
    public async Task A_new_address_gets_an_account_a_request_and_a_confirmation()
    {
        var w = await BuildAsync();

        var body = Body(await w.Controller.Submit(Submission(w.OrgId, StrangerEmail), default));
        Assert.True(body.Succeeded);

        var user = await w.Users.FindByEmailAsync(StrangerEmail);
        Assert.NotNull(user);
        Assert.Equal("Casey Miller", user!.DisplayName);
        Assert.False(user.EmailConfirmed);

        // C1: the wizard never asks for an @name, so one must be allocated — an account with a
        // null handle cannot be mentioned anywhere until a restart's backfill runs.
        Assert.False(string.IsNullOrWhiteSpace(user.Handle));

        await using var db = await w.Factory.CreateDbContextAsync();
        var request = await db.ClientRequests.SingleAsync();
        Assert.Equal(user.Id, request.AppUserId);
        Assert.Equal(ClientRequestStatus.Submitted, request.Status);
        Assert.Equal("2500 West End Ave", request.StreetAddress1);

        var application = await db.ClientRequestOrganizations.SingleAsync();
        Assert.Equal(w.OrgId, application.OrganizationId);
        Assert.Equal(ClientOrgRequestStatus.Pending, application.Status);

        // Nothing is parked: the request is the person's own from the start.
        Assert.Empty(await db.PendingClientRequests.ToListAsync());
    }

    // ── An address that already has an account ───────────────────────────────

    [Fact]
    public async Task An_existing_address_is_never_confirmed_to_the_caller()
    {
        var mine  = await BuildAsync();
        var taken = await BuildAsync(seedTakenAccount: true);

        var forNew      = Body(await mine.Controller.Submit(Submission(mine.OrgId, StrangerEmail), default));
        var forExisting = Body(await taken.Controller.Submit(Submission(taken.OrgId, TakenEmail), default));

        // Same success, same sentence, same absent field. Any difference here is the oracle.
        Assert.Equal(forNew.Succeeded, forExisting.Succeeded);
        Assert.Equal(forNew.Message, forExisting.Message);
        Assert.Equal(forNew.Field, forExisting.Field);
    }

    [Fact]
    public async Task An_existing_address_gets_nothing_written_to_its_account()
    {
        var w = await BuildAsync(seedTakenAccount: true);

        Assert.True(Body(await w.Controller.Submit(Submission(w.OrgId, TakenEmail), default)).Succeeded);

        await using var db = await w.Factory.CreateDbContextAsync();

        // The heart of it: an anonymous caller cannot add a request to somebody else's account.
        Assert.Empty(await db.ClientRequests.ToListAsync());
        Assert.Empty(await db.ClientRequestOrganizations.ToListAsync());

        var parked = await db.PendingClientRequests.SingleAsync();
        Assert.Equal(TakenEmail.ToUpperInvariant(), parked.NormalizedEmail);
        Assert.Equal("2500 West End Ave", parked.StreetAddress1);

        // The secret is hashed, so reading this table is not enough to forge the link.
        Assert.DoesNotContain("=", parked.SecretHash.Replace("_", ""));
        Assert.NotEqual(parked.Id.ToString(), parked.SecretHash);
    }

    [Fact]
    public async Task The_account_holder_is_told_and_the_stranger_is_not()
    {
        var w = await BuildAsync(seedTakenAccount: true);

        Assert.True(Body(await w.Controller.Submit(Submission(w.OrgId, TakenEmail), default)).Succeeded);

        var note = Assert.Single(w.Sent);
        Assert.Equal(TakenEmail, note.To);
        Assert.Contains("2500 West End Ave", note.Body);
        Assert.Contains("/my-requests/adopt/", note.Body);
    }

    [Fact]
    public async Task An_address_cannot_be_mail_bombed_through_the_request_form()
    {
        // Every parked request emails the holder, and the address is the only thing a sender needs
        // to know. Past the cap nothing is written and nothing is sent — and the caller is told
        // exactly what a first-time sender is told, so the cap is not an oracle either.
        var w = await BuildAsync(seedTakenAccount: true);

        for (var i = 0; i < PublicClientRequestController.MaxPendingPerAddress + 2; i++)
            Assert.True(Body(await w.Controller.Submit(Submission(w.OrgId, TakenEmail), default)).Succeeded);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Equal(PublicClientRequestController.MaxPendingPerAddress,
            await db.PendingClientRequests.CountAsync());
        Assert.Equal(PublicClientRequestController.MaxPendingPerAddress, w.Sent.Count);
    }

    // ── The refusals, which must not vary with the address either ────────────

    [Fact]
    public async Task A_weak_password_is_refused_the_same_way_for_both_addresses()
    {
        var mine  = await BuildAsync();
        var taken = await BuildAsync(seedTakenAccount: true);

        var forNew      = Body(await mine.Controller.Submit(Submission(mine.OrgId, StrangerEmail, password: "abc"), default));
        var forExisting = Body(await taken.Controller.Submit(Submission(taken.OrgId, TakenEmail, password: "abc"), default));

        Assert.False(forNew.Succeeded);
        Assert.Equal(forNew.Message, forExisting.Message);

        // And nothing was parked for the registered address on the way to that refusal.
        await using var db = await taken.Factory.CreateDbContextAsync();
        Assert.Empty(await db.PendingClientRequests.ToListAsync());
    }

    [Fact]
    public async Task An_unverified_address_is_refused()
    {
        var w = await BuildAsync();

        var body = Body(await w.Controller.Submit(
            Submission(w.OrgId, StrangerEmail, lat: null, lon: null), default));

        Assert.False(body.Succeeded);
        Assert.Contains("geocoded", body.Message);
        Assert.Null(await w.Users.FindByEmailAsync(StrangerEmail));
    }

    [Fact]
    public async Task No_story_no_request()
    {
        var w = await BuildAsync();

        var body = Body(await w.Controller.Submit(
            Submission(w.OrgId, StrangerEmail, description: "   "), default));

        Assert.False(body.Succeeded);
        Assert.Null(await w.Users.FindByEmailAsync(StrangerEmail));
    }

    [Fact]
    public async Task A_group_that_does_not_exist_is_refused_before_an_account_is_made()
    {
        var w = await BuildAsync();

        var body = Body(await w.Controller.Submit(
            Submission(Guid.NewGuid(), StrangerEmail), default));

        Assert.False(body.Succeeded);
        // No half-made account left behind by a request that could never be sent.
        Assert.Null(await w.Users.FindByEmailAsync(StrangerEmail));
    }

    [Fact]
    public async Task Three_groups_are_refused()
    {
        var w = await BuildAsync();
        var submission = Submission(w.OrgId, StrangerEmail) with
        {
            OrganizationIds = [w.OrgId, Guid.NewGuid(), Guid.NewGuid()],
        };

        var body = Body(await w.Controller.Submit(submission, default));

        Assert.False(body.Succeeded);
        Assert.Contains("maximum of 2", body.Message);
    }
}
