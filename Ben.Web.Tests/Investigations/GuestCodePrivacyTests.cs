using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services.Investigations;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Investigations;

/// <summary>
/// A guest code never reaches private work, and never names a client (C7).
/// </summary>
/// <remarks>
/// <para>Found by crawling the site the day item 248 shipped, and photographed happening: the
/// join page showed a stranger "Initial Site Assessment" — a real client case — because nothing
/// asked where the visit was or redacted what it was called.</para>
///
/// <para>Two halves, and both are needed. The issue door stops a code being minted on private
/// work at all; the redaction covers codes minted before that rule existed, and the general
/// truth that a rule enforced only where rows are WRITTEN is one release from being no rule.</para>
/// </remarks>
public sealed class GuestCodePrivacyTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid Lead = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();
    private static readonly Guid Guest = Guid.NewGuid();

    private sealed record World(SqliteTestDb Db, Guid InvestigationId, Guid PlaceId);

    /// <summary>A visit, at a place of the given kind, optionally on a private-engagement case.</summary>
    private static async Task<World> SeedAsync(
        PlaceKind kind = PlaceKind.PublicLocation,
        bool privateEngagement = false,
        string title = "Franklin cemetery")
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.Factory.CreateDbContextAsync();

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "Apple-Beta", DateCreated = DateTime.UtcNow, CreatedByAppUserId = Lead });
        foreach (var (id, name) in new[] { (Lead, "The lead"), (Client, "Margaret Winchester"), (Guest, "A walk-up") })
            db.AppUsers.Add(new AppUser { Id = id, DisplayName = name, DateCreated = DateTime.UtcNow });

        var placeId = Guid.NewGuid();
        db.Places.Add(new Place
        {
            Id = placeId, Name = "Somewhere", Kind = kind,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Lead,
        });

        Guid? caseId = null;
        if (privateEngagement)
        {
            caseId = Guid.NewGuid();
            db.Cases.Add(new Case
            {
                Id = caseId.Value, OrganizationId = OrgId, Title = title,
                CaseYear = 2026, OrgCaseNumber = 1,
                IsPrivateEngagement = true,
                PublicPseudonym = "a private client",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Lead,
            });

            // The roster is built from the case's CLIENT (through its originating request) and
            // from its related people. A related person is the cheaper of the two to seed and is
            // a real redaction source — the householder whose name a visit's title picks up.
            db.CaseRelatedPeople.Add(new CaseRelatedPerson
            {
                Id = Guid.NewGuid(), CaseId = caseId.Value,
                Name = "Margaret Winchester",
                LivesAtProperty = true,
                PublicLabel = "a resident",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Lead,
            });
        }

        var investigationId = Guid.NewGuid();
        db.Investigations.Add(new Investigation
        {
            Id = investigationId, OrganizationId = OrgId, Title = title,
            PlaceId = placeId, CaseId = caseId,
            ScheduledDateTime = DateTime.UtcNow, DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = Lead,
        });

        await db.SaveChangesAsync();
        return new World(sqlite, investigationId, placeId);
    }

    private static PublicInvestigationCodeController Guests(SqliteTestDb sqlite, Guid? who = null)
        => new(sqlite.Factory, NullLogger<PublicInvestigationCodeController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = who is { } id
                        ? new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim(ClaimTypes.NameIdentifier, id.ToString())], "Bearer"))
                        : new ClaimsPrincipal(new ClaimsIdentity()),
                },
            },
        };

    // ── The issue door ───────────────────────────────────────────────────────

    [Fact]
    public async Task A_visit_to_somebodys_home_cannot_have_a_code()
    {
        var w = await SeedAsync(PlaceKind.PrivateResidence);
        await using var _ = w.Db;

        await using var db = await w.Db.Factory.CreateDbContextAsync();
        var investigation = await db.Investigations.FirstAsync(i => i.Id == w.InvestigationId);

        var refusal = await GuestCodes.WhyACodeMayNotBeIssuedAsync(db, investigation, default);

        Assert.NotNull(refusal);
        Assert.Contains("somebody's home", refusal);
    }

    [Fact]
    public async Task A_private_engagement_cannot_have_a_code_even_at_a_public_place()
    {
        // The two conditions are different questions: a client can ask for privacy about work at
        // a landmark, and the place alone would wave that through.
        var w = await SeedAsync(PlaceKind.PublicLocation, privateEngagement: true);
        await using var _ = w.Db;

        await using var db = await w.Db.Factory.CreateDbContextAsync();
        var investigation = await db.Investigations.FirstAsync(i => i.Id == w.InvestigationId);

        var refusal = await GuestCodes.WhyACodeMayNotBeIssuedAsync(db, investigation, default);

        Assert.NotNull(refusal);
        Assert.Contains("private engagement", refusal);
    }

    [Fact]
    public async Task Ordinary_public_work_is_not_refused()
    {
        var w = await SeedAsync();
        await using var _ = w.Db;

        await using var db = await w.Db.Factory.CreateDbContextAsync();
        var investigation = await db.Investigations.FirstAsync(i => i.Id == w.InvestigationId);

        // The control. Without it a rule that refused everything would pass both tests above.
        Assert.Null(await GuestCodes.WhyACodeMayNotBeIssuedAsync(db, investigation, default));
    }

    // ── What a stranger is told ──────────────────────────────────────────────

    [Fact]
    public async Task A_code_already_out_there_never_names_the_client()
    {
        // Minted directly, as one issued before the rule existed would have been.
        var w = await SeedAsync(PlaceKind.PublicLocation, privateEngagement: true,
                                title: "Margaret Winchester — the upstairs landing");
        await using var _ = w.Db;

        string typed;
        await using (var db = await w.Db.Factory.CreateDbContextAsync())
        {
            var investigation = await db.Investigations.FirstAsync(i => i.Id == w.InvestigationId);
            var code = await GuestCodes.IssueAsync(db, investigation, Lead, DateTime.UtcNow.AddHours(8), default);
            await db.SaveChangesAsync();
            typed = code.TypedCode;
        }

        var looked = await Guests(w.Db).Look(typed, default);
        var invitation = Assert.IsType<InvestigationCodeInvitation>(
            Assert.IsType<OkObjectResult>(looked.Result).Value);

        Assert.True(invitation.Recognised);
        Assert.NotNull(invitation.InvestigationTitle);

        // The whole point. An anonymous caller with a code off a sheet is not entitled to the
        // name of somebody who asked for their haunting to stay private.
        Assert.DoesNotContain("Margaret", invitation.InvestigationTitle);
        Assert.DoesNotContain("Winchester", invitation.InvestigationTitle);
    }

    [Fact]
    public async Task Redeeming_does_not_name_the_client_either()
    {
        var w = await SeedAsync(PlaceKind.PublicLocation, privateEngagement: true,
                                title: "Margaret Winchester — the upstairs landing");
        await using var _ = w.Db;

        string typed;
        await using (var db = await w.Db.Factory.CreateDbContextAsync())
        {
            var investigation = await db.Investigations.FirstAsync(i => i.Id == w.InvestigationId);
            var code = await GuestCodes.IssueAsync(db, investigation, Lead, DateTime.UtcNow.AddHours(8), default);
            await db.SaveChangesAsync();
            typed = code.TypedCode;
        }

        var joined = await Guests(w.Db, Guest).Redeem(
            new RedeemInvestigationCodeRequest(typed, "A walk-up"), default);
        var redemption = Assert.IsType<InvestigationCodeRedemption>(
            Assert.IsType<OkObjectResult>(joined.Result).Value);

        Assert.True(redemption.Joined);
        // Both the field and the sentence built from it — the sentence is what the page shows.
        Assert.DoesNotContain("Margaret", redemption.InvestigationTitle);
        Assert.DoesNotContain("Margaret", redemption.Says);
    }

    [Fact]
    public async Task An_ordinary_title_is_shown_exactly_as_written()
    {
        var w = await SeedAsync(title: "Franklin cemetery, the north wall");
        await using var _ = w.Db;

        string typed;
        await using (var db = await w.Db.Factory.CreateDbContextAsync())
        {
            var investigation = await db.Investigations.FirstAsync(i => i.Id == w.InvestigationId);
            var code = await GuestCodes.IssueAsync(db, investigation, Lead, DateTime.UtcNow.AddHours(8), default);
            await db.SaveChangesAsync();
            typed = code.TypedCode;
        }

        var looked = await Guests(w.Db).Look(typed, default);
        var invitation = Assert.IsType<InvestigationCodeInvitation>(
            Assert.IsType<OkObjectResult>(looked.Result).Value);

        // Redaction that ate ordinary titles would be its own bug, and a guest standing at a gate
        // needs to recognise the name of the place they are standing at.
        Assert.Equal("Franklin cemetery, the north wall", invitation.InvestigationTitle);
    }
}
