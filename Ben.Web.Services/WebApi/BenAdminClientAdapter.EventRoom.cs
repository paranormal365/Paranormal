using Ben.Service.Models.Entities;

namespace Ben.Web.Services.WebApi;

/// <summary>The event room slice — see <see cref="IBenEventRoomClient"/>.</summary>
public sealed partial class BenAdminClientAdapter
{
    private static string RoomUrl(Guid eventId) => $"/api/public/hosted-events/{eventId}/room";

    public Task<ItemResult<EventRoomRecord>> GetEventRoomAsync(Guid eventId, CancellationToken token = default)
        => _api.GetItemAsync<EventRoomRecord>(RoomUrl(eventId), token);

    public Task<(EventRoomRecord? Result, string? Error)> PostToEventRoomAsync(Guid eventId, MultipartFormDataContent content, CancellationToken token = default)
        => _api.PostMultipartExpectingReasonAsync<EventRoomRecord>(RoomUrl(eventId), content, token);

    public Task<(EventRoomRecord? Result, string? Error)> SendEventRoomPhotoToHostsAsync(Guid eventId, Guid messageId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, EventRoomRecord>(HttpMethod.Post, $"{RoomUrl(eventId)}/messages/{messageId}/send-to-hosts", new { }, token);

    public Task<(EventRoomRecord? Result, string? Error)> RemoveEventRoomMessageAsync(Guid eventId, Guid messageId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, EventRoomRecord>(HttpMethod.Delete, $"{RoomUrl(eventId)}/messages/{messageId}", new { }, token);

    public Task<(EventRoomRecord? Result, string? Error)> ReportEventRoomMessageAsync(Guid eventId, Guid messageId, ReportEventRoomMessageRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<ReportEventRoomMessageRequest, EventRoomRecord>(HttpMethod.Post, $"{RoomUrl(eventId)}/messages/{messageId}/report", request, token);

    public Task<(EventRoomRecord? Result, string? Error)> HideEventRoomMessageAsync(Guid eventId, Guid messageId, bool hide, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, EventRoomRecord>(HttpMethod.Post, $"{RoomUrl(eventId)}/messages/{messageId}/{(hide ? "hide" : "unhide")}", new { }, token);

    public Task<(EventRoomRecord? Result, string? Error)> SetEventRoomClosedAsync(Guid eventId, bool closed, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, EventRoomRecord>(HttpMethod.Post, $"{RoomUrl(eventId)}/{(closed ? "close" : "reopen")}", new { }, token);

    public Task<(EventRoomRecord? Result, string? Error)> SetEventRoomSettingsAsync(Guid eventId, EventRoomSettingsRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<EventRoomSettingsRequest, EventRoomRecord>(HttpMethod.Put, $"{RoomUrl(eventId)}/settings", request, token);

    public Task<ItemResult<EventWallRecord>> GetEventWallAsync(Guid eventId, CancellationToken token = default)
        => _api.GetItemAsync<EventWallRecord>($"{RoomUrl(eventId)}/photos", token);
}
