using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ben.Video.Editor.Extensions;
using Ben.Video.Editor.Models;
using Microsoft.Extensions.Options;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Saves a project over HTTP, for a host whose browser carries its own credentials.
/// </summary>
/// <remarks>
/// The standalone editor's implementation: the browser is the caller, so the named client's
/// bearer handler attaches the token that host already holds. On a Blazor Server host the token
/// lives in the circuit and this cannot reach it — which is exactly why the interface exists.
/// </remarks>
public sealed class HttpProjectServerStore(
    IHttpClientFactory httpClientFactory,
    IOptions<VideoEditorOptions> options) : IProjectServerStore
{
    private readonly VideoEditorOptions _options = options.Value;

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_options.DocumentPostUrl);

    public async Task<(Guid? Id, string? Problem)> SaveAsync(
        ProjectFile file, Guid? existingId, Guid? caseId = null, CancellationToken ct = default)
    {
        if (_options.DocumentPostUrl is not { Length: > 0 } baseUrl)
            return (null, "This editor is not configured to save to a server.");

        var client = httpClientFactory.CreateClient(
            ServiceCollectionExtensions.ProjectPersistenceHttpClientName);

        var content = new StringContent(ProjectSerializer.Serialize(file), Encoding.UTF8, "application/json");

        // PUT when the project already exists there. Every save used to POST, so saving five times
        // made five projects (2026-09-05 audit, persistence-13).
        var response = existingId is { } id
            ? await client.PutAsync($"{baseUrl.TrimEnd('/')}/{id}", content, ct)
            : await client.PostAsync(
                caseId is { } c ? $"{baseUrl}?caseId={c}" : baseUrl, content, ct);

        if (!response.IsSuccessStatusCode)
            return (null, Describe(response.StatusCode));

        // An update answers with the row it updated; a create answers with the one it made. Either
        // way the id is what the caller needs, so the next save updates instead of creating.
        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (body.TryGetProperty("id", out var idProperty) && idProperty.TryGetGuid(out var guid))
                return (guid, null);
        }
        catch (JsonException)
        {
            // A success with an unreadable body still saved the project; only the id is lost, and
            // the next save creates a second row rather than losing anything.
        }

        return (existingId, null);
    }

    private static string Describe(System.Net.HttpStatusCode status) => status switch
    {
        System.Net.HttpStatusCode.Unauthorized =>
            "Your sign-in has expired. Sign in again and save once more.",
        System.Net.HttpStatusCode.Forbidden =>
            "Your account is not allowed to save projects to the server.",
        System.Net.HttpStatusCode.NotFound =>
            "That project is no longer on the server. Saving again will create a new one.",
        _ => $"The server would not save the project ({(int)status}).",
    };
}
