namespace Ben.Video.Editor.Models;

/// <summary>
/// Which slice of the library to list.
/// </summary>
/// <remarks>
/// <para>A scope narrows what the person can already see; it never widens it. That is enforced by
/// the server, which computes the full audience set and then intersects — so an editor that sent a
/// scope it had no business with would get an empty list rather than somebody else's files.</para>
///
/// <para>The editor deliberately does not know what a "case" is. It receives labels and ids from
/// <see cref="IMediaLibraryScopeSource"/>, shows them, and sends an id back. Keeping the meaning on
/// the server side is what stops a general-purpose editor component growing a dependency on one
/// product's domain model.</para>
/// </remarks>
public enum MediaLibraryScopeKind
{
    /// <summary>Everything the person may see. The default, and what the tab did before scoping.</summary>
    All = 0,

    /// <summary>Only files they own.</summary>
    Personal = 1,

    /// <summary>One case, optionally narrowed to a single visit within it.</summary>
    Case = 2,
}

/// <summary>A chosen scope: the kind, plus the ids it needs.</summary>
/// <param name="Kind">Which slice.</param>
/// <param name="CaseId">Required by <see cref="MediaLibraryScopeKind.Case"/>; ignored otherwise.</param>
/// <param name="InvestigationId">Optional narrowing within the case.</param>
public sealed record MediaLibraryScope(
    MediaLibraryScopeKind Kind = MediaLibraryScopeKind.All,
    Guid? CaseId = null,
    Guid? InvestigationId = null)
{
    public static readonly MediaLibraryScope All = new();

    /// <summary>The scope as the query-string value the server expects.</summary>
    public string Wire => Kind switch
    {
        MediaLibraryScopeKind.Personal => "personal",
        MediaLibraryScopeKind.Case     => "case",
        _                              => "all",
    };
}

/// <summary>One group of media the library can be scoped to, and the sub-groups inside it.</summary>
/// <remarks>
/// Named for what it is to the editor — a group with children — rather than for what it is to the
/// site, which is a case and its investigations. The editor renders the labels it is given.
/// </remarks>
public sealed record MediaLibraryScopeGroup(Guid Id, string Label, IReadOnlyList<MediaLibraryScopeItem> Items);

/// <summary>One sub-group within a <see cref="MediaLibraryScopeGroup"/>.</summary>
public sealed record MediaLibraryScopeItem(Guid Id, string Label);
