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
