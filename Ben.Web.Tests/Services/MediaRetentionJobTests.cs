using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing;
using Ben.Data.WebApi.Services.Media;
using Ben.Data.WebApi.Services.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The retention job's two questions — who is owed a warning, and what may be taken — and the
/// promise that nothing is taken unannounced (item 233).
/// </summary>
/// <remarks>
/// <para><b>Why the SQL Server test exists.</b> The warning query shipped with a column minus a
/// <see cref="TimeSpan"/> in it. EF could not translate that for SQL Server, so wherever mail was
/// set up the job threw on every pass: no notice was ever sent, and since the warning runs before
/// the sweep, nothing was swept either. No test caught it because none ran the query on a relational
/// provider at all. The InMemory provider compiles the expression to C# and accepts anything. SQLite
/// refuses this particular expression too, but it is a different translator with its own date
/// functions and cannot vouch for SQL Server. <c>ToQueryString</c> against the SQL Server provider
/// can, with no server at all.</para>
///
/// <para>Running the queries on an actual server is opt-in, through
/// <c>BEN_SQLSERVER_TEST_CONNECTION</c>. It only reads.</para>
/// </remarks>
public sealed class MediaRetentionJobTests
{
    private static readonly DateTime Now = new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

    // ── Translation ───────────────────────────────────────────────────────────

    /// <summary>A context on the SQL Server provider that never opens a connection.</summary>
    private static BenDataContext SqlServerContext(string connection = "Server=unused;Database=unused;")
        => new(new DbContextOptionsBuilder<BenDataContext>().UseSqlServer(connection).Options);

