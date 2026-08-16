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
}
