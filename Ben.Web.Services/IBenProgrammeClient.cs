using Ben.Service.Models.Entities;
using Ben.Web.Services.WebApi;

namespace Ben.Web.Services;

/// <summary>
/// An event's programme: the host's sessions, and a guest signing up (item 235 phase 10).
/// </summary>
/// <remarks>
/// Reads are <see cref="ItemResult{T}"/>, so a refused roster never reads as "nobody signed up";
/// writes keep the server's sentence, because "signing up opens once the venue confirms your place"
/// is the thing a guest needs to read, not "something went wrong".
/// </remarks>
public interface IBenProgrammeClient
{
    // ── the host ─────────────────────────────────────────────────────────────

    Task<ItemResult<HostedEventProgrammeRecord>> GetEventProgrammeAsync(Guid orgId, Guid eventId, CancellationToken token = default);

    Task<(HostedEventProgrammeRecord? Result, string? Error)> AddEventSessionAsync(
        Guid orgId, Guid eventId, SaveHostedEventSessionRequest request, CancellationToken token = default);

    Task<(HostedEventProgrammeRecord? Result, string? Error)> SaveEventSessionAsync(
        Guid orgId, Guid eventId, Guid sessionId, SaveHostedEventSessionRequest request, CancellationToken token = default);

    Task<(HostedEventProgrammeRecord? Result, string? Error)> CancelEventSessionAsync(
        Guid orgId, Guid eventId, Guid sessionId, CancelHostedEventSessionRequest request, CancellationToken token = default);

    Task<(HostedEventProgrammeRecord? Result, string? Error)> DeleteEventSessionAsync(
        Guid orgId, Guid eventId, Guid sessionId, CancellationToken token = default);

    Task<(HostedEventProgrammeRecord? Result, string? Error)> PublishEventProgrammeAsync(
        Guid orgId, Guid eventId, CancellationToken token = default);

    Task<(HostedEventProgrammeRecord? Result, string? Error)> UnpublishEventProgrammeAsync(
        Guid orgId, Guid eventId, CancellationToken token = default);

    Task<ItemResult<HostedEventSessionRosterRecord>> GetEventSessionRosterAsync(
        Guid orgId, Guid eventId, Guid sessionId, CancellationToken token = default);

    Task<(HostedEventSessionRosterRecord? Result, string? Error)> RemoveEventSessionSignUpAsync(
        Guid orgId, Guid eventId, Guid sessionId, Guid signUpId, CancellationToken token = default);

    // ── the guest ────────────────────────────────────────────────────────────

    /// <summary>The published programme; with this viewer's places when signed in.</summary>
    Task<ItemResult<PublicProgrammeRecord>> GetPublicProgrammeAsync(Guid eventId, bool signedIn, CancellationToken token = default);

    Task<(PublicProgrammeRecord? Result, string? Error)> SignUpForSessionAsync(
        Guid eventId, Guid sessionId, SessionSignUpRequest request, CancellationToken token = default);

    Task<(PublicProgrammeRecord? Result, string? Error)> LeaveSessionAsync(
        Guid eventId, Guid sessionId, CancellationToken token = default);

    /// <summary>Clears the bell's "the programme changed" row for this guest.</summary>
    Task MarkProgrammeSeenAsync(Guid eventId, CancellationToken token = default);
}
