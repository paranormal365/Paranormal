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
    string? Notes);
