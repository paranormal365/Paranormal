using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Counts what the rate limits turn away, tells the SuperAdmins once, and keeps the tally
/// somewhere it can be looked at afterwards.
/// </summary>
/// <remarks>
/// <para><b>Ben's rule, which is the design:</b> <i>"Doesn't matter if it is 50,000 times… one
/// time letting me know is enough. Then just a place to track more than the one time. So, 650
/// takes less of a look than 6,500."</i> So there is exactly one message per limit, ever, and
/// the running total lives on the admin page instead. A repeated message would be worse than no
/// message: a limit under real pressure refuses continuously, and an alert that arrives every
/// hour is one the reader learns to dismiss without opening.</para>
///
/// <para><b>What it costs on the refusal path.</b> Nothing but an increment. Counting is in
/// memory and synchronous; the database is touched by <see cref="FlushAsync"/> on a timer, and
/// the message is the only thing that happens out of band. Fifty thousand refusals in a minute
/// cost one UPDATE, which is the point of keeping a tally per policy rather than a log per
/// request.</para>
///
/// <para><b>"Close to the limit" is deliberately not alerted on.</b> Approaching a limit is
/// normal and harms nobody — a minute that reaches 590 of 600 turned nobody away. The event
/// worth a message is somebody actually being refused. The near-miss version fires on every busy
/// evening, which is how an alert becomes noise.</para>
/// </remarks>
public sealed class RateLimitAlerting
{
    /// <summary>Refusals before a limit is worth the one message.</summary>
    /// <remarks>
    /// Above the handful one confused client produces, below what a genuinely blocked crowd
    /// reaches in seconds. Only the first message is gated on this; the tally counts from one.
    /// </remarks>
    public const int AlertThreshold = 25;

    /// <summary>How often accumulated counts are written to the tracking table.</summary>
    public static readonly TimeSpan FlushInterval = TimeSpan.FromMinutes(1);

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly PlatformMessageService _messages;
    private readonly ILogger<RateLimitAlerting> _logger;
    private readonly Func<DateTime> _now;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PolicyState> _byPolicy = new();

    public RateLimitAlerting(
        IDbContextFactory<BenDataContext> dbFactory,
        PlatformMessageService messages,
        ILogger<RateLimitAlerting> logger,
        Func<DateTime>? now = null)
    {
        _dbFactory = dbFactory;
        _messages  = messages;
        _logger    = logger;
        _now       = now ?? (() => DateTime.UtcNow);
    }

    /// <summary>What one policy has accumulated since the last flush.</summary>
    private sealed class PolicyState
    {
        public readonly Lock Gate = new();
        public long Pending;
        public readonly HashSet<string> Callers = [];
        public bool MessageConsidered;
    }

    /// <summary>
    /// Records one refusal. Returns an alert only for the first crossing of the threshold in this
    /// process — the common answer, by an enormous margin, is null.
    /// </summary>
    /// <remarks>
    /// A non-null return still has to survive <see cref="TryNotifyAsync"/>, which checks whether
    /// this limit has ever been announced. The in-process flag is only there to keep the database
    /// out of the refusal path; the row is what actually decides.
    /// </remarks>
    public RateLimitAlert? Record(string policyName, string callerKey)
    {
        var state = _byPolicy.GetOrAdd(policyName, _ => new PolicyState());

        lock (state.Gate)
        {
            state.Pending++;
            state.Callers.Add(callerKey);

            if (state.MessageConsidered || state.Pending < AlertThreshold) return null;

            state.MessageConsidered = true;
            return new RateLimitAlert(
                PolicyName: policyName,
                Refusals: state.Pending,
                DistinctCallers: state.Callers.Count);
        }
    }

    /// <summary>
    /// Writes accumulated counts into the tracking table. Safe to call when nothing has happened.
    /// </summary>
    public async Task FlushAsync(CancellationToken ct)
    {
        var batch = new List<(string Policy, long Count, int Callers)>();

        foreach (var (policy, state) in _byPolicy)
        {
            lock (state.Gate)
            {
                if (state.Pending == 0) continue;
                batch.Add((policy, state.Pending, state.Callers.Count));
                state.Pending = 0;
                state.Callers.Clear();
            }
        }

        if (batch.Count == 0) return;

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var now = _now();

            foreach (var (policy, count, callers) in batch)
            {
                var row = await db.RateLimitRefusals.FirstOrDefaultAsync(r => r.PolicyName == policy, ct);

                if (row is null)
                {
                    db.RateLimitRefusals.Add(new RateLimitRefusal
                    {
                        Id = Guid.NewGuid(),
                        PolicyName = policy,
                        Refusals = count,
                        DistinctCallers = callers,
                        PeakDistinctCallers = callers,
                        DateFirstSeen = now,
                        DateLastSeen = now,
                    });
                }
                else
                {
                    row.Refusals += count;
                    row.DistinctCallers = callers;
                    row.PeakDistinctCallers = Math.Max(row.PeakDistinctCallers, callers);
                    row.DateLastSeen = now;
                }
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Losing a minute of counts is not worth failing anything over, but a tally that
            // silently stops climbing would be read as "the problem stopped".
            _logger.LogError(ex, "Could not write rate-limit refusal counts.");
        }
    }

