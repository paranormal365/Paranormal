using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The free lane's only limit (Ben, 2026-08-31).
/// </summary>
/// <remarks>
/// <para>Everything paid is org-scoped: every method on <c>SubscriptionLimitGuard</c> takes an
/// organization id. The free individual the field archive exists for belongs to no group, so
/// until this guard they were the one kind of account with nothing paying for them AND no limit
/// at all — which became true silently, the day the sitewide upload caps were removed.</para>
///
/// <para>The tests that matter here are the ones about what is NOT counted: group work, and
/// anything already stored. A cap that quietly bills a member for their group's files, or that
/// retroactively refuses storage somebody was already allowed to use, is worse than none.</para>
/// </remarks>
public sealed class AccountStorageGuardTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private const long Megabyte = 1024L * 1024L;

    private static async Task<Guid> AddUserAsync(IDbContextFactory<BenDataContext> f)
    {
        var id = Guid.NewGuid();
        await using var db = await f.CreateDbContextAsync();
        db.AppUsers.Add(new AppUser
        {
            Id = id, UserName = $"{id}@t.com", Email = $"{id}@t.com",
            DisplayName = "Solo", DateCreated = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>One stored session file of a given size, personal unless an investigation is passed.</summary>
    private static async Task StoreAsync(
        IDbContextFactory<BenDataContext> f, Guid userId, long bytes,
        Guid? investigationId = null, bool published = false)
    {
        await using var db = await f.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        var fileId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, FileName = "clip.m4a", ContentType = "audio/mp4",
            // The real shape LocalFileStorageService.UserFilePath produces. It used to be
            // "u/{fileId}.m4a", which nothing in the product ever writes — and the moment the
            // guard started counting by path (C1) every one of these tests began measuring an
            // empty account. Invented fixtures pass until the code looks at the thing they
            // invented.
            StoragePath = $"users/{userId}/{fileId}.m4a", FileSize = bytes,
            AppUserId = userId,
            DateCreated = now, CreatedByAppUserId = userId,
        });
        db.FieldSessionUploads.Add(new FieldSessionUpload
        {
            Id = sessionId, SubmittedByAppUserId = userId,
            InvestigationId = investigationId,
            PublishedAtUtc = published ? now : null,
            DocumentUploadFileId = Guid.NewGuid(),
            StartedAt = now, DeviceModel = "iPhone 17",
            DateCreated = now, CreatedByAppUserId = userId,
        });
        db.FieldSessionUploadFiles.Add(new FieldSessionUploadFile
        {
            Id = Guid.NewGuid(), FieldSessionUploadId = sessionId, UploadFileId = fileId,
            RelativePath = "media/clip.m4a",
            DateCreated = now, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Puts this person in a group, optionally one whose subscription is Active.</summary>
    private static async Task JoinGroupAsync(
        IDbContextFactory<BenDataContext> f, Guid userId, bool paying)
    {
        await using var db = await f.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        var orgId = Guid.NewGuid();

        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Group", UrlName = $"g-{orgId:N}",
            DateCreated = now, CreatedByAppUserId = userId,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = OrganizationMemberRole.Member, IsActive = true,
            DateCreated = now, CreatedByAppUserId = userId,
        });
        db.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            Id = Guid.NewGuid(), OrganizationId = orgId,
            Status = paying ? SubscriptionStatus.Active : SubscriptionStatus.Lapsed,
            Interval = BillingInterval.Monthly,
            DateCreated = now, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<string?> AskAsync(
        IDbContextFactory<BenDataContext> f, Guid userId, long incoming)
    {
        await using var db = await f.CreateDbContextAsync();
        return await AccountStorageGuard.WhyCannotStoreAsync(db, userId, incoming, default);
    }

    /// <summary>A bare personal file, with no session behind it.</summary>
    /// <remarks>
    /// Most personal uploads are not field sessions — equipment photos, feed media, audio clips,
    /// video projects. This seeds the shape they all share: bytes under users/{id}/.
    /// </remarks>
    private static async Task<Guid> StorePlainAsync(
        IDbContextFactory<BenDataContext> f, Guid userId, long bytes,
        Guid? archivedFrom = null, Guid? parentFileId = null, string? path = null)
    {
        await using var db = await f.CreateDbContextAsync();
        var id = Guid.NewGuid();
        db.UploadFiles.Add(new UploadFile
        {
            Id = id, FileName = "video.mp4", ContentType = "video/mp4",
            StoragePath = path ?? $"users/{userId}/{id}.mp4", FileSize = bytes,
            AppUserId = userId,
            ArchivedFromUploadFileId = archivedFrom,
            ParentFileId = parentFileId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return id;
    }

    // ── what is counted, and what is not (C1, 2026-09-22) ────────────────────

    /// <summary>
    /// A group's file is not charged to the person who uploaded it.
    /// </summary>
    /// <remarks>
    /// The reason the guard counts by path rather than by <c>AppUserId</c>. Case evidence sets
    /// <c>AppUserId</c> to the uploader — the entity comment claiming otherwise describes handover
    /// only — so counting by ownership would bill a member for their group's case files.
    /// </remarks>
    [Fact]
    public async Task A_groups_case_file_is_not_charged_to_whoever_uploaded_it()
    {
        var factory = CreateFactory();
        var userId = await AddUserAsync(factory);

        // Exactly what CaseFileController writes: owned by the uploader, stored under the case.
        await StorePlainAsync(factory, userId, 4L * 1024 * 1024 * 1024,
                              path: $"cases/{Guid.NewGuid()}/files/big.mp4");

        Assert.Null(await AskAsync(factory, userId, 1024));
    }

    /// <summary>
    /// Clipping a video and keeping the original charges for both.
    /// </summary>
    /// <remarks>
    /// Ben, 2026-09-22: "sometimes videos are clipped or audio creates clips and they don't want
    /// to delete the original version". Both files are in the person's listing and either can be
    /// deleted, so holding both is a choice they can undo — and it is charged. This is the case
    /// that must NOT be confused with a replace archive below.
    /// </remarks>
    [Fact]
    public async Task A_clip_and_its_original_are_both_counted()
    {
        var factory = CreateFactory();
        var userId = await AddUserAsync(factory);

        var original = await StorePlainAsync(factory, userId, 1200L * 1024 * 1024);
        await StorePlainAsync(factory, userId, 100L * 1024 * 1024, parentFileId: original);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1300L * 1024 * 1024,
            await AccountStorageGuard.UsedBytesAsync(db, userId, default));
    }

    /// <summary>
    /// Cutting a file down lowers the usage; the superseded bytes are not charged.
    /// </summary>
    /// <remarks>
    /// Ben asked what happens when a 1 GB file is replaced by a 100 MB one. Replacing archives the
    /// old bytes under <c>ArchivedFromUploadFileId</c>, and that row is filtered out of every
    /// listing and pruned by nothing. Charging for it would mean somebody's usage ROSE because
    /// they made a file smaller, with no screen showing the difference and no way to remove it.
    /// The archives want a retention job; they do not want a surcharge.
    /// </remarks>
    [Fact]
    public async Task Replacing_a_big_file_with_a_small_one_lowers_what_is_counted()
    {
        var factory = CreateFactory();
        var userId = await AddUserAsync(factory);

        // The live row now holds the small version; the old bytes live on in an archive row.
        var live = await StorePlainAsync(factory, userId, 100L * 1024 * 1024);
        await StorePlainAsync(factory, userId, 1024L * 1024 * 1024, archivedFrom: live);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(100L * 1024 * 1024,
            await AccountStorageGuard.UsedBytesAsync(db, userId, default));
    }

    /// <summary>Deleting a personal file gives the room straight back.</summary>
    /// <remarks>
    /// Usage is summed from the rows that exist rather than kept as a running total, so there is
    /// no counter that can drift away from the files. Worth a test because the cheap alternative —
    /// a stored figure updated on write — is the version that goes wrong.
    /// </remarks>
    [Fact]
    public async Task Deleting_a_file_frees_the_room_immediately()
    {
        var factory = CreateFactory();
        var userId = await AddUserAsync(factory);
        var id = await StorePlainAsync(factory, userId, 500L * 1024 * 1024);

        await using (var db = await factory.CreateDbContextAsync())
            Assert.Equal(500L * 1024 * 1024, await AccountStorageGuard.UsedBytesAsync(db, userId, default));

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Remove(db.UploadFiles.Single(f => f.Id == id));
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
            Assert.Equal(0L, await AccountStorageGuard.UsedBytesAsync(db, userId, default));
    }

    /// <summary>Another person's files are not this person's problem.</summary>
    [Fact]
    public async Task Somebody_elses_files_are_not_counted()
    {
        var factory = CreateFactory();
        var mine = await AddUserAsync(factory);
        var theirs = await AddUserAsync(factory);

        await StorePlainAsync(factory, theirs, 3L * 1024 * 1024 * 1024);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(0L, await AccountStorageGuard.UsedBytesAsync(db, mine, default));
    }

    // ── the cap itself ───────────────────────────────────────────────────────

    [Fact]
    public async Task An_empty_account_may_store()
    {
        var f = CreateFactory();
        Assert.Null(await AskAsync(f, await AddUserAsync(f), 10 * Megabyte));
    }

    [Fact]
    public async Task Storing_past_the_cap_is_refused()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f);

        await StoreAsync(f, user, AccountStorageGuard.DefaultFreeMegabytes * Megabyte);

        var why = await AskAsync(f, user, Megabyte);
        Assert.NotNull(why);
        // The refusal has to be actionable, not a bare no — somebody who meets this must be able
        // to tell what it is and what would fix it.
        Assert.Contains("2 GB", why);
        Assert.Contains("paid plan", why);
    }

    /// <summary>
    /// The incoming file counts BEFORE it is written, so the cap is a limit rather than something
    /// noticed after the disk already holds it.
    /// </summary>
    [Fact]
    public async Task A_single_file_larger_than_the_whole_cap_is_refused_on_an_empty_account()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f);

        Assert.NotNull(await AskAsync(f, user, (AccountStorageGuard.DefaultFreeMegabytes + 1) * Megabyte));
    }

    [Fact]
    public async Task Filling_the_cap_exactly_is_still_allowed()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f);

        await StoreAsync(f, user, (AccountStorageGuard.DefaultFreeMegabytes - 1) * Megabyte);

        Assert.Null(await AskAsync(f, user, Megabyte));
    }

    // ── what is deliberately not counted ─────────────────────────────────────

    /// <summary>
    /// Group work lives under the organization's path and answers to the group's own plan.
    /// Counting it here would charge a member twice for belonging somewhere.
    /// </summary>
    [Fact]
    public async Task Files_belonging_to_an_investigation_are_not_counted()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f);

        await StoreAsync(f, user, 5000 * Megabyte, investigationId: Guid.NewGuid());

        Assert.Null(await AskAsync(f, user, Megabyte));
    }

    [Fact]
    public async Task A_member_of_a_paying_group_is_not_capped_at_all()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f);
        await JoinGroupAsync(f, user, paying: true);

        await StoreAsync(f, user, 9000 * Megabyte);

        Assert.Null(await AskAsync(f, user, 1000 * Megabyte));
    }

    /// <summary>
    /// A lapsed group is not a paying one. Otherwise letting a subscription expire would be a way
    /// to keep unlimited storage forever.
    /// </summary>
    [Fact]
    public async Task A_member_of_a_lapsed_group_is_capped_like_anybody_else()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f);
        await JoinGroupAsync(f, user, paying: false);

        await StoreAsync(f, user, AccountStorageGuard.DefaultFreeMegabytes * Megabyte);

        Assert.NotNull(await AskAsync(f, user, Megabyte));
    }

    [Fact]
    public async Task One_persons_storage_does_not_count_against_another()
    {
        var f = CreateFactory();
        var heavy = await AddUserAsync(f);
        var light = await AddUserAsync(f);

        await StoreAsync(f, heavy, AccountStorageGuard.DefaultFreeMegabytes * Megabyte);

        Assert.Null(await AskAsync(f, light, 10 * Megabyte));
    }

    // ── contributed storage is not charged for ───────────────────────────────

    /// <summary>
    /// Ben, 2026-08-31: "I just want to make sure I am not giving out space for things that do not
    /// benefit me." A published recording earns its disk — it is what makes the archive worth
    /// reading and what the comparison engine measures against. One nobody else will ever see
    /// earns nothing.
    /// </summary>
    [Fact]
    public async Task Published_sessions_do_not_count_against_the_cap()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f);

        await StoreAsync(f, user, 5000 * Megabyte, published: true);

        Assert.Null(await AskAsync(f, user, 100 * Megabyte));
    }

    [Fact]
    public async Task What_is_kept_private_still_counts()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f);

        await StoreAsync(f, user, AccountStorageGuard.DefaultFreeMegabytes * Megabyte, published: false);

        Assert.NotNull(await AskAsync(f, user, Megabyte));
    }

    /// <summary>
    /// The cap is a nudge, not a tax: publishing what you already uploaded gives the room back.
    /// </summary>
    [Fact]
    public async Task Publishing_what_you_stored_frees_the_room_again()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f);

        await StoreAsync(f, user, AccountStorageGuard.DefaultFreeMegabytes * Megabyte);
        Assert.NotNull(await AskAsync(f, user, Megabyte));   // full

        await using (var db = await f.CreateDbContextAsync())
        {
            foreach (var session in await db.FieldSessionUploads.ToListAsync())
                session.PublishedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        Assert.Null(await AskAsync(f, user, Megabyte));      // contributed, so not charged
    }

    // ── the setting ──────────────────────────────────────────────────────────

    [Fact]
    public async Task The_cap_can_be_changed_without_code()
    {
        var f = CreateFactory();
        await using (var db = await f.CreateDbContextAsync())
        {
            db.SiteSettings.Add(new SiteSetting
            {
                Key = SiteSettingKeys.FreeAccountStorageMegabytes, Value = "50",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }

        await using var read = await f.CreateDbContextAsync();
        Assert.Equal(50 * Megabyte, await AccountStorageGuard.CapBytesAsync(read, default));
    }

    /// <summary>
    /// A typo in an admin box must not remove everybody's limit, and must not set it to zero
    /// either — the degrade-to-default rule the other numeric settings follow.
    /// </summary>
    [Theory]
    [InlineData("not a number")]
    [InlineData("0")]
    [InlineData("-500")]
    [InlineData("")]
    public async Task A_bad_setting_falls_back_to_the_default(string value)
    {
        var f = CreateFactory();
        await using (var db = await f.CreateDbContextAsync())
        {
            db.SiteSettings.Add(new SiteSetting
            {
                Key = SiteSettingKeys.FreeAccountStorageMegabytes, Value = value,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }

        await using var read = await f.CreateDbContextAsync();
        Assert.Equal(AccountStorageGuard.DefaultFreeMegabytes * Megabyte,
                     await AccountStorageGuard.CapBytesAsync(read, default));
    }
}
