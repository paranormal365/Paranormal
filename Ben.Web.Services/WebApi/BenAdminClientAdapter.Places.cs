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

    // ── Rooms inside a place (item 197) ───────────────────────────────────────

    private static string Rooms(Guid orgId, Guid placeId)
        => $"/api/organizations/{orgId}/places/{placeId}/rooms";

    public Task<LoadResult<PlaceRoomRecord>> GetPlaceRoomsAsync(
        Guid orgId, Guid placeId, CancellationToken token = default)
        => _api.GetListAsync<PlaceRoomRecord>(Rooms(orgId, placeId), token);

    public Task<PlaceRoomRecord?> CreatePlaceRoomAsync(
        Guid orgId, Guid placeId, SavePlaceRoomRequest request, CancellationToken token = default)
        => _api.PostAsync<SavePlaceRoomRequest, PlaceRoomRecord>(Rooms(orgId, placeId), request, token);

    public Task<PlaceRoomRecord?> UpdatePlaceRoomAsync(
        Guid orgId, Guid placeId, Guid roomId, SavePlaceRoomRequest request, CancellationToken token = default)
        => _api.PutAsync<SavePlaceRoomRequest, PlaceRoomRecord>($"{Rooms(orgId, placeId)}/{roomId}", request, token);

    public Task<bool> DeletePlaceRoomAsync(
        Guid orgId, Guid placeId, Guid roomId, CancellationToken token = default)
        => _api.DeleteAsync($"{Rooms(orgId, placeId)}/{roomId}", token);

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

    // ── Investigation duties (item 158) ──────────────────────────────────────

    public Task<InvestigationDutyBoard?> GetInvestigationDutyBoardAsync(
        Guid orgId, Guid investigationId, CancellationToken token = default)
        => _api.GetAsync<InvestigationDutyBoard>(
            $"/api/organizations/{orgId}/investigations/{investigationId}/duties", token);

    /// <summary>Assigns a duty; a 409 carries the eligibility sentence (assign again with
    /// <paramref name="overrideEligibility"/> to confirm the exception).</summary>
    public async Task<(InvestigationDutyBoard? Board, string? Refusal)> AssignInvestigationDutyAsync(
        Guid orgId, Guid investigationId, Guid attendeeId, Guid dutyId, bool overrideEligibility,
        CancellationToken token = default)
    {
        var (result, error) = await _api.SendExpectingReasonAsync<object, InvestigationDutyBoard>(
            HttpMethod.Put,
            $"/api/organizations/{orgId}/investigations/{investigationId}/attendees/{attendeeId}/duties/{dutyId}",
            new { Override = overrideEligibility }, token);
        return (result, result is null ? error ?? "The duty could not be assigned." : null);
    }

    public async Task<InvestigationDutyBoard?> UnassignInvestigationDutyAsync(
        Guid orgId, Guid investigationId, Guid attendeeId, Guid dutyId, CancellationToken token = default)
    {
        var (result, _) = await _api.SendExpectingReasonAsync<object?, InvestigationDutyBoard>(
            HttpMethod.Delete,
            $"/api/organizations/{orgId}/investigations/{investigationId}/attendees/{attendeeId}/duties/{dutyId}",
            null, token);
        return result;
    }

    // ── Duty definitions (group Settings) ────────────────────────────────────

    public Task<LoadResult<OrgInvestigationDutyItem>> GetInvestigationDutiesAsync(
        Guid orgId, CancellationToken token = default)
        => _api.GetListAsync<OrgInvestigationDutyItem>($"/api/organizations/{orgId}/investigation-duties", token);

    public Task<OrgInvestigationDutyItem?> CreateInvestigationDutyAsync(
        Guid orgId, string name, int sortOrder, bool isActive, bool isSingleHolder, Guid? minimumLevelId,
        CancellationToken token = default)
        => _api.PostAsync<object, OrgInvestigationDutyItem>($"/api/organizations/{orgId}/investigation-duties",
            new { Name = name, SortOrder = sortOrder, IsActive = isActive, IsSingleHolder = isSingleHolder, MinimumMemberLevelId = minimumLevelId }, token);

    public Task<OrgInvestigationDutyItem?> UpdateInvestigationDutyAsync(
        Guid orgId, Guid dutyId, string name, int sortOrder, bool isActive, bool isSingleHolder, Guid? minimumLevelId,
        CancellationToken token = default)
        => _api.PutAsync<object, OrgInvestigationDutyItem>($"/api/organizations/{orgId}/investigation-duties/{dutyId}",
            new { Name = name, SortOrder = sortOrder, IsActive = isActive, IsSingleHolder = isSingleHolder, MinimumMemberLevelId = minimumLevelId }, token);

    public Task<bool> DeleteInvestigationDutyAsync(Guid orgId, Guid dutyId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/investigation-duties/{dutyId}", token);

    // ── Case contacts (item 158) ─────────────────────────────────────────────

    public Task<LoadResult<CaseContactItem>> GetCaseContactsAsync(
        Guid orgId, Guid caseId, CancellationToken token = default)
        => _api.GetListAsync<CaseContactItem>($"/api/orgs/{orgId}/cases/{caseId}/contacts", token);

    public async Task<(IReadOnlyList<CaseContactItem> Contacts, string? Error)> SetCaseContactsAsync(
        Guid orgId, Guid caseId, IReadOnlyList<Guid> appUserIds, CancellationToken token = default)
    {
        var (result, error) = await _api.SendExpectingReasonAsync<object, IReadOnlyList<CaseContactItem>>(
            HttpMethod.Put, $"/api/orgs/{orgId}/cases/{caseId}/contacts",
            new { AppUserIds = appUserIds }, token);

        // A refused save answers with the empty list AND the reason together, never the empty
        // list alone — the tuple is what keeps this from being the swallowed-refusal pattern.
        if (result is null)
            return (Array.Empty<CaseContactItem>(), error ?? "The contacts could not be saved.");

        return (result, null);
    }

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

    /// <inheritdoc />
    public Task<LoadResult<DuplicatePlaceGroup>> GetDuplicatePlacesAsync(CancellationToken token = default)
        => _api.GetListAsync<DuplicatePlaceGroup>("/api/admin/places/duplicates", token);

    /// <inheritdoc />
    public Task<LoadResult<OrphanedFieldSessionRecord>> GetOrphanedFieldSessionsAsync(
        CancellationToken token = default)
        => _api.GetListAsync<OrphanedFieldSessionRecord>("/api/admin/orphaned-field-sessions", token);

    /// <inheritdoc />
    public Task<(OrphanedFieldSessionPurgeResult? Result, string? Error)> PurgeOrphanedFieldSessionsAsync(
        int expectedCount, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, OrphanedFieldSessionPurgeResult>(
            HttpMethod.Delete, $"/api/admin/orphaned-field-sessions?expectedCount={expectedCount}",
            new { }, token);

    /// <inheritdoc />
    public Task<(PlaceMergeResult? Result, string? Error)> MergePlaceAsync(
        Guid losingPlaceId, Guid intoPlaceId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<MergePlaceRequest, PlaceMergeResult>(
            HttpMethod.Post, $"/api/admin/places/{losingPlaceId}/merge",
            new MergePlaceRequest(intoPlaceId), token);
}

/// <summary>The body the merge endpoint expects.</summary>
public sealed record MergePlaceRequest(Guid IntoPlaceId);