    [Fact]
    public void The_notice_query_translates_for_SQL_Server()
    {
        using var db = SqlServerContext();

        var sql = db.UploadFiles
            .Where(MediaRetentionJob.DueForNotice(Now))
            .OrderBy(f => f.ExpiresAtUtc)
            .Take(200)
            .ToQueryString();

        // The last-window comparison is done in the database, not dropped or evaluated in memory.
        Assert.Contains("DATEADD", sql);
        Assert.Contains("[ExpiryNoticeSentAtUtc]", sql);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_sweep_query_translates_for_SQL_Server(bool canWarn)
    {
        using var db = SqlServerContext();

        var sql = db.UploadFiles
            .Where(MediaRetentionJob.DueForSweep(Now, canWarn))
            .OrderBy(f => f.ExpiresAtUtc)
            .Take(200)
            .ToQueryString();

        Assert.Contains("[ExpiresAtUtc]", sql);
    }

    [Fact]
    public void The_last_notice_is_whole_days_because_SQL_Server_adds_days_as_an_integer()
    {
        // DATEADD(day, …) takes an int, so EF casts. A LastNotice of 36 hours would quietly become
        // one day in the database while meaning a day and a half in C#.
        Assert.Equal(Math.Floor(MediaRetentionJob.LastNotice.TotalDays), MediaRetentionJob.LastNotice.TotalDays);
    }

    [SkippableFact]
    public async Task Both_queries_run_on_a_real_SQL_Server()
    {
        var connection = Environment.GetEnvironmentVariable("BEN_SQLSERVER_TEST_CONNECTION");
        Skip.If(string.IsNullOrWhiteSpace(connection),
            "Set BEN_SQLSERVER_TEST_CONNECTION to run the retention queries against a real SQL Server. Read-only.");

        await using var db = SqlServerContext(connection!);
        var now = DateTime.UtcNow;

        await db.UploadFiles.AsNoTracking()
            .Where(MediaRetentionJob.DueForNotice(now)).OrderBy(f => f.ExpiresAtUtc).Take(200).ToListAsync();
        await db.UploadFiles.AsNoTracking()
            .Where(MediaRetentionJob.DueForSweep(now, canWarn: true)).OrderBy(f => f.ExpiresAtUtc).Take(200).ToListAsync();
        await db.UploadFiles.AsNoTracking()
            .Where(MediaRetentionJob.DueForSweep(now, canWarn: false)).OrderBy(f => f.ExpiresAtUtc).Take(200).ToListAsync();
    }

    // ── Who is owed a warning ─────────────────────────────────────────────────

    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid FileType = Guid.NewGuid();

    /// <summary>One file in one state, and whether it is owed a warning at <see cref="Now"/>.</summary>
    private sealed record Case(string Name, DateTime? Expires, DateTime? Noticed, bool Kept, bool Due);

    private static readonly Case[] Cases =
    [
        new("no clock at all",                          null,                  null,                 false, false),
        new("a week out, never told",                   Now.AddDays(5),        null,                 false, true),
        new("beyond the first notice",                  Now.AddDays(10),       null,                 false, false),
        new("already gone, never told",                 Now.AddHours(-1),      null,                 false, false),
        new("kept",                                     Now.AddDays(5),        null,                 true,  false),
        new("told this week, not yet the last day",     Now.AddDays(5),        Now.AddDays(-1),      false, false),
        new("told a week out, now the last day",        Now.AddHours(12),      Now.AddDays(-6),      false, true),
        new("told inside the last day already",         Now.AddHours(12),      Now.AddHours(-2),     false, false),
        new("told just before the last day began",      Now.AddHours(20),      Now.AddHours(-4).AddMinutes(-5), false, true),
        new("told just after the last day began",       Now.AddHours(20),      Now.AddHours(-4).AddMinutes(5),  false, false),
    ];

    /// <summary>The rule as it was first written, before it had to be expressible in SQL.</summary>
    private static bool AsFirstWritten(Case c)
        => c.Expires != null
        && !c.Kept
        && c.Expires > Now
        && c.Expires <= Now + MediaRetentionJob.FirstNotice
        && (c.Noticed == null
            || (c.Expires <= Now + MediaRetentionJob.LastNotice
                && c.Noticed < c.Expires.Value - MediaRetentionJob.LastNotice));

    [Fact]
    public void The_cases_below_are_what_the_rule_always_meant()
    {
        foreach (var c in Cases) Assert.True(AsFirstWritten(c) == c.Due, c.Name);
    }

    [Fact]
    public async Task Each_file_is_warned_or_not_as_the_rule_says_in_a_real_query()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var ids = new Dictionary<Guid, Case>();

        await using (var db = await sqlite.NewContextAsync())
        {
            SeedOwner(db);
            foreach (var c in Cases)
            {
                var id = Guid.NewGuid();
                ids[id] = c;
                db.UploadFiles.Add(Upload(id, c.Expires, c.Noticed, c.Kept ? Now.AddDays(-2) : null));
            }
            await db.SaveChangesAsync();
        }

        await using var read = await sqlite.NewContextAsync();
        var due = await read.UploadFiles.Where(MediaRetentionJob.DueForNotice(Now)).Select(f => f.Id).ToListAsync();

        foreach (var (id, c) in ids) Assert.True(due.Contains(id) == c.Due, c.Name);
    }

    // ── What may be taken ─────────────────────────────────────────────────────

    private sealed class FakeEmail(bool configured) : IEmailService
    {
        public bool IsConfigured => configured;
        public List<string> Sent { get; } = [];

        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        {
            Sent.Add(to);
            return Task.CompletedTask;
        }
    }

    private static readonly Guid OrgId = Guid.NewGuid();

    private static MediaRetentionJob Build(SqliteTestDb sqlite, IEmailService email)
        => new(sqlite.Factory, email,
               new Mock<IFileStorageService>().Object,
               new Mock<IMediaIngestService>().Object,
               new MediaRetentionPolicy(new SubscriptionLimitGuard(sqlite.Factory)),
               Options.Create(new SiteIdentity { Name = "IsHaunted.com", BaseUrl = "https://ishaunted.com" }),
               NullLogger<MediaRetentionJob>.Instance);

