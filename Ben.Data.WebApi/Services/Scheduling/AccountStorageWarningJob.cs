using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Billing;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Tells somebody their storage is nearly full, before the refusal does (Ben, 2026-09-22).
/// </summary>
/// <remarks>
/// <para><b>Why a notice at all.</b> <see cref="AccountStorageGuard"/> refuses an upload that
/// would pass the cap, and that refusal is the first thing most people would ever have heard
/// about the limit — at the moment they were trying to do something, with a file already chosen.
/// A limit somebody meets without warning reads as the site breaking.</para>
///
/// <para><b>Two bands, deliberately.</b> Ten percent left is a heads-up and says what to do; five
/// percent is the one that says it plainly. Crossing from the first to the second sends the
/// second, because "already warned" would otherwise swallow the warning that matters.</para>
///
/// <para><b>Why a job rather than a check on the upload path.</b> Every personal door already
/// calls the guard, so a check could ride along there — but the guard is a pure function used by
/// twenty fixtures, and a function that sends messages is one nobody can call in a test without
/// arranging a mailbox. It would also fire on the upload that crosses the line, when the person is
/// mid-task and least able to act on it. Here it is one place, with one dependency, and a day's
/// latency on "you are running low" costs nothing: the hard stop is still the guard's.</para>
///
/// <para><b>Accounts covered by a paid plan are skipped</b>, matching the guard exactly. They have
/// no cap to be near, and a warning about a limit that does not apply is worse than silence.</para>
/// </remarks>
public sealed class AccountStorageWarningJob : IScheduledJob
{
    /// <summary>The bands, worst first — the order the check has to read them in.</summary>
    private static readonly int[] Bands = [5, 10];

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly PlatformMessageService _messages;
    private readonly ILogger<AccountStorageWarningJob> _log;

    public AccountStorageWarningJob(
        IDbContextFactory<BenDataContext> dbFactory,
        PlatformMessageService messages,
        ILogger<AccountStorageWarningJob> log)
    { _dbFactory = dbFactory; _messages = messages; _log = log; }

    public string Name => "account-storage-warning";

    public async Task RunAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Only accounts that actually store something personal, so the pass is bounded by people
        // with files rather than by every account on the site. The authoritative figure still
        // comes from the guard, one account at a time — this query only decides who to ask about.
        var candidates = await db.UploadFiles.AsNoTracking()
            .Where(f => f.AppUserId != null
                     && f.StoragePath != null
                     && f.StoragePath.StartsWith("users/")
                     && f.ArchivedFromUploadFileId == null)
            .Select(f => f.AppUserId!.Value)
            .Distinct()
            .ToListAsync(ct);

        // Anybody already carrying a band has to be looked at even with nothing stored: that is
        // how a band is cleared when somebody deletes everything.
        var warned = await db.AppUsers.AsNoTracking()
            .Where(u => u.StorageWarningBand != null)
            .Select(u => u.Id)
            .ToListAsync(ct);

        var cap = await AccountStorageGuard.CapBytesAsync(db, ct);
        if (cap <= 0) return;

        var sent = 0;
        var cleared = 0;

        foreach (var userId in candidates.Concat(warned).Distinct())
        {
            if (ct.IsCancellationRequested) return;

            var person = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (person is null || person.DateClosed is not null) continue;

            // A closed account keeps its row for ever; writing to its mailbox would be writing to
            // nobody. Paid accounts have no cap, so no band.
            var band = await PaidPlan.CoversAsync(db, userId, ct)
                ? null
                : BandFor(await AccountStorageGuard.UsedBytesAsync(db, userId, ct), cap);

            if (band == person.StorageWarningBand) continue;

            person.StorageWarningBand = band;

            if (band is { } percent)
            {
                await _messages.SendAsync(
                    SubjectFor(percent), BodyFor(percent, cap), [userId], userId, ct);
                sent++;
            }
            else cleared++;

            await db.SaveChangesAsync(ct);
        }

        if (sent > 0 || cleared > 0)
            _log.LogInformation(
                "Storage warnings: {Sent} sent, {Cleared} cleared.", sent, cleared);
    }

    /// <summary>
    /// Which warning this usage deserves, or null when it deserves none.
    /// </summary>
    /// <remarks>
    /// Worst band first, so an account at 3% is told it is at 5% or less rather than at 10% or
    /// less — both are true and only one of them is the warning worth reading. Over the cap counts
    /// as the lowest band rather than as no band: it is the most urgent state there is.
    /// </remarks>
    internal static int? BandFor(long usedBytes, long capBytes)
    {
        if (capBytes <= 0) return null;

        var remaining = (double)(capBytes - usedBytes) / capBytes * 100d;
        foreach (var band in Bands)
            if (remaining <= band) return band;

        return null;
    }

    private static string SubjectFor(int percent) => percent <= 5
        ? "Your storage is almost full"
        : "You are running low on storage";

    private static string BodyFor(int percent, long capBytes)
    {
        var allowance = capBytes >= 1024L * 1024 * 1024
            ? $"{capBytes / (double)(1024L * 1024 * 1024):0.#} GB"
            : $"{capBytes / (double)(1024 * 1024):0} MB";

        // Said as what to DO, not as a percentage to interpret. Both name the same three ways out,
        // in the order somebody would actually reach for them.
        return percent <= 5
            ? $"You have used almost all of the {allowance} a free account can store — less than "
            + "5% of it is left, and the next upload may be refused.\n\n"
            + "Three things make room. Publishing a recording to a public location gives its space "
            + "back, because a contribution to the archive earns its place. Deleting files you no "
            + "longer need frees theirs. And joining a group on a paid plan lifts the limit "
            + "entirely.\n\n"
            + "Nothing has been deleted and nothing will be — this is a limit on what you can add, "
            + "not on what you already have."
            : $"You have used about 90% of the {allowance} a free account can store.\n\n"
            + "Nothing needs doing yet. When you get closer, publishing a recording to a public "
            + "location gives its space back, deleting files you no longer need frees theirs, and "
            + "joining a group on a paid plan lifts the limit entirely.";
    }
}
