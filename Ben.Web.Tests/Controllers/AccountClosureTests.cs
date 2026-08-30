using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Deleting your own account: the person goes, the work stays, and an owner is stopped first.
/// </summary>
/// <remarks>
/// <para>Required by App Review Guideline 5.1.1(v). The shape is Ben's decision of 2026-08-28:
/// anonymise rather than delete, because a member does not own the case files they authored on
/// their group's behalf, and refuse an organization's owner until they have handed it over,
/// because exactly one owner exists per organization and anonymising them strands the group.</para>
///
/// <para><b>What these cannot check.</b> The in-memory provider has no transactions, so the
/// atomicity of <c>CloseAsync</c> is not exercised here — only that each individual effect
/// happened. The transaction is still right; it just is not what is under test.</para>
/// </remarks>
public sealed class AccountClosureTests
{
    private static IDbContextFactory<BenDataContext> Db()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            // The service opens a transaction; the in-memory store silently has none. Ignoring the
            // warning is the honest move — see the remarks above — rather than dropping the
            // transaction from production code to make a test provider happy.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static AccountClosureService Service(IDbContextFactory<BenDataContext> db) =>
        new(db, NullLogger<AccountClosureService>.Instance);

    /// <summary>A person, with everything personal about them filled in.</summary>
    private static async Task<Guid> SeedPersonAsync(IDbContextFactory<BenDataContext> factory)
    {
        var id = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.AppUsers.Add(new AppUser
        {
            Id = id,
            DisplayName = "Sarah Mitchell",
            FirstName = "Sarah",
            LastName = "Mitchell",
            Handle = "sarah",
            BirthYear = 1988,
            Email = "sarah.mitchell@benco.dev",
            NormalizedEmail = "SARAH.MITCHELL@BENCO.DEV",
            UserName = "sarah.mitchell@benco.dev",
            NormalizedUserName = "SARAH.MITCHELL@BENCO.DEV",
            PasswordHash = "a-real-hash",
            PhoneNumber = "615-555-0100",
            EmailConfirmed = true,
            TwoFactorEnabled = true,
        });
        db.UserPhones.Add(new UserPhone { Id = Guid.NewGuid(), AppUserId = id, PhoneNumber = "615-555-0100", ValidationToken = "t" });
        db.UserLinks.Add(new UserLink { Id = Guid.NewGuid(), AppUserId = id, LinkUrl = "https://example.com" });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> SeedOrgWithRoleAsync(
        IDbContextFactory<BenDataContext> factory, Guid userId, OrganizationMemberRole role,
        string name = "Paranormal 365", bool active = true)
    {
        var orgId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = name, UrlName = name.ToLowerInvariant().Replace(' ', '-'),
            CreatedByAppUserId = userId,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = role, IsActive = active, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return orgId;
    }

    // ── the owner rule ────────────────────────────────────────────────────────

    [Fact]
    public async Task An_owner_is_refused_and_told_which_group_to_hand_over()
    {
        var factory = Db();
        var userId = await SeedPersonAsync(factory);
        await SeedOrgWithRoleAsync(factory, userId, OrganizationMemberRole.Owner);

        var check = await Service(factory).CheckAsync(userId);
        Assert.False(check.CanClose);
        Assert.Equal("Paranormal 365", Assert.Single(check.OwnedOrganizations).Name);

        var result = await Service(factory).CloseAsync(userId);
        Assert.False(result.Closed);
        // Naming the group is the whole point — "you can't do this" with no route out is the
        // dead-end class of refusal, and Apple rejects a blocked path that does not explain itself.
        Assert.Contains("Paranormal 365", result.Refusal);

        await using var db = await factory.CreateDbContextAsync();
        var user = await db.AppUsers.FirstAsync(u => u.Id == userId);
        Assert.Null(user.DateClosed);
        Assert.Equal("Sarah Mitchell", user.DisplayName);
    }

    [Fact]
    public async Task An_administrator_who_is_not_the_owner_may_close()
    {
        var factory = Db();
        var userId = await SeedPersonAsync(factory);
        await SeedOrgWithRoleAsync(factory, userId, OrganizationMemberRole.Administrator);

        Assert.True((await Service(factory).CheckAsync(userId)).CanClose);
        Assert.True((await Service(factory).CloseAsync(userId)).Closed);
    }

    [Fact]
    public async Task An_ownership_they_have_already_left_does_not_block_them_forever()
    {
        var factory = Db();
        var userId = await SeedPersonAsync(factory);
        await SeedOrgWithRoleAsync(factory, userId, OrganizationMemberRole.Owner, active: false);

        Assert.True((await Service(factory).CheckAsync(userId)).CanClose);
    }

    [Fact]
    public async Task Every_group_they_own_is_named_not_just_the_first()
    {
        var factory = Db();
        var userId = await SeedPersonAsync(factory);
        await SeedOrgWithRoleAsync(factory, userId, OrganizationMemberRole.Owner, "Paranormal 365");
        await SeedOrgWithRoleAsync(factory, userId, OrganizationMemberRole.Owner, "Nashville Ghosts");

        var refusal = (await Service(factory).CloseAsync(userId)).Refusal;
        Assert.Contains("Paranormal 365", refusal);
        Assert.Contains("Nashville Ghosts", refusal);
    }

    // ── what closing actually does ────────────────────────────────────────────

    [Fact]
    public async Task The_person_is_erased_and_the_account_can_never_sign_in_again()
    {
        var factory = Db();
        var userId = await SeedPersonAsync(factory);

        Assert.True((await Service(factory).CloseAsync(userId)).Closed);

        await using var db = await factory.CreateDbContextAsync();
        var user = await db.AppUsers.FirstAsync(u => u.Id == userId);

        Assert.NotNull(user.DateClosed);
        Assert.Equal(AccountClosure.FormerMemberName, user.DisplayName);
        Assert.Null(user.FirstName);
        Assert.Null(user.LastName);
        Assert.Null(user.BirthYear);
        Assert.Null(user.PhoneNumber);

        // Nothing that identifies the person survives in the address, and the address it does
        // carry is on a domain RFC 2606 reserves so nothing can ever post to it.
        Assert.DoesNotContain("sarah", user.Email, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sarah", user.UserName, StringComparison.OrdinalIgnoreCase);
        Assert.True(AccountClosure.IsClosedEmail(user.Email));
        Assert.Equal(user.Email!.ToUpperInvariant(), user.NormalizedEmail);

        // The @name is replaced rather than removed: it appears inside other people's posts, so
        // nulling it would break their text, and keeping it would go on naming somebody.
        Assert.DoesNotContain("sarah", user.Handle, StringComparison.OrdinalIgnoreCase);
        Assert.True(user.Handle!.Length <= Ben.Data.Common.Helpers.UserHandle.MaxLength,
                    "the replacement @name must still be a legal handle");

        Assert.Null(user.PasswordHash);
        Assert.False(user.TwoFactorEnabled);
        Assert.True(user.LockoutEnabled);
        Assert.Equal(DateTimeOffset.MaxValue, user.LockoutEnd);
    }

    [Fact]
    public async Task Contact_rows_are_deleted_outright()
    {
        var factory = Db();
        var userId = await SeedPersonAsync(factory);

        await Service(factory).CloseAsync(userId);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(db.UserPhones.Where(p => p.AppUserId == userId));
        Assert.Empty(db.UserLinks.Where(l => l.AppUserId == userId));
    }

    [Fact]
    public async Task Their_group_keeps_the_case_they_wrote()
    {
        // The reason this shape was chosen over a hard delete. A case belongs to the group and
        // often to a paying client; one member leaving must not erase it.
        var factory = Db();
        var userId = await SeedPersonAsync(factory);
        var orgId = await SeedOrgWithRoleAsync(factory, userId, OrganizationMemberRole.Member);

        var caseId = Guid.NewGuid();
        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.Cases.Add(new Case
            {
                Id = caseId, OrganizationId = orgId, Title = "The Bell Witch cellar",
                CreatedByAppUserId = userId,
            });
            await seed.SaveChangesAsync();
        }

        Assert.True((await Service(factory).CloseAsync(userId)).Closed);

        await using var db = await factory.CreateDbContextAsync();
        var kept = await db.Cases.FirstOrDefaultAsync(c => c.Id == caseId);
        Assert.NotNull(kept);
        Assert.Equal("The Bell Witch cellar", kept.Title);
        // Still attributed to the same row — which now renders as "A former member" everywhere
        // that reads DisplayName, without a single one of those surfaces being changed.
        Assert.Equal(userId, kept.CreatedByAppUserId);
        Assert.Equal(AccountClosure.FormerMemberName,
                     (await db.AppUsers.FirstAsync(u => u.Id == userId)).DisplayName);
    }

    [Fact]
    public async Task External_logins_go_so_Sign_in_with_Apple_cannot_walk_back_in()
    {
        // The one hole that would make all the anonymising pointless: a left-behind login row
        // matches on the Apple subject, not the email, so it would resurrect the closed account.
        var factory = Db();
        var userId = await SeedPersonAsync(factory);
        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.UserLogins.Add(new Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>
            {
                UserId = userId, LoginProvider = "Apple", ProviderKey = "001234.abcdef",
                ProviderDisplayName = "Apple",
            });
            await seed.SaveChangesAsync();
        }

        await Service(factory).CloseAsync(userId);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(db.UserLogins.Where(l => l.UserId == userId));
    }

