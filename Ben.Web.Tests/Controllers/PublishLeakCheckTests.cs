using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Item 176: the case-title leak warning. The pseudonym machinery hides the client's name on
/// every public surface except the one the org typed themselves — the title. Found by item 166
/// W4's anonymous-path audit ("Park, Nashville TN" published the client's surname). Advisory
/// only: the endpoint returns sentences, never a refusal.
/// </summary>
public sealed class PublishLeakCheckTests
{
    // ── The check itself ──────────────────────────────────────────────────────

    [Fact]
    public void A_surname_in_the_title_warns_and_names_the_token()
    {
        var warnings = PublicTitleLeakCheck.Check(
            "Park, Nashville TN", null, ["Daniel", "Park", "Daniel Park"], "742 Evergreen Terrace");
        var w = Assert.Single(warnings);
        Assert.Contains("\"Park\"", w);
        Assert.Contains("client's name", w);
    }

    [Fact]
    public void Whole_words_only_the_parkers_farmhouse_is_not_the_park_family()
    {
        // "Park" inside "Parker" or "Parkway" is not the client's name in the title.
        Assert.Empty(PublicTitleLeakCheck.Check(
            "The Parker Farmhouse on Old Parkway", null, ["Daniel", "Park"], null));
    }

    [Fact]
    public void Matching_is_case_insensitive()
    {
        Assert.NotEmpty(PublicTitleLeakCheck.Check("the PARK house", null, ["Park"], null));
    }

    [Fact]
    public void Short_name_tokens_are_skipped_initials_would_flag_half_the_alphabet()
    {
        Assert.Empty(PublicTitleLeakCheck.Check("A Night at Lo Manor", null, ["Lo", "J."], null));
    }

    [Fact]
    public void The_street_matches_without_its_house_number()
    {
        var warnings = PublicTitleLeakCheck.Check(
            "The Elm Street Apparition", null, [], "1428 Elm Street");
        var w = Assert.Single(warnings);
        Assert.Contains("Elm Street", w);
        Assert.Contains("street", w, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_clean_place_named_title_warns_about_nothing()
    {
        Assert.Empty(PublicTitleLeakCheck.Check(
            "The Hargrove Farmhouse, Franklin TN", null, ["Daniel", "Park", "Daniel Park"], "1428 Elm Street"));
    }

    [Fact]
    public void Name_and_street_together_produce_both_warnings()
    {
        Assert.Equal(2, PublicTitleLeakCheck.Check(
            "Park case, Elm Street", null, ["Park"], "1428 Elm Street").Count);
    }

    [Fact]
    public void A_pseudonym_built_from_the_real_surname_defeats_itself_and_warns()
    {
        // The dev seed shipped exactly this: client Daniel Park, pseudonym "The Park Family".
        var warnings = PublicTitleLeakCheck.Check(
            "The Farmhouse", "The Park Family", ["Daniel", "Park"], null);
        var w = Assert.Single(warnings);
        Assert.Contains("pseudonym", w);
        Assert.Contains("\"Park\"", w);
    }

    [Fact]
    public void An_unrelated_pseudonym_is_what_a_pseudonym_should_be()
    {
        Assert.Empty(PublicTitleLeakCheck.Check(
            "The Farmhouse", "The Hargrove Family", ["Daniel", "Park"], "1428 Elm Street"));
    }

    [Fact]
    public void Null_and_blank_inputs_are_silence_not_a_crash()
    {
        Assert.Empty(PublicTitleLeakCheck.Check(null, null, [null, " "], null));
        Assert.Empty(PublicTitleLeakCheck.Check("  ", null, ["Park"], "1428 Elm St"));
    }

    // ── The endpoint ─────────────────────────────────────────────────────────

    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    private static CaseController Build(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var m = new Mock<AutoMapper.IMapper>();
        return new CaseController(
            factory, m.Object,
            new Ben.Data.WebApi.Services.Billing.SubscriptionLimitGuard(factory),
            new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory),
            new Ben.Data.WebApi.Services.RequestReviewNotifier(factory, new Ben.Data.WebApi.Services.PlatformMessageService(factory)),
            Ben.Web.Tests.TestMailer.Quiet())
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
    }

    private static async Task<(IDbContextFactory<BenDataContext> factory, Guid orgId, Guid userId, Guid caseId)>
        SeedCaseAsync(string title, Guid? clientRequestId = null)
    {
        var factory = new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        Guid orgId = Guid.NewGuid(), userId = Guid.NewGuid(), caseId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = userId, UserName = "u@t.com", Email = "u@t.com", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization { Id = orgId, Name = "Org", UrlName = "org", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = OrganizationMemberRole.Member, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });

        if (clientRequestId is { } reqId)
        {
            var clientId = Guid.NewGuid();
            db.Users.Add(new AppUser
            {
                Id = clientId, UserName = "c@t.com", Email = "c@t.com",
                FirstName = "Daniel", LastName = "Park", DisplayName = "Daniel Park",
                DateCreated = DateTime.UtcNow,
            });
            db.ClientRequests.Add(new ClientRequest
            {
                Id = reqId, AppUserId = clientId, Status = ClientRequestStatus.Assigned,
                StreetAddress1 = "1428 Elm Street", City = "Nashville", State = "TN",
                ZipCode = "37201", DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
            });
        }

        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = orgId, Title = title, Status = CaseStatus.Active,
            ClientRequestId = clientRequestId,
            StreetAddress1 = "1428 Elm Street", City = "Nashville", State = "TN",
            ZipCode = "37201", Country = "US",
            DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        await TestSeeds.BridgeAsync(factory, orgId);
        return (factory, orgId, userId, caseId);
    }

    private static IReadOnlyList<string> Body(ActionResult<IReadOnlyList<string>> result)
        => Assert.IsAssignableFrom<IReadOnlyList<string>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

    [Fact]
    public async Task The_endpoint_finds_the_clients_surname_the_org_never_saw()
    {
        // The org-facing records deliberately carry no client name — the whole point of the
        // endpoint is that the check runs where the name lives.
        var (factory, orgId, userId, caseId) = await SeedCaseAsync("anything", Guid.NewGuid());
        var warnings = Body(await Build(factory, userId)
            .PublishLeakCheck(orgId, caseId, "Park, Nashville TN", null, default));
        Assert.Contains(warnings, w => w.Contains("\"Park\""));
    }

    [Fact]
    public async Task An_internal_case_still_checks_the_street_but_has_no_client_to_leak()
    {
        var (factory, orgId, userId, caseId) = await SeedCaseAsync("anything");

        Assert.Empty(Body(await Build(factory, userId)
            .PublishLeakCheck(orgId, caseId, "Park, Nashville TN", null, default)));
        Assert.Single(Body(await Build(factory, userId)
            .PublishLeakCheck(orgId, caseId, "The Elm Street Apparition", null, default)));
    }

    [Fact]
    public async Task Outsiders_are_refused_and_a_foreign_caseId_does_not_resolve()
    {
        var (factory, orgId, userId, caseId) = await SeedCaseAsync("anything", Guid.NewGuid());

        Assert.IsType<ForbidResult>((await Build(factory, Guid.NewGuid())
            .PublishLeakCheck(orgId, caseId, "Park", null, default)).Result);
        Assert.IsType<NotFoundObjectResult>((await Build(factory, userId)
            .PublishLeakCheck(orgId, Guid.NewGuid(), "Park", null, default)).Result);
    }
}
