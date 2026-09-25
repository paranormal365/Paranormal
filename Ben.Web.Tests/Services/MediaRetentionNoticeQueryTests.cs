using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Scheduling;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The retention job's warning query survives translation to SQL Server (item 233).
/// </summary>
/// <remarks>
/// <para><b>Why this test exists.</b> The query shipped with a column minus a
/// <see cref="TimeSpan"/> in it — <c>ExpiryNoticeSentAtUtc &lt; ExpiresAtUtc.Value - LastNotice</c>.
/// EF cannot translate that for SQL Server, so from the day expiries shipped (2026-09-10) the job
/// threw on every pass wherever mail was configured: nobody was ever warned that a file was coming
/// down, and because the warning runs before the sweep, nothing was swept there either. The job's
/// failures are logged and swallowed by the scheduler, so the only trace was an error line every
/// six hours.</para>
///
/// <para><b>Why no existing test caught it.</b> None of them ran the query on a relational provider.
/// The InMemory provider compiles the expression to C# and will accept anything a compiler will,
/// including arithmetic no database can do. <c>ToQueryString</c> on the SQL Server provider asks
/// the real translator and needs no server, no connection and no database.</para>
///
/// <para>This targets <see cref="MediaRetentionJob.DueForNotice"/>, which is the expression the job
/// itself passes to <c>Where</c>. A test that rebuilt the predicate locally would pass while the
/// job stayed broken, which is the failure this file is here to prevent.</para>
/// </remarks>
public sealed class MediaRetentionNoticeQueryTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A context on the SQL Server provider that never opens a connection.</summary>
    private static BenDataContext SqlServerContext()
        => new(new DbContextOptionsBuilder<BenDataContext>()
            .UseSqlServer("Server=unused;Database=unused;")
            .Options);

    [Fact]
    public void The_notice_query_translates_for_SQL_Server()
    {
        using var db = SqlServerContext();

        // Throws InvalidOperationException ("could not be translated") on the old predicate.
        var sql = db.UploadFiles
            .Where(MediaRetentionJob.DueForNotice(Now))
            .OrderBy(f => f.ExpiresAtUtc)
            .ToQueryString();

        // Translated, not dropped: the comparison is done by the database.
        Assert.Contains("DATEADD", sql);
        Assert.Contains("[ExpiryNoticeSentAtUtc]", sql);
    }

    [Fact]
    public void The_last_notice_window_is_whole_days()
    {
        // DATEADD(day, …) takes an integer, so EF casts the argument. A LastNotice of, say, 36
        // hours would keep meaning a day and a half in C# while quietly becoming one day in the
        // database — the fix would then change the rule rather than preserve it.
        Assert.Equal(Math.Floor(MediaRetentionJob.LastNotice.TotalDays),
                     MediaRetentionJob.LastNotice.TotalDays);
    }
}
