namespace Ben.Service.Models.Entities;

// ── CMS page drafts (backlog item #80, part 3) ───────────────────────────────
// Defined here rather than in the WebApi so both sides share one definition. The older CMS and
// public-page responses are hand-mirrored across the boundary, which is exactly the drift risk the
// equipment records were moved here to avoid.

/// <summary>Where a page stands with respect to drafts.</summary>
/// <param name="LivePageId">The page a draft would replace.</param>
/// <param name="DraftPageId">The open draft, or null when there is none.</param>
/// <param name="NeedsDraft">
/// True when the page is published, so edits must go through a draft. False for a page nobody can
/// see yet, which is edited directly.
/// </param>
/// <param name="DraftStarted">When the draft was begun, or null when there is none.</param>
public sealed record CmsDraftStateResponse(
    Guid LivePageId,
    Guid? DraftPageId,
    bool NeedsDraft,
    DateTime? DraftStarted);
