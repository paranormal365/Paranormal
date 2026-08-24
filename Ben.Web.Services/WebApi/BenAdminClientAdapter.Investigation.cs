using Ben.Data.Common.Enums;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Web.Services;
using Microsoft.Extensions.Options;

namespace Ben.Web.Services.WebApi;

/// <summary>
/// The Investigation half of the adapter — implements <see cref="Ben.Web.Services.IBenInvestigationClient"/>.
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

    public Task<LoadResult<InvestigationRecord>> GetInvestigationsAsync(Guid orgId, Guid caseId, CancellationToken token = default)
        => _api.GetListAsync<InvestigationRecord>(InvBase(orgId, caseId), token);

    public Task<InvestigationRecord?> GetInvestigationAsync(Guid orgId, Guid caseId, Guid id, CancellationToken token = default)
        => _api.GetAsync<InvestigationRecord>($"{InvBase(orgId, caseId)}/{id}", token);

    // Reason-carrying (item 184): binding a residence place can refuse with the plan sentence,
    // and the investigation dialog must render it rather than "Save failed."
    public Task<(InvestigationRecord? Result, string? Error)> CreateInvestigationAsync(Guid orgId, Guid caseId, UpsertInvestigationRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<UpsertInvestigationRequest, InvestigationRecord>(
               HttpMethod.Post, InvBase(orgId, caseId), request, token);

    public Task<(InvestigationRecord? Result, string? Error)> UpdateInvestigationAsync(Guid orgId, Guid caseId, Guid id, UpsertInvestigationRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<UpsertInvestigationRequest, InvestigationRecord>(
               HttpMethod.Put, $"{InvBase(orgId, caseId)}/{id}", request, token);

    public Task<bool> DeleteInvestigationAsync(Guid orgId, Guid caseId, Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"{InvBase(orgId, caseId)}/{id}", token);

    public Task<bool> CancelInvestigationByOrgAsync(Guid orgId, Guid caseId, Guid id, CancellationToken token = default)
        => _api.PostVoidAsync($"{InvBase(orgId, caseId)}/{id}/cancel", new { }, token);

    public Task<LoadResult<InvestigationAttendeeRecord>> GetInvestigationAttendeesAsync(Guid orgId, Guid caseId, Guid id, CancellationToken token = default)
        => _api.GetListAsync<InvestigationAttendeeRecord>($"{InvBase(orgId, caseId)}/{id}/attendees", token);

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

    public Task<LoadResult<EvidenceVoteRecord>> GetEvidenceVotesAsync(Guid uploadFileId, CancellationToken token = default)
        => _api.GetListAsync<EvidenceVoteRecord>($"/api/evidence-votes/{uploadFileId}", token);

    public Task<EvidenceVoteSummary?> CastEvidenceVoteAsync(Guid uploadFileId, Ben.Data.Common.Enums.EvidenceVoteType voteType, string? comment, CancellationToken token = default)
        => _api.PostAsync<object, EvidenceVoteSummary>(
               $"/api/evidence-votes/{uploadFileId}",
               new { VoteType = voteType, Comment = comment }, token);

    public Task<bool> RemoveEvidenceVoteAsync(Guid uploadFileId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/evidence-votes/{uploadFileId}", token);

    // ── Org-wide investigations (Area 9) ──────────────────────────────────────

    public Task<LoadResult<OrgInvestigationRow>> GetOrgInvestigationsAsync(Guid orgId, CancellationToken token = default)
        => _api.GetListAsync<OrgInvestigationRow>($"/api/organizations/{orgId}/investigations", token);

    public Task<(InvestigationRecord? Result, string? Error)> CreateOrgInvestigationAsync(
        Guid orgId, CreateOrgInvestigationRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<CreateOrgInvestigationRequest, InvestigationRecord>(
            HttpMethod.Post, $"/api/organizations/{orgId}/investigations", request, token);

    // ── Investigation Scheduling ──────────────────────────────────────────────

    public Task<bool> CancelMyInvestigationAsync(Guid caseId, Guid investigationId, CancellationToken token = default)
        => _api.PostAsync<object, object>($"/api/my-cases/{caseId}/investigations/{investigationId}/cancel", new { }, token)
               .ContinueWith(t => t.Result is not null);

    public Task<LoadResult<ScheduleProposalDto>> GetScheduleProposalsAsync(Guid orgId, Guid caseId, CancellationToken token = default)
        => _api.GetListAsync<ScheduleProposalDto>($"/api/orgs/{orgId}/cases/{caseId}/schedule-proposals", token);

    public Task<ScheduleProposalDto?> CreateScheduleProposalAsync(Guid orgId, Guid caseId, CreateProposalRequest request, CancellationToken token = default)
        => _api.PostAsync<CreateProposalRequest, ScheduleProposalDto>($"/api/orgs/{orgId}/cases/{caseId}/schedule-proposals", request, token);

    public Task<bool> WithdrawScheduleProposalAsync(Guid orgId, Guid caseId, Guid proposalId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/orgs/{orgId}/cases/{caseId}/schedule-proposals/{proposalId}", token);

    public Task<ScheduleProposalDto?> ConvertProposalToInvestigationAsync(Guid orgId, Guid caseId, Guid proposalId, ConvertProposalRequest request, CancellationToken token = default)
        => _api.PostAsync<ConvertProposalRequest, ScheduleProposalDto>($"/api/orgs/{orgId}/cases/{caseId}/schedule-proposals/{proposalId}/convert", request, token);

    public Task<LoadResult<ScheduleProposalDto>> GetMyScheduleProposalsAsync(Guid caseId, CancellationToken token = default)
        => _api.GetListAsync<ScheduleProposalDto>($"/api/my-cases/{caseId}/schedule-proposals", token);

    public Task<ScheduleProposalDto?> AcceptScheduleProposalAsync(Guid caseId, Guid proposalId, Guid slotId, CancellationToken token = default)
        => _api.PostAsync<object, ScheduleProposalDto>($"/api/my-cases/{caseId}/schedule-proposals/{proposalId}/accept", new { SlotId = slotId }, token);

    public Task<ScheduleProposalDto?> CounterScheduleProposalAsync(Guid caseId, Guid proposalId, DateTime preferredDateTime, string? notes, CancellationToken token = default)
        => _api.PostAsync<object, ScheduleProposalDto>($"/api/my-cases/{caseId}/schedule-proposals/{proposalId}/counter", new { PreferredDateTime = preferredDateTime, Notes = notes }, token);

    public Task<ScheduleProposalDto?> DeclineScheduleProposalAsync(Guid caseId, Guid proposalId, string? notes, CancellationToken token = default)
        => _api.PostAsync<object, ScheduleProposalDto>($"/api/my-cases/{caseId}/schedule-proposals/{proposalId}/decline", new { Notes = notes }, token);

    // ── My Investigations ───────────────────────────────────────────────────

    public Task<LoadResult<MyInvestigationItem>> GetMyInvestigationsAsync(CancellationToken token = default)
        => _api.GetListAsync<MyInvestigationItem>("/api/my-investigations", token);

    public Task<LoadResult<AttendedInvestigationItem>> GetAttendedInvestigationsAsync(CancellationToken token = default)
        => _api.GetListAsync<AttendedInvestigationItem>("/api/my-investigations/attended", token);

    public async Task UpdateMyInvestigationRsvpAsync(Guid attendeeId, Ben.Data.Common.Enums.RsvpStatus rsvp, CancellationToken token = default)
        => await _api.PutVoidAsync($"/api/my-investigations/{attendeeId}/rsvp", new { Rsvp = rsvp }, token);
}
