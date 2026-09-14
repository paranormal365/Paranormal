namespace Ben.Data.Common.Enums;

/// <summary>What the person claiming a venue says they are to it (item 235 phase 9). Append only.</summary>
/// <remarks>
/// Ben, 2026-09-13: the claim is completed by verifying who is in charge of the venue, "or a rep of
/// the venue". Recorded so the person reviewing a claim knows whether they are hearing from the
/// owner, the manager, or somebody acting for them, and can ask the right question.
/// </remarks>
public enum VenueClaimantRole
{
    Owner = 0,
    Manager = 1,
    Representative = 2,
}
