using Ben.Service.Models.Entities;
using Ben.Web.Services.WebApi;

namespace Ben.Web.Services;

/// <summary>An event's files: the host adding them, and a guest's list of what they may have (item 235 phase 11).</summary>
public interface IBenEventFileClient
{
    Task<LoadResult<HostedEventFileRecord>> GetEventFilesAsync(Guid orgId, Guid eventId, CancellationToken token = default);

    /// <summary>Adds a file. The content carries <c>file</c>, <c>folder</c>, <c>description</c> and <c>audience</c>.</summary>
    Task<(List<HostedEventFileRecord>? Result, string? Error)> AddEventFileAsync(
        Guid orgId, Guid eventId, MultipartFormDataContent content, CancellationToken token = default);

    Task<(List<HostedEventFileRecord>? Result, string? Error)> UpdateEventFileAsync(
        Guid orgId, Guid eventId, Guid fileId, UpdateHostedEventFileRequest request, CancellationToken token = default);

    Task<(List<HostedEventFileRecord>? Result, string? Error)> DeleteEventFileAsync(
        Guid orgId, Guid eventId, Guid fileId, CancellationToken token = default);

    /// <summary>The files this viewer may have. Empty for somebody who may have none.</summary>
    Task<LoadResult<HostedEventFileRecord>> GetPublicEventFilesAsync(Guid eventId, bool signedIn, CancellationToken token = default);
}
