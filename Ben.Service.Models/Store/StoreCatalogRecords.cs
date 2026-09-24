using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Store;

// Catalogue shapes the admin screens and the public store both draw (storefront S1.2).

/// <summary>One product picture. <paramref name="VariantId"/> set = shown when that variant is chosen.</summary>
public sealed record StoreImageRecord(
    Guid Id, Guid UploadFileId, string? AltText, int SortOrder, Guid? VariantId, int Width, int Height);

public sealed record StoreOptionValueRecord(Guid Id, string Value, string? SwatchHex, int SortOrder, bool IsActive);

/// <summary>An option a buyer chooses — "Colour", "Size" — with its values in order.</summary>
public sealed record StoreOptionRecord(Guid Id, string Name, StoreOptionKind Kind, IReadOnlyList<StoreOptionValueRecord> Values);

public sealed record StoreSpecRecord(string Name, string Value);

/// <summary>A heading on the product page's specification table — "Detection", "Power".</summary>
public sealed record StoreSpecGroup(string GroupName, IReadOnlyList<StoreSpecRecord> Items);
