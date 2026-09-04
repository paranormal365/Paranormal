using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The person purge actually running, against a real relational database with foreign keys
/// enforced (item 219).
/// </summary>
/// <remarks>
/// <para><b>Why this one needed a real database more than the others.</b> Deleting a person has
/// two possible endings — the row goes, or the row stays emptied because a group's records still
/// point at it — and which one happens is decided by the database, not by the code. The preview
/// promises one of the two in advance from a census of every foreign key into
/// <c>AppUsers</c>. Until now nothing checked that the promise came true, because the InMemory
/// provider cannot run the deletes at all. These tests drive both endings.</para>
///
/// <para>The refusals — the last SuperAdmin, the typed name, deleting yourself — are covered next
/// door in <c>AdminAppUserPurgeControllerTests</c>, which does not need a real provider because
/// none of them reach a delete.</para>
/// </remarks>
public sealed class AppUserPurgeBehaviourTests
{
    private const string TargetName = "Sam Recorder";

    private sealed record Harness(
        SqliteTestDb Sqlite, Guid AdminId, Guid TargetId, Guid OrgId, Guid FileTypeId);

    /// <summary>An acting SuperAdmin, the person to be deleted, a group and a file type.</summary>
    private static async Task<Harness> NewAsync()
    {
        var sqlite   = await SqliteTestDb.CreateAsync();
        var adminId  = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var orgId    = Guid.NewGuid();
        var fileType = Guid.NewGuid();

        await using var db = await sqlite.NewContextAsync();
        db.Users.Add(new AppUser
        {
            Id = adminId, Email = "admin@example.com", UserName = "admin@example.com",
            NormalizedEmail = "ADMIN@EXAMPLE.COM", NormalizedUserName = "ADMIN@EXAMPLE.COM",
            DisplayName = "The Admin", DateCreated = DateTime.UtcNow,
        });
        db.Users.Add(new AppUser
        {
            Id = targetId, Email = "sam@example.com", UserName = "sam@example.com",
            NormalizedEmail = "SAM@EXAMPLE.COM", NormalizedUserName = "SAM@EXAMPLE.COM",
            DisplayName = TargetName, Handle = "sam", PasswordHash = "a-real-looking-hash",
            PhoneNumber = "+16155551234", EmailConfirmed = true, DateCreated = DateTime.UtcNow,
        });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Night Watch", UrlName = "night-watch",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
        });
        db.UploadFileTypes.Add(new UploadFileType
        {
            Id = fileType, Name = "Evidence", DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
        });
        await db.SaveChangesAsync();

        return new Harness(sqlite, adminId, targetId, orgId, fileType);
    }

    private static (AppUserPurge Purge, Mock<Ben.Data.Common.Interfaces.IFileStorageService> Storage) Build(Harness h)
    {
        var storage = new Mock<Ben.Data.Common.Interfaces.IFileStorageService>();
        return (new AppUserPurge(h.Sqlite.Factory, storage.Object, NullLogger<AppUserPurge>.Instance), storage);
    }

    /// <summary>Everything that is only ever this person's, so the "destroyed" half has substance.</summary>
    private static async Task AddPersonalThingsAsync(Harness h)
    {
        await using var db = await h.Sqlite.NewContextAsync();

        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = h.OrgId, AppUserId = h.TargetId,
            Role = OrganizationMemberRole.Member, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = h.AdminId,
        });
        db.SignInEvents.Add(new SignInEvent
        {
            Id = Guid.NewGuid(), AppUserId = h.TargetId, Utc = DateTime.UtcNow,
            Succeeded = true, Method = "Password",
        });
        db.UserFollows.Add(new UserFollow
        {
            Id = Guid.NewGuid(), FollowerAppUserId = h.TargetId, FollowedAppUserId = h.AdminId,
            DateCreated = DateTime.UtcNow,
        });
        db.UserBlocks.Add(new UserBlock
        {
            Id = Guid.NewGuid(), BlockerAppUserId = h.TargetId, BlockedAppUserId = h.AdminId,
            DateCreated = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>A session recorded on their own, and the file it was made of.</summary>
    private static async Task<(Guid SessionId, Guid FileId)> AddPersonalSessionAsync(Harness h)
    {
        await using var db = await h.Sqlite.NewContextAsync();

        var documentId = Guid.NewGuid();
        var mediaId    = Guid.NewGuid();
        db.UploadFiles.Add(new UploadFile
        {
            Id = documentId, UploadFileTypeId = h.FileTypeId, AppUserId = h.TargetId,
            FileName = "data.json", StoredFileName = "data.json", ContentType = "application/json",
            FileSize = 1, StoragePath = "users/sam/data.json",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = h.TargetId,
        });
        db.UploadFiles.Add(new UploadFile
        {
            Id = mediaId, UploadFileTypeId = h.FileTypeId, AppUserId = h.TargetId,
            FileName = "a.m4a", StoredFileName = "a.m4a", ContentType = "audio/mp4",
            FileSize = 2, StoragePath = "users/sam/a.m4a",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = h.TargetId,
        });

        var sessionId = Guid.NewGuid();
        db.FieldSessionUploads.Add(new FieldSessionUpload
        {
            Id = sessionId, SubmittedByAppUserId = h.TargetId, DeviceSessionId = Guid.NewGuid(),
            DeviceModel = "iPhone", DocumentUploadFileId = documentId, InvestigationId = null,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = h.TargetId,
        });
        db.FieldSessionUploadFiles.Add(new FieldSessionUploadFile
        {
            Id = Guid.NewGuid(), FieldSessionUploadId = sessionId, UploadFileId = mediaId,
            RelativePath = "media/a.m4a", DateCreated = DateTime.UtcNow, CreatedByAppUserId = h.TargetId,
        });
        await db.SaveChangesAsync();
        return (sessionId, mediaId);
    }

    // ── the two endings ──────────────────────────────────────────────────────

    [Fact]
    public async Task An_account_holding_nothing_of_a_groups_disappears_row_and_all()
    {
        var h = await NewAsync();
        await using var _ = h.Sqlite;
        await AddPersonalThingsAsync(h);
        var (purge, storage) = Build(h);

        var (result, error) = await purge.PurgeAsync(h.TargetId, TargetName, h.AdminId);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.True(result!.RowRemoved, "Nothing else referred to this account, so its row should be gone.");

        await using var db = await h.Sqlite.NewContextAsync();
        Assert.Null(await db.Users.FindAsync(h.TargetId));
        Assert.Empty(await db.OrganizationUserMemberships.ToListAsync());
        Assert.Empty(await db.SignInEvents.ToListAsync());
        Assert.Empty(await db.UserFollows.ToListAsync());
        Assert.Empty(await db.UserBlocks.ToListAsync());
        // The acting SuperAdmin is untouched.
        Assert.NotNull(await db.Users.FindAsync(h.AdminId));
        storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_account_that_wrote_for_a_group_keeps_an_emptied_row_and_the_work_stays()
    {
        var h = await NewAsync();
        await using var _ = h.Sqlite;
        await AddPersonalThingsAsync(h);

        var caseId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        await using (var db = await h.Sqlite.NewContextAsync())
        {
            db.Cases.Add(new Case
            {
                Id = caseId, OrganizationId = h.OrgId, Title = "A case", CaseYear = 2026, OrgCaseNumber = 1,
                Status = CaseStatus.Active,
                StreetAddress1 = "1 Elm", City = "Franklin", State = "TN", ZipCode = "37064",
                DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = h.AdminId,
            });
            db.CaseNotes.Add(new CaseNote
            {
                Id = noteId, CaseId = caseId, AuthorAppUserId = h.TargetId,
                Body = "The knocking started at 2am.",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = h.TargetId,
            });
            await db.SaveChangesAsync();
        }

        var (purge, _s) = Build(h);
        var (result, error) = await purge.PurgeAsync(h.TargetId, TargetName, h.AdminId);

        Assert.Null(error);
        Assert.False(result!.RowRemoved, "A case note still refers to this account, so the row has to stay.");

        await using var verify = await h.Sqlite.NewContextAsync();

        // The group's record of its own work survives, word for word.
        var note = await verify.CaseNotes.FindAsync(noteId);
        Assert.NotNull(note);
        Assert.Equal("The knocking started at 2am.", note!.Body);

        // And the person is gone from the row that had to stay.
        var user = await verify.Users.FindAsync(h.TargetId);
        Assert.NotNull(user);
        Assert.Equal(AccountClosure.FormerMemberName, user!.DisplayName);
        Assert.Equal(AccountClosure.ClosedEmailFor(h.TargetId), user.Email);
        Assert.True(AccountClosure.IsClosedEmail(user.Email));
        Assert.Null(user.PasswordHash);
        Assert.Null(user.PhoneNumber);
        Assert.False(user.EmailConfirmed);
        Assert.NotNull(user.DateClosed);
        Assert.StartsWith("former-", user.Handle);
        Assert.DoesNotContain("Sam", user.DisplayName);
    }

    // ── what is destroyed, and what is not ───────────────────────────────────

    [Fact]
    public async Task A_session_they_recorded_on_their_own_is_destroyed_with_its_bytes()
    {
        var h = await NewAsync();
        await using var _ = h.Sqlite;
        var (sessionId, mediaId) = await AddPersonalSessionAsync(h);
        var (purge, storage) = Build(h);

        var (result, error) = await purge.PurgeAsync(h.TargetId, TargetName, h.AdminId);

        Assert.Null(error);
        Assert.Equal(1, result!.PersonalFieldSessions);

        await using var db = await h.Sqlite.NewContextAsync();
        Assert.Null(await db.FieldSessionUploads.FindAsync(sessionId));
        Assert.Empty(await db.FieldSessionUploadFiles.ToListAsync());
        Assert.Null(await db.UploadFiles.FindAsync(mediaId));

        storage.Verify(s => s.DeleteAsync("users/sam/a.m4a", It.IsAny<CancellationToken>()), Times.Once);
        storage.Verify(s => s.DeleteAsync("users/sam/data.json", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_session_recorded_for_an_investigation_is_the_groups_and_survives()
    {
        // The whole rule in one test: InvestigationId is what makes a session somebody's own.
        var h = await NewAsync();
        await using var _ = h.Sqlite;

        var sessionId = Guid.NewGuid();
        await using (var db = await h.Sqlite.NewContextAsync())
        {
            var investigationId = Guid.NewGuid();
            db.Investigations.Add(new Investigation
            {
                Id = investigationId, OrganizationId = h.OrgId, Title = "A visit",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = h.AdminId,
            });
            var documentId = Guid.NewGuid();
            db.UploadFiles.Add(new UploadFile
            {
                Id = documentId, UploadFileTypeId = h.FileTypeId, AppUserId = h.TargetId,
                FileName = "data.json", StoredFileName = "data.json", ContentType = "application/json",
                FileSize = 1, StoragePath = "users/sam/group.json",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = h.TargetId,
            });
            db.FieldSessionUploads.Add(new FieldSessionUpload
            {
                Id = sessionId, SubmittedByAppUserId = h.TargetId, DeviceSessionId = Guid.NewGuid(),
                DeviceModel = "iPhone", DocumentUploadFileId = documentId, InvestigationId = investigationId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = h.TargetId,
            });
            await db.SaveChangesAsync();
        }

        var (purge, storage) = Build(h);

        // Said BEFORE the button: the session and the file it is made of still name this account,
        // so the row cannot go. An earlier census skipped both tables outright and promised a
        // complete removal here, which the database then refused — with the account already
        // anonymised. This assertion is the one that fails if that comes back.
        var preview = await purge.PreviewAsync(h.TargetId);
        Assert.NotNull(preview);
        Assert.True(preview!.RowWillSurvive,
            "A group field session still refers to this account, so the preview must not promise "
          + "the row will be removed.");

        var (result, error) = await purge.PurgeAsync(h.TargetId, TargetName, h.AdminId);

        Assert.Null(error);
        Assert.Equal(0, result!.PersonalFieldSessions);

        await using var db2 = await h.Sqlite.NewContextAsync();
        Assert.NotNull(await db2.FieldSessionUploads.FindAsync(sessionId));
        storage.Verify(s => s.DeleteAsync("users/sam/group.json", It.IsAny<CancellationToken>()), Times.Never);
        // And what happened matches what was promised.
        Assert.False(result.RowRemoved);
        Assert.Equal(preview.RowWillSurvive, !result.RowRemoved);
    }

    [Fact]
    public async Task Every_way_back_into_the_account_is_removed()
    {
        // A left-behind external login would let Sign in with Apple walk straight back into an
        // anonymised account, which would make the rest of it pointless.
        var h = await NewAsync();
        await using var _ = h.Sqlite;
        var roleId = Guid.NewGuid();
        await using (var db = await h.Sqlite.NewContextAsync())
        {
            db.Roles.Add(new IdentityRole<Guid> { Id = roleId, Name = "Moderator", NormalizedName = "MODERATOR" });
            db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = h.TargetId, RoleId = roleId });
            db.UserLogins.Add(new IdentityUserLogin<Guid>
            {
                LoginProvider = "Apple", ProviderKey = "apple-subject-1",
                ProviderDisplayName = "Apple", UserId = h.TargetId,
            });
            db.UserTokens.Add(new IdentityUserToken<Guid>
            {
                UserId = h.TargetId, LoginProvider = "Apple", Name = "refresh", Value = "x",
            });
            await db.SaveChangesAsync();
        }

        var (purge, _s) = Build(h);
        Assert.Null((await purge.PurgeAsync(h.TargetId, TargetName, h.AdminId)).Error);

        await using var db2 = await h.Sqlite.NewContextAsync();
        Assert.Empty(await db2.UserLogins.Where(l => l.UserId == h.TargetId).ToListAsync());
        Assert.Empty(await db2.UserRoles.Where(r => r.UserId == h.TargetId).ToListAsync());
        Assert.Empty(await db2.UserTokens.Where(t => t.UserId == h.TargetId).ToListAsync());
    }

    [Fact]
    public async Task A_mistyped_name_deletes_nothing_at_all()
    {
        var h = await NewAsync();
        await using var _ = h.Sqlite;
        await AddPersonalThingsAsync(h);
        await AddPersonalSessionAsync(h);
        var (purge, storage) = Build(h);

        var (result, error) = await purge.PurgeAsync(h.TargetId, "sam recorder", h.AdminId);

        Assert.Null(result);
        Assert.Contains(TargetName, error);

        await using var db = await h.Sqlite.NewContextAsync();
        var user = await db.Users.FindAsync(h.TargetId);
        Assert.NotNull(user);
        Assert.Equal(TargetName, user!.DisplayName);
        Assert.Single(await db.FieldSessionUploads.ToListAsync());
        Assert.Single(await db.OrganizationUserMemberships.ToListAsync());
        storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_preview_promise_about_the_row_matches_what_happens()
    {
        // The preview tells a SuperAdmin, in advance, which of the two endings they will get.
        // Nothing checked that the promise came true until there was a database to check it with.
        foreach (var writesForTheGroup in new[] { false, true })
        {
            var h = await NewAsync();
            await using var _ = h.Sqlite;

            if (writesForTheGroup)
            {
                await using var db = await h.Sqlite.NewContextAsync();
                var caseId = Guid.NewGuid();
                db.Cases.Add(new Case
                {
                    Id = caseId, OrganizationId = h.OrgId, Title = "A case", CaseYear = 2026, OrgCaseNumber = 1,
                    Status = CaseStatus.Active,
                    StreetAddress1 = "1 Elm", City = "Franklin", State = "TN", ZipCode = "37064",
                    DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = h.AdminId,
                });
                db.CaseNotes.Add(new CaseNote
                {
                    Id = Guid.NewGuid(), CaseId = caseId, AuthorAppUserId = h.TargetId, Body = "A note.",
                    DateCreated = DateTime.UtcNow, CreatedByAppUserId = h.TargetId,
                });
                await db.SaveChangesAsync();
            }

            var (purge, _s) = Build(h);
            var preview = await purge.PreviewAsync(h.TargetId);
            Assert.NotNull(preview);

            var (result, error) = await purge.PurgeAsync(h.TargetId, TargetName, h.AdminId);
            Assert.Null(error);

            Assert.Equal(preview!.RowWillSurvive, !result!.RowRemoved);
            Assert.Equal(writesForTheGroup, result.RowRemoved is false);
        }
    }
}
