namespace Ben.Service.Models.Entities;

// ── Shared vocabulary maintenance ────────────────────────────────────────────
// These describe what happens when a user-grown taxonomy meets a name that is nearly, or exactly,
// one that already exists. Both the equipment catalog (item #55) and the experience taxonomy grow
// by proposal rather than by decree, so both hit the same two moments: somebody typing a name that
// is probably a slip, and somebody renaming a row onto a name already taken.
//
// They live here rather than beside either taxonomy because a record named TaxonomyMergeOffer
// sitting in EquipmentRecords.cs while serving experience types is the kind of small lie that
// makes a codebase harder to read than it needs to be.

/// <summary>
/// "Did you mean one of these?" — returned instead of creating a probable duplicate.
/// </summary>
public sealed record ProbableDuplicateResponse(string ProposedName, IReadOnlyList<string> DidYouMean);

/// <summary>
/// A rename that turned out to be a merge, and what merging would mean.
/// </summary>
/// <remarks>
/// Returned rather than performed. Renaming onto an existing name makes two entries into one and
/// changes what somebody's records say — what make their equipment is, or what an occurrence was
/// tagged as. That is a large thing to have happen because a name was typed, so it is offered and
/// confirmed rather than done.
/// </remarks>
public sealed record TaxonomyMergeOffer(
    Guid SourceId,
    string SourceName,
    Guid TargetId,
    string TargetName,
    string Message);
