using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The warning is sent once, gets stronger, and comes back if the account fills again.
/// </summary>
/// <remarks>
/// <para>The banding arithmetic is covered next door. This is about the half that decides whether
/// somebody is TOLD: a notice repeated on every pass is one people learn to ignore before it
/// matters, and a notice sent once for ever is one that never fires again for somebody who tidies
/// up and refills a year later.</para>
///
/// <para>Run against SQLite rather than the in-memory provider because the job writes and reads
/// the same rows across several passes, which is where a provider that is not a database starts
/// disagreeing with one that is.</para>
/// </remarks>
public sealed class AccountStorageWarningJobTests
{
    private const int CapMegabytes = 100;
    private static readonly Guid FileTypeId = new("70000000-0000-0000-0000-000000000003");
    private static long Megabytes(double n) => (long)(n * 1024 * 1024);

    private static async Task<(SqliteTestDb Db, Guid UserId)> SeedAsync()
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.Factory.CreateDbContextAsync();

        var userId = Guid.NewGuid();
        db.AppUsers.Add(new AppUser
        {
            Id = userId, UserName = $"{userId}@t.com", Email = $"{userId}@t.com",
            DisplayName = "Solo", DateCreated = DateTime.UtcNow,
        });
        // SQLite enforces the keys, which is why this fixture needs a real file type where the
        // in-memory ones did not.
        db.UploadFileTypes.Add(new UploadFileType
        {
            Id = FileTypeId, Name = "Personal media",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.SiteSettings.Add(new SiteSetting
        {
            Key = SiteSettingKeys.FreeAccountStorageMegabytes, Value = CapMegabytes.ToString(),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return (sqlite, userId);
    }

    /// <summary>Puts a personal file of a given size under the account.</summary>
    private static async Task<Guid> StoreAsync(SqliteTestDb sqlite, Guid userId, long bytes)
    {
        await using var db = await sqlite.Factory.CreateDbContextAsync();
        var id = Guid.NewGuid();
        db.UploadFiles.Add(new UploadFile
        {
            Id = id, UploadFileTypeId = FileTypeId, FileName = "a.mp4", ContentType = "video/mp4",
            StoragePath = $"users/{userId}/{id}.mp4", FileSize = bytes, AppUserId = userId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static AccountStorageWarningJob Job(SqliteTestDb sqlite)
        => new(sqlite.Factory,
               new PlatformMessageService(sqlite.Factory),
               NullLogger<AccountStorageWarningJob>.Instance);

    private static async Task<(int Count, string? Latest, int? Band)> StateAsync(
        SqliteTestDb sqlite, Guid userId)
    {
        await using var db = await sqlite.Factory.CreateDbContextAsync();
        var mine = await db.UserMessageTos.AsNoTracking()
            .Where(t => t.ToAppUserId == userId)
            .Join(db.UserMessages.AsNoTracking(), t => t.MessageId, m => m.Id, (t, m) => m)
            .OrderByDescending(m => m.DateCreated)
            .ToListAsync();

        var band = await db.AppUsers.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.StorageWarningBand).SingleAsync();

        return (mine.Count, mine.FirstOrDefault()?.MessageSubject, band);
    }

    [Fact]
    public async Task An_account_with_room_hears_nothing()
    {
        var (sqlite, userId) = await SeedAsync();
        await using var _ = sqlite;

        await StoreAsync(sqlite, userId, Megabytes(50));
        await Job(sqlite).RunAsync(default);

        var state = await StateAsync(sqlite, userId);
        Assert.Equal(0, state.Count);
        Assert.Null(state.Band);
    }

    /// <summary>
    /// The gentle notice arrives once and does not arrive again.
    /// </summary>
    /// <remarks>
    /// The second pass is the assertion that matters. Everything about this job is easy except
    /// not repeating itself, and a scheduler runs it every pass for ever.
    /// </remarks>
    [Fact]
    public async Task The_first_warning_is_sent_once_and_only_once()
    {
        var (sqlite, userId) = await SeedAsync();
        await using var _ = sqlite;

        await StoreAsync(sqlite, userId, Megabytes(92));   // 8% left

        await Job(sqlite).RunAsync(default);
        var first = await StateAsync(sqlite, userId);
        Assert.Equal(1, first.Count);
        Assert.Equal(10, first.Band);
        Assert.Contains("running low", first.Latest);

        await Job(sqlite).RunAsync(default);
        await Job(sqlite).RunAsync(default);
        Assert.Equal(1, (await StateAsync(sqlite, userId)).Count);
    }

    /// <summary>
    /// Getting worse sends the stronger notice rather than being swallowed as "already warned".
    /// </summary>
    [Fact]
    public async Task Crossing_into_the_lower_band_says_so()
    {
        var (sqlite, userId) = await SeedAsync();
        await using var _ = sqlite;

        await StoreAsync(sqlite, userId, Megabytes(92));
        await Job(sqlite).RunAsync(default);

        await StoreAsync(sqlite, userId, Megabytes(5));    // now 3% left
        await Job(sqlite).RunAsync(default);

        var state = await StateAsync(sqlite, userId);
        Assert.Equal(2, state.Count);
        Assert.Equal(5, state.Band);
        Assert.Contains("almost full", state.Latest);
    }

    /// <summary>
    /// Freeing space re-arms it, so the same person is told again next time.
    /// </summary>
    /// <remarks>
    /// The reason the stored value is a band rather than a flag. Somebody who clears their account
    /// and fills it again a year later has to hear about it again; a one-way flag would warn them
    /// once in their life.
    /// </remarks>
    [Fact]
    public async Task Clearing_space_arms_it_again()
    {
        var (sqlite, userId) = await SeedAsync();
        await using var _ = sqlite;

        var big = await StoreAsync(sqlite, userId, Megabytes(92));
        await Job(sqlite).RunAsync(default);
        Assert.Equal(10, (await StateAsync(sqlite, userId)).Band);

        await using (var db = await sqlite.Factory.CreateDbContextAsync())
        {
            db.UploadFiles.Remove(db.UploadFiles.Single(f => f.Id == big));
            await db.SaveChangesAsync();
        }

        await Job(sqlite).RunAsync(default);
        Assert.Null((await StateAsync(sqlite, userId)).Band);

        // ...and filling it again tells them a second time.
        await StoreAsync(sqlite, userId, Megabytes(92));
        await Job(sqlite).RunAsync(default);

        var state = await StateAsync(sqlite, userId);
        Assert.Equal(2, state.Count);
        Assert.Equal(10, state.Band);
    }

    /// <summary>A closed account is not written to.</summary>
    /// <remarks>
    /// Its row survives for ever by design, so without this the job would post to the mailbox of
    /// somebody who has left, every pass, for ever.
    /// </remarks>
    [Fact]
    public async Task A_closed_account_is_left_alone()
    {
        var (sqlite, userId) = await SeedAsync();
        await using var _ = sqlite;

        await StoreAsync(sqlite, userId, Megabytes(98));
        await using (var db = await sqlite.Factory.CreateDbContextAsync())
        {
            db.AppUsers.Single(u => u.Id == userId).DateClosed = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await Job(sqlite).RunAsync(default);
        Assert.Equal(0, (await StateAsync(sqlite, userId)).Count);
    }
}
