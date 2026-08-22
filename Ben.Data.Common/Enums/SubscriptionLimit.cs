namespace Ben.Data.Common.Enums;

/// <summary>
/// A thing a subscription band can cap. The key half of a keyed limit.
/// </summary>
/// <remarks>
/// <para><b>Why an enum and a row rather than columns on the tier.</b> Ben's list — equipment,
/// equipment loans, open cases — is explicitly a starting point, not a specification. Columns would
/// mean a migration and a price-list screen change for every idea anybody has; a keyed row means a
/// new cap is a value here, a row a SuperAdmin types, and one call at the place it applies.</para>
///
/// <para><b>Numbered explicitly and never renumbered.</b> These end up in rows that outlive the
/// deployment that wrote them, and a reordered enum silently turns an equipment cap into a case
/// cap — which would read as a bug in whatever feature noticed first.</para>
///
/// <para><b>What belongs here.</b> A countable thing a group accumulates, where the count is cheap
/// to take and refusing the next one is a sentence somebody can act on. Member count deliberately
/// does <b>not</b> belong: it decides which band you are on rather than being capped inside a band,
/// and modelling it twice would let the two disagree.</para>
/// </remarks>
public enum SubscriptionLimit
{
    /// <summary>Cases that are open at one time. Closed and archived cases do not count.</summary>
    /// <remarks>
    /// A cap on <i>concurrent</i> work rather than total history. Capping the total would mean a
    /// group's own past eventually locks them out, and asking somebody to delete last year's
    /// investigation to start this year's is not a subscription prompt, it is data loss.
    /// </remarks>
    OpenCases = 1,

    /// <summary>Pieces of equipment on the group's books.</summary>
    EquipmentItems = 2,

    /// <summary>Equipment loans out at one time.</summary>
    /// <remarks>
    /// Separate from <see cref="EquipmentItems"/> because lending is the part that costs the
    /// platform something to coordinate, and a group with ten items lending nine of them is doing
    /// more with the feature than a group with fifty sitting in a cupboard.
    /// </remarks>
    ActiveEquipmentLoans = 3,

    /// <summary>Investigations open at one time.</summary>
    OpenInvestigations = 4,

    /// <summary>People invited to a group but not yet accepted.</summary>
    /// <remarks>
    /// Not a monetisation lever so much as an abuse one — an unbounded invite list is a way to send
    /// mail through the platform. It sits here because the mechanism is identical.
    /// </remarks>
    PendingInvites = 5,

    /// <summary>Total uploaded file storage, in megabytes.</summary>
    /// <remarks>
    /// The one cap here that is a real cost rather than a packaging decision, and the one most
    /// likely to need a different band shape than the rest.
    /// </remarks>
    StorageMegabytes = 6,

    /// <summary>Public pages the group may publish.</summary>
    PublishedPages = 7,

    /// <summary>Custom roles the group can define for itself.</summary>
    /// <remarks>
    /// In the enum because Ben raised it; probably best left unset on every band. Ben's own
    /// principle for these — maximise earnings without turning people off — cuts against it:
    /// caps on <i>scale</i> (storage, cases, equipment) charge groups that are getting value,
    /// while caps on <i>organising yourselves</i> read as petty and cost goodwill for pennies.
    /// A row nobody writes is exactly what the no-row-no-cap default is for.
    /// </remarks>
    CustomRoles = 8,
}
