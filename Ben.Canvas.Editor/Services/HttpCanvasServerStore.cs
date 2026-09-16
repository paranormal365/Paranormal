using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ben.Canvas.Core.Options;
using Ben.Canvas.Core.Text;
using Ben.Canvas.Editor.Extensions;
using Microsoft.Extensions.Options;

namespace Ben.Canvas.Editor.Services;

/// <summary>The IsHaunted API behind <see cref="ICanvasServerStore"/>.</summary>
/// <remarks>
/// Every address is an absolute string built from <see cref="CanvasEditorOptions.ApiBaseUrl"/>. A leading-slash
/// relative path would drop the /webapi mount and reach the website instead.
/// </remarks>
public sealed class HttpCanvasServerStore(IHttpClientFactory http, IOptions<CanvasEditorOptions> options, ICanvasSignInState? signIn = null) : ICanvasServerStore
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private string? Base => string.IsNullOrWhiteSpace(options.Value.ApiBaseUrl) ? null : options.Value.ApiBaseUrl.Trim().TrimEnd('/');

    public bool IsAvailable => Base is not null && options.Value.ServerSave && (signIn?.IsSignedIn ?? true);

    public async Task<(IReadOnlyList<CanvasServerSummary>? Items, string? Problem)> ListAsync(Guid? caseId, CancellationToken ct = default)
    {
        if (Unavailable() is { } refusal) return (null, refusal);
        var url = caseId is null ? $"{Base}/api/canvas-documents" : $"{Base}/api/canvas-documents?caseId={caseId}";
        var (response, problem) = await SendAsync(new HttpRequestMessage(HttpMethod.Get, url), ct);
        if (response is null) return (null, problem);
        using (response)
        {
            if (response.StatusCode != HttpStatusCode.OK) return (null, await DescribeAsync(response, ct));
            return (await response.Content.ReadFromJsonAsync<List<CanvasServerSummary>>(Json, ct) ?? [], null);
        }
    }

    public async Task<(CanvasServerDocument? Document, string? Problem)> GetAsync(Guid id, CancellationToken ct = default)
    {
        if (Unavailable() is { } refusal) return (null, refusal);
        var (response, problem) = await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{Base}/api/canvas-documents/{id}"), ct);
        if (response is null) return (null, problem);
        using (response)
        {
            if (response.StatusCode != HttpStatusCode.OK) return (null, await DescribeAsync(response, ct));
            return (await response.Content.ReadFromJsonAsync<CanvasServerDocument>(Json, ct), null);
        }
    }

    public async Task<CanvasSaveResult> SaveAsync(string documentJson, Guid? existingId, int revision, Guid? caseId, CancellationToken ct = default)
    {
        if (Unavailable() is { } refusal) return new(CanvasSaveOutcome.Failed, null, refusal);

        HttpRequestMessage request;
        if (existingId is null)
        {
            var url = caseId is null ? $"{Base}/api/canvas-documents" : $"{Base}/api/canvas-documents?caseId={caseId}";
            request = new HttpRequestMessage(HttpMethod.Post, url);
        }
        else
        {
            request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/api/canvas-documents/{existingId}");
            request.Headers.TryAddWithoutValidation("If-Match", "\"" + revision.ToString(CultureInfo.InvariantCulture) + "\"");
        }
        request.Content = new StringContent(documentJson, Encoding.UTF8, "application/json");

        var (response, problem) = await SendAsync(request, ct);
        if (response is null) return new(CanvasSaveOutcome.Failed, null, problem);
        using (response)
        {
            if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created)
                return new(CanvasSaveOutcome.Saved, await response.Content.ReadFromJsonAsync<CanvasServerDocument>(Json, ct), null);

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var server = await response.Content.ReadFromJsonAsync<CanvasServerDocument>(Json, ct);
                return new(CanvasSaveOutcome.Conflict, server, CanvasCopy.Sentences.NewerServerCopy(server?.Revision ?? 0));
            }

            return new(CanvasSaveOutcome.Failed, null, await DescribeAsync(response, ct), response.StatusCode == HttpStatusCode.Forbidden);
        }
    }

    public async Task<(CanvasServerDocument? Document, string? Problem)> PublishAsync(Guid id, byte[] png, string fileName, CancellationToken ct = default)
    {
        if (Unavailable() is { } refusal) return (null, refusal);

        var picture = new ByteArrayContent(png);
        picture.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var form = new MultipartFormDataContent { { picture, "file", fileName + ".png" } };
        var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/api/canvas-documents/{id}/publish") { Content = form };

        var (response, problem) = await SendAsync(request, ct);
        if (response is null) return (null, problem);
        using (response)
        {
            if (response.StatusCode != HttpStatusCode.OK) return (null, await DescribeAsync(response, ct));
            return (await response.Content.ReadFromJsonAsync<CanvasServerDocument>(Json, ct), null);
        }
    }

    public async Task<(bool Ok, string? Problem)> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (Unavailable() is { } refusal) return (false, refusal);
        var (response, problem) = await SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{Base}/api/canvas-documents/{id}"), ct);
        if (response is null) return (false, problem);
        using (response)
            return response.IsSuccessStatusCode ? (true, null) : (false, await DescribeAsync(response, ct));
    }

    private string? Unavailable()
    {
        if (Base is null || !options.Value.ServerSave) return CanvasCopy.Sentences.ServerNotConfigured;
        if (signIn is { IsSignedIn: false }) return CanvasCopy.Sentences.SignedOutSave;
        return null;
    }

    private async Task<(HttpResponseMessage? Response, string? Problem)> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            var client = http.CreateClient(CanvasEditorServiceCollectionExtensions.PersistenceHttpClientName);
            return (await client.SendAsync(request, ct), null);
        }
        catch (HttpRequestException)
        {
            return (null, CanvasCopy.Sentences.ServerUnreachable);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, CanvasCopy.Sentences.ServerUnreachable);
        }
    }

    private static async Task<string> DescribeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized: return CanvasCopy.Sentences.SignInExpired;
            case HttpStatusCode.Forbidden: return CanvasCopy.Sentences.SaveForbidden;
            // A 404 while the site's canvas switch is off reads the same; the board cannot be reached either way.
            case HttpStatusCode.NotFound: return CanvasCopy.Sentences.BoardGoneFromServer;
        }

        var text = (await response.Content.ReadAsStringAsync(ct)).Trim();
        // A sentence the API wrote for people, not a problem-details JSON body.
        return text.Length is > 0 and < 400 && !text.StartsWith('{') && !text.StartsWith('<')
            ? text.Trim('"')
            : CanvasCopy.Sentences.ServerRefusedBoard;
    }
}
