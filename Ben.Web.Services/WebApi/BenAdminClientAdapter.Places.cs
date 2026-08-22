using Ben.Data.Common.Enums;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Web.Services;
using Microsoft.Extensions.Options;

namespace Ben.Web.Services.WebApi;

/// <summary>
/// The Places half of the adapter — implements <see cref="Ben.Web.Services.IBenPlacesClient"/>.
/// </summary>
/// <remarks>
/// One partial class split across files by domain, matching the slices of IBenAdminClient.
/// The constructor and shared fields live in BenAdminClientAdapter.cs.
/// </remarks>
public sealed partial class BenAdminClientAdapter
{
    // ── Places (Area 9) ───────────────────────────────────────────────────────

    public Task<PlaceRecord?> GetPlaceAsync(Guid placeId, CancellationToken token = default)
        => _api.GetAsync<PlaceRecord>($"/api/places/{placeId}", token);

    public Task<LoadResult<PlaceInvestigationRow>> GetPlaceInvestigationsAsync(
        Guid placeId, CancellationToken token = default)
        => _api.GetListAsync<PlaceInvestigationRow>($"/api/places/{placeId}/investigations", token);

    public Task<PlaceSummary?> GetPlaceSummaryAsync(Guid placeId, CancellationToken token = default)
        => _api.GetAsync<PlaceSummary>($"/api/places/{placeId}/summary", token);

    public Task<LoadResult<PlaceCandidate>> FindPlaceCandidatesAsync(
        string? street, string? city, string? state, string? zip, string? name,
        decimal? latitude, decimal? longitude, CancellationToken token = default)
    {
        var query = new List<string>();
        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                query.Add($"{key}={Uri.EscapeDataString(value)}");
        }

        Add("street", street);
        Add("city", city);
        Add("state", state);
        Add("zip", zip);
        Add("name", name);
        // Invariant culture on purpose: a decimal formatted under a comma-decimal locale would
        // arrive as a different number, and this one is compared against a tenth of a mile.
        Add("latitude", latitude?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add("longitude", longitude?.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return _api.GetListAsync<PlaceCandidate>(
            $"/api/places/candidates?{string.Join("&", query)}", token);
    }

    public Task<PublicPlaceResponse?> GetPublicPlaceAsync(Guid placeId, CancellationToken token = default)
        => _api.GetAnonymousAsync<PublicPlaceResponse>($"/api/public/places/{placeId}", token);

    public Task<LoadResult<InvestigationFindingRecord>> GetInvestigationFindingsAsync(
        Guid orgId, Guid investigationId, CancellationToken token = default)
        => _api.GetListAsync<InvestigationFindingRecord>($"/api/organizations/{orgId}/investigations/{investigationId}/findings", token);

    public Task<InvestigationFindingRecord?> SaveMyInvestigationFindingAsync(
        Guid orgId, Guid investigationId, string narrative, CancellationToken token = default)
        => _api.PutAsync<object, InvestigationFindingRecord>(
            $"/api/organizations/{orgId}/investigations/{investigationId}/findings/mine",
            new { Narrative = narrative }, token);

    public Task<bool> DeleteMyInvestigationFindingAsync(
        Guid orgId, Guid investigationId, CancellationToken token = default)
        => _api.DeleteAsync(
            $"/api/organizations/{orgId}/investigations/{investigationId}/findings/mine", token);

    /// <summary>
    /// Sets or clears an attendee's lead flag and returns the roster as it now stands.
    /// </summary>
    /// <remarks>
    /// A save, so no <see cref="LoadResult{T}"/> — but it had the same defect. A refused PUT became
    /// <c>null</c> and then an empty roster, which reads as "this investigation now has nobody on
    /// it": the opposite of what the caller just asked for, presented as the result of asking.
    /// </remarks>
    public async Task<(IReadOnlyList<InvestigationRosterEntry> Roster, string? Error)> SetInvestigationLeadAsync(
        Guid orgId, Guid investigationId, Guid attendeeId, bool isLead, CancellationToken token = default)
    {
        var (result, error) = await _api.SendExpectingReasonAsync<object, IReadOnlyList<InvestigationRosterEntry>>(
            HttpMethod.Put,
            $"/api/organizations/{orgId}/investigations/{investigationId}/attendees/{attendeeId}/lead",
            new { IsLead = isLead }, token);

        if (result is null)
            return ([], error ?? "The lead could not be changed.");

        return (result, null);
    }

    public Task<LoadResult<InvestigationRosterEntry>> GetInvestigationRosterAsync(
        Guid orgId, Guid investigationId, CancellationToken token = default)
        => _api.GetListAsync<InvestigationRosterEntry>($"/api/organizations/{orgId}/investigations/{investigationId}/roster", token);

    public Task<InvestigationRosterEntry?> CheckInToInvestigationAsync(
        Guid orgId, Guid investigationId, DateTime? statedArrivalTime = null, CancellationToken token = default)
        => _api.PostAsync<object, InvestigationRosterEntry>(
            $"/api/organizations/{orgId}/investigations/{investigationId}/check-in",
            new { StatedArrivalTime = statedArrivalTime }, token);

    public Task<InvestigationRosterEntry?> OverrideInvestigationAttendanceAsync(
        Guid orgId, Guid investigationId, Guid attendeeId, bool? didAttend,
        DateTime? statedArrivalTime = null, CancellationToken token = default)
        => _api.PutAsync<object, InvestigationRosterEntry>(
            $"/api/organizations/{orgId}/investigations/{investigationId}/attendees/{attendeeId}/attendance",
            new { DidAttend = didAttend, StatedArrivalTime = statedArrivalTime }, token);

    // ── Directions ────────────────────────────────────────────────────────────
    public Task<DirectionsResult?> GetDirectionsAsync(double fromLat, double fromLon, double toLat, double toLon, CancellationToken token = default)
    {
        var fLat = fromLat.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
        var fLon = fromLon.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
        var tLat = toLat.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
        var tLon = toLon.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
        return _api.GetAsync<DirectionsResult>($"/api/directions?fromLat={fLat}&fromLon={fLon}&toLat={tLat}&toLon={tLon}", token);
    }
}
