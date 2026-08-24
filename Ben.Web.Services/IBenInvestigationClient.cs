using Ben.Web.Services.WebApi;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Data.Common.Enums;

namespace Ben.Web.Services;

/// <summary>
/// The Investigation slice of <see cref="IBenAdminClient"/> — investigations, their scheduling and evidence.
/// </summary>
/// <remarks>
/// Part of splitting one 383-method interface into domain-sized pieces.
/// <see cref="IBenAdminClient"/> inherits every slice, so existing callers and the single
/// adapter are unchanged; new code (and test doubles) can depend on just the slice it needs.
/// </remarks>
public interface IBenInvestigationClient
{
    // ── Investigations ────────────────────────────────────────────────────────

    Task<LoadResult<InvestigationRecord>> GetInvestigationsAsync(Guid orgId, Guid caseId, CancellationToken token = default);
    Task<InvestigationRecord?> GetInvestigationAsync(Guid orgId, Guid caseId, Guid id, CancellationToken token = default);
    Task<(InvestigationRecord? Result, string? Error)> CreateInvestigationAsync(Guid orgId, Guid caseId, UpsertInvestigationRequest request, CancellationToken token = default);
    Task<(InvestigationRecord? Result, string? Error)> UpdateInvestigationAsync(Guid orgId, Guid caseId, Guid id, UpsertInvestigationRequest request, CancellationToken token = default);
    Task<bool> DeleteInvestigationAsync(Guid orgId, Guid caseId, Guid id, CancellationToken token = default);
    Task<bool> CancelInvestigationByOrgAsync(Guid orgId, Guid caseId, Guid id, CancellationToken token = default);
    Task<LoadResult<InvestigationAttendeeRecord>> GetInvestigationAttendeesAsync(Guid orgId, Guid caseId, Guid id, CancellationToken token = default);
    Task<InvestigationAttendeeRecord?> AddInvestigationAttendeeAsync(Guid orgId, Guid caseId, Guid id, AddInvestigationAttendeeRequest request, CancellationToken token = default);
    Task<InvestigationAttendeeRecord?> UpdateInvestigationAttendanceAsync(Guid orgId, Guid caseId, Guid id, Guid attendeeId, bool? didAttend, string? assignedRole, Ben.Data.Common.Enums.RsvpStatus? rsvp = null, CancellationToken token = default);
    Task<bool> RemoveInvestigationAttendeeAsync(Guid orgId, Guid caseId, Guid id, Guid attendeeId, CancellationToken token = default);

    // ── Evidence Voting ───────────────────────────────────────────────────────

    Task<EvidenceVoteSummary?> GetEvidenceVoteSummaryAsync(Guid uploadFileId, CancellationToken token = default);
    Task<LoadResult<EvidenceVoteRecord>> GetEvidenceVotesAsync(Guid uploadFileId, CancellationToken token = default);
    Task<EvidenceVoteSummary?> CastEvidenceVoteAsync(Guid uploadFileId, Ben.Data.Common.Enums.EvidenceVoteType voteType, string? comment, CancellationToken token = default);
    Task<bool> RemoveEvidenceVoteAsync(Guid uploadFileId, CancellationToken token = default);

    // ── Org-wide investigations (Area 9) ──────────────────────────────────────

    /// <summary>
    /// Every investigation the organization ran — including ones with no client case — each
    /// carrying the server's verdict on what this viewer may do with it.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="GetInvestigationsAsync"/>, which is nested under one case and
    /// therefore cannot see a case-less visit at all. Render <c>CanEditRecord</c> as given; a UI
    /// that works out edit rights for itself will eventually disagree with the endpoint.
    /// </remarks>
    Task<LoadResult<OrgInvestigationRow>> GetOrgInvestigationsAsync(Guid orgId, CancellationToken token = default);

    // ── Investigation Scheduling ──────────────────────────────────────────────

    // Org side
    Task<LoadResult<ScheduleProposalDto>> GetScheduleProposalsAsync(Guid orgId, Guid caseId, CancellationToken token = default);
    Task<ScheduleProposalDto?> CreateScheduleProposalAsync(Guid orgId, Guid caseId, CreateProposalRequest request, CancellationToken token = default);
    Task<bool> WithdrawScheduleProposalAsync(Guid orgId, Guid caseId, Guid proposalId, CancellationToken token = default);
    Task<ScheduleProposalDto?> ConvertProposalToInvestigationAsync(Guid orgId, Guid caseId, Guid proposalId, ConvertProposalRequest request, CancellationToken token = default);

    // Client side
    Task<LoadResult<ScheduleProposalDto>> GetMyScheduleProposalsAsync(Guid caseId, CancellationToken token = default);
    Task<ScheduleProposalDto?> AcceptScheduleProposalAsync(Guid caseId, Guid proposalId, Guid slotId, CancellationToken token = default);
    Task<ScheduleProposalDto?> CounterScheduleProposalAsync(Guid caseId, Guid proposalId, DateTime preferredDateTime, string? notes, CancellationToken token = default);
    Task<ScheduleProposalDto?> DeclineScheduleProposalAsync(Guid caseId, Guid proposalId, string? notes, CancellationToken token = default);

    // ── My Investigations (member dashboard) ──────────────────────────────────

    /// <summary>Returns all investigations the current user is assigned to attend.</summary>
    Task<LoadResult<MyInvestigationItem>> GetMyInvestigationsAsync(CancellationToken token = default);

    /// <summary>
    /// Where the signed-in person has actually been: past investigations they attended.
    /// </summary>
    /// <remarks>
    /// Only rows marked attended, so it is expected to be sparse — and honestly so — until arrival
    /// check-in exists. A map of places you were invited to is not a map of where you have been.
    /// </remarks>
    Task<LoadResult<AttendedInvestigationItem>> GetAttendedInvestigationsAsync(CancellationToken token = default);

    /// <summary>Sets the current user's RSVP on their attendee record.</summary>
    Task UpdateMyInvestigationRsvpAsync(Guid attendeeId, Ben.Data.Common.Enums.RsvpStatus rsvp, CancellationToken token = default);
}
