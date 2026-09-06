using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Web.Services;

/// <summary>
/// Saves a project through the site's own authenticated client.
/// </summary>
/// <remarks>
/// <para>The editor's default store posts over a named HttpClient. On this host the bearer token
/// lives in the circuit, and a message handler registered at the application root cannot reach it
/// — so Save to Server, and the prompt offered after every export, both answered 401. Nobody had
/// ever successfully saved a project to the server from the site (2026-09-05 audit, F13).</para>
///
/// <para><see cref="IBenAdminClient"/> is the thing that already attaches the circuit's token to
/// every other call the site makes. Going through it is the whole fix.</para>
/// </remarks>
public sealed class BenProjectServerStore(IBenAdminClient adminClient, IBenUserState userState)
    : IProjectServerStore
{
    /// <summary>
    /// Signed in is the whole condition here — the endpoint exists for every account.
    /// </summary>
    public bool IsAvailable => userState.IsAuthenticated;

    public async Task<(Guid? Id, string? Problem)> SaveAsync(
        ProjectFile file, Guid? existingId, Guid? caseId = null, CancellationToken ct = default)
    {
        if (!IsAvailable)
            return (null, "You are not signed in, so the project could not be saved to the server.");

        try
        {
            // Update when it is already there. Every save used to create a new row, so saving five
            // times made five projects with the same name (2026-09-05 audit, persistence-13).
            var saved = existingId is { } id
                ? await adminClient.UpdateMyVideoProjectAsync(id, file, ct)
                : await adminClient.SaveMyVideoProjectAsync(file, caseId, ct);

            return saved is null
                ? (null, "The server did not accept the project.")
                : (saved.Id, null);
        }
        catch (Exception ex)
        {
            return (null, $"The project could not be saved to the server: {ex.Message}");
        }
    }

    public async Task<(ProjectFile? File, string? Name, string? Problem)> GetAsync(
        Guid id, CancellationToken ct = default)
    {
        if (!IsAvailable)
            return (null, null, "You are not signed in, so the project could not be opened.");

        try
        {
            var record = await adminClient.GetMyVideoProjectAsync(id, ct);

            if (record is null)
                return (null, null, "That project is no longer on the server.");

            // ProjectSerializer rather than a reader of its own: the editor writes every enum as a
            // string, and a reader without that converter throws on every project it is given
            // (2026-09-05 audit, persistence-1).
            var (file, problem) = ProjectSerializer.Parse(record.ProjectJson);

            return problem is not null ? (null, null, problem) : (file, record.Name, null);
        }
        catch (Exception ex)
        {
            return (null, null, $"The project could not be opened: {ex.Message}");
        }
    }
}