    [Fact]
    public async Task Closing_twice_is_not_an_error_and_does_not_re_stamp_the_date()
    {
        // A retry after a dropped connection must not read as a failure and send somebody looking
        // for an account that is already gone.
        var factory = Db();
        var userId = await SeedPersonAsync(factory);

        Assert.True((await Service(factory).CloseAsync(userId)).Closed);
        DateTime? first;
        await using (var db = await factory.CreateDbContextAsync())
            first = (await db.AppUsers.FirstAsync(u => u.Id == userId)).DateClosed;

        var second = await Service(factory).CloseAsync(userId);
        Assert.True(second.Closed);
        Assert.Null(second.Refusal);

        await using var after = await factory.CreateDbContextAsync();
        Assert.Equal(first, (await after.AppUsers.FirstAsync(u => u.Id == userId)).DateClosed);
    }

    [Fact]
    public async Task An_account_that_does_not_exist_is_refused_rather_than_crashing()
    {
        var result = await Service(Db()).CloseAsync(Guid.NewGuid());
        Assert.False(result.Closed);
        Assert.NotNull(result.Refusal);
    }
}

/// <summary>
/// The exact JSON the closure check goes over the wire as.
/// </summary>
/// <remarks>
/// <para>The iOS client decodes this shape, and its decoder applies no key strategy — so a
/// property renamed on either side compiles cleanly on both and produces a screen that silently
/// says "nothing is blocking you" to somebody it should be refusing. An invented fixture has
/// already shipped a broken iOS feature past 130 green tests once.</para>
///
/// <para>The literal below is duplicated verbatim in <c>AccountClosureContractTests.swift</c>.
/// That duplication is the test: one side proves the server emits it, the other proves the app
/// reads it, and they cannot drift without one of them failing.</para>
/// </remarks>
public sealed class AccountClosureWireShapeTests
{
    /// The API's own serializer settings — camelCase, as ASP.NET configures for controllers.
    private static readonly System.Text.Json.JsonSerializerOptions Web = new(System.Text.Json.JsonSerializerDefaults.Web);

    [Fact]
    public void The_check_serializes_with_the_keys_the_iOS_client_decodes()
    {
        var check = new AccountClosureService.ClosureCheck(
            CanClose: false,
            OwnedOrganizations:
            [
                new AccountClosureService.BlockingOrganization(
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    "Paranormal 365", "paranormal365"),
            ]);

        var json = System.Text.Json.JsonSerializer.Serialize(check, Web);

        Assert.Equal(
            """
            {"canClose":false,"ownedOrganizations":[{"organizationId":"11111111-1111-1111-1111-111111111111","name":"Paranormal 365","urlName":"paranormal365"}]}
            """,
            json);
    }

    [Fact]
    public void The_confirmation_word_is_the_same_one_the_app_sends()
    {
        // AccountClosureCheck.confirmationWord in BenKit. The server rejects anything else, so a
        // change on one side without the other makes the button fail with "Type DELETE to confirm."
        Assert.Equal("DELETE", Ben.Data.WebApi.Controllers.MyAccountClosureController.RequiredConfirmation);
    }
}
