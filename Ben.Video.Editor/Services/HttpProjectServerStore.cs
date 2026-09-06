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
    IOptions<VideoEditorOptions> options,
    IEditorSignInState? signInState = null) : IProjectServerStore
{
    private readonly VideoEditorOptions _options = options.Value;

    /// <summary>
    /// A server to save to, and somebody to save it as.
    /// </summary>
    /// <remarks>
    /// A configured URL alone was the old answer, and it offered Save to Server to somebody who
    /// was signed out — a button that could only ever answer 401. A host that can say whether
    /// anybody is signed in gets asked; one that cannot keeps the old behaviour, which is right
    /// for a host with no accounts.
    /// </remarks>
    public bool IsAvailable =>
        !string.IsNullOrWhiteSpace(_options.DocumentPostUrl)
        && (signInState?.IsSignedIn ?? true);

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

    public async Task<(ProjectFile? File, string? Name, string? Problem)> GetAsync(
        Guid id, CancellationToken ct = default)
    {
        if (_options.DocumentPostUrl is not { Length: > 0 } baseUrl)
            return (null, null, "This editor is not configured to open projects from a server.");

        var client = httpClientFactory.CreateClient(
            ServiceCollectionExtensions.ProjectPersistenceHttpClientName);

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync($"{baseUrl.TrimEnd('/')}/{id}", ct);
        }
        catch (HttpRequestException)
        {
            return (null, null, "Could not reach the server.");
        }

        if (!response.IsSuccessStatusCode)
            return (null, null, Describe(response.StatusCode));

        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            var json = body.TryGetProperty("projectJson", out var stored) ? stored.GetString() : null;
            var name = body.TryGetProperty("name", out var named) ? named.GetString() : null;

            if (string.IsNullOrWhiteSpace(json))
                return (null, null, "The server returned a project with nothing in it.");

            // ProjectSerializer and not a fresh options object: the editor writes every enum as a
            // string, and a reader without that converter throws on every project it is given
            // (2026-09-05 audit, persistence-1).
            var (file, problem) = ProjectSerializer.Parse(json);

            return problem is not null ? (null, null, problem) : (file, name, null);
        }
        catch (JsonException)
        {
            return (null, null, "The server's answer could not be read as a project.");
        }
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
