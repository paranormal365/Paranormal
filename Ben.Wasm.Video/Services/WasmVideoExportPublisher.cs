using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EditorServices = Ben.Video.Editor.Extensions.ServiceCollectionExtensions;
using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Wasm.Video.Services;

/// <summary>
/// Sends a finished render from the standalone editor up to the server.
/// </summary>
/// <remarks>
/// <para>This host could not publish at all. The editor only offers a destination when the page
/// supplies a publish handler, and this page supplied none — so every export went straight to the
/// downloads folder, with no way to put it anywhere else. The sign-in page meanwhile promised that
/// signing in lets you "publish finished renders" (2026-09-05 audit, F12).</para>
///
/// <para>The publish endpoint attaches a video to an <i>existing</i> project row and gives a 404
/// without one, so a person who rendered without ever saving to the server has nothing to publish
/// against. That case saves the project first and publishes to what it just created — the same
/// two-step the site host does, arrived at for the same reason.</para>
///
/// <para><b>Throws on every failure.</b> That is the contract the editor's destination prompt
/// asks for: it catches, stays open, and keeps "Save to my machine" available. Returning normally
/// tells the editor the video is safely on the server, at which point it discards the only
/// remaining copy — so a swallowed error here loses somebody's render.</para>
/// </remarks>
public sealed class WasmVideoExportPublisher(
    IHttpClientFactory httpClientFactory,
    ProjectService projects,
    ProjectStore projectStore)
{
    /// <summary>
    /// Publishes <paramref name="exported"/> against the project currently open, creating the
    /// server-side row first when there is not one yet.
    /// </summary>
    /// <returns>The server project id the video was attached to.</returns>
    public async Task<Guid> PublishAsync(ExportedVideo exported, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient(EditorServices.ProjectPersistenceHttpClientName);

        var projectId = projectStore.CurrentServerId ?? await CreateProjectAsync(client, ct);

        // The one point the render lands in .NET memory — deliberately not read until there is
        // somewhere to put it.
        var bytes = await exported.ReadBytesAsync()
            ?? throw new InvalidOperationException("Couldn't read the rendered file back from the browser.");

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(exported.ContentType);
        content.Add(fileContent, "file", exported.FileName);

        var response = await client.PostAsync($"api/video-projects/{projectId}/publish", content, ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await DescribeAsync(response, "upload the video", ct));

        // Remembered so a second export in the same session updates the same project instead of
        // piling up a row per render.
        projectStore.CurrentServerId = projectId;
        return projectId;
    }

    private async Task<Guid> CreateProjectAsync(HttpClient client, CancellationToken ct)
    {
        var file = projects.BuildCurrentProjectFile(projectStore.CurrentProjectName);

        var body = new StringContent(
            ProjectSerializer.Serialize(file), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("api/video-projects", body, ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                await DescribeAsync(response, "save the project to the server", ct));

        var created = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

        return created.TryGetProperty("id", out var id) && id.TryGetGuid(out var guid)
            ? guid
            : throw new InvalidOperationException(
                "The server saved the project but did not say which one, so the video could not be attached.");
    }

    /// <summary>
    /// Turns a failed response into something worth showing somebody.
    /// </summary>
    /// <remarks>
    /// The status code alone is not much use to the person holding an unsaved render. 401 in
    /// particular has an obvious action attached to it, and saying so is the difference between a
    /// prompt they can act on and one they cannot.
    /// </remarks>
    private static async Task<string> DescribeAsync(
        HttpResponseMessage response, string what, CancellationToken ct)
    {
        var detail = await SafeReadAsync(response, ct);

        return response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized =>
                $"Your sign-in has expired, so the editor could not {what}. Sign in again and try "
                + "the upload once more — the render is still here.",

            System.Net.HttpStatusCode.Forbidden =>
                $"Your account is not allowed to {what}.",

            System.Net.HttpStatusCode.RequestEntityTooLarge =>
                "The server refused the file for being too large.",

            _ => $"The server would not {what} ({(int)response.StatusCode})."
                 + (string.IsNullOrWhiteSpace(detail) ? "" : $" {detail}"),
        };
    }

    private static async Task<string?> SafeReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            return text.Length > 300 ? text[..300] : text;
        }
        catch
        {
            return null;
        }
    }
}
