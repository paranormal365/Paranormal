using System.Collections.Concurrent;
using Ben.Service.Models.Admin;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// What each scheduled job did on its most recent pass, and how often it has failed, since this server started.
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> A job that fails is logged and the scheduler carries on — which is right, and which made
/// "has the hold-expiry job actually been running?" a question only the log could answer, one line at a time. The
/// SuperAdmin's Event health tab reads this instead (Ben, 2026-09-14: charts to help with development).</para>
///
/// <para><b>In memory, on purpose.</b> One process runs the jobs, so one ledger is the whole truth for that process; a
/// table would need a migration carried to production for a development panel. The panel says "since this server
/// started", which is exactly what this can promise.</para>
/// </remarks>
public sealed class ScheduledJobLedger
{
    private readonly ConcurrentDictionary<string, Entry> _jobs = new(StringComparer.Ordinal);

    /// <summary>When this ledger began counting — the server's start, as far as the panel is concerned.</summary>
    public DateTime SinceUtc { get; } = DateTime.UtcNow;

    private sealed class Entry
    {
        public DateTime LastStartedUtc;
        public long LastDurationMs;
        public bool LastSucceeded;
        public string? LastError;
        public DateTime? LastFailedUtc;
        public int Runs;
        public int Failures;
    }

    /// <summary>Records one run of a job. <paramref name="error"/> is null when it succeeded.</summary>
    public void Record(string job, DateTime startedUtc, TimeSpan duration, Exception? error)
    {
        var entry = _jobs.GetOrAdd(job, _ => new Entry());
        lock (entry)
        {
            entry.LastStartedUtc = startedUtc;
            entry.LastDurationMs = (long)duration.TotalMilliseconds;
            entry.LastSucceeded = error is null;
            entry.Runs++;
            if (error is null) return;

            entry.Failures++;
            entry.LastFailedUtc = startedUtc;
            // The exception's own first line: enough to know which error it was, without a stack trace on a web page.
            var firstLine = error.Message.Split('\n')[0].Trim();
            entry.LastError = firstLine.Length > 300 ? firstLine[..300] : firstLine;
        }
    }

    /// <summary>Every job that has run, by name.</summary>
    public IReadOnlyList<ScheduledJobRunRecord> Snapshot()
        => [.. _jobs.OrderBy(j => j.Key, StringComparer.Ordinal).Select(j =>
        {
            lock (j.Value)
            {
                var e = j.Value;
                return new ScheduledJobRunRecord(j.Key, e.LastStartedUtc, e.LastDurationMs, e.LastSucceeded, e.LastError,
                    e.LastFailedUtc, e.Runs, e.Failures);
            }
        })];
}
