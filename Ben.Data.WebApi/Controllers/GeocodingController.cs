using Ben.Data.WebApi.Services;
using Ben.Service.RepositoryService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Lightweight geocoding utilities used by the Blazor admin UI for live map
/// preview while the user is authoring an address.  No data is persisted.
/// </summary>
[ApiController]
[Authorize]
[Route("api/geocode")]
public sealed class GeocodingController : ControllerBase
{
    /// <summary>
    /// Resolves coordinates for a postal address without saving anything.
    /// Used by the address form to move the map pin as the user types.
    /// </summary>
    [HttpGet("preview")]
    public async Task<ActionResult<GeocodingPreviewResponse>> Preview(
        [FromQuery] string streetAddress1,
        [FromQuery] string? streetAddress2,
        [FromQuery] string city,
        [FromQuery] string state,
        [FromQuery] string zipCode,
        [FromQuery] string country,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(streetAddress1) ||
            string.IsNullOrWhiteSpace(city) ||
            string.IsNullOrWhiteSpace(state) ||
            string.IsNullOrWhiteSpace(zipCode) ||
            string.IsNullOrWhiteSpace(country))
        {
            return Ok(new GeocodingPreviewResponse(null, null, null));
        }

        var result = await AddressGeocodingService.TryResolveCoordinatesAsync(
            streetAddress1, streetAddress2, city, state, zipCode, country, ct);

        return Ok(new GeocodingPreviewResponse(result.Latitude, result.Longitude, result.ResultType));
    }

    /// <summary>
    /// Reverse-geocodes a lat/lon pair into a structured postal address.
    /// Used by the Blazor form's "Use my location" button.
    /// </summary>
    [HttpGet("reverse")]
    public async Task<ActionResult<ReverseGeocodingResponse>> Reverse(
        [FromQuery] double latitude,
        [FromQuery] double longitude,
        CancellationToken ct)
    {
        var result = await AddressGeocodingService.ReverseGeocodeAsync(latitude, longitude, ct);
        return Ok(new ReverseGeocodingResponse(
            result.StreetAddress1,
            result.City,
            result.State,
            result.ZipCode,
            result.Country));
    }
}

public sealed record GeocodingPreviewResponse(
    decimal? Latitude,
    decimal? Longitude,
    string? ResultType);

public sealed record ReverseGeocodingResponse(
    string? StreetAddress1,
    string? City,
    string? State,
    string? ZipCode,
    string? Country);
