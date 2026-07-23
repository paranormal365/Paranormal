using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Text.Json;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Returns driving directions between two lat/lon points using the free
/// OSRM public routing API (router.project-osrm.org).
/// </summary>
[ApiController]
[Authorize]
[Route("api/directions")]
public sealed class DirectionsController : BenControllerBase
{
    private static readonly HttpClient _http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("BenApp/1.0");
        return c;
    }

    [HttpGet]
    public async Task<ActionResult<DirectionsResult>> Get(
        [FromQuery] double fromLat, [FromQuery] double fromLon,
        [FromQuery] double toLat,   [FromQuery] double toLon,
        CancellationToken ct)
    {
        try
        {
            var fLon = fromLon.ToString("G17", CultureInfo.InvariantCulture);
            var fLat = fromLat.ToString("G17", CultureInfo.InvariantCulture);
            var tLon = toLon.ToString("G17", CultureInfo.InvariantCulture);
            var tLat = toLat.ToString("G17", CultureInfo.InvariantCulture);

            var url = $"https://router.project-osrm.org/route/v1/driving/" +
                      $"{fLon},{fLat};{tLon},{tLat}" +
                      "?geometries=geojson&overview=full&steps=true";

            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
                return StatusCode(502, "Routing service unavailable.");

            using var doc  = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0)
                return NotFound("No route found between these points.");

            var route  = routes[0];
            var leg    = route.GetProperty("legs")[0];
            var distM  = route.GetProperty("distance").GetDouble();
            var durS   = route.GetProperty("duration").GetDouble();

            // Build FeatureCollection GeoJSON from the route geometry
            var geomRaw = route.GetProperty("geometry").GetRawText();
            var featureCollection = $"{{\"type\":\"FeatureCollection\",\"features\":[{{\"type\":\"Feature\",\"geometry\":{geomRaw},\"properties\":{{}}}}]}}";

            // Parse route coordinates for map centering
            var coords    = route.GetProperty("geometry").GetProperty("coordinates");
            var routeCoords = coords.EnumerateArray()
                .Select(c => new RoutePoint(c[1].GetDouble(), c[0].GetDouble()))
                .ToList();

            // Build step-by-step instructions
            var steps = leg.GetProperty("steps").EnumerateArray()
                .Select(s =>
                {
                    var mv   = s.GetProperty("maneuver");
                    var type = mv.GetProperty("type").GetString() ?? "";
                    var mod  = mv.TryGetProperty("modifier", out var m) ? m.GetString() : null;
                    var name = s.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString())
                               ? n.GetString()! : null;
                    return new RouteStep(
                        BuildInstruction(type, mod, name),
                        s.GetProperty("distance").GetDouble() * 0.000621371,
                        s.GetProperty("duration").GetDouble());
                })
                .ToList();

            return Ok(new DirectionsResult(
                RouteGeoJson:         featureCollection,
                RoutePoints:          routeCoords,
                TotalDistanceMiles:   distM * 0.000621371,
                TotalDurationMinutes: durS / 60.0,
                Steps:                steps));
        }
        catch (TaskCanceledException)
        {
            return StatusCode(504, "Routing request timed out.");
        }
        catch
        {
            return StatusCode(502, "Unable to retrieve route. Please try again.");
        }
    }

    private static string BuildInstruction(string type, string? modifier, string? name)
    {
        var road = name ?? "the road";
        return type switch
        {
            "depart"  => $"Head {modifier ?? "forward"} on {road}",
            "arrive"  => "Arrive at your destination",
            "merge"   => $"Merge onto {road}",
            "ramp"    => modifier?.Contains("left") == true ? $"Take the left ramp onto {road}" : $"Take the right ramp onto {road}",
            "fork"    => modifier?.Contains("left") == true ? $"Keep left at fork toward {road}" : $"Keep right at fork toward {road}",
            "end of road" => modifier?.Contains("left") == true ? $"Turn left onto {road}" : $"Turn right onto {road}",
            "roundabout" or "rotary" => $"Enter the roundabout and exit onto {road}",
            "turn" => modifier switch
            {
                "left"         => $"Turn left onto {road}",
                "right"        => $"Turn right onto {road}",
                "sharp left"   => $"Turn sharp left onto {road}",
                "sharp right"  => $"Turn sharp right onto {road}",
                "slight left"  => $"Keep slight left onto {road}",
                "slight right" => $"Keep slight right onto {road}",
                _              => $"Continue onto {road}"
            },
            _ => $"Continue onto {road}"
        };
    }
}

public sealed record DirectionsResult(
    string                   RouteGeoJson,
    IReadOnlyList<RoutePoint> RoutePoints,
    double                   TotalDistanceMiles,
    double                   TotalDurationMinutes,
    IReadOnlyList<RouteStep> Steps);

public sealed record RoutePoint(double Lat, double Lon);

public sealed record RouteStep(
    string Instruction,
    double DistanceMiles,
    double DurationSeconds);
