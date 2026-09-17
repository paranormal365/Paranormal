using System.Net.Http.Json;
using Ben.Canvas.Core.Options;
using Ben.Canvas.Editor.Extensions;
using Microsoft.Extensions.Options;

namespace Ben.Canvas.Editor.Services;

/// <summary>Where a written address is on the earth.</summary>
/// <remarks>
/// Ben, 2026-09-17: "Be able to look up address to make the map." A map box could always be given
/// coordinates, and an address was only ever its label — so making a map of a house meant finding
/// the numbers somewhere else first, which is the sort of errand that ends with no map.
/// <para>The API's <c>api/geocode/search</c> answers anonymously and is rate limited there, because
/// every call spends the same Apple Maps allowance as the maps themselves. A board open on somebody's
/// own machine with no account can use it, which is the board most likely to be the first one.</para>
/// <para>Nothing found and could-not-ask are the same answer here — <c>null</c>. The caller says so in
/// a sentence and leaves what the person typed alone: an address the geocoder does not know is still
/// worth keeping on the board.</para>
/// </remarks>
public interface ICanvasGeocoder
{
    /// <summary>The place that address names, or null when it is not found or cannot be asked for.</summary>
    Task<CanvasGeocoderResult?> FindAsync(string? address, CancellationToken ct = default);
}

/// <summary>A found place: where it is, and how exactly the geocoder knew it.</summary>
public sealed record CanvasGeocoderResult(double Latitude, double Longitude, string? ResultType)
{
    /// <summary>
    /// How close to zoom in. A rooftop is a building and takes the closest look; a town or a region
    /// is a shape, and a street-level zoom on one shows an arbitrary corner of it.
    /// </summary>
    public double Zoom => ResultType?.ToLowerInvariant() switch
    {
        "rooftop" => 17,
        "street" or "streetaddress" or "address" => 16,
        "place" or "pointofinterest" => 14,
        _ => 13,
    };
}

/// <inheritdoc cref="ICanvasGeocoder"/>
public sealed class CanvasGeocoder(IHttpClientFactory http, IOptions<CanvasEditorOptions> options) : ICanvasGeocoder
{
    private sealed record Answer(double? Latitude, double? Longitude, string? ResultType);

    public async Task<CanvasGeocoderResult?> FindAsync(string? address, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;

        var apiBase = options.Value.ApiBaseUrl?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(apiBase)) return null;

        var url = $"{apiBase}/geocode/search?q={Uri.EscapeDataString(address.Trim())}";

        try
        {
            var client = http.CreateClient(CanvasEditorServiceCollectionExtensions.PublicHttpClientName);
            var answer = await client.GetFromJsonAsync<Answer>(url, ct);
            return answer is { Latitude: { } lat, Longitude: { } lng }
                ? new CanvasGeocoderResult(lat, lng, answer.ResultType)
                : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException
                                      or System.Text.Json.JsonException or NotSupportedException or UriFormatException)
        {
            return null;
        }
    }
}
