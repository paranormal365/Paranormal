using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

// ── Backlog item #55 — Equipment inventory & checkout tracking ────────────────
// DTOs shared between Ben.Data.WebApi and Ben.Web.Library (both reference this project),
// avoiding the hand-mirrored-record drift risk that BenAdminClientAdapter's HTTP-only slices
// carry. Grouped by area; more records are added here in later phases (sharing, org-owned,
// checkout lifecycle, condition photos/history) rather than a new file per phase.

// ── Catalog (Phase 1) ───────────────────────────────────────────────────────

public sealed record EquipmentCategoryRecord(
    Guid Id,
    string Name,
    string? Description,
    string? IconClass,
    int SortOrder,
    bool IsActive);

public sealed record EquipmentBrandRecord(
    Guid Id,
    string Name,
    bool IsApproved,
    Guid? ProposedByOrganizationId,
    Guid? ProposedByAppUserId,
    DateTime DateCreated);

public sealed record EquipmentModelRecord(
    Guid Id,
    Guid EquipmentBrandId,
    string BrandName,
    Guid EquipmentCategoryId,
    string CategoryName,
    string Name,
    string? ModelNumber,
    string? Description,
    bool IsApproved,
    Guid? ProposedByOrganizationId,
    Guid? ProposedByAppUserId,
    DateTime DateCreated);

public sealed record UpsertEquipmentBrandRequest(string Name);

public sealed record UpsertEquipmentModelRequest(
    Guid EquipmentBrandId,
    Guid EquipmentCategoryId,
    string Name,
    string? ModelNumber,
    string? Description);

public sealed record UpsertEquipmentCategoryRequest(
    string Name,
    string? Description,
    string? IconClass,
    int SortOrder,
    bool IsActive);

// ── Items (Phase 1) ─────────────────────────────────────────────────────────

/// <summary>
/// The server's verdict on what the current caller may do with an <see cref="EquipmentItemRecord"/>.
/// Render as given — never re-derive from whether a call succeeded. Missing/unset means false,
/// per the platform's "a permission gap should close, not open" convention.
/// </summary>
public sealed record EquipmentItemFlags(
    bool IsOwner,
    bool CanEdit,
    bool CanDelete,
    bool CanManageSharing,
    bool CanSeeSerial,
    bool CanRequestCheckout,
    bool CanManageServiceLog)
{
    public static readonly EquipmentItemFlags None = new(false, false, false, false, false, false, false);
}

public sealed record EquipmentItemPhotoRecord(
    Guid Id,
    Guid EquipmentItemId,
    Guid UploadFileId,
    bool IsPrimary,
    string? Caption,
    int SortOrder);

public sealed record EquipmentItemRecord(
    Guid Id,
    Guid? OwnerAppUserId,
    string? OwnerDisplayName,
    Guid? OwningOrganizationId,
    string? OwningOrganizationName,
    Guid EquipmentModelId,
    string ModelName,
    string BrandName,
    string CategoryName,
    string DisplayName,
    /// <summary>Null unless <see cref="Flags"/>.CanSeeSerial is true — resolved server-side.</summary>
    string? SerialNumber,
    DateTime? AcquisitionDate,
    string? Notes,
    bool IsRetired,
    bool IncludeInGlobalCatalog,
    EquipmentLoanAudience LoanAudience,
    Guid? CurrentHolderAppUserId,
    string? CurrentHolderDisplayName,
    DateTime? LastServicedDate,
    string? DefectNotes,
    IReadOnlyList<EquipmentItemPhotoRecord> Photos,
    EquipmentItemFlags Flags);

public sealed record UpsertEquipmentItemRequest(
    Guid EquipmentModelId,
    string DisplayName,
    string? SerialNumber,
    DateTime? AcquisitionDate,
    string? Notes,
    bool IncludeInGlobalCatalog = false,
    EquipmentLoanAudience LoanAudience = EquipmentLoanAudience.NotLoanable);

// ── Condition photos, renewals, history (Phase 5) ───────────────────────────

/// <summary>One condition photo attached to a loan, at one end of it.</summary>
public sealed record EquipmentCheckoutPhotoRecord(
    Guid Id,
    Guid EquipmentCheckoutId,
    Guid UploadFileId,
    EquipmentPhotoStage Stage,
    string? Caption,
    DateTime DateCreated,
    Guid CreatedByAppUserId,
    string? CreatedByDisplayName);

/// <summary>One request for more time on a loan, and what was said about it.</summary>
public sealed record EquipmentCheckoutRenewalRecord(
    Guid Id,
    Guid EquipmentCheckoutId,
    DateTime RequestedDateDue,
    EquipmentRenewalStatus Status,
    string? RequestNotes,
    string? ReviewNotes,
    Guid? ReviewedByAppUserId,
    string? ReviewedByDisplayName,
    DateTime? DateReviewed,
    DateTime DateCreated,
    bool CanReview,
    bool CanCancel);

