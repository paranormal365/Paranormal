using Ben.Service.Models.Admin;
using Ben.Web.Services.WebApi;

namespace Ben.Web.Services;

/// <summary>
/// IsHaunted's oversight of hosted events: the SuperAdmin's dashboard, list, removal and appeals, and the organizer's
/// side of a removal (item 235 phase 17b).
/// </summary>
/// <remarks>
/// Reads are <see cref="ItemResult{T}"/> so a refusal never reads as an empty list; writes keep the server's sentence.
/// </remarks>
public interface IBenEventOversightClient
{
    /// <summary>Every hosted event, with the appeals waiting and recently answered. SuperAdmin.</summary>
    Task<ItemResult<AdminHostedEventsRecord>> GetAdminHostedEventsAsync(CancellationToken token = default);

    /// <summary>The events dashboard over a window of days. SuperAdmin.</summary>
    Task<ItemResult<AdminHostedEventStats>> GetAdminHostedEventStatsAsync(int days = 30, CancellationToken token = default);

    /// <summary>What removing this event would do: whether its credit comes back and who with a place is told.</summary>
    Task<ItemResult<AdminHostedEventRemovalEffect>> GetHostedEventRemovalEffectAsync(Guid eventId, CancellationToken token = default);

    /// <summary>Removes an event: off the site, credit back, guests and organizer told, appeal offered.</summary>
    Task<(AdminHostedEventsRecord? Result, string? Error)> RemoveHostedEventAsync(
        Guid eventId, RemoveHostedEventRequest request, CancellationToken token = default);

    /// <summary>Upholds or declines an organizer's appeal.</summary>
    Task<(AdminHostedEventsRecord? Result, string? Error)> DecideHostedEventAppealAsync(
        Guid removalId, DecideHostedEventAppealRequest request, CancellationToken token = default);

    /// <summary>The newest removal of an event, as its organizer reads it. Empty when it was never removed.</summary>
    Task<ItemResult<HostedEventRemovalRecord>> GetHostedEventRemovalAsync(Guid orgId, Guid eventId, CancellationToken token = default);

    /// <summary>The organizer's appeal.</summary>
    Task<(HostedEventRemovalRecord? Result, string? Error)> AppealHostedEventRemovalAsync(
        Guid orgId, Guid eventId, AppealHostedEventRemovalRequest request, CancellationToken token = default);
}
