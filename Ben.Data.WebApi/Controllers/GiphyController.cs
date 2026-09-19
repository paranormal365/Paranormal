using System.Net.Http.Json;
using System.Text.Json;
using Ben.Service.Models.Feed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Giphy, through us (item 233, Ben 2026-09-11).
/// </summary>
/// <remarks>
/// <para><b>Why this exists at all.</b> The obvious way to add a GIF picker is to call Giphy from
/// the page, which means shipping the key to every browser that loads it — where anyone can read
/// it out of the network tab and spend our quota. The key stays here; the page asks us.</para>
///
/// <para>What comes back is trimmed to what a picker needs: an id, a preview, the full image, and
/// the alt text Giphy holds. Passing their whole payload through would make every field they ever
/// add part of our contract.</para>
/// </remarks>
[ApiController]
[Route("api/giphy")]
public sealed class GiphyController : BenControllerBase
{
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GiphyController> _logger;

    public GiphyController(
        IHttpClientFactory http, IConfiguration configuration, ILogger<GiphyController> logger)
    { _http = http; _configuration = configuration; _logger = logger; }

    /// <summary>How many to bring back. A picker shows a couple of rows, not a catalogue.</summary>
    private const int Limit = 24;

    /// <summary>
    /// Search, or the trending ones when nothing has been typed yet.
    /// </summary>
    /// <remarks>
    /// Signed in only. The quota is ours and an open proxy is somebody else's free Giphy account.
    /// </remarks>
    [HttpGet("search")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<GiphyItem>>> Search(
        [FromQuery] string? q, CancellationToken ct)
    {
        var key = _configuration["GiphyAPI"];
        if (string.IsNullOrWhiteSpace(key))
        {
            // Not configured is not an error a reader should see as a crash: the button is simply
            // not offered, and the log says why for whoever set the server up.
            _logger.LogInformation("GiphyAPI is not configured; the GIF picker is unavailable.");
            return NotFound();
        }

        var term = q?.Trim();
        var url = string.IsNullOrWhiteSpace(term)
            ? $"https://api.giphy.com/v1/gifs/trending?api_key={key}&limit={Limit}&rating=pg-13"
            : $"https://api.giphy.com/v1/gifs/search?api_key={key}&limit={Limit}&rating=pg-13"
              + $"&q={Uri.EscapeDataString(term)}";

        try
        {
            var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(8);

            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Giphy answered {Status} for a {Kind}.",
                    (int)response.StatusCode, term is null ? "trending request" : "search");
                return StatusCode(StatusCodes.Status502BadGateway);
            }

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (!payload.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return Ok(Array.Empty<GiphyItem>());

            var items = new List<GiphyItem>();
            foreach (var gif in data.EnumerateArray())
            {
                if (Pick(gif) is { } item) items.Add(item);
            }

            return Ok(items);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // Their timeout, not the caller's. A picker that spins forever is worse than one that
            // says it could not reach them.
            _logger.LogWarning("Giphy did not answer in time.");
            return StatusCode(StatusCodes.Status504GatewayTimeout);
        }
        catch (HttpRequestException e)
        {
            _logger.LogWarning(e, "Giphy could not be reached.");
            return StatusCode(StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>The key the iOS app's own Giphy SDK needs, for a signed-in app.</summary>
    /// <remarks>
    /// <para>Served rather than compiled into the app so it can be rotated without shipping a
    /// build. A different key from the one above on purpose: Giphy issues SDK keys separately, and
    /// an app that shipped the server key would put it on every phone.</para>
    ///
    /// <para>Wrapped in an object rather than returned as a bare string. MVC serves a string
    /// result as <c>text/plain</c>, and a key is not valid JSON, so any client reading this the
    /// way every other endpoint is read would throw on it. Nothing consumes it yet, which is
    /// exactly when it is free to fix (2026-09-11, item 232).</para>
    /// </remarks>
    [HttpGet("sdk-key")]
    [Authorize]
    public ActionResult<GiphySdkKeyRecord> SdkKey()
    {
        var key = _configuration["GiphySdk"];
        return string.IsNullOrWhiteSpace(key) ? NotFound() : Ok(new GiphySdkKeyRecord(key));
    }

    /// <summary>
    /// The two images a picker needs, or null when this entry has neither.
    /// </summary>
    /// <remarks>
    /// <c>fixed_width</c> for the grid and <c>original</c> for what gets posted. Both are asked
    /// for by name rather than taken positionally: Giphy's <c>images</c> object has dozens of
    /// renditions and their order is not a contract.
    /// </remarks>
    private static GiphyItem? Pick(JsonElement gif)
    {
        if (!gif.TryGetProperty("id", out var id)) return null;
        if (!gif.TryGetProperty("images", out var images)) return null;

        var preview = UrlOf(images, "fixed_width") ?? UrlOf(images, "original");
        var full = UrlOf(images, "original") ?? preview;
        if (preview is null || full is null) return null;

        var title = gif.TryGetProperty("title", out var t) ? t.GetString() : null;
        return new GiphyItem(id.GetString() ?? "", preview, full,
            string.IsNullOrWhiteSpace(title) ? "A GIF" : title);
    }

    private static string? UrlOf(JsonElement images, string rendition)
        => images.TryGetProperty(rendition, out var r)
        && r.TryGetProperty("url", out var u)
            ? u.GetString()
            : null;
}
