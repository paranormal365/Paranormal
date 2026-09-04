using System.Reflection;
using System.Security.Claims;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Deleting a person from the SuperAdmin screen.
/// </summary>
/// <remarks>
/// <para>The in-memory provider has no <c>ExecuteDeleteAsync</c>, no transactions and no raw SQL,
/// so the deleting half is covered by <c>AppUserPurgeCoverageTests</c> against the model instead.
/// What IS testable here is every decision made before a row is touched — who is refused, what the
/// counts say, and what the screen is told will survive — and those are the parts a SuperAdmin
/// reads and acts on.</para>
/// </remarks>
public sealed class AdminAppUserPurgeControllerTests
{
    private static readonly Guid OrgId          = Guid.NewGuid();
    private static readonly Guid SuperAdminRole = Guid.NewGuid();
    private static readonly Guid ActingAdminId  = Guid.NewGuid();
    private static readonly Guid PlainUserId    = Guid.NewGuid();
    private static readonly Guid OwnerUserId    = Guid.NewGuid();
    private static readonly Guid SoleAdminId    = Guid.NewGuid();

    private static IDbContextFactory<BenDataContext> Factory() =>
        new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// <param name="soleSuperAdmin">
    /// When true the only SuperAdmin is <see cref="SoleAdminId"/> — the one state that refuses.
    /// </param>
    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync(bool soleSuperAdmin = false)
    {
        var factory = Factory();
        await using var db = await factory.CreateDbContextAsync();

        db.Roles.Add(new IdentityRole<Guid> { Id = SuperAdminRole, Name = RoleNames.SuperAdmin });

        foreach (var (id, name) in new[]
                 { (ActingAdminId, "The Acting Admin"), (PlainUserId, "Plain Person"),
                   (OwnerUserId, "Group Owner"), (SoleAdminId, "Sole Admin") })
            db.Users.Add(new AppUser
            { Id = id, UserName = $"{id:N}@t", Email = $"{id:N}@t", DisplayName = name });

        if (soleSuperAdmin)
        {
            db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = SoleAdminId, RoleId = SuperAdminRole });
        }
        else
        {
            db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = SoleAdminId, RoleId = SuperAdminRole });
            db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = ActingAdminId, RoleId = SuperAdminRole });
        }

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "Ghost Squad", UrlName = "ghost-squad", DateCreated = DateTime.UtcNow });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = OwnerUserId,
            Role = OrganizationMemberRole.Owner, IsActive = true, DateCreated = DateTime.UtcNow,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = PlainUserId,
            Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
        return factory;
    }

    private static AdminAppUserPurgeController Build(IDbContextFactory<BenDataContext> factory)
    {
        var storage = new Mock<Ben.Data.Common.Interfaces.IFileStorageService>();
        var purge = new AppUserPurge(factory, storage.Object, NullLogger<AppUserPurge>.Instance);

        var controller = new AdminAppUserPurgeController(purge)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, ActingAdminId.ToString())], "Bearer")),
                },
            },
        };
        return controller;
    }

    private static async Task<AppUserPurgePreview> PreviewAsync(
        IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var result = await Build(factory).Preview(userId, default);
        return Assert.IsType<AppUserPurgePreview>(Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    // ── the preview, which is what a SuperAdmin actually reads ────────────────

    [Fact]
    public async Task An_account_nobody_else_refers_to_is_promised_a_complete_removal()
    {
        var factory = await SeedAsync();

        // Nothing has been authored by this person and their only tie is a membership, which the
        // purge deletes. The screen should promise the row itself goes — and it is a promise, so
        // getting it wrong is worse than saying nothing.
        var preview = await PreviewAsync(factory, PlainUserId);

        Assert.False(preview.RowWillSurvive);
        Assert.Equal(1, preview.Memberships);
        Assert.Null(preview.Refusal);
    }

    [Fact]
    public async Task Work_written_for_a_group_is_counted_as_kept_not_destroyed()
    {
        var factory = await SeedAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var caseId = Guid.NewGuid();
            db.Cases.Add(new Case
            {
                Id = caseId, OrganizationId = OrgId, Title = "The Old Mill",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = PlainUserId,
            });
            db.CaseNotes.Add(new CaseNote
            {
                Id = Guid.NewGuid(), CaseId = caseId, AuthorAppUserId = PlainUserId,
                Body = "Cold spot on the stairs.",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = PlainUserId,
            });
            await db.SaveChangesAsync();
        }

        var preview = await PreviewAsync(factory, PlainUserId);

        // The note is the group's record of its own work. It is not in the destroyed column, and
        // the row cannot go while it points at the account — both halves said in advance.
        Assert.Equal(1, preview.CaseNotes);
        Assert.True(preview.RowWillSurvive);
    }

    [Fact]
    public async Task A_personal_session_counts_as_destroyed_and_a_groups_does_not()
    {
        var factory = await SeedAsync();
        var investigationId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Investigations.Add(new Investigation
            {
                Id = investigationId, OrganizationId = OrgId, Title = "The Old Mill",
                ScheduledDateTime = DateTime.UtcNow.AddDays(-1), DateCreated = DateTime.UtcNow,
            });

            foreach (var (investigation, label) in new (Guid?, string)[]
                     { (null, "their own walk-through"), (investigationId, "the group's night") })
            {
                var fileId = Guid.NewGuid();
                db.UploadFiles.Add(new UploadFile
                {
                    Id = fileId, FileName = "data.json", StoragePath = $"users/{fileId}.json",
                    ContentType = "application/json", FileSize = 10,
                    DateCreated = DateTime.UtcNow, CreatedByAppUserId = PlainUserId,
                });
                db.FieldSessionUploads.Add(new FieldSessionUpload
                {
                    Id = Guid.NewGuid(), InvestigationId = investigation,
                    SubmittedByAppUserId = PlainUserId, CreatedByAppUserId = PlainUserId,
                    DeviceSessionId = Guid.NewGuid(), DocumentUploadFileId = fileId,
                    DeviceModel = "iPhone17,1", LocationLabel = label,
                    StartedAt = DateTime.UtcNow.AddHours(-2), DateCreated = DateTime.UtcNow,
                });
            }
            await db.SaveChangesAsync();
        }

        var preview = await PreviewAsync(factory, PlainUserId);

        // The whole rule in one assertion: a session recorded FOR a group is the group's evidence
        // and outlives whoever carried the phone; one recorded on somebody's own walk-through is
        // theirs alone and goes with them.
        Assert.Equal(1, preview.PersonalFieldSessions);
        Assert.Equal(1, preview.GroupFieldSessions);
    }

    // ── notices, which Ben chose over refusals ────────────────────────────────

    [Fact]
    public async Task Owning_a_group_is_a_notice_and_does_not_block_the_delete()
    {
        var factory = await SeedAsync();

        var preview = await PreviewAsync(factory, OwnerUserId);

        // Ben's call, 2026-09-04: a SuperAdmin can appoint a new owner afterwards, so being told
        // is what matters. Self-service closure still refuses an owner; these are different acts
        // by different people and the difference is deliberate.
        Assert.Contains("Ghost Squad", preview.OwnedOrganizations);
        Assert.Null(preview.Refusal);
    }

    [Fact]
    public async Task An_active_paid_seat_is_a_notice_and_does_not_block_the_delete()
    {
        var factory = await SeedAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MemberSeatSubscriptions.Add(new MemberSeatSubscription
            {
                Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = PlainUserId,
                Status = SubscriptionStatus.Active, PriceAtStart = 9.99m,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = PlainUserId,
            });
            await db.SaveChangesAsync();
        }

        var preview = await PreviewAsync(factory, PlainUserId);

        // Nothing here cancels a subscription, so the card keeps being charged for an account that
        // is gone. Saying so is the whole feature; refusing over it is not what Ben asked for.
        Assert.Contains("Ghost Squad", preview.PaidSubscriptions);
        Assert.Null(preview.Refusal);
    }

    [Fact]
    public async Task A_lapsed_seat_is_not_reported_as_something_still_being_charged()
    {
        var factory = await SeedAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MemberSeatSubscriptions.Add(new MemberSeatSubscription
            {
                Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = PlainUserId,
                Status = SubscriptionStatus.Lapsed, PriceAtStart = 9.99m,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = PlainUserId,
            });
            await db.SaveChangesAsync();
        }

        var preview = await PreviewAsync(factory, PlainUserId);

        // A warning that fires on something already stopped teaches a SuperAdmin to ignore the
        // warning, which costs more than the one it was meant to catch.
        Assert.Empty(preview.PaidSubscriptions);
    }

    // ── the one refusal ───────────────────────────────────────────────────────

    [Fact]
    public async Task The_last_SuperAdmin_cannot_be_deleted()
    {
        var factory = await SeedAsync(soleSuperAdmin: true);

        var preview = await PreviewAsync(factory, SoleAdminId);

        Assert.NotNull(preview.Refusal);
        Assert.Contains("only SuperAdmin", preview.Refusal);

        var attempt = await Build(factory).Purge(
            SoleAdminId, new AdminAppUserPurgeController.PurgeUserRequest("Sole Admin"), default);
        Assert.IsType<BadRequestObjectResult>(attempt.Result);
    }

    [Fact]
    public async Task A_SuperAdmin_with_a_colleague_can_be_deleted()
    {
        var factory = await SeedAsync();

        // The positive half. A refusal that fired on every SuperAdmin would pass the test above
        // and make the role undeletable for ever.
        var preview = await PreviewAsync(factory, SoleAdminId);

        Assert.Null(preview.Refusal);
    }

    // ── the confirmation ──────────────────────────────────────────────────────

    [Fact]
    public async Task The_typed_name_must_match_exactly()
    {
        var factory = await SeedAsync();

        foreach (var typed in new[] { "", "plain person", "Plain  Person", "Plain Perso" })
        {
            var result = await Build(factory).Purge(
                PlainUserId, new AdminAppUserPurgeController.PurgeUserRequest(typed), default);

            // Checked on the server as well as in the UI: the screen's job is to make an accident
            // hard, the server's is to make one impossible.
            Assert.IsType<BadRequestObjectResult>(result.Result);
        }
    }

    [Fact]
    public async Task A_SuperAdmin_cannot_delete_themselves_from_here()
    {
        var factory = await SeedAsync();

        var result = await Build(factory).Purge(
            ActingAdminId, new AdminAppUserPurgeController.PurgeUserRequest("The Acting Admin"), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("your profile", bad.Value?.ToString());
    }

    [Fact]
    public async Task An_account_that_does_not_exist_is_a_plain_not_found()
    {
        var factory = await SeedAsync();

        var result = await Build(factory).Preview(Guid.NewGuid(), default);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── the shape the screen reads ────────────────────────────────────────────

    [Fact]
    public void The_preview_keeps_destroyed_and_kept_as_separate_fields()
    {
        var names = typeof(AppUserPurgePreview)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToList();

        // A single total would describe something the delete does not do. These two groups exist
        // because they are two different outcomes, and the screen renders them apart.
        foreach (var destroyed in new[]
                 { "PersonalFieldSessions", "StoredFiles", "Memberships", "SignInEvents" })
            Assert.Contains(destroyed, names);

        foreach (var kept in new[]
                 { "CaseNotes", "TimelineEntries", "GroupMessages", "GroupFieldSessions" })
            Assert.Contains(kept, names);

        // And the honest one: whether the row actually goes.
        Assert.Contains("RowWillSurvive", names);
    }
}
