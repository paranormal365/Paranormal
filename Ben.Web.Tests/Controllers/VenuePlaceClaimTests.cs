using System.Security.Claims;
using System.Text.RegularExpressions;
using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Scheduling;
using Ben.Data.WebApi.Services.Venues;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Proving a group runs a place (item 235 phase 9), and what a claim may never do.
/// </summary>
/// <remarks>
/// Ben, 2026-09-13: "Code validation is okay as long as we verify we have the right people." Most of
/// these are about the second half — that the address a code goes to is one the claimant did not
/// supply — because a code to an address the claimant chose proves only that they read their own mail.
/// </remarks>
public sealed class VenuePlaceClaimTests
{
    private static readonly Guid HotelOrgId = Guid.NewGuid();     // claims to run the place
    private static readonly Guid SocietyOrgId = Guid.NewGuid();   // has held events there for years
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly Guid SocietyChair = Guid.NewGuid();
    private static readonly Guid Reviewer = Guid.NewGuid();

    private static readonly DateTime LongAgo = DateTime.UtcNow.AddDays(-30);

    private sealed record Mail(List<EmailMessage> Sent, IEmailService Service);

    private static Mail Mailbox()
    {
        var sent = new List<EmailMessage>();
        var email = new Mock<IEmailService>();
        email.SetupGet(e => e.IsConfigured).Returns(true);
        email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessage, CancellationToken>((m, _) => sent.Add(m)).Returns(Task.CompletedTask);
        return new Mail(sent, email.Object);
    }

