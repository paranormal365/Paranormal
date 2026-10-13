using Ben.Service.Models.Entities;
using Ben.Web.Services.WebApi;

namespace Ben.Web.Services;

/// <summary>
/// Designed menus for hosted events (Ben, 10/13/2026): the builder's item library and menus, and the
/// published menus guests read.
/// </summary>
public interface IBenEventMenuClient
{
    /// <summary>Everything the menu builder needs: the menus, categories, the item library and the meals.</summary>
    Task<ItemResult<EventMenuWorkspaceRecord>> GetMenuWorkspaceAsync(Guid orgId, Guid eventId, CancellationToken token = default);

    /// <summary>The designs a menu can be drawn in.</summary>
    Task<LoadResult<MenuDesignRecord>> GetMenuDesignsAsync(Guid orgId, Guid eventId, CancellationToken token = default);

    Task<(AddMenuCategoryResult? Result, string? Error)> AddMenuCategoryAsync(
        Guid orgId, Guid eventId, string name, CancellationToken token = default);

    Task<(MenuLibraryItemRecord? Result, string? Error)> AddMenuItemAsync(
        Guid orgId, Guid eventId, SaveMenuLibraryItemRequest request, CancellationToken token = default);

    Task<(MenuLibraryItemRecord? Result, string? Error)> SaveMenuItemAsync(
        Guid orgId, Guid eventId, Guid itemId, SaveMenuLibraryItemRequest request, CancellationToken token = default);

    /// <summary>Stops offering an item for new menus; menus that have it keep it.</summary>
    Task<(MenuLibraryItemRecord? Result, string? Error)> RetireMenuItemAsync(
        Guid orgId, Guid eventId, Guid itemId, CancellationToken token = default);

    Task<(MenuLibraryItemRecord? Result, string? Error)> SetMenuItemImageAsync(
        Guid orgId, Guid eventId, Guid itemId, MultipartFormDataContent content, CancellationToken token = default);

    Task<(MenuLibraryItemRecord? Result, string? Error)> RemoveMenuItemImageAsync(
        Guid orgId, Guid eventId, Guid itemId, CancellationToken token = default);

    Task<(EventMenuRecord? Result, string? Error)> CreateEventMenuAsync(
        Guid orgId, Guid eventId, CreateEventMenuRequest request, CancellationToken token = default);

    Task<ItemResult<EventMenuRecord>> GetEventMenuAsync(Guid orgId, Guid eventId, Guid menuId, CancellationToken token = default);

    Task<(EventMenuRecord? Result, string? Error)> SaveEventMenuAsync(
        Guid orgId, Guid eventId, Guid menuId, SaveEventMenuRequest request, CancellationToken token = default);

    Task<(bool Deleted, string? Error)> DeleteEventMenuAsync(Guid orgId, Guid eventId, Guid menuId, CancellationToken token = default);

    Task<(EventMenuRecord? Result, string? Error)> CopyEventMenuAsync(
        Guid orgId, Guid eventId, Guid menuId, CancellationToken token = default);

    Task<(EventMenuRecord? Result, string? Error)> PublishEventMenuAsync(
        Guid orgId, Guid eventId, Guid menuId, CancellationToken token = default);

    Task<(EventMenuRecord? Result, string? Error)> UnpublishEventMenuAsync(
        Guid orgId, Guid eventId, Guid menuId, CancellationToken token = default);

    Task<(EventMenuRecord? Result, string? Error)> SetEventMenuLogoAsync(
        Guid orgId, Guid eventId, Guid menuId, MultipartFormDataContent content, CancellationToken token = default);

    Task<(EventMenuRecord? Result, string? Error)> RemoveEventMenuLogoAsync(
        Guid orgId, Guid eventId, Guid menuId, CancellationToken token = default);

    /// <summary>The menu as a PDF, in its design or the one named.</summary>
    Task<(byte[] Data, string ContentType, string FileName)?> GetEventMenuPdfAsync(
        Guid orgId, Guid eventId, Guid menuId, string? designKey = null, CancellationToken token = default);

    /// <summary>An event's published menus, for anybody looking at the event.</summary>
    Task<LoadResult<EventMenuRecord>> GetPublishedEventMenusAsync(Guid eventId, CancellationToken token = default);

    /// <summary>A published menu as a PDF.</summary>
    Task<(byte[] Data, string ContentType, string FileName)?> GetPublishedEventMenuPdfAsync(
        Guid eventId, Guid menuId, CancellationToken token = default);

}
