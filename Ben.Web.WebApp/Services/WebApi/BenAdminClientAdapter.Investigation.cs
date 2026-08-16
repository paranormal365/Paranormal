using Ben.Data.Common.Enums;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Web.Library.Services;
using Microsoft.Extensions.Options;

namespace Ben.Web.WebApp.Services.WebApi;

/// <summary>
/// The Investigation half of the adapter — implements <see cref="Ben.Web.Library.Services.IBenInvestigationClient"/>.
/// </summary>
/// <remarks>
/// One partial class split across files by domain, matching the slices of IBenAdminClient.
/// The constructor and shared fields live in BenAdminClientAdapter.cs.
/// </remarks>
public sealed partial class BenAdminClientAdapter
{
    // ── Investigations ────────────────────────────────────────────────────────

    private static string InvBase(Guid orgId, Guid caseId)
        => $"/api/organizations/{orgId}/cases/{caseId}/investigations";

    public async Task<IReadOnlyList<InvestigationRecord>> GetInvestigationsAsync(Guid orgId, Guid caseId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<InvestigationRecord>>(InvBase(orgId, caseId), token);
        return result ?? [];
    }

    public Task<InvestigationRecord?> GetInvestigationAsync(Guid orgId, Guid caseId, Guid id, CancellationToken token = default)
        => _api.GetAsync<InvestigationRecord>($"{InvBase(orgId, caseId)}/{id}", token);

