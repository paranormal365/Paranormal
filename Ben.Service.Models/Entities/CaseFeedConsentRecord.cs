namespace Ben.Service.Models.Entities;

/// <summary>
/// One recorded agreement to publish a private-engagement case's footage to the feed.
/// </summary>
/// <remarks>
/// <para><b>Why this record exists.</b> <c>FeedPostConsent</c> is append-only and its entity doc
/// states its purpose plainly: "When a client asks 'who put this footage up', this row is the
/// answer." The 2026-09-17 audit found the whole table write-only — the only other references in
/// the tree were two purges — so the record was correct, outlived the post on purpose, and could
/// not be read by anybody.</para>
///
/// <para><b><see cref="PostExists"/> is part of the answer, not a filter.</b> The consent survives
/// the post being hidden or deleted, because what the table remembers is that somebody agreed.
/// "Somebody agreed and then took it down" and "nobody ever agreed" are different facts and a
/// client asking deserves whichever is true.</para>
///
/// <para><see cref="WordingVersion"/> is which wording they ticked. Version 1 is the item 186 F7
/// dialog; a future rewording bumps it, so "what exactly did they agree to" always has an
/// answer.</para>
/// </remarks>
public sealed record CaseFeedConsentRecord(
    Guid Id,
    Guid AgreedByAppUserId,
    string AgreedByDisplayName,
    DateTime AgreedUtc,
    int WordingVersion,
    Guid? OrgMessageId,
    bool PostExists);
