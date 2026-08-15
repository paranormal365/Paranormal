using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Self-service contact info: the caller's own emails, phones, addresses and links.
/// </summary>
/// <remarks>
/// Two things carry real weight here and the rest is ordinary CRUD. First, every row is matched on
/// id <i>and</i> owner, so somebody else's row is invisible rather than merely forbidden. Second,
/// an email address only becomes publishable once it has been validated — that is what stops a
/// person publishing a stranger's address and then being found by it.
/// </remarks>
public class MyContactInfoControllerTests
{
    private static readonly Guid EmailTypeId = Guid.NewGuid();
    private static readonly Guid PhoneTypeId = Guid.NewGuid();
    private static readonly Guid AddressTypeId = Guid.NewGuid();
    private static readonly Guid LinkTypeId = Guid.NewGuid();

    private static ClaimsPrincipal As(Guid userId) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer"));

    private static MyContactInfoController Build(IDbContextFactory<BenDataContext> factory, Guid userId)
        => new(factory, new Mock<IAuditLogService>().Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = As(userId) }
            }
        };

    /// <summary>An unconfigured mail service — the default in every environment today.</summary>
    private static IEmailService UnconfiguredEmail()
    {
        var m = new Mock<IEmailService>();
        m.SetupGet(x => x.IsConfigured).Returns(false);
        // Matches the real SmtpEmailService contract: calling it unguarded throws. If the
        // controller ever stops checking IsConfigured first, these tests fail loudly.
        m.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
         .ThrowsAsync(new InvalidOperationException("Email service is not configured."));
        return m.Object;
    }

    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["AppBaseUrl"] = "https://example.test" })
        .Build();

    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync()
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();

        db.UserEmailTypes.Add(new UserEmailType { Id = EmailTypeId, Name = "Personal", DateCreated = DateTime.UtcNow });
        db.UserPhoneTypes.Add(new UserPhoneType { Id = PhoneTypeId, Name = "Mobile", DateCreated = DateTime.UtcNow });
        db.UserAddressTypes.Add(new UserAddressType { Id = AddressTypeId, Name = "Home", DateCreated = DateTime.UtcNow });
        db.UserLinkTypes.Add(new UserLinkType { Id = LinkTypeId, Name = "Website", DateCreated = DateTime.UtcNow });

        await db.SaveChangesAsync();
        return factory;
    }

    // IsAssignableFrom, not IsType: list actions return a lazy Select iterator, whose runtime type
    // is an implementation detail rather than IEnumerable<T> itself.
    private static T Value<T>(ActionResult<T> result)
        => Assert.IsAssignableFrom<T>(Assert.IsType<OkObjectResult>(result.Result).Value);

    // ── Ownership ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Another_users_email_reads_as_not_found_not_forbidden()
    {
        var factory = await SeedAsync();
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();

        var created = Value(await Build(factory, owner).CreateEmail(
            new UpsertMyEmailRequest(EmailTypeId, "owner@example.test", false, false), default));

        var update = await Build(factory, stranger).UpdateEmail(
            created.Id, new UpsertMyEmailRequest(EmailTypeId, "hijack@example.test", false, false), default);
        var delete = await Build(factory, stranger).DeleteEmail(created.Id, default);

        // NotFound, not Forbid: a 403 would confirm the row exists to somebody who has no business
        // knowing that.
        Assert.IsType<NotFoundResult>(update.Result);
        Assert.IsType<NotFoundResult>(delete);
    }

    [Fact]
    public async Task Listing_returns_only_the_callers_own_rows()
    {
        var factory = await SeedAsync();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();

        await Build(factory, mine).CreateEmail(new UpsertMyEmailRequest(EmailTypeId, "mine@example.test", false, false), default);
        await Build(factory, theirs).CreateEmail(new UpsertMyEmailRequest(EmailTypeId, "theirs@example.test", false, false), default);

        var rows = Value(await Build(factory, mine).GetEmails(default)).ToList();

        Assert.Single(rows);
        Assert.Equal("mine@example.test", rows[0].EmailAddress);
    }

    // ── Publishing requires validation ────────────────────────────────────────

    [Fact]
    public async Task A_new_email_cannot_be_created_public()
    {
        var factory = await SeedAsync();

        var result = await Build(factory, Guid.NewGuid()).CreateEmail(
            new UpsertMyEmailRequest(EmailTypeId, "new@example.test", false, IsPublic: true), default);

        // Refused outright rather than silently coerced to false — a client that asked for public
        // and got private without being told would ship that bug.
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task An_unvalidated_email_cannot_be_made_public_by_update()
    {
        var factory = await SeedAsync();
        var userId = Guid.NewGuid();

        var created = Value(await Build(factory, userId).CreateEmail(
            new UpsertMyEmailRequest(EmailTypeId, "later@example.test", false, false), default));

        var result = await Build(factory, userId).UpdateEmail(
            created.Id, new UpsertMyEmailRequest(EmailTypeId, "later@example.test", false, IsPublic: true), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Changing_the_address_text_clears_validation_and_unpublishes()
    {
        var factory = await SeedAsync();
        var userId = Guid.NewGuid();

        var created = Value(await Build(factory, userId).CreateEmail(
            new UpsertMyEmailRequest(EmailTypeId, "before@example.test", false, false), default));

        // Validate and publish it, the way the redemption endpoint would.
        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = await db.UserEmails.FirstAsync(e => e.Id == created.Id);
            row.IsValidated = true;
            row.DateValidated = DateTime.UtcNow;
            row.IsPublic = true;
            await db.SaveChangesAsync();
        }

        var updated = Value(await Build(factory, userId).UpdateEmail(
            created.Id, new UpsertMyEmailRequest(EmailTypeId, "after@example.test", false, IsPublic: true), default));

        // A naive "just write the new string" update leaves a published, supposedly-validated row
        // pointing at an address nobody has confirmed. That is the failure this catches.
        Assert.Equal("after@example.test", updated.EmailAddress);
        Assert.False(updated.IsValidated);
        Assert.False(updated.IsPublic);
        Assert.Null(updated.DateValidated);
    }

    [Fact]
    public async Task Republishing_an_unchanged_validated_address_is_allowed()
    {
        var factory = await SeedAsync();
        var userId = Guid.NewGuid();

        var created = Value(await Build(factory, userId).CreateEmail(
            new UpsertMyEmailRequest(EmailTypeId, "steady@example.test", false, false), default));

        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = await db.UserEmails.FirstAsync(e => e.Id == created.Id);
            row.IsValidated = true;
            await db.SaveChangesAsync();
        }

        // Same text, differing only in case — the reset must not fire, or a validated address
        // could never be published at all.
        var updated = Value(await Build(factory, userId).UpdateEmail(
            created.Id, new UpsertMyEmailRequest(EmailTypeId, "Steady@Example.test", false, IsPublic: true), default));

        Assert.True(updated.IsValidated);
        Assert.True(updated.IsPublic);
    }

    // ── Validation link ───────────────────────────────────────────────────────

    [Fact]
    public async Task Send_validation_returns_the_link_even_with_no_mail_service()
    {
        var factory = await SeedAsync();
        var userId = Guid.NewGuid();

        var created = Value(await Build(factory, userId).CreateEmail(
            new UpsertMyEmailRequest(EmailTypeId, "confirm@example.test", false, false), default));

        var response = Value(await Build(factory, userId).SendValidation(
            created.Id, UnconfiguredEmail(), Config(), default));

        Assert.False(response.EmailSent);
        Assert.StartsWith("https://example.test/validate-email/", response.ValidationLink);

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.UserEmails.FirstAsync(e => e.Id == created.Id);
        Assert.False(string.IsNullOrWhiteSpace(row.ValidationToken));
        Assert.EndsWith(row.ValidationToken!, response.ValidationLink);
        Assert.NotNull(row.DateValidationSent);
    }

    [Fact]
    public async Task Send_validation_twice_in_a_row_is_throttled()
    {
        var factory = await SeedAsync();
        var userId = Guid.NewGuid();

        var created = Value(await Build(factory, userId).CreateEmail(
            new UpsertMyEmailRequest(EmailTypeId, "twice@example.test", false, false), default));

        await Build(factory, userId).SendValidation(created.Id, UnconfiguredEmail(), Config(), default);
        var second = await Build(factory, userId).SendValidation(created.Id, UnconfiguredEmail(), Config(), default);

        Assert.IsType<BadRequestObjectResult>(second.Result);
    }

    [Fact]
    public async Task Reissuing_a_link_retires_the_previous_token()
    {
        var factory = await SeedAsync();
        var userId = Guid.NewGuid();

        var created = Value(await Build(factory, userId).CreateEmail(
            new UpsertMyEmailRequest(EmailTypeId, "reissue@example.test", false, false), default));

        var first = Value(await Build(factory, userId).SendValidation(created.Id, UnconfiguredEmail(), Config(), default));

        // Step past the cooldown without waiting a real minute.
        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = await db.UserEmails.FirstAsync(e => e.Id == created.Id);
            row.DateValidationSent = DateTime.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }

        var second = Value(await Build(factory, userId).SendValidation(created.Id, UnconfiguredEmail(), Config(), default));

        Assert.NotEqual(first.ValidationLink, second.ValidationLink);

        await using var check = await factory.CreateDbContextAsync();
        var stored = await check.UserEmails.FirstAsync(e => e.Id == created.Id);
        Assert.DoesNotContain(stored.ValidationToken!, first.ValidationLink);
    }

    [Fact]
    public async Task Send_validation_on_an_already_validated_address_is_refused()
    {
        var factory = await SeedAsync();
        var userId = Guid.NewGuid();

        var created = Value(await Build(factory, userId).CreateEmail(
            new UpsertMyEmailRequest(EmailTypeId, "done@example.test", false, false), default));

        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = await db.UserEmails.FirstAsync(e => e.Id == created.Id);
            row.IsValidated = true;
            await db.SaveChangesAsync();
        }

        var result = await Build(factory, userId).SendValidation(created.Id, UnconfiguredEmail(), Config(), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ── IsPrimary is a slot ───────────────────────────────────────────────────

    [Fact]
    public async Task Marking_an_email_primary_clears_the_previous_one()
    {
        var factory = await SeedAsync();
        var userId = Guid.NewGuid();

        var first = Value(await Build(factory, userId).CreateEmail(
            new UpsertMyEmailRequest(EmailTypeId, "one@example.test", IsPrimary: true, false), default));
        var second = Value(await Build(factory, userId).CreateEmail(
            new UpsertMyEmailRequest(EmailTypeId, "two@example.test", IsPrimary: true, false), default));

        var rows = Value(await Build(factory, userId).GetEmails(default)).ToList();

        Assert.False(rows.Single(r => r.Id == first.Id).IsPrimary);
        Assert.True(rows.Single(r => r.Id == second.Id).IsPrimary);
    }

    [Fact]
    public async Task Marking_a_phone_primary_clears_the_previous_one()
    {
        var factory = await SeedAsync();
        var userId = Guid.NewGuid();

        var first = Value(await Build(factory, userId).CreatePhone(
            new UpsertMyPhoneRequest(PhoneTypeId, "555-0100", "US", IsPrimary: true, true, false), default));
        var second = Value(await Build(factory, userId).CreatePhone(
            new UpsertMyPhoneRequest(PhoneTypeId, "555-0200", "US", IsPrimary: true, true, false), default));

        var rows = Value(await Build(factory, userId).GetPhones(default)).ToList();

        Assert.False(rows.Single(r => r.Id == first.Id).IsPrimary);
        Assert.True(rows.Single(r => r.Id == second.Id).IsPrimary);
    }

    [Fact]
    public async Task One_persons_primary_email_does_not_disturb_anothers()
    {
        var factory = await SeedAsync();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();

        var minePrimary = Value(await Build(factory, mine).CreateEmail(
            new UpsertMyEmailRequest(EmailTypeId, "mine@example.test", IsPrimary: true, false), default));
        await Build(factory, theirs).CreateEmail(
            new UpsertMyEmailRequest(EmailTypeId, "theirs@example.test", IsPrimary: true, false), default);

        var rows = Value(await Build(factory, mine).GetEmails(default)).ToList();

        // An unset-the-others query missing its AppUserId filter would demote this row.
        Assert.True(rows.Single(r => r.Id == minePrimary.Id).IsPrimary);
    }

    // ── Types ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_unknown_type_id_is_refused()
    {
        var factory = await SeedAsync();

        var result = await Build(factory, Guid.NewGuid()).CreateEmail(
            new UpsertMyEmailRequest(Guid.NewGuid(), "typeless@example.test", false, false), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ── Addresses ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_address_saves_when_the_geocoder_resolves_nothing()
    {
        var factory = await SeedAsync();
        var userId = Guid.NewGuid();

        // No coordinates supplied, and no geocoder API key configured under test — the address
        // still has to save. An address that refuses to save because a map lookup failed would be
        // an outage caused by a nicety.
        var created = Value(await Build(factory, userId).CreateAddress(
            new UpsertMyAddressRequest(AddressTypeId, "1 Nowhere Rd", null, "Nashville", "TN", "37201", "US", false),
            default));

        Assert.Equal("1 Nowhere Rd", created.StreetAddress1);
        Assert.Null(created.Latitude);
        Assert.Null(created.Longitude);
    }

    [Fact]
    public async Task Client_supplied_coordinates_are_kept()
    {
        var factory = await SeedAsync();
        var userId = Guid.NewGuid();

        var created = Value(await Build(factory, userId).CreateAddress(
            new UpsertMyAddressRequest(AddressTypeId, "1 Somewhere Rd", null, "Nashville", "TN", "37201", "US",
                false, SortOrder: 0, Latitude: 36.1627m, Longitude: -86.7816m),
            default));

        Assert.Equal(36.1627m, created.Latitude);
        Assert.Equal(-86.7816m, created.Longitude);
    }

    [Fact]
    public async Task An_incomplete_address_is_refused()
    {
        var factory = await SeedAsync();

        var result = await Build(factory, Guid.NewGuid()).CreateAddress(
            new UpsertMyAddressRequest(AddressTypeId, "1 Nowhere Rd", null, "", "TN", "37201", "US", false), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ── Links ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://files.example.test")]
    public async Task Only_absolute_http_urls_are_accepted(string url)
    {
        var factory = await SeedAsync();

        var result = await Build(factory, Guid.NewGuid()).CreateLink(
            new UpsertMyLinkRequest(LinkTypeId, url, null, false), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Changing_a_link_url_withdraws_its_approval()
    {
        var factory = await SeedAsync();
        var userId = Guid.NewGuid();

        var created = Value(await Build(factory, userId).CreateLink(
            new UpsertMyLinkRequest(LinkTypeId, "https://example.test/one", "Mine", false), default));

        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = await db.UserLinks.FirstAsync(l => l.Id == created.Id);
            row.IsVerifiedApproved = true;
            await db.SaveChangesAsync();
        }

        var updated = Value(await Build(factory, userId).UpdateLink(
            created.Id, new UpsertMyLinkRequest(LinkTypeId, "https://elsewhere.test/two", "Mine", false), default));

        Assert.False(updated.IsVerifiedApproved);
    }

    [Fact]
    public async Task A_self_service_link_is_never_created_pre_approved()
    {
        var factory = await SeedAsync();

        var created = Value(await Build(factory, Guid.NewGuid()).CreateLink(
            new UpsertMyLinkRequest(LinkTypeId, "https://example.test", null, false), default));

        Assert.False(created.IsVerifiedApproved);
    }

    // ── Audit ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Creating_a_row_writes_an_audit_entry()
    {
        var factory = await SeedAsync();
        var userId = Guid.NewGuid();
        var audit = new Mock<IAuditLogService>();

        var controller = new MyContactInfoController(factory, audit.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = As(userId) }
            }
        };

        await controller.CreateEmail(new UpsertMyEmailRequest(EmailTypeId, "audited@example.test", false, false), default);

        audit.Verify(a => a.LogCreateAsync(
            nameof(UserEmail), It.IsAny<Guid>(), It.IsAny<object>(), userId,
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
