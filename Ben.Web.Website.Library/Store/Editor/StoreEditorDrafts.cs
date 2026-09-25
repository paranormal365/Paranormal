using Ben.Data.Common.Enums;
using Ben.Service.Models.Store;

namespace Ben.Web.Website.Library.Store.Editor;

// What an item's editors hold while somebody types (store sellers, backlog 251, P3): the admin's
// editor and the seller's share these, and the components under Store/Editor that edit them, so
// the two cannot drift in how a line of specifications or an option is kept.

/// <summary>One specification line as typed: a heading, a name and a value.</summary>
public sealed class StoreSpecLine
{
    public string Group { get; set; } = "";
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class StoreOptionDraft
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = "";
    public StoreOptionKind Kind { get; set; } = StoreOptionKind.Pill;
    public List<StoreOptionValueDraft> Values { get; set; } = [];
}

public sealed class StoreOptionValueDraft
{
    public Guid? Id { get; set; }
    public string Value { get; set; } = "";
    public string? SwatchHex { get; set; } = "#000000";
    public bool IsActive { get; set; } = true;
}

/// <summary>A variant row as typed. A seller's editor never sends <see cref="Price"/>.</summary>
public sealed class StoreVariantDraft
{
    public string Sku { get; set; } = "";
    public decimal Price { get; set; }
    public decimal? CompareAtPrice { get; set; }
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
}

public sealed class StorePictureDraft
{
    public string? AltText { get; set; }
    public Guid? VariantId { get; set; }
}

/// <summary>Turning a saved item into drafts, and drafts back into what a save sends.</summary>
public static class StoreEditorDrafts
{
    public static List<StoreSpecLine> SpecLines(StoreProductAdminRecord p)
        => p.Specs.SelectMany(g => g.Items.Select(i => new StoreSpecLine { Group = g.GroupName, Name = i.Name, Value = i.Value })).ToList();

    /// <summary>The specification lines as the page groups them — the save and the preview share it.</summary>
    public static List<StoreSpecGroup> SpecGroups(IEnumerable<StoreSpecLine> lines) => lines
        .Where(s => !string.IsNullOrWhiteSpace(s.Name) || !string.IsNullOrWhiteSpace(s.Value))
        .GroupBy(s => s.Group.Trim())
        .Select(g => new StoreSpecGroup(g.Key, g.Select(s => new StoreSpecRecord(s.Name, s.Value)).ToList()))
        .ToList();

    public static List<StoreOptionDraft> Options(StoreProductAdminRecord p) => p.Options.Select(o => new StoreOptionDraft
    {
        Id = o.Id, Name = o.Name, Kind = o.Kind,
        Values = o.Values.Select(v => new StoreOptionValueDraft { Id = v.Id, Value = v.Value, SwatchHex = v.SwatchHex ?? "#000000", IsActive = v.IsActive }).ToList(),
    }).ToList();

    public static SaveStoreOptionsRequest OptionsRequest(IEnumerable<StoreOptionDraft> options, DateTime? expectedDateUpdated) => new(
        options.Select(o => new SaveStoreOptionRequest(o.Id, o.Name, o.Kind,
            o.Values.Select(v => new SaveStoreOptionValueRequest(v.Id, v.Value, o.Kind == StoreOptionKind.Swatch ? v.SwatchHex : null, v.IsActive)).ToList())).ToList(),
        expectedDateUpdated);

    public static StoreVariantDraft Variant(StoreVariantAdminRecord v)
        => new() { Sku = v.Sku, Price = v.Price, CompareAtPrice = v.CompareAtPrice, IsActive = v.IsActive, IsDefault = v.IsDefault };

    public static StorePictureDraft Picture(StoreImageRecord i) => new() { AltText = i.AltText, VariantId = i.VariantId };
}
