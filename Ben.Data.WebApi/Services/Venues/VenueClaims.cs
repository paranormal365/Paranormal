using System.Security.Cryptography;
using System.Text;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Venues;

/// <summary>
/// The rules for proving a group runs a place (item 235 phase 9).
/// </summary>
/// <remarks>
/// <para><b>Ben, 2026-09-13:</b> "Code validation is okay as long as we verify we have the right
/// people." A code proves somebody reads an inbox. Whether that inbox is the venue's is the part that
/// has to be got right, and it is got right by where the address came from, not by the code.</para>
///
/// <para><b>So a code may only go to an address the claimant did not supply.</b> A public email
/// recorded for the place by another group, or by the site, and on the record for a week before the
/// claim — long enough that a claimant cannot have a friend's group type their own address in the
/// afternoon they claim. An address anybody in the claiming group added proves nothing and is not
/// offered.</para>
///
/// <para><b>And a proved claim still waits a week</b>, during which every group that knows the place
/// is told and may object. An objection sends it to a person. Without an address to prove it by,
/// a person reviews what the claimant sends from the start.</para>
/// </remarks>
public static class VenueClaims
{
    /// <summary>How long a public address has to have been on the record before it may prove a claim.</summary>
    public static readonly TimeSpan ContactMinimumAge = TimeSpan.FromDays(7);

    /// <summary>How long a proved claim stands open to objection before it takes effect.</summary>
    public static readonly TimeSpan ObjectionWindow = TimeSpan.FromDays(7);

    /// <summary>How long a code works.</summary>
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromHours(24);

    /// <summary>Wrong guesses allowed before a code stops working.</summary>
    public const int MaxAttempts = 5;

    /// <summary>The states in which a claim is still open.</summary>
    public static readonly VenueClaimState[] Open =
        [VenueClaimState.Pending, VenueClaimState.Proved, VenueClaimState.Contested];

    /// <summary>
    /// The public email addresses on the record for this place that may prove this group's claim.
    /// </summary>
    public static async Task<List<PlaceContact>> ProvingContactsAsync(
        BenDataContext db, Guid placeId, Guid claimantOrgId, DateTime now, CancellationToken ct)
    {
        var members = db.OrganizationUserMemberships
            .Where(m => m.OrganizationId == claimantOrgId)
            .Select(m => m.AppUserId);

        var oldEnough = now - ContactMinimumAge;

        return await db.PlaceContacts.AsNoTracking()
            .Where(c => c.PlaceId == placeId
                     && c.Kind == PlaceContactKind.Email
                     && c.IsPublic
                     && c.OrganizationId != claimantOrgId
                     && !members.Contains(c.CreatedByAppUserId)
                     && c.DateCreated <= oldEnough)
            .OrderBy(c => c.DateCreated)
            .ToListAsync(ct);
    }

    /// <summary>"j•••@thomashousehotel.com" — enough to recognise, not enough to harvest.</summary>
    public static string Mask(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0) return "•••";
        return $"{email[0]}•••{email[at..]}";
    }

    /// <summary>A six-digit code, and its hash to store.</summary>
    public static (string Code, string Hash) NewCode()
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        return (code, Hash(code));
    }

    public static string Hash(string code)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim())));

    /// <summary>
    /// Why this code does not prove the claim, or null when it does. Counts the attempt either way.
    /// </summary>
    public static string? Check(VenuePlaceClaim claim, string? code, DateTime now)
    {
        if (claim.State != VenueClaimState.Pending || claim.CodeHash is null)
            return "There is no code waiting on this claim.";

        if (claim.CodeSentUtc is not { } sent || now - sent > CodeLifetime)
            return "That code has expired. Send a new one.";

        if (claim.CodeAttempts >= MaxAttempts)
            return "Too many wrong codes. Send a new one.";

        claim.CodeAttempts++;

        if (string.IsNullOrWhiteSpace(code) || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(Hash(code)), Encoding.UTF8.GetBytes(claim.CodeHash)))
            return claim.CodeAttempts >= MaxAttempts
                ? "That isn't the code, and that was the last try. Send a new one."
                : "That isn't the code. Check the email and try again.";

        return null;
    }

    /// <summary>
    /// Makes the claiming group the venue at the place. The caller saves.
    /// </summary>
    /// <returns>A refusal when another group is already the venue there.</returns>
    public static async Task<string?> ApproveAsync(
        BenDataContext db, VenuePlaceClaim claim, Guid? actorId, string? note, DateTime now, CancellationToken ct)
    {
        var other = await db.OrganizationVenueProfiles
            .Include(v => v.Organization)
            .FirstOrDefaultAsync(v => v.PlaceId == claim.PlaceId && v.VerifiedUtc != null
                                   && v.OrganizationId != claim.OrganizationId, ct);
        if (other is not null)
            return $"{other.Organization.Name} is already confirmed as the venue there. Remove that first if it is wrong.";

        var profile = await db.OrganizationVenueProfiles
            .FirstOrDefaultAsync(v => v.PlaceId == claim.PlaceId && v.OrganizationId == claim.OrganizationId, ct);

        var by = actorId ?? claim.ClaimantAppUserId;
        if (profile is null)
        {
            profile = new OrganizationVenueProfile
            {
                Id = Guid.NewGuid(), OrganizationId = claim.OrganizationId, PlaceId = claim.PlaceId,
                DateCreated = now, CreatedByAppUserId = by,
            };
            db.OrganizationVenueProfiles.Add(profile);
        }

        profile.VerifiedUtc ??= now;
        profile.DateUpdated = now;
        profile.UpdatedByAppUserId = by;

        claim.State = VenueClaimState.Approved;
        claim.DecidedUtc = now;
        claim.DecidedByAppUserId = actorId;
        claim.DecisionNote = note;
        claim.DateUpdated = now;
        claim.UpdatedByAppUserId = by;

        return null;
    }

    /// <summary>
    /// The groups that know this place and should hear that somebody claims to run it: any that hold
    /// events, name rooms, keep contact details, or have investigated or taken a case there.
    /// </summary>
    public static async Task<List<Guid>> InterestedGroupsAsync(
        BenDataContext db, Guid placeId, Guid exceptOrgId, CancellationToken ct)
    {
        var ids = new HashSet<Guid>();
        ids.UnionWith(await db.HostedEvents.Where(e => e.PlaceId == placeId).Select(e => e.OrganizationId).ToListAsync(ct));
        ids.UnionWith(await db.PlaceRooms.Where(r => r.PlaceId == placeId).Select(r => r.OrganizationId).ToListAsync(ct));
        ids.UnionWith(await db.PlaceContacts.Where(c => c.PlaceId == placeId && c.OrganizationId != null)
            .Select(c => c.OrganizationId!.Value).ToListAsync(ct));
        ids.UnionWith(await db.Investigations.Where(i => i.PlaceId == placeId).Select(i => i.OrganizationId).ToListAsync(ct));
        ids.UnionWith(await db.Cases.Where(c => c.PlaceId == placeId).Select(c => c.OrganizationId).ToListAsync(ct));
        ids.Remove(exceptOrgId);
        return [.. ids];
    }
}
