using Ben.Data.Common.Enums;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Web.Library.Services;
using Microsoft.Extensions.Options;

namespace Ben.Web.WebApp.Services.WebApi;

/// <summary>
/// The Cms half of the adapter — implements <see cref="Ben.Web.Library.Services.IBenCmsClient"/>.
/// </summary>
/// <remarks>
/// One partial class split across files by domain, matching the slices of IBenAdminClient.
/// The constructor and shared fields live in BenAdminClientAdapter.cs.
/// </remarks>
public sealed partial class BenAdminClientAdapter
{
    // ── CMS Pages ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<CmsPageListItem>> GetCmsPagesAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<CmsPageListItem>>($"/api/organizations/{orgId}/pages", token);
        return result ?? [];
    }

    public Task<CmsPageDetail?> GetCmsPageAsync(Guid orgId, Guid pageId, CancellationToken token = default)
        => _api.GetAsync<CmsPageDetail>($"/api/organizations/{orgId}/pages/{pageId}", token);

    public Task<CmsPageDetail?> CreateCmsPageAsync(Guid orgId, CmsCreatePageRequest request, CancellationToken token = default)
        => _api.PostAsync<CmsCreatePageRequest, CmsPageDetail>($"/api/organizations/{orgId}/pages", request, token);

    public Task<CmsPageDetail?> UpdateCmsPageAsync(Guid orgId, Guid pageId, CmsUpdatePageRequest request, CancellationToken token = default)
        => _api.PutAsync<CmsUpdatePageRequest, CmsPageDetail>($"/api/organizations/{orgId}/pages/{pageId}", request, token);

    public Task<bool> DeleteCmsPageAsync(Guid orgId, Guid pageId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/pages/{pageId}", token);

    // ── CMS Sections ──────────────────────────────────────────────────────────

    public Task<CmsSectionRecord?> CreateCmsSectionAsync(Guid orgId, Guid pageId, CmsCreateSectionRequest request, CancellationToken token = default)
        => _api.PostAsync<CmsCreateSectionRequest, CmsSectionRecord>($"/api/organizations/{orgId}/pages/{pageId}/sections", request, token);

    public Task<CmsSectionRecord?> UpdateCmsSectionAsync(Guid orgId, Guid pageId, Guid sectionId, CmsUpdateSectionRequest request, CancellationToken token = default)
        => _api.PutAsync<CmsUpdateSectionRequest, CmsSectionRecord>($"/api/organizations/{orgId}/pages/{pageId}/sections/{sectionId}", request, token);

    public Task<bool> ReorderCmsSectionsAsync(Guid orgId, Guid pageId, IList<Guid> orderedIds, CancellationToken token = default)
        => _api.PutVoidAsync($"/api/organizations/{orgId}/pages/{pageId}/sections/reorder",
               new { OrderedSectionIds = orderedIds }, token);

    public Task<bool> DeleteCmsSectionAsync(Guid orgId, Guid pageId, Guid sectionId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/pages/{pageId}/sections/{sectionId}", token);

    // ── CMS Page Permissions ──────────────────────────────────────────────────

    public async Task<IReadOnlyList<CmsPagePermissionRecord>> GetPagePermissionsAsync(Guid orgId, Guid pageId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<CmsPagePermissionRecord>>($"/api/organizations/{orgId}/pages/{pageId}/permissions", token);
        return result ?? [];
    }

    public Task<CmsPagePermissionRecord?> CreatePagePermissionAsync(Guid orgId, Guid pageId, PagePermissionCreateRequest request, CancellationToken token = default)
        => _api.PostAsync<PagePermissionCreateRequest, CmsPagePermissionRecord>($"/api/organizations/{orgId}/pages/{pageId}/permissions", request, token);

    public Task<CmsPagePermissionRecord?> UpdatePagePermissionAsync(Guid orgId, Guid pageId, Guid permId, CmsPageAction actions, CancellationToken token = default)
        => _api.PutAsync<object, CmsPagePermissionRecord>($"/api/organizations/{orgId}/pages/{pageId}/permissions/{permId}",
            new { Actions = actions }, token);

    public Task<bool> DeletePagePermissionAsync(Guid orgId, Guid pageId, Guid permId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/pages/{pageId}/permissions/{permId}", token);

    // ── Drafts (item #80, part 3) ───────────────────────────────────────────

    public Task<CmsDraftStateResponse?> GetCmsDraftStateAsync(Guid orgId, Guid pageId, CancellationToken token = default)
        => _api.GetAsync<CmsDraftStateResponse>($"{DraftBase(orgId, pageId)}", token);

    public Task<CmsDraftStateResponse?> StartCmsDraftAsync(Guid orgId, Guid pageId, CancellationToken token = default)
        => _api.PostAsync<object, CmsDraftStateResponse>($"{DraftBase(orgId, pageId)}", new object(), token);

    public Task<bool> PublishCmsDraftAsync(Guid orgId, Guid pageId, CancellationToken token = default)
        => _api.PostVoidAsync($"{DraftBase(orgId, pageId)}/publish", new object(), token);

    public Task<bool> DiscardCmsDraftAsync(Guid orgId, Guid pageId, CancellationToken token = default)
        => _api.DeleteAsync($"{DraftBase(orgId, pageId)}", token);

    private static string DraftBase(Guid orgId, Guid pageId)
        => $"/api/organizations/{orgId}/pages/{pageId}/draft";

    // ── The group's saved templates (item #80, part 2) ──────────────────────

    public async Task<IReadOnlyList<CmsTemplateRecord>> GetCmsTemplatesAsync(Guid orgId, CmsTemplateScope? scope = null, CancellationToken token = default)
    {
        var url = $"/api/organizations/{orgId}/cms-templates" + (scope is null ? "" : $"?scope={scope}");
        var result = await _api.GetAsync<IReadOnlyList<CmsTemplateRecord>>(url, token);
        return result ?? [];
    }

    public Task<CmsTemplateRecord?> SaveCmsTemplateAsync(Guid orgId, SaveCmsTemplateRequest request, CancellationToken token = default)
        => _api.PostAsync<SaveCmsTemplateRequest, CmsTemplateRecord>($"/api/organizations/{orgId}/cms-templates", request, token);

    public Task<CmsTemplateRecord?> UpdateCmsTemplateAsync(Guid orgId, Guid templateId, SaveCmsTemplateRequest request, CancellationToken token = default)
        => _api.PutAsync<SaveCmsTemplateRequest, CmsTemplateRecord>($"/api/organizations/{orgId}/cms-templates/{templateId}", request, token);

    public Task<bool> DeleteCmsTemplateAsync(Guid orgId, Guid templateId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/cms-templates/{templateId}", token);
}
