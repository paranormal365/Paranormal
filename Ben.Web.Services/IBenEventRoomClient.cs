using Ben.Service.Models.Entities;
using Ben.Web.Services.WebApi;

namespace Ben.Web.Services;

/// <summary>An event's room and its photo wall (item 235 phase 11).</summary>
public interface IBenEventRoomClient
{
    /// <summary>The room, for somebody in it. A failure with no reason is "not in this room".</summary>
    Task<ItemResult<EventRoomRecord>> GetEventRoomAsync(Guid eventId, CancellationToken token = default);

    /// <summary>Posts to the room. The content carries <c>body</c>, an optional <c>media</c> and <c>sendToHosts</c>.</summary>
    Task<(EventRoomRecord? Result, string? Error)> PostToEventRoomAsync(Guid eventId, MultipartFormDataContent content, CancellationToken token = default);

    Task<(EventRoomRecord? Result, string? Error)> SendEventRoomPhotoToHostsAsync(Guid eventId, Guid messageId, CancellationToken token = default);

    Task<(EventRoomRecord? Result, string? Error)> RemoveEventRoomMessageAsync(Guid eventId, Guid messageId, CancellationToken token = default);

    Task<(EventRoomRecord? Result, string? Error)> ReportEventRoomMessageAsync(Guid eventId, Guid messageId, ReportEventRoomMessageRequest request, CancellationToken token = default);

    Task<(EventRoomRecord? Result, string? Error)> HideEventRoomMessageAsync(Guid eventId, Guid messageId, bool hide, CancellationToken token = default);

    Task<(EventRoomRecord? Result, string? Error)> SetEventRoomClosedAsync(Guid eventId, bool closed, CancellationToken token = default);

    Task<(EventRoomRecord? Result, string? Error)> SetEventRoomSettingsAsync(Guid eventId, EventRoomSettingsRequest request, CancellationToken token = default);

    Task<ItemResult<EventWallRecord>> GetEventWallAsync(Guid eventId, CancellationToken token = default);
}