/// <summary>Asks for more time on a loan that is already out.</summary>
public sealed record RequestEquipmentRenewalRequest(DateTime RequestedDateDue, string? RequestNotes);

/// <summary>Decides a renewal. A reason is required to refuse one.</summary>
public sealed record ReviewEquipmentRenewalRequest(bool Approve, string? ReviewNotes);

/// <summary>What kind of thing happened to a piece of equipment.</summary>
public enum EquipmentHistoryKind
{
    Loan = 1,
    Renewal = 2,
    Service = 3,
    Defect = 4,
}

/// <summary>
/// One entry in a piece of equipment's combined history — loans, renewals, service and defects
/// merged into a single chronological account.
/// </summary>
/// <remarks>
/// Deliberately flat and pre-described: the server writes the sentence, so every surface showing a
/// history says the same thing about the same event. Carries no serial number, because history is
/// visible to people the serial is not.
/// </remarks>
public sealed record EquipmentHistoryEntryRecord(
    DateTime DateUtc,
    EquipmentHistoryKind Kind,
    string Summary,
    string? ActorDisplayName,
    Guid? CheckoutId,
    int PhotoCount);

// ── Checkouts (Phase 4) ─────────────────────────────────────────────────────

/// <summary>
/// One loan, as whichever party is looking at it sees it.
/// </summary>
/// <remarks>
/// <c>IsOverdue</c> is computed server-side rather than left to the client: it depends on "now",
/// and a borrower whose clock is wrong should not see a different answer from the lender's.
/// </remarks>
public sealed record EquipmentCheckoutRecord(
    Guid Id,
    Guid EquipmentItemId,
    string ItemDisplayName,
    string BrandName,
    string ModelName,
    Guid? ItemOwnerAppUserId,
    string? ItemOwnerDisplayName,
    Guid? ItemOwningOrganizationId,
    Guid BorrowerAppUserId,
    string? BorrowerDisplayName,
    Guid? BorrowedForOrganizationId,
    string? BorrowedForOrganizationName,
    Guid? InvestigationId,
    string? InvestigationTitle,
    EquipmentCheckoutStatus Status,
    bool IsOverdue,
    string? RequestNotes,
    string? ReviewNotes,
    Guid? ReviewedByAppUserId,
    string? ReviewedByDisplayName,
    DateTime? DateReviewed,
    DateTime? DateNeededFrom,
    DateTime? DateDue,
    DateTime? DateCheckedOut,
    DateTime? DateReturned,
    string? ReturnConditionNotes,
    DateTime DateCreated,
    EquipmentCheckoutFlags Flags);

/// <summary>What the viewer may do with this loan right now. Rendered, never re-derived.</summary>
public sealed record EquipmentCheckoutFlags(
    bool IsBorrower,
    bool IsApprover,
    bool CanCancel,
    bool CanApprove,
    bool CanDeny,
    bool CanConfirmHandoff,
    bool CanReceiveReturn);

/// <summary>
/// A group the caller could borrow a given item for — or, with a null id, borrowing it personally.
/// </summary>
/// <remarks>
/// Personal borrowing is offered as an option in the same list rather than a separate control,
/// because from the borrower's side "who am I borrowing this for?" is one question with several
/// answers, one of which is "myself".
/// </remarks>
public sealed record BorrowOptionRecord(Guid? OrganizationId, string Label);

/// <summary>
/// Whether the caller may ask to borrow an item, and on whose behalf they could.
/// </summary>
/// <remarks>
/// Returned by the server so the request form never has to work out the loan-audience rules for
/// itself. An empty <c>Options</c> with <c>CanRequest</c> false is the ordinary answer for gear that
/// is visible but not lent out.
/// </remarks>
public sealed record BorrowEligibilityRecord(
    Guid EquipmentItemId,
    bool CanRequest,
    string? Reason,
    IReadOnlyList<BorrowOptionRecord> Options,
    /// <summary>True when somebody already has this piece, or is about to collect it.</summary>
    bool IsCurrentlyOut = false,
    /// <summary>When it is expected back, if a due date was set.</summary>
    DateTime? ExpectedBackOn = null);

/// <summary>Asks to borrow a piece of equipment.</summary>
public sealed record RequestEquipmentCheckoutRequest(
    Guid EquipmentItemId,
    Guid? BorrowedForOrganizationId,
    Guid? InvestigationId,
    DateTime? DateNeededFrom,
    string? RequestNotes);

/// <summary>Approves a request, optionally setting when the gear is due back.</summary>
public sealed record ApproveEquipmentCheckoutRequest(DateTime? DateDue, string? ReviewNotes);

