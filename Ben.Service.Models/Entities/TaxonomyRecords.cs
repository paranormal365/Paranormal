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
/// What came back from proposing a name: the entry, or the near-misses that stopped it.
/// </summary>
/// <remarks>
/// <para>Exactly one side is set. The server answers a probable typo with a 409 carrying the
/// suggestions, and a client that only knows "did it work" throws that away — which is how the
/// first version of this shipped: the check fired, the person saw "could not be added", and the
/// word they wanted was simply unreachable. Worse than not having the check at all.</para>
///
/// <para><c>Failed</c> is the third state, and it is neither of the other two: no entry, no
/// suggestions, because the call was refused or never arrived.</para>
/// </remarks>
public sealed record TaxonomyProposal<T>(T? Created, ProbableDuplicateResponse? DidYouMean)
    where T : class
{
    /// <summary>Nothing was created and there is nothing to suggest — the call did not succeed.</summary>
    public bool Failed => Created is null && DidYouMean is null;
}

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
