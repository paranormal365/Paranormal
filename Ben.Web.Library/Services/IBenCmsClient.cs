using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Data.Common.Enums;

namespace Ben.Web.Library.Services;

/// <summary>
/// The Cms slice of <see cref="IBenAdminClient"/> — organization-authored pages and their sections.
/// </summary>
/// <remarks>
/// Part of splitting one 383-method interface into domain-sized pieces.
/// <see cref="IBenAdminClient"/> inherits every slice, so existing callers and the single
/// adapter are unchanged; new code (and test doubles) can depend on just the slice it needs.
/// </remarks>
public interface IBenCmsClient
{
    // ── CMS Pages ─────────────────────────────────────────────────────────────

    Task<IReadOnlyList<CmsPageListItem>> GetCmsPagesAsync(Guid orgId, CancellationToken token = default);
    Task<CmsPageDetail?> GetCmsPageAsync(Guid orgId, Guid pageId, CancellationToken token = default);
    Task<CmsPageDetail?> CreateCmsPageAsync(Guid orgId, CmsCreatePageRequest request, CancellationToken token = default);
    Task<CmsPageDetail?> UpdateCmsPageAsync(Guid orgId, Guid pageId, CmsUpdatePageRequest request, CancellationToken token = default);
    Task<bool> DeleteCmsPageAsync(Guid orgId, Guid pageId, CancellationToken token = default);

    // ── CMS Sections ──────────────────────────────────────────────────────────

    Task<CmsSectionRecord?> CreateCmsSectionAsync(Guid orgId, Guid pageId, CmsCreateSectionRequest request, CancellationToken token = default);
    Task<CmsSectionRecord?> UpdateCmsSectionAsync(Guid orgId, Guid pageId, Guid sectionId, CmsUpdateSectionRequest request, CancellationToken token = default);
    Task<bool> ReorderCmsSectionsAsync(Guid orgId, Guid pageId, IList<Guid> orderedIds, CancellationToken token = default);
    Task<bool> DeleteCmsSectionAsync(Guid orgId, Guid pageId, Guid sectionId, CancellationToken token = default);

    // ── CMS Page Permissions ──────────────────────────────────────────────────

    Task<IReadOnlyList<CmsPagePermissionRecord>> GetPagePermissionsAsync(Guid orgId, Guid pageId, CancellationToken token = default);
    Task<CmsPagePermissionRecord?> CreatePagePermissionAsync(Guid orgId, Guid pageId, PagePermissionCreateRequest request, CancellationToken token = default);
    Task<CmsPagePermissionRecord?> UpdatePagePermissionAsync(Guid orgId, Guid pageId, Guid permId, CmsPageAction actions, CancellationToken token = default);
    Task<bool> DeletePagePermissionAsync(Guid orgId, Guid pageId, Guid permId, CancellationToken token = default);

    // ── Drafts (item #80, part 3) ───────────────────────────────────────────

    /// <summary>Whether this page has a draft open, and whether editing it needs one.</summary>
    Task<CmsDraftStateResponse?> GetCmsDraftStateAsync(Guid orgId, Guid pageId, CancellationToken token = default);

    /// <summary>Starts a draft, or returns the one already open. Idempotent.</summary>
    Task<CmsDraftStateResponse?> StartCmsDraftAsync(Guid orgId, Guid pageId, CancellationToken token = default);

    /// <summary>Copies the draft onto the live page and removes the draft.</summary>
    Task<bool> PublishCmsDraftAsync(Guid orgId, Guid pageId, CancellationToken token = default);

    /// <summary>Throws the draft away, leaving the live page as it was.</summary>
    Task<bool> DiscardCmsDraftAsync(Guid orgId, Guid pageId, CancellationToken token = default);

    // ── The group's saved templates (item #80, part 2) ──────────────────────

    Task<IReadOnlyList<CmsTemplateRecord>> GetCmsTemplatesAsync(Guid orgId, CmsTemplateScope? scope = null, CancellationToken token = default);
    Task<CmsTemplateRecord?> SaveCmsTemplateAsync(Guid orgId, SaveCmsTemplateRequest request, CancellationToken token = default);
    Task<CmsTemplateRecord?> UpdateCmsTemplateAsync(Guid orgId, Guid templateId, SaveCmsTemplateRequest request, CancellationToken token = default);
    Task<bool> DeleteCmsTemplateAsync(Guid orgId, Guid templateId, CancellationToken token = default);
}