/// <summary>Turns a request down. A reason is required.</summary>
public sealed record DenyEquipmentCheckoutRequest(string ReviewNotes);

/// <summary>Records the gear coming back, with any note on its condition.</summary>
public sealed record ReturnEquipmentCheckoutRequest(string? ReturnConditionNotes);

// ── Group-owned equipment and the service log (Phase 3) ─────────────────────

/// <summary>
/// The group's own equipment, plus whether this caller may add to it.
/// </summary>
/// <remarks>
/// The list is wrapped rather than returned bare because <c>CanManage</c> cannot be inferred from
/// the rows: a group with no equipment yet has no row to carry a flag, and deriving the verdict
/// from "are there any editable items" would leave the first piece impossible to add. The server
/// knows the answer whether or not anything exists, so it says so.
/// </remarks>
public sealed record OrgEquipmentListRecord(
    bool CanManage,
    IReadOnlyList<EquipmentItemRecord> Items);

/// <summary>Creates or edits a piece of the group's own equipment.</summary>
public sealed record UpsertOrgEquipmentItemRequest(
    Guid EquipmentModelId,
    string DisplayName,
    string? SerialNumber,
    DateTime? AcquisitionDate,
    string? Notes,
    bool IncludeInGlobalCatalog = false);

/// <summary>Sets (or clears, with null) who is currently holding a piece of group gear.</summary>
public sealed record SetEquipmentHolderRequest(Guid? AppUserId);

/// <summary>One entry in a piece of equipment's service and defect history.</summary>
public sealed record EquipmentServiceLogRecord(
    Guid Id,
    Guid EquipmentItemId,
    EquipmentServiceLogType EntryType,
    DateTime EntryDate,
    string Notes,
    Guid? PerformedByAppUserId,
    string? PerformedByDisplayName,
    DateTime DateCreated,
    Guid CreatedByAppUserId,
    string? CreatedByDisplayName);

/// <summary>
/// Adds a service-log entry. The entry type drives a side effect on the item itself, in the same
/// save: a reported defect becomes the item's current defect note, a resolved one clears it, and a
/// service entry moves its last-serviced date.
/// </summary>
public sealed record AddEquipmentServiceLogRequest(
    EquipmentServiceLogType EntryType,
    DateTime EntryDate,
    string Notes,
    Guid? PerformedByAppUserId);

// ── Sharing (Phase 2) ───────────────────────────────────────────────────────

/// <summary>
/// One group the owner could share an item with, and whether it currently is. Returned as a whole
/// list so the sharing editor needs a single call: the owner's groups and the item's shares are the
/// same question asked from two sides.
/// </summary>
public sealed record EquipmentShareOptionRecord(
    Guid OrganizationId,
    string OrganizationName,
    bool IsShared);

/// <summary>Replaces an item's shares wholesale. Any group not listed is unshared.</summary>
public sealed record SetEquipmentSharesRequest(IReadOnlyList<Guid> OrganizationIds);

/// <summary>
/// Shares or unshares every one of the caller's non-retired items with one group at once — the
/// "share all my gear with this group" convenience. Still writes per-item rows, so a single piece
/// can be excluded afterwards without unpicking anything.
/// </summary>
public sealed record BulkEquipmentShareRequest(Guid OrganizationId, bool Share);

/// <summary>What a bulk share/unshare actually changed, so the UI can say so plainly.</summary>
public sealed record BulkEquipmentShareResult(int ItemsAffected, int TotalItems);

/// <summary>
/// A member's gear as seen by another member of a group it is shared with. Owner name is present —
/// that is the point of sharing — but there is no serial number property on the shape at all: the
/// serial stays with the owner even here, and a projection that cannot carry it cannot leak it.
/// </summary>
public sealed record SharedEquipmentItemRecord(
    Guid Id,
    Guid OwnerAppUserId,
    string? OwnerDisplayName,
    string DisplayName,
    string BrandName,
    string ModelName,
    string CategoryName,
    string? Notes,
    EquipmentLoanAudience LoanAudience,
    bool IsRetired,
    IReadOnlyList<EquipmentItemPhotoRecord> Photos);

/// <summary>
/// One publicly-listed item as an anonymous visitor sees it. Deliberately not
/// <see cref="EquipmentItemRecord"/>: there is no owner id, no owner name, no serial and no
/// permission flags on this shape at all, so a public projection cannot leak them by omission of a
/// check somewhere downstream.
/// </summary>
public sealed record PublicEquipmentItemRecord(
    Guid Id,
    string DisplayName,
    string BrandName,
    string ModelName,
    string CategoryName,
    DateTime? AcquisitionDate,
    string? Notes,
    EquipmentLoanAudience LoanAudience,
    IReadOnlyList<EquipmentItemPhotoRecord> Photos);
