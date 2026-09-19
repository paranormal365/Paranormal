using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Venues;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Confirms a proved claim once its week for objections has passed with none (item 235 phase 9).
/// </summary>
/// <remarks>
/// <para><b>Only the unchallenged.</b> A claim somebody objected to is Contested and waits for a
/// person; this never looks at it. And a claim that has become impossible — another group was
/// confirmed as the venue in the meantime — is not quietly dropped: it becomes Contested with the
/// reason, so a person sees two groups each saying they run the building.</para>
///
/// <para>One claim, one save, inside a try, like every job here.</para>
/// </remarks>
public sealed class VenueClaimJob : IScheduledJob
{
    public string Name => "venue-claims";

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly PlatformMessageService _messages;
    private readonly ILogger<VenueClaimJob> _logger;

    public VenueClaimJob(IDbContextFactory<BenDataContext> dbFactory, PlatformMessageService messages, ILogger<VenueClaimJob> logger)
    { _dbFactory = dbFactory; _messages = messages; _logger = logger; }

    public Task RunAsync(CancellationToken ct) => RunAtAsync(DateTime.UtcNow, ct);

    internal async Task RunAtAsync(DateTime now, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var due = await db.VenuePlaceClaims
            .Where(c => c.State == VenueClaimState.Proved && c.ObjectionsCloseUtc <= now && c.ObjectedUtc == null)
            .Select(c => c.Id)
            .ToListAsync(ct);

        foreach (var claimId in due)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var claim = await db.VenuePlaceClaims.Include(c => c.Place).Include(c => c.Organization)
                    .FirstAsync(c => c.Id == claimId, ct);

                var refusal = await VenueClaims.ApproveAsync(db, claim, actorId: null, note: null, now, ct);
                if (refusal is not null)
                {
                    claim.State = VenueClaimState.Contested;
                    claim.ObjectionText = refusal;
                    claim.ObjectedUtc = now;
                    claim.DateUpdated = now;
                }

                await db.SaveChangesAsync(ct);

                if (refusal is null)
                    await _messages.SendAsync(
                        $"{claim.Organization.Name} is confirmed as the venue at {claim.Place.Name}",
                        $"<p>Nobody objected, so your group is now confirmed as the venue at "
                        + $"<strong>{VenueNotices.Safe(claim.Place.Name)}</strong>. Other groups will ask you before "
                        + $"publishing events there.</p><p><a href=\"/organizations/{claim.OrganizationId}/venue\">Open your venue</a></p>",
                        [claim.ClaimantAppUserId], claim.ClaimantAppUserId, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger.LogError(e, "Could not settle venue claim {ClaimId}.", claimId);
                db.ChangeTracker.Clear();
            }
        }
    }
}