    private static IOrganizationSecurityService Security()
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid user, Guid org, OrganizationSecurityTable _, OrganizationSecurityAction _, CancellationToken _) =>
                (user == Manager && org == HotelOrgId) || (user == SocietyChair && org == SocietyOrgId));
        return security.Object;
    }

    private static T As<T>(T controller, Guid userId) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
            },
        };
        return controller;
    }

    private static VenueClaimController Claims(SqliteTestDb sqlite, Mail mail, Guid who)
        => As(new VenueClaimController(sqlite.Factory, Security(), new PlatformMessageService(sqlite.Factory),
            mail.Service, Options.Create(new SiteIdentity())), who);

    private static PlaceContactController Contacts(SqliteTestDb sqlite, Guid who)
        => As(new PlaceContactController(sqlite.Factory, Security()), who);

    private static async Task<SqliteTestDb> SeedAsync()
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;

        foreach (var (id, name) in new[] { (Manager, "Mrs Cole"), (SocietyChair, "Sam"), (Reviewer, "Site reviewer") })
            db.Users.Add(new AppUser { Id = id, Email = $"{id:N}@example.test", UserName = $"{id:N}@example.test", DisplayName = name, DateCreated = now });

        db.Organizations.AddRange(
            new Organization { Id = HotelOrgId, Name = "The Thomas House", UrlName = "thomas-house", DateCreated = now, CreatedByAppUserId = Manager },
            new Organization { Id = SocietyOrgId, Name = "Nashville Paranormal", UrlName = "nashville-paranormal", DateCreated = now, CreatedByAppUserId = SocietyChair });

        foreach (var (user, org) in new[] { (Manager, HotelOrgId), (SocietyChair, SocietyOrgId) })
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), AppUserId = user, OrganizationId = org, IsActive = true,
                Role = OrganizationMemberRole.Owner, DateCreated = now, CreatedByAppUserId = user,
            });

        db.Places.Add(new Place { Id = PlaceId, Name = "The Thomas House Hotel", DateCreated = LongAgo, CreatedByAppUserId = SocietyChair });

        // The society's weekend there, published long before anybody claimed the building.
        db.HostedEvents.Add(new HostedEvent
        {
            Id = Guid.NewGuid(), OrganizationId = SocietyOrgId, PlaceId = PlaceId,
            Name = "October Weekend", UrlName = "october-weekend",
            StartsOn = now.Date.AddDays(20), EndsOn = now.Date.AddDays(21),
            LifecycleState = HostedEventLifecycleState.Published,
            VenueArrangement = HostedEventVenueArrangement.External, VenueContactName = "Mrs Cole",
            FirstPublishedUtc = LongAgo, DateCreated = LongAgo, CreatedByAppUserId = SocietyChair,
        });

        await db.SaveChangesAsync();
        return sqlite;
    }

    private static async Task<Guid> ContactAsync(SqliteTestDb sqlite, Guid orgId, Guid by, DateTime at,
        string value = "frontdesk@thomashousehotel.com", bool isPublic = true)
    {
        await using var db = await sqlite.NewContextAsync();
        var id = Guid.NewGuid();
        db.PlaceContacts.Add(new PlaceContact
        {
            Id = id, PlaceId = PlaceId, Kind = PlaceContactKind.Email, Value = value, IsPublic = isPublic,
            OrganizationId = orgId, DateCreated = at, CreatedByAppUserId = by,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static VenueClaimRecord Ok(ActionResult<VenueClaimRecord> result)
        => Assert.IsType<VenueClaimRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private static string CodeIn(Mail mail) => Regex.Match(mail.Sent[^1].HtmlBody, @"\b\d{6}\b").Value;

    // ── which address may prove it ───────────────────────────────────────────

    [Fact]
    public async Task An_address_the_claiming_group_added_proves_nothing()
    {
        await using var sqlite = await SeedAsync();
        var theirOwn = await ContactAsync(sqlite, HotelOrgId, Manager, LongAgo, "manager@gmail.com");

        var mail = Mailbox();
        var start = Assert.IsType<VenueClaimStartRecord>(Assert.IsType<OkObjectResult>(
            (await Claims(sqlite, mail, Manager).Start(HotelOrgId, PlaceId, default)).Result).Value);
        Assert.Empty(start.ProvingContacts);

        var result = await Claims(sqlite, mail, Manager).Claim(HotelOrgId,
            new(PlaceId, VenueClaimantRole.Manager, null, theirOwn), default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(mail.Sent);
    }

    [Fact]
    public async Task An_address_somebody_else_added_only_yesterday_proves_nothing_yet()
    {
        // Or a claimant could have a friend's group type their address in the afternoon they claim.
        await using var sqlite = await SeedAsync();
        await ContactAsync(sqlite, SocietyOrgId, SocietyChair, DateTime.UtcNow.AddDays(-1));

        var start = Assert.IsType<VenueClaimStartRecord>(Assert.IsType<OkObjectResult>(
            (await Claims(sqlite, Mailbox(), Manager).Start(HotelOrgId, PlaceId, default)).Result).Value);
        Assert.Empty(start.ProvingContacts);
    }

    /// <summary>
    /// The code letter hands a template the code — which is the whole letter.
    /// </summary>
    /// <remarks>
    /// Until 2026-09-23 the kind declared no code and the letter handed a template nothing, so a
    /// template written for it would have gone out proving nothing. The code is now REQUIRED of a
    /// template, and supplied here.
    /// </remarks>
    [Fact]
    public async Task The_code_letter_hands_a_template_the_code_it_carries()
    {
        await using var sqlite = await SeedAsync();
        var frontDesk = await ContactAsync(sqlite, SocietyOrgId, SocietyChair, LongAgo);
        var mail = Mailbox();

        Ok(await Claims(sqlite, mail, Manager).Claim(HotelOrgId,
            new(PlaceId, VenueClaimantRole.Manager, null, frontDesk), default));

        var letter = Assert.Single(mail.Sent);
        Assert.Equal(Ben.Data.Common.Mail.MailKinds.VenueClaimCode.Key, letter.Kind);
        Assert.Equal(CodeIn(mail), letter.Payload!.Supplied!["ClaimCode"].Value);
        Assert.Contains("frontdesk@thomashousehotel.com", letter.Payload.Tables!["AppUsers"]["Email"]!.ToString());

        // And a template without the code is refused when it is saved.
        Assert.Equal(["the code"], Ben.Data.Common.Mail.MailTokens.MissingRequired(
            "A code", "<p>Somebody wants to confirm {Places.Name}.</p>", Ben.Data.Common.Mail.MailKinds.VenueClaimCode));
    }

    [Fact]
    public async Task A_code_to_the_venues_own_address_proves_the_claim_and_opens_a_week_for_objections()
    {
        await using var sqlite = await SeedAsync();
        var frontDesk = await ContactAsync(sqlite, SocietyOrgId, SocietyChair, LongAgo);
        var mail = Mailbox();

        var start = Assert.IsType<VenueClaimStartRecord>(Assert.IsType<OkObjectResult>(
            (await Claims(sqlite, mail, Manager).Start(HotelOrgId, PlaceId, default)).Result).Value);
        Assert.Equal("f•••@thomashousehotel.com", Assert.Single(start.ProvingContacts).Masked);

        var claim = Ok(await Claims(sqlite, mail, Manager).Claim(HotelOrgId,
            new(PlaceId, VenueClaimantRole.Manager, null, frontDesk), default));
        Assert.Equal("frontdesk@thomashousehotel.com", Assert.Single(mail.Sent).To);
        Assert.Equal(VenueClaimState.Pending, claim.State);

        var proved = Ok(await Claims(sqlite, mail, Manager).Code(HotelOrgId, claim.Id, new(CodeIn(mail)), default));
        Assert.Equal(VenueClaimState.Proved, proved.State);
        Assert.NotNull(proved.ObjectionsCloseUtc);

        // Proved is not confirmed. Nobody is the venue until the week has passed.
        await using var db = await sqlite.NewContextAsync();
        Assert.Null(await VenueGrants.VerifiedVenueAtAsync(db, PlaceId, default));
    }

    [Fact]
    public async Task Five_wrong_codes_and_the_code_stops_working()
    {
        await using var sqlite = await SeedAsync();
        var frontDesk = await ContactAsync(sqlite, SocietyOrgId, SocietyChair, LongAgo);
        var mail = Mailbox();
        var claim = Ok(await Claims(sqlite, mail, Manager).Claim(HotelOrgId,
            new(PlaceId, VenueClaimantRole.Manager, null, frontDesk), default));
        var right = CodeIn(mail);
        var wrong = right == "000000" ? "111111" : "000000";

        for (var i = 0; i < VenueClaims.MaxAttempts; i++)
            Assert.IsType<BadRequestObjectResult>((await Claims(sqlite, mail, Manager).Code(HotelOrgId, claim.Id, new(wrong), default)).Result);

        var tooLate = await Claims(sqlite, mail, Manager).Code(HotelOrgId, claim.Id, new(right), default);
        var refused = Assert.IsType<BadRequestObjectResult>(tooLate.Result);
        Assert.Contains("Send a new one", Assert.IsType<string>(refused.Value));
    }

    // ── the week, and objecting ──────────────────────────────────────────────

    private static async Task<Guid> ProvedClaimAsync(SqliteTestDb sqlite)
    {
        var frontDesk = await ContactAsync(sqlite, SocietyOrgId, SocietyChair, LongAgo);
        var mail = Mailbox();
        var claim = Ok(await Claims(sqlite, mail, Manager).Claim(HotelOrgId,
            new(PlaceId, VenueClaimantRole.Owner, null, frontDesk), default));
        Ok(await Claims(sqlite, mail, Manager).Code(HotelOrgId, claim.Id, new(CodeIn(mail)), default));
        return claim.Id;
    }

    private static VenueClaimJob Job(SqliteTestDb sqlite)
        => new(sqlite.Factory, new PlatformMessageService(sqlite.Factory), NullLogger<VenueClaimJob>.Instance);

    [Fact]
    public async Task An_unchallenged_claim_takes_effect_after_its_week_and_never_moves_an_event()
    {
        await using var sqlite = await SeedAsync();
        var claimId = await ProvedClaimAsync(sqlite);

        await Job(sqlite).RunAtAsync(DateTime.UtcNow.AddDays(3), default);
        await using (var db = await sqlite.NewContextAsync())
            Assert.Null(await VenueGrants.VerifiedVenueAtAsync(db, PlaceId, default));

        await Job(sqlite).RunAtAsync(DateTime.UtcNow.AddDays(8), default);

        await using var check = await sqlite.NewContextAsync();
        var venue = await VenueGrants.VerifiedVenueAtAsync(check, PlaceId, default);
        Assert.Equal(HotelOrgId, venue?.OrganizationId);
        Assert.Equal(VenueClaimState.Approved, (await check.VenuePlaceClaims.SingleAsync(c => c.Id == claimId)).State);

        // The society's weekend, published before the hotel claimed the building, is exactly as it was.
        var weekend = await check.HostedEvents.SingleAsync();
        Assert.Equal(HostedEventLifecycleState.Published, weekend.LifecycleState);
        Assert.Equal(HostedEventVenueArrangement.External, weekend.VenueArrangement);
        Assert.Null(weekend.VenueGrantId);
    }

    [Fact]
    public async Task An_objection_stops_the_week_and_waits_for_a_person()
    {
        await using var sqlite = await SeedAsync();
        var claimId = await ProvedClaimAsync(sqlite);

        var seen = Assert.IsType<VenueClaimRecord>(Assert.IsType<OkObjectResult>(
            (await Claims(sqlite, Mailbox(), SocietyChair).Get(claimId, default)).Result).Value);
        Assert.Contains(SocietyOrgId, seen.MayObjectFor);

        var objected = Ok(await Claims(sqlite, Mailbox(), SocietyChair).Object(claimId,
            new(SocietyOrgId, "Mrs Cole left in August; the new owners are the Hendersons."), default));
        Assert.Equal(VenueClaimState.Contested, objected.State);

        await Job(sqlite).RunAtAsync(DateTime.UtcNow.AddDays(30), default);

        await using var db = await sqlite.NewContextAsync();
        Assert.Null(await VenueGrants.VerifiedVenueAtAsync(db, PlaceId, default));
    }

    [Fact]
    public async Task Without_an_address_a_claim_needs_evidence_and_a_person()
    {
        await using var sqlite = await SeedAsync();
        var mail = Mailbox();

        Assert.IsType<BadRequestObjectResult>((await Claims(sqlite, mail, Manager).Claim(HotelOrgId,
            new(PlaceId, VenueClaimantRole.Representative, "  ", null), default)).Result);

        var claim = Ok(await Claims(sqlite, mail, Manager).Claim(HotelOrgId,
            new(PlaceId, VenueClaimantRole.Representative, "I book events for the owners; our business licence is attached to the listing.", null), default));
        Assert.Equal(VenueClaimState.Pending, claim.State);
        Assert.Empty(mail.Sent);

        // The job never settles one of these.
        await Job(sqlite).RunAtAsync(DateTime.UtcNow.AddDays(30), default);
        await using (var db = await sqlite.NewContextAsync())
            Assert.Null(await VenueGrants.VerifiedVenueAtAsync(db, PlaceId, default));

        var admin = new AdminVenueClaimController(sqlite.Factory, new PlatformMessageService(sqlite.Factory));
        As(admin, Reviewer);
        var list = Assert.IsType<AdminVenueClaimListRecord>(Assert.IsType<OkObjectResult>((await admin.Get(default)).Result).Value);
        Assert.Contains(list.ToDecide, c => c.Id == claim.Id);

        Assert.IsType<OkObjectResult>((await admin.Approve(claim.Id, new("Checked the licence."), default)).Result);
        await using var check = await sqlite.NewContextAsync();
        Assert.Equal(HotelOrgId, (await VenueGrants.VerifiedVenueAtAsync(check, PlaceId, default))?.OrganizationId);
    }

    [Fact]
    public async Task A_second_group_cannot_be_confirmed_where_one_already_is()
    {
        await using var sqlite = await SeedAsync();
        await using (var db = await sqlite.NewContextAsync())
        {
            db.OrganizationVenueProfiles.Add(new OrganizationVenueProfile
            {
                Id = Guid.NewGuid(), OrganizationId = SocietyOrgId, PlaceId = PlaceId, VerifiedUtc = LongAgo,
                DateCreated = LongAgo, CreatedByAppUserId = SocietyChair,
            });
            await db.SaveChangesAsync();
        }

        var result = await Claims(sqlite, Mailbox(), Manager).Claim(HotelOrgId,
            new(PlaceId, VenueClaimantRole.Owner, "We bought it.", null), default);
        var refused = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Contains("already confirmed", Assert.IsType<string>(refused.Value));
    }

    [Fact]
    public void Nothing_that_handles_a_claim_writes_to_an_event()
    {
        // The rule that makes claiming safe: it changes who must say yes to FUTURE events, and
        // nothing else. A claim that could reach an event could seize an organizer's weekend.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Ben.slnx"))) root = root.Parent;

        foreach (var name in new[] { "VenueClaimController.cs", "AdminVenueClaimController.cs", "VenueClaims.cs", "VenueClaimJob.cs" })
        {
            var path = Directory.EnumerateFiles(Path.Combine(root!.FullName, "Ben.Data.WebApi"), name, SearchOption.AllDirectories).Single();
            var source = Regex.Replace(File.ReadAllText(path), @"//[^\n]*|/\*.*?\*/", "", RegexOptions.Singleline);

            // Reading events is fine — the interested groups are found that way. Writing one is not.
            string[] writes =
            [
                "HostedEvents.Add", "HostedEvents.Remove", "HostedEvents.Update",
                ".VenueArrangement =", ".LifecycleState =", ".VenueGrantId =",
            ];
            Assert.True(!writes.Any(source.Contains)
                        && !Regex.IsMatch(source, @"HostedEvents[^;]*ExecuteUpdateAsync|HostedEvents[^;]*ExecuteDeleteAsync"),
                $"{name} writes to an event.");
        }
    }

    // ── contact details ──────────────────────────────────────────────────────

    [Fact]
    public async Task A_private_detail_is_only_the_recording_groups_and_a_public_one_is_everybodys()
    {
        await using var sqlite = await SeedAsync();
        await ContactAsync(sqlite, SocietyOrgId, SocietyChair, LongAgo, "frontdesk@thomashousehotel.com", isPublic: true);
        await ContactAsync(sqlite, SocietyOrgId, SocietyChair, LongAgo, "cole.mobile@gmail.com", isPublic: false);

        var anyone = new PlaceContactController(sqlite.Factory, Security())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        var publicList = Assert.IsType<PlaceContactListRecord>(Assert.IsType<OkObjectResult>((await anyone.GetPublic(PlaceId, default)).Result).Value);
        Assert.Equal(["frontdesk@thomashousehotel.com"], publicList.Contacts.Select(c => c.Value));

        var hotel = Assert.IsType<PlaceContactListRecord>(Assert.IsType<OkObjectResult>((await Contacts(sqlite, Manager).Get(PlaceId, default)).Result).Value);
        Assert.DoesNotContain(hotel.Contacts, c => c.Value == "cole.mobile@gmail.com");

        var society = Assert.IsType<PlaceContactListRecord>(Assert.IsType<OkObjectResult>((await Contacts(sqlite, SocietyChair).Get(PlaceId, default)).Result).Value);
        Assert.Contains(society.Contacts, c => c.Value == "cole.mobile@gmail.com" && !c.IsPublic);
    }

    [Fact]
    public async Task Once_a_venue_is_confirmed_other_groups_add_private_notes_only_and_the_venue_vouches()
    {
        await using var sqlite = await SeedAsync();
        var societysPublic = await ContactAsync(sqlite, SocietyOrgId, SocietyChair, LongAgo);
        await using (var db = await sqlite.NewContextAsync())
        {
            db.OrganizationVenueProfiles.Add(new OrganizationVenueProfile
            {
                Id = Guid.NewGuid(), OrganizationId = HotelOrgId, PlaceId = PlaceId, VerifiedUtc = LongAgo,
                DateCreated = LongAgo, CreatedByAppUserId = Manager,
            });
            await db.SaveChangesAsync();
        }

        var publicTry = await Contacts(sqlite, SocietyChair).Add(PlaceId,
            new(SocietyOrgId, PlaceContactKind.Phone, "(615) 555-0100", "Front desk", IsPublic: true), default);
        Assert.Contains("private note", Assert.IsType<string>(Assert.IsType<ConflictObjectResult>(publicTry.Result).Value));

        var privateNote = await Contacts(sqlite, SocietyChair).Add(PlaceId,
            new(SocietyOrgId, PlaceContactKind.Phone, "(615) 555-0199", "Mrs Cole for scheduling", IsPublic: false), default);
        Assert.IsType<OkObjectResult>(privateNote.Result);

        var asVenue = Assert.IsType<PlaceContactListRecord>(Assert.IsType<OkObjectResult>((await Contacts(sqlite, Manager).Get(PlaceId, default)).Result).Value);
        var provisional = Assert.Single(asVenue.Contacts, c => c.Id == societysPublic);
        Assert.True(provisional.IsProvisional);
        Assert.True(provisional.CanConfirm);

        var confirmed = Assert.IsType<PlaceContactListRecord>(Assert.IsType<OkObjectResult>(
            (await Contacts(sqlite, Manager).Confirm(PlaceId, societysPublic, default)).Result).Value);
        Assert.False(Assert.Single(confirmed.Contacts, c => c.Id == societysPublic).IsProvisional);

        // And the society can no longer take down what is now the venue's.
        var removeTry = await Contacts(sqlite, SocietyChair).Remove(PlaceId, societysPublic, default);
        Assert.IsType<NotFoundObjectResult>(removeTry.Result);
    }
}