    /// <summary>
    /// Sends the one message for this limit, if it has never been sent.
    /// </summary>
    /// <remarks>
    /// The row is the authority rather than any in-memory flag, so a restart does not produce a
    /// second message and several API instances do not each send their own. Re-arming is a
    /// deliberate act on the admin page, never a timer.
    /// </remarks>
    public async Task TryNotifyAsync(RateLimitAlert alert, CancellationToken ct)
    {
        try
        {
            await FlushAsync(ct);   // so the number in the message matches the number on the page

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var row = await db.RateLimitRefusals
                .FirstOrDefaultAsync(r => r.PolicyName == alert.PolicyName, ct);

            if (row is null || row.DateNotified is not null) return;

            var superAdmins = await (
                from userRole in db.Set<IdentityUserRole<Guid>>()
                join role in db.Set<IdentityRole<Guid>>() on userRole.RoleId equals role.Id
                where role.Name == RoleNames.SuperAdmin
                select userRole.UserId).Distinct().ToListAsync(ct);

            if (superAdmins.Count == 0)
            {
                _logger.LogWarning(
                    "Rate limit {Policy} has refused {Refusals} requests and there is no SuperAdmin to tell.",
                    alert.PolicyName, row.Refusals);
                return;
            }

            // The sender is a SuperAdmin rather than an invented identity: PlatformMessageService
            // stamps CreatedByAppUserId, and an id matching no row would be a dangling author on
            // every screen that resolves names.
            var toSend = alert with { Refusals = row.Refusals, DistinctCallers = row.DistinctCallers };
            await _messages.SendAsync(toSend.Subject(), toSend.Body(), superAdmins, superAdmins[0], ct);

            row.DateNotified = _now();
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // An alert that cannot be delivered must not disturb the request that triggered it.
            // The log carries the same information the message would have.
            _logger.LogError(ex,
                "Could not deliver the rate-limit alert for {Policy}.", alert.PolicyName);
        }
    }
}

/// <summary>The one thing worth telling a SuperAdmin about a rate limit.</summary>
public sealed record RateLimitAlert(string PolicyName, long Refusals, int DistinctCallers)
{
    /// <summary>How the limit is named to a person rather than in configuration.</summary>
    public string FriendlyName => PolicyName switch
    {
        RateLimiting.EventAttendancePolicy => "public event sign-up",
        RateLimiting.GeocodingPolicy       => "address lookup",
        RateLimiting.AuthPolicy            => "sign in and register",
        _                                  => "general requests",
    };

    public string Subject() => $"Rate limit reached — {FriendlyName}";

    /// <summary>
    /// The message, written to be acted on rather than merely noticed.
    /// </summary>
    /// <remarks>
    /// The distinct-address count is the diagnosis and comes first: several addresses means real
    /// people cannot get through and the limit is too low, one address means the limit is doing
    /// its job. It also says plainly that this is the only message, because a reader who expects
    /// another one will wait for it instead of looking.
    /// </remarks>
    public string Body()
    {
        var crowd = DistinctCallers > 1;

        var reading = crowd
            ? $"{Refusals:N0} requests from {DistinctCallers:N0} different addresses were refused. "
              + "Separate callers being turned away usually means real people cannot get through, "
              + "and the limit is set too low for what the site is now being used for."
            : $"{Refusals:N0} requests from a single address were refused. One address hitting a "
              + "limit repeatedly is usually a script or a stuck client — the limit doing exactly "
              + "what it is for.";

        var advice = crowd
            ? "\n\nTo raise it: Site settings → the rate limit for " + FriendlyName + ". "
              + "Changes take effect within a minute and need no restart."
            : string.Empty;

        return $"The {FriendlyName} rate limit turned requests away.\n\n{reading}{advice}\n\n"
             + "This is the only message you will get about this limit, however many more times it "
             + "happens. The running total is on the SuperAdmin page under Rate Limits, where the "
             + "size of the number is the thing worth reading.";
    }
}
