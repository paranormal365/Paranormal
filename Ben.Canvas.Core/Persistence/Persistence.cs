using System.Text.Json;
using System.Text.Json.Serialization;
using Ben.Canvas.Core.Formatting;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Options;
using Ben.Canvas.Core.Text;

namespace Ben.Canvas.Core.Persistence;

/// <summary>Whether closing the tab should ask first.</summary>
/// <remarks>
/// Copied from the video editor. Two things are worth stopping for: edits not yet stored, and a publish
/// that lives only in this tab. A clean board sitting idle must never trigger it, because a browser that
/// warns every time gets its warning ignored.
/// </remarks>
public static class UnloadGuardPolicy
{
    public static bool ShouldGuard(bool hasUnsavedChanges, bool autosavePending, bool publishRunning) =>
        hasUnsavedChanges || autosavePending || publishRunning;

    public static string Reason(bool hasUnsavedChanges, bool publishRunning) => publishRunning
        ? CanvasCopy.Sentences.PublishRunning
        : hasUnsavedChanges
            ? CanvasCopy.Sentences.Unsaved
            : CanvasCopy.Sentences.NotFinishedSaving;
}

/// <summary>Works out which stored pictures and files nothing refers to any more.</summary>
/// <remarks>
/// Copied from the video editor's OpfsGarbageCollector. The rule is reconciliation, not reference counting:
/// a file is deletable only when no stored board, nothing open, and nothing in undo history refers to it.
/// </remarks>
public static class AssetGarbageCollector
{
    public static IReadOnlyList<Guid> FindOrphans(IEnumerable<Guid> storedAssetIds, IEnumerable<Guid> referencedAssetIds)
    {
        ArgumentNullException.ThrowIfNull(storedAssetIds);
        ArgumentNullException.ThrowIfNull(referencedAssetIds);
        var referenced = referencedAssetIds.ToHashSet();
        return storedAssetIds.Distinct().Where(id => !referenced.Contains(id)).OrderBy(id => id).ToList();
    }

    /// <summary>
    /// Whether it is safe to sweep at all. A board list that could not be read makes every file look
    /// unreferenced; a failure to read is not evidence of absence.
    /// </summary>
    public static bool CanSweep(bool indexWasRead, int knownDocumentCount, int storedFileCount)
    {
        if (!indexWasRead) return false;
        return knownDocumentCount > 0 || storedFileCount == 0;
    }
}

/// <summary>Which stored assets a board or a block's data names.</summary>
public static class AssetReferences
{
    public static IEnumerable<Guid> ReferencedAssetIds(this CanvasDocument document) =>
        document.Nodes.Select(n => n.Data).SelectMany(AssetIdsOf).Distinct();

    public static IEnumerable<Guid> AssetIdsOf(NodeData? data)
    {
        switch (data)
        {
            case ImageData { AssetId: { } image }:
                yield return image;
                break;
            case FileData { AssetId: { } file }:
                yield return file;
                break;
            case LinkData { ImageAssetId: { } link }:
                yield return link;
                break;
        }
    }
}

/// <summary>One board stored on this device, as the board list shows it.</summary>
/// <remarks>
/// The device key and the server id are separate fields on purpose. The video editor once used one field
/// for both, and a board opened from the server then overwrote an unrelated local board.
/// </remarks>
public sealed class DocumentSummary
{
    /// <summary>The key the board is stored under on this device (bc-doc-{LocalId}).</summary>
    public Guid LocalId { get; set; }

    /// <summary>The board's own id, kept across import and export.</summary>
    public Guid DocumentId { get; set; }

    public string Title { get; set; } = CanvasCopy.Titles.DefaultBoardTitle;
    public Guid? CaseId { get; set; }
    public Guid? OrganizationId { get; set; }

    /// <summary>The server's id for this board, once it has been saved to a case.</summary>
    public Guid? ServerId { get; set; }

    public int ServerRevision { get; set; }

    /// <summary>True when the board was edited on this device after its last save to the case.</summary>
    public bool ChangedSinceServer { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public long SizeBytes { get; set; }

    /// <summary>The stored pictures and files the board names, so a sweep never has to open every board.</summary>
    public List<Guid> AssetIds { get; set; } = [];

    [JsonIgnore]
    public string FormattedSize => BcFileSize.Format(SizeBytes);
}

public enum SaveStateKind { Clean, Editing, SavingLocal, SavedLocal, SavingServer, SavedServer, ServerConflict, ServerFailed, Offline }

/// <summary>Where the board's changes are, in one short phrase for the header.</summary>
public readonly record struct SaveState(SaveStateKind Kind, DateTime? AtUtc = null, string? Problem = null)
{
    public static SaveState Clean => new(SaveStateKind.Clean);

    public string Text(DateTime nowUtc) => Kind switch
    {
        SaveStateKind.SavedServer => CanvasCopy.Status.SavedServer(
            AtUtc is { } at
                ? BcRelativeTime.Format(nowUtc - at) ?? BcDateFormat.Date(DateOnly.FromDateTime(at))
                : ""),
        SaveStateKind.Clean or SaveStateKind.SavedLocal => CanvasCopy.Status.SavedLocal,
        SaveStateKind.Editing or SaveStateKind.SavingLocal or SaveStateKind.SavingServer => CanvasCopy.Status.Saving,
        SaveStateKind.ServerFailed => CanvasCopy.Status.SaveRetry,
        SaveStateKind.Offline => CanvasCopy.Status.OfflineShort,
        SaveStateKind.ServerConflict => CanvasCopy.Status.ConflictShort,
        _ => CanvasCopy.Status.SavedLocal,
    };
}

/// <summary>
/// The editor's panel layout on this device, stored under <see cref="StorageKey"/>.
/// </summary>
/// <remarks>
/// Every field is nullable and <see cref="Apply"/> resolves each one, because an older build may have
/// written fewer fields and a hand-edited entry can say anything.
/// </remarks>
public sealed record LayoutSnapshot
{
    public const string StorageKey = "bc-layout";

    private static readonly string[] SheetSnaps = ["collapsed", "half", "full"];

    [JsonPropertyName("propsOpen")] public bool? PropsOpen { get; init; }
    [JsonPropertyName("sheetSnap")] public string? SheetSnap { get; init; }
    [JsonPropertyName("showMinimap")] public bool? ShowMinimap { get; init; }

    public string Serialise() => JsonSerializer.Serialize(this);

    public static LayoutSnapshot? Deserialise(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<LayoutSnapshot>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public LayoutSnapshot Apply(CanvasEditorOptions options) => new()
    {
        PropsOpen = PropsOpen ?? true,
        SheetSnap = SheetSnap is { } snap && SheetSnaps.Contains(snap, StringComparer.Ordinal) ? snap : "half",
        ShowMinimap = ShowMinimap ?? options.ShowMinimap,
    };
}
