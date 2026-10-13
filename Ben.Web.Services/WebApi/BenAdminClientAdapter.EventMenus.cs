using Ben.Service.Models.Entities;

namespace Ben.Web.Services.WebApi;

/// <summary>The designed-menus slice; see <see cref="IBenEventMenuClient"/> for the contract.</summary>
public sealed partial class BenAdminClientAdapter
{
    private static string BuilderUrl(Guid orgId, Guid eventId) => $"/api/organizations/{orgId}/events/{eventId}/menu-builder";

    public Task<ItemResult<EventMenuWorkspaceRecord>> GetMenuWorkspaceAsync(Guid orgId, Guid eventId, CancellationToken token = default)
        => _api.GetItemAsync<EventMenuWorkspaceRecord>(BuilderUrl(orgId, eventId), token);

    public Task<LoadResult<MenuDesignRecord>> GetMenuDesignsAsync(Guid orgId, Guid eventId, CancellationToken token = default)
        => _api.GetListAsync<MenuDesignRecord>($"{BuilderUrl(orgId, eventId)}/designs", token);

    public Task<(AddMenuCategoryResult? Result, string? Error)> AddMenuCategoryAsync(
        Guid orgId, Guid eventId, string name, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<AddMenuCategoryRequest, AddMenuCategoryResult>(
               HttpMethod.Post, $"{BuilderUrl(orgId, eventId)}/categories", new AddMenuCategoryRequest(name), token);

    public Task<(MenuLibraryItemRecord? Result, string? Error)> AddMenuItemAsync(
        Guid orgId, Guid eventId, SaveMenuLibraryItemRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveMenuLibraryItemRequest, MenuLibraryItemRecord>(
               HttpMethod.Post, $"{BuilderUrl(orgId, eventId)}/library", request, token);

    public Task<(MenuLibraryItemRecord? Result, string? Error)> SaveMenuItemAsync(
        Guid orgId, Guid eventId, Guid itemId, SaveMenuLibraryItemRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveMenuLibraryItemRequest, MenuLibraryItemRecord>(
               HttpMethod.Put, $"{BuilderUrl(orgId, eventId)}/library/{itemId}", request, token);

    public Task<(MenuLibraryItemRecord? Result, string? Error)> RetireMenuItemAsync(
        Guid orgId, Guid eventId, Guid itemId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, MenuLibraryItemRecord>(
               HttpMethod.Post, $"{BuilderUrl(orgId, eventId)}/library/{itemId}/retire", new { }, token);

    public Task<(MenuLibraryItemRecord? Result, string? Error)> SetMenuItemImageAsync(
        Guid orgId, Guid eventId, Guid itemId, MultipartFormDataContent content, CancellationToken token = default)
        => _api.PostMultipartExpectingReasonAsync<MenuLibraryItemRecord>($"{BuilderUrl(orgId, eventId)}/library/{itemId}/image", content, token);

    public Task<(MenuLibraryItemRecord? Result, string? Error)> RemoveMenuItemImageAsync(
        Guid orgId, Guid eventId, Guid itemId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, MenuLibraryItemRecord>(
               HttpMethod.Delete, $"{BuilderUrl(orgId, eventId)}/library/{itemId}/image", new { }, token);

    public Task<(EventMenuRecord? Result, string? Error)> CreateEventMenuAsync(
        Guid orgId, Guid eventId, CreateEventMenuRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<CreateEventMenuRequest, EventMenuRecord>(HttpMethod.Post, BuilderUrl(orgId, eventId), request, token);

    public Task<ItemResult<EventMenuRecord>> GetEventMenuAsync(Guid orgId, Guid eventId, Guid menuId, CancellationToken token = default)
        => _api.GetItemAsync<EventMenuRecord>($"{BuilderUrl(orgId, eventId)}/{menuId}", token);

    public Task<(EventMenuRecord? Result, string? Error)> SaveEventMenuAsync(
        Guid orgId, Guid eventId, Guid menuId, SaveEventMenuRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveEventMenuRequest, EventMenuRecord>(
               HttpMethod.Put, $"{BuilderUrl(orgId, eventId)}/{menuId}", request, token);

    public Task<(bool Deleted, string? Error)> DeleteEventMenuAsync(Guid orgId, Guid eventId, Guid menuId, CancellationToken token = default)
        => _api.DeleteExpectingReasonAsync($"{BuilderUrl(orgId, eventId)}/{menuId}", token);

    public Task<(EventMenuRecord? Result, string? Error)> CopyEventMenuAsync(
        Guid orgId, Guid eventId, Guid menuId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, EventMenuRecord>(HttpMethod.Post, $"{BuilderUrl(orgId, eventId)}/{menuId}/copy", new { }, token);

    public Task<(EventMenuRecord? Result, string? Error)> PublishEventMenuAsync(
        Guid orgId, Guid eventId, Guid menuId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, EventMenuRecord>(HttpMethod.Post, $"{BuilderUrl(orgId, eventId)}/{menuId}/publish", new { }, token);

    public Task<(EventMenuRecord? Result, string? Error)> UnpublishEventMenuAsync(
        Guid orgId, Guid eventId, Guid menuId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, EventMenuRecord>(HttpMethod.Post, $"{BuilderUrl(orgId, eventId)}/{menuId}/unpublish", new { }, token);

    public Task<(EventMenuRecord? Result, string? Error)> SetEventMenuLogoAsync(
        Guid orgId, Guid eventId, Guid menuId, MultipartFormDataContent content, CancellationToken token = default)
        => _api.PostMultipartExpectingReasonAsync<EventMenuRecord>($"{BuilderUrl(orgId, eventId)}/{menuId}/logo", content, token);

    public Task<(EventMenuRecord? Result, string? Error)> RemoveEventMenuLogoAsync(
        Guid orgId, Guid eventId, Guid menuId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, EventMenuRecord>(HttpMethod.Delete, $"{BuilderUrl(orgId, eventId)}/{menuId}/logo", new { }, token);

    public Task<(byte[] Data, string ContentType, string FileName)?> GetEventMenuPdfAsync(
        Guid orgId, Guid eventId, Guid menuId, string? designKey = null, CancellationToken token = default)
        => _api.GetBytesAsync($"{BuilderUrl(orgId, eventId)}/{menuId}/pdf" + (designKey is { Length: > 0 } d ? $"?design={Uri.EscapeDataString(d)}" : ""),
                              "menu.pdf", token);

    public Task<LoadResult<EventMenuRecord>> GetPublishedEventMenusAsync(Guid eventId, CancellationToken token = default)
        => _api.GetAnonymousListAsync<EventMenuRecord>($"/api/public/hosted-events/{eventId}/published-menus", token);

    public Task<(byte[] Data, string ContentType, string FileName)?> GetPublishedEventMenuPdfAsync(
        Guid eventId, Guid menuId, CancellationToken token = default)
        => _api.GetBytesAsync($"/api/public/hosted-events/{eventId}/published-menus/{menuId}/pdf", "menu.pdf", token);

}