    public Task<InvestigationRecord?> CreateInvestigationAsync(Guid orgId, Guid caseId, UpsertInvestigationRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertInvestigationRequest, InvestigationRecord>(InvBase(orgId, caseId), request, token);

    public Task<InvestigationRecord?> UpdateInvestigationAsync(Guid orgId, Guid caseId, Guid id, UpsertInvestigationRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertInvestigationRequest, InvestigationRecord>($"{InvBase(orgId, caseId)}/{id}", request, token);

    public Task<bool> DeleteInvestigationAsync(Guid orgId, Guid caseId, Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"{InvBase(orgId, caseId)}/{id}", token);

    public Task<bool> CancelInvestigationByOrgAsync(Guid orgId, Guid caseId, Guid id, CancellationToken token = default)
        => _api.PostVoidAsync($"{InvBase(orgId, caseId)}/{id}/cancel", new { }, token);

    public async Task<IReadOnlyList<InvestigationAttendeeRecord>> GetInvestigationAttendeesAsync(Guid orgId, Guid caseId, Guid id, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<InvestigationAttendeeRecord>>($"{InvBase(orgId, caseId)}/{id}/attendees", token);
        return result ?? [];
    }

    public Task<InvestigationAttendeeRecord?> AddInvestigationAttendeeAsync(Guid orgId, Guid caseId, Guid id, AddInvestigationAttendeeRequest request, CancellationToken token = default)
        => _api.PostAsync<AddInvestigationAttendeeRequest, InvestigationAttendeeRecord>($"{InvBase(orgId, caseId)}/{id}/attendees", request, token);

    public Task<InvestigationAttendeeRecord?> UpdateInvestigationAttendanceAsync(Guid orgId, Guid caseId, Guid id, Guid attendeeId, bool? didAttend, string? assignedRole, Ben.Data.Common.Enums.RsvpStatus? rsvp = null, CancellationToken token = default)
        => _api.PutAsync<object, InvestigationAttendeeRecord>(
               $"{InvBase(orgId, caseId)}/{id}/attendees/{attendeeId}/attendance",
               new { DidAttend = didAttend, AssignedRole = assignedRole, Rsvp = rsvp }, token);

    public Task<bool> RemoveInvestigationAttendeeAsync(Guid orgId, Guid caseId, Guid id, Guid attendeeId, CancellationToken token = default)
        => _api.DeleteAsync($"{InvBase(orgId, caseId)}/{id}/attendees/{attendeeId}", token);

    // ── Evidence Voting ───────────────────────────────────────────────────────

    public Task<EvidenceVoteSummary?> GetEvidenceVoteSummaryAsync(Guid uploadFileId, CancellationToken token = default)
        => _api.GetAnonymousAsync<EvidenceVoteSummary>($"/api/evidence-votes/{uploadFileId}/summary", token);

    public async Task<IReadOnlyList<EvidenceVoteRecord>> GetEvidenceVotesAsync(Guid uploadFileId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<EvidenceVoteRecord>>($"/api/evidence-votes/{uploadFileId}", token);
        return result ?? [];
    }

    public Task<EvidenceVoteSummary?> CastEvidenceVoteAsync(Guid uploadFileId, Ben.Data.Common.Enums.EvidenceVoteType voteType, string? comment, CancellationToken token = default)
        => _api.PostAsync<object, EvidenceVoteSummary>(
               $"/api/evidence-votes/{uploadFileId}",
               new { VoteType = voteType, Comment = comment }, token);

    public Task<bool> RemoveEvidenceVoteAsync(Guid uploadFileId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/evidence-votes/{uploadFileId}", token);

    // ── Org-wide investigations (Area 9) ──────────────────────────────────────

    public async Task<IReadOnlyList<OrgInvestigationRow>> GetOrgInvestigationsAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrgInvestigationRow>>($"/api/organizations/{orgId}/investigations", token);
        return result ?? [];
    }

    public Task<InvestigationRecord?> CreateOrgInvestigationAsync(
        Guid orgId, CreateOrgInvestigationRequest request, CancellationToken token = default)
        => _api.PostAsync<CreateOrgInvestigationRequest, InvestigationRecord>(
            $"/api/organizations/{orgId}/investigations", request, token);

    // ── Investigation Scheduling ──────────────────────────────────────────────

    public Task<bool> CancelMyInvestigationAsync(Guid caseId, Guid investigationId, CancellationToken token = default)
        => _api.PostAsync<object, object>($"/api/my-cases/{caseId}/investigations/{investigationId}/cancel", new { }, token)
               .ContinueWith(t => t.Result is not null);

    public async Task<IReadOnlyList<ScheduleProposalDto>> GetScheduleProposalsAsync(Guid orgId, Guid caseId, CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<ScheduleProposalDto>>($"/api/orgs/{orgId}/cases/{caseId}/schedule-proposals", token) ?? [];

    public Task<ScheduleProposalDto?> CreateScheduleProposalAsync(Guid orgId, Guid caseId, CreateProposalRequest request, CancellationToken token = default)
        => _api.PostAsync<CreateProposalRequest, ScheduleProposalDto>($"/api/orgs/{orgId}/cases/{caseId}/schedule-proposals", request, token);

    public Task<bool> WithdrawScheduleProposalAsync(Guid orgId, Guid caseId, Guid proposalId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/orgs/{orgId}/cases/{caseId}/schedule-proposals/{proposalId}", token);

    public Task<ScheduleProposalDto?> ConvertProposalToInvestigationAsync(Guid orgId, Guid caseId, Guid proposalId, ConvertProposalRequest request, CancellationToken token = default)
        => _api.PostAsync<ConvertProposalRequest, ScheduleProposalDto>($"/api/orgs/{orgId}/cases/{caseId}/schedule-proposals/{proposalId}/convert", request, token);

    public async Task<IReadOnlyList<ScheduleProposalDto>> GetMyScheduleProposalsAsync(Guid caseId, CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<ScheduleProposalDto>>($"/api/my-cases/{caseId}/schedule-proposals", token) ?? [];

    public Task<ScheduleProposalDto?> AcceptScheduleProposalAsync(Guid caseId, Guid proposalId, Guid slotId, CancellationToken token = default)
        => _api.PostAsync<object, ScheduleProposalDto>($"/api/my-cases/{caseId}/schedule-proposals/{proposalId}/accept", new { SlotId = slotId }, token);

    public Task<ScheduleProposalDto?> CounterScheduleProposalAsync(Guid caseId, Guid proposalId, DateTime preferredDateTime, string? notes, CancellationToken token = default)
        => _api.PostAsync<object, ScheduleProposalDto>($"/api/my-cases/{caseId}/schedule-proposals/{proposalId}/counter", new { PreferredDateTime = preferredDateTime, Notes = notes }, token);

    public Task<ScheduleProposalDto?> DeclineScheduleProposalAsync(Guid caseId, Guid proposalId, string? notes, CancellationToken token = default)
        => _api.PostAsync<object, ScheduleProposalDto>($"/api/my-cases/{caseId}/schedule-proposals/{proposalId}/decline", new { Notes = notes }, token);

    // ── My Investigations ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<MyInvestigationItem>> GetMyInvestigationsAsync(CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<MyInvestigationItem>>("/api/my-investigations", token);
        return result ?? [];
    }

    public async Task<IReadOnlyList<AttendedInvestigationItem>> GetAttendedInvestigationsAsync(CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<AttendedInvestigationItem>>("/api/my-investigations/attended", token);
        return result ?? [];
    }

    public async Task UpdateMyInvestigationRsvpAsync(Guid attendeeId, Ben.Data.Common.Enums.RsvpStatus rsvp, CancellationToken token = default)
        => await _api.PutVoidAsync($"/api/my-investigations/{attendeeId}/rsvp", new { Rsvp = rsvp }, token);
}