    /// <summary>A tour business on a plan with a seven-day recording rule, owning one recording.</summary>
    private static async Task<Guid> SeedExpiredRecordingAsync(SqliteTestDb sqlite, DateTime? noticed)
    {
        var fileId = Guid.NewGuid();
        var tierId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var db = await sqlite.NewContextAsync();
        SeedOwner(db);
        db.Organizations.Add(new Organization
        {
            Id = OrgId, Name = "Night Walks", UrlName = "night-walks",
            Kind = OrganizationKind.GhostWalkingTour, DateCreated = now, CreatedByAppUserId = OwnerId,
        });
        db.SubscriptionTiers.Add(new SubscriptionTier
        {
            Id = tierId, Name = "Tour & Event Business", MinMembers = 1, MaxMembers = null,
            IsBandedByMembers = false, IsActive = true, DateCreated = now, CreatedByAppUserId = OwnerId,
        });
        db.SubscriptionTierLimits.Add(new SubscriptionTierLimit
        {
            Id = Guid.NewGuid(), SubscriptionTierId = tierId,
            Limit = SubscriptionLimit.RecordingRetentionDays, MaxValue = 7,
            DateCreated = now, CreatedByAppUserId = OwnerId,
        });
        db.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, Status = SubscriptionStatus.Active,
            SubscriptionTierId = tierId, Interval = BillingInterval.Monthly,
            CurrentPeriodStart = now.AddDays(-10), CurrentPeriodEnd = now.AddDays(20),
            DateCreated = now, CreatedByAppUserId = OwnerId,
        });

        var file = Upload(fileId, now.AddHours(-1), noticed, kept: null);
        file.AppUserId = null;
        file.OwnerOrganizationId = OrgId;
        db.UploadFiles.Add(file);

        await db.SaveChangesAsync();
        return fileId;
    }

    [Fact]
    public async Task An_expired_file_nobody_was_told_about_is_given_its_last_day_not_deleted()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var fileId = await SeedExpiredRecordingAsync(sqlite, noticed: null);

        await Build(sqlite, new FakeEmail(configured: true)).PassAsync(default);

        await using var db = await sqlite.NewContextAsync();
        var file = await db.UploadFiles.SingleOrDefaultAsync(f => f.Id == fileId);
        Assert.NotNull(file);
        Assert.InRange(file!.ExpiresAtUtc!.Value,
            DateTime.UtcNow.AddHours(23), DateTime.UtcNow.Add(MediaRetentionJob.LastNotice));

        // And it is then owed the warning it never had.
        var due = await db.UploadFiles.Where(MediaRetentionJob.DueForNotice(DateTime.UtcNow)).Select(f => f.Id).ToListAsync();
        Assert.Contains(fileId, due);
    }

    [Fact]
    public async Task With_no_mail_an_unannounced_file_is_left_exactly_as_it_was()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var fileId = await SeedExpiredRecordingAsync(sqlite, noticed: null);
        DateTime? before;
        await using (var db = await sqlite.NewContextAsync())
            before = (await db.UploadFiles.SingleAsync(f => f.Id == fileId)).ExpiresAtUtc;

        await Build(sqlite, new FakeEmail(configured: false)).PassAsync(default);

        await using var after = await sqlite.NewContextAsync();
        var file = await after.UploadFiles.SingleOrDefaultAsync(f => f.Id == fileId);
        Assert.NotNull(file);
        Assert.Equal(before, file!.ExpiresAtUtc);
    }

    [Fact]
    public async Task An_expired_file_that_was_warned_is_taken()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var fileId = await SeedExpiredRecordingAsync(sqlite, noticed: DateTime.UtcNow.AddDays(-2));

        await Build(sqlite, new FakeEmail(configured: true)).PassAsync(default);

        await using var db = await sqlite.NewContextAsync();
        Assert.False(await db.UploadFiles.AnyAsync(f => f.Id == fileId));
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static void SeedOwner(BenDataContext db)
    {
        db.Users.Add(new AppUser
        {
            Id = OwnerId, Email = "owner@example.com", UserName = "owner@example.com",
            DisplayName = "Sam Recorder", DateCreated = Now,
        });
        db.UploadFileTypes.Add(new UploadFileType
        {
            Id = FileType, Name = "Evidence", DateCreated = Now, CreatedByAppUserId = OwnerId,
        });
    }

    private static UploadFile Upload(Guid id, DateTime? expires, DateTime? noticed, DateTime? kept) => new()
    {
        Id = id, UploadFileTypeId = FileType, AppUserId = OwnerId,
        FileName = "evp.m4a", StoredFileName = "evp.m4a", ContentType = "audio/mp4",
        FileSize = 1, StoragePath = $"users/sam/{id}.m4a",
        ExpiresAtUtc = expires, ExpiryNoticeSentAtUtc = noticed, KeptAtUtc = kept,
        DateCreated = Now, CreatedByAppUserId = OwnerId,
    };
}
