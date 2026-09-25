using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Who may write in the feed, as opposed to read it (item 186 F2).
/// </summary>
/// <remarks>
/// <para><b>Ben's rule: anyone scrolls, people who belong here post.</b> The open scroll is the
/// hook; a voice in it is what belonging buys. That makes the feed the recruitment funnel rather
/// than a second forum — somebody who wants to join the conversation has a reason to join a group,
/// which is the thing the whole site is for.</para>
///
/// <para><b>Belonging is two doors, not one.</b> An active member of any group, at any role —
/// including a Viewer seat, because a Viewer is somebody a group deliberately let in — and a
/// <b>client</b>, whose case is being worked. Ben chose to include clients: the family whose house
/// is being investigated has more standing to describe what happens there than most, and telling
/// them the site's conversation is not for them would be an odd thanks for their trust.</para>
///
/// <para><b>Reporting is deliberately NOT gated.</b> Safety must not require belonging: if a
/// signed-in stranger is the first to see something that should not be there, we want to hear it.
/// </para>
///
/// <para>Sentence-or-null, the house shape — and every caller must render the sentence, or the
/// refusal is one the UI silently discards.</para>
/// </remarks>
public static class FeedParticipation
{
    /// <summary>
    /// The reason this person may not write in the feed, or null when they may.
    /// </summary>
    /// <param name="db">Context.</param>
    /// <param name="userId">The signed-in reader. Guid.Empty never reaches here — the endpoints
    /// carry <c>[Authorize]</c>, so an anonymous caller is refused before this is asked.</param>
    /// <param name="ct">Cancellation.</param>
    public static async Task<string?> RefusalAsync(
        BenDataContext db, Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty)
            return "Sign in to post on the feed.";

        var belongs = await db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.AppUserId == userId && m.IsActive, ct);

        // Two ways to be a client: the person who made the request the case came from, and anybody
        // the case was later shared with (a spouse, an adult child at the same address). Both are
        // clients in every sense that matters here.
        if (!belongs)
            belongs = await db.Cases.AsNoTracking()
                .AnyAsync(c => c.ClientRequest != null && c.ClientRequest.AppUserId == userId, ct);

        if (!belongs)
            belongs = await db.CaseClientAccesses.AsNoTracking()
                .AnyAsync(a => a.AppUserId == userId, ct);

        return belongs
            ? null
            : "Posting on the feed is for people who belong here — members of an investigation "
            + "group, and clients whose case is being worked. Join a group, start your own, or "
            + "request an investigation, and the feed is yours too.";
    }

    /// <summary>
    /// The reason this person may not post about a PLACE, or null when they may.
    /// </summary>
    /// <remarks>
    /// <para><b>Signed in is enough, and that is deliberately wider than the feed's front page</b>
    /// (Ben confirmed, 2026-09-17). The two doors answer differently because they are for different
    /// things. The front page is the site's conversation, and having a voice in it is what belonging
    /// buys — that is what makes the feed a recruitment funnel rather than a second forum. A public
    /// location's page is the opposite errand: it is the free lane's whole purpose, a shared record
    /// of somewhere anyone may visit, and the person with no group is exactly who fills it. Telling
    /// the visitor who went on the evening tour and photographed something that their photograph is
    /// not welcome would be turning away the contribution the archive exists to collect.</para>
    ///
    /// <para>Both rules live here, next to each other, so the difference is stated once and on
    /// purpose rather than discovered later as an inconsistency.</para>
    ///
    /// <para><b>What still refuses:</b> not being signed in — a post needs a name attached to it —
    /// and the place not being a public location, which the write door checks because only it knows
    /// which place is meant.</para>
    /// </remarks>
    public static string? PlaceRefusal(Guid userId)
        => userId == Guid.Empty
            ? "Sign in to post about a place."
            : null;
}
