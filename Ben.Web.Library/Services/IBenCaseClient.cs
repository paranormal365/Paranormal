using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Data.Common.Enums;

namespace Ben.Web.Library.Services;

/// <summary>
/// The Case slice of <see cref="IBenAdminClient"/> — cases and everything hanging off one.
/// </summary>
/// <remarks>
/// Part of splitting one 383-method interface into domain-sized pieces.
/// <see cref="IBenAdminClient"/> inherits every slice, so existing callers and the single
/// adapter are unchanged; new code (and test doubles) can depend on just the slice it needs.
/// </remarks>
public interface IBenCaseClient
{
    // ── Case Transfers ────────────────────────────────────────────────────────

    Task<IReadOnlyList<CaseTransferLogRecord>> GetCaseTransfersAsync(Guid orgId, Guid caseId, CancellationToken token = default);
    Task<CaseTransferLogRecord?> ProposeCaseTransferAsync(Guid orgId, Guid caseId, Guid toOrganizationId, string? reason, CancellationToken token = default);
    Task<CaseTransferLogRecord?> RespondCaseTransferAsync(Guid orgId, Guid caseId, Guid logId, bool accept, string? rejectionReason, CancellationToken token = default);
    /// <summary>Cancels an outgoing pending transfer proposed by this org. Only the proposing org can cancel.</summary>
    Task<CaseTransferLogRecord?> CancelCaseTransferAsync(Guid orgId, Guid caseId, Guid logId, CancellationToken token = default);

    // ── Public Case Discovery ─────────────────────────────────────────────────

    Task<IReadOnlyList<PublicCaseListItem>> GetPublicCasesAsync(string orgUrlName, CancellationToken token = default);
    /// <summary>Returns a single public case by org URL name and case reference (e.g. "2026-042").</summary>
    Task<PublicCaseDetail?> GetPublicCaseAsync(string orgUrlName, string caseRef, CancellationToken token = default);

    /// <summary>
    /// Returns a paginated, cross-organization list of all public cases worldwide,
    /// with city-level approximate coordinates and aggregated vote counts.
    /// Used to drive the home-page investigation map and ranked list.
    /// </summary>
    /// <param name="sort">"votes" (default) sorts by total votes desc; "date" sorts by open date desc.</param>
    Task<PublicCaseDiscoveryPagedResponse?> GetPublicCaseDiscoveryAsync(int page = 1, int pageSize = 20, string sort = "votes", CancellationToken token = default);

    // ── Case votes (community rating) ─────────────────────────────────────────

    /// <summary>
    /// Returns the aggregate vote summary for a public case.
    /// Anonymous-friendly: <c>CurrentUserVote</c> is non-null only when the bearer token is present.
    /// Calls <c>GET api/public/cases/{caseId}/votes</c>.
    /// </summary>
    Task<CaseVoteSummary?> GetCaseVoteSummaryAsync(Guid caseId, CancellationToken token = default);

    /// <summary>
    /// Returns vote summaries for multiple cases in one request.
    /// Used by <c>PublicCaseDiscovery.razor</c> to pre-load summaries for all visible
    /// list-cards without N individual requests. Calls <c>GET api/public/cases/vote-summaries</c>.
    /// </summary>
    Task<IReadOnlyList<CaseVoteSummary>> GetCaseVoteSummariesAsync(IEnumerable<Guid> caseIds, CancellationToken token = default);

    Task<CaseVoteSummary?> CastCaseVoteAsync(Guid caseId, Ben.Data.Common.Enums.EvidenceVoteType voteType, CancellationToken token = default);
    Task<bool> RemoveCaseVoteAsync(Guid caseId, CancellationToken token = default);

    // ── Cases ─────────────────────────────────────────────────────────────────

    Task<IReadOnlyList<CaseRecord>> GetOrgCasesAsync(Guid orgId, CancellationToken token = default);
    Task<CaseRecord?> GetOrgCaseAsync(Guid orgId, Guid caseId, CancellationToken token = default);

    /// <summary>
    /// The client request this case was created from, or null when it was raised internally (or the
    /// caller can't read it). Read-only — the case's own description is an editable snapshot that
    /// diverges from what the client actually wrote.
    /// </summary>
    Task<CaseClientRequestRecord?> GetOrgCaseClientRequestAsync(Guid orgId, Guid caseId, CancellationToken token = default);
    Task<CaseRecord?> CreateOrgCaseAsync(Guid orgId, CreateCaseRequest request, CancellationToken token = default);
    Task<IReadOnlyList<OrgPendingRequestRecord>> GetOrgPendingRequestsAsync(Guid orgId, CancellationToken token = default);
    Task<CaseRecord?> AcceptClientRequestAsCaseAsync(Guid orgId, Guid clientRequestId, AcceptClientRequestAsCaseRequest request, CancellationToken token = default);
    Task<bool> DeclineClientRequestAsync(Guid orgId, Guid clientRequestId, CancellationToken token = default);
    /// <summary>Marks a pending request as Viewed or UnderReview without accepting or declining.</summary>
    Task<bool> UpdatePendingRequestStatusAsync(Guid orgId, Guid clientRequestId, Ben.Data.Common.Enums.ClientOrgRequestStatus status, CancellationToken token = default);
    Task<CaseRecord?> UpdateOrgCaseAsync(Guid orgId, Guid caseId, UpdateCaseRequest request, CancellationToken token = default);
    /// <summary>
    /// The case timeline. Pass <paramref name="investigationId"/> for the binder view — only the
    /// entries recorded during that investigation.
    /// </summary>
    Task<IReadOnlyList<CaseTimelineEntryRecord>> GetCaseTimelineAsync(Guid orgId, Guid caseId, Guid? investigationId = null, CancellationToken token = default);
    Task<CaseTimelineEntryRecord?> AddCaseTimelineEntryAsync(Guid orgId, Guid caseId, UpsertTimelineEntryRequest request, CancellationToken token = default);
    Task<CaseTimelineEntryRecord?> UpdateCaseTimelineEntryAsync(Guid orgId, Guid caseId, Guid entryId, UpsertTimelineEntryRequest request, CancellationToken token = default);
    Task<bool> DeleteCaseTimelineEntryAsync(Guid orgId, Guid caseId, Guid entryId, CancellationToken token = default);

    /// <summary>Returns published reports the client can view for their case.</summary>
    Task<IReadOnlyList<CaseReportSummary>> GetMyCaseReportsAsync(Guid caseId, CancellationToken token = default);

    /// <summary>Returns a URL to stream the published report PDF for the client.</summary>
    string GetMyCaseReportPdfUrl(Guid caseId, Guid reportId);

    // ── Case Report Builder ───────────────────────────────────────────────────

    Task<IReadOnlyList<CaseReportSummary>> GetCaseReportsAsync(Guid orgId, Guid caseId, CancellationToken token = default);
    Task<CaseReportDetail?> GetCaseReportAsync(Guid orgId, Guid caseId, Guid reportId, CancellationToken token = default);
    Task<CaseReportDetail?> CreateCaseReportAsync(Guid orgId, Guid caseId, UpsertCaseReportRequest request, CancellationToken token = default);
    Task<CaseReportDetail?> UpdateCaseReportAsync(Guid orgId, Guid caseId, Guid reportId, UpsertCaseReportRequest request, CancellationToken token = default);
    Task<CaseReportDetail?> PublishCaseReportAsync(Guid orgId, Guid caseId, Guid reportId, CancellationToken token = default);
    Task<bool> DeleteCaseReportAsync(Guid orgId, Guid caseId, Guid reportId, CancellationToken token = default);
    Task<CaseReportSectionDto?> AddReportSectionAsync(Guid orgId, Guid caseId, Guid reportId, UpsertSectionRequest request, CancellationToken token = default);
    Task<CaseReportSectionDto?> UpdateReportSectionAsync(Guid orgId, Guid caseId, Guid reportId, Guid sectionId, UpsertSectionRequest request, CancellationToken token = default);
    Task<bool> DeleteReportSectionAsync(Guid orgId, Guid caseId, Guid reportId, Guid sectionId, CancellationToken token = default);
    Task<CaseReportSectionFileDto?> AddReportSectionFileAsync(Guid orgId, Guid caseId, Guid reportId, Guid sectionId, Guid uploadFileId, string? caption, CancellationToken token = default);
    Task<bool> RemoveReportSectionFileAsync(Guid orgId, Guid caseId, Guid reportId, Guid sectionId, Guid fileId, CancellationToken token = default);
    /// <summary>Returns a URL to stream the PDF export for in-browser viewing.</summary>
    string GetReportPdfUrl(Guid orgId, Guid caseId, Guid reportId);

    /// <summary>Downloads the report PDF bytes using the bearer token.</summary>
    Task<(byte[] Data, string FileName)?> DownloadCaseReportPdfAsync(Guid orgId, Guid caseId, Guid reportId, CancellationToken token = default);

    /// <summary>Downloads the published report PDF bytes for the client.</summary>
    Task<(byte[] Data, string FileName)?> DownloadMyCaseReportPdfAsync(Guid caseId, Guid reportId, CancellationToken token = default);

    // ── Case Research ─────────────────────────────────────────────────────────

    Task<IReadOnlyList<CaseResearchEntryDto>> GetCaseResearchAsync(Guid orgId, Guid caseId, CancellationToken token = default);
    Task<CaseResearchEntryDto?> AddCaseResearchAsync(Guid orgId, Guid caseId, UpsertResearchRequest request, CancellationToken token = default);
    Task<CaseResearchEntryDto?> UploadCaseResearchFileAsync(Guid orgId, Guid caseId, string title, string? description, Stream content, string fileName, string contentType, CancellationToken token = default);
    Task<CaseResearchEntryDto?> UpdateCaseResearchAsync(Guid orgId, Guid caseId, Guid entryId, UpsertResearchRequest request, CancellationToken token = default);
    Task<bool> DeleteCaseResearchAsync(Guid orgId, Guid caseId, Guid entryId, CancellationToken token = default);

    // ── Case Files (Files/Evidence tab) ──────────────────────────────────────

    /// <summary>Returns all files linked to a case's Files/Evidence tab, newest first.</summary>
    Task<IReadOnlyList<CaseFileRecord>> GetCaseFilesAsync(Guid orgId, Guid caseId, CancellationToken token = default);

    /// <summary>Uploads a file of any content type and links it to the case's Files/Evidence tab.</summary>
    Task<CaseFileRecord?> UploadCaseFileAsync(Guid orgId, Guid caseId, string? description, Stream content, string fileName, string contentType, CancellationToken token = default);

    /// <summary>Un-links a file from the case. The underlying UploadFile is preserved.</summary>
    Task<bool> DeleteCaseFileAsync(Guid orgId, Guid caseId, Guid caseFileId, CancellationToken token = default);

    /// <summary>Links an existing UploadFile (e.g. picked from the media library) to the case's Files tab — no bytes are copied.</summary>
    Task<CaseFileRecord?> LinkCaseFileAsync(Guid orgId, Guid caseId, Guid uploadFileId, string? description = null, CancellationToken token = default);

    /// <summary>Renders the placed clips down to a single mixed audio file and saves it to the case's Files tab.</summary>
    Task<CaseFileRecord?> ExportAudioMixAsync(Guid orgId, Guid caseId, ExportAudioMixRequest request, CancellationToken token = default);

    // ── Case Notes ────────────────────────────────────────────────────────────

    Task<IReadOnlyList<CaseNoteDto>> GetCaseNotesAsync(Guid orgId, Guid caseId, CancellationToken token = default);
    Task<CaseNoteDto?> CreateCaseNoteAsync(Guid orgId, Guid caseId, UpsertCaseNoteDto request, CancellationToken token = default);
    Task<CaseNoteDto?> UpdateCaseNoteAsync(Guid orgId, Guid caseId, Guid noteId, UpsertCaseNoteDto request, CancellationToken token = default);
    Task<bool> DeleteCaseNoteAsync(Guid orgId, Guid caseId, Guid noteId, CancellationToken token = default);

    // ── Client Requests ───────────────────────────────────────────────────────

    Task<IReadOnlyList<ClientRequestRecord>> GetMyClientRequestsAsync(CancellationToken token = default);
    Task<ClientRequestRecord?> GetClientRequestAsync(Guid id, CancellationToken token = default);
    Task<IReadOnlyList<ClientRequestOrganizationRecord>> GetClientRequestOrgsAsync(Guid id, CancellationToken token = default);
    Task<ClientRequestRecord?> CreateClientRequestAsync(UpsertClientRequestRequest request, CancellationToken token = default);
    Task<ClientRequestRecord?> UpdateClientRequestAsync(Guid id, UpsertClientRequestRequest request, CancellationToken token = default);
    Task<ClientRequestRecord?> SubmitClientRequestAsync(Guid id, IList<Guid> organizationIds, CancellationToken token = default);
    Task<ClientRequestRecord?> WithdrawClientRequestAsync(Guid id, CancellationToken token = default);
    Task<ClientRequestRecord?> AddOrganizationToRequestAsync(Guid id, Guid organizationId, CancellationToken token = default);

    // ── My Cases (client dashboard) ───────────────────────────────────────────

    /// <summary>Returns all cases where the current user is the originating client.</summary>
    Task<IReadOnlyList<ClientCaseListItem>> GetMyCasesAsync(CancellationToken token = default);

    /// <summary>Returns case detail + client-visible occurrences and upcoming investigations.</summary>
    Task<ClientCaseDetail?> GetMyCaseAsync(Guid caseId, CancellationToken token = default);

    /// <summary>Logs a new occurrence (ClientReport timeline entry) on the client's case.</summary>
    Task<CaseTimelineEntryRecord?> LogOccurrenceAsync(Guid caseId, LogOccurrenceRequest request, CancellationToken token = default);

    /// <summary>Updates a previously logged occurrence.</summary>
    Task<CaseTimelineEntryRecord?> UpdateOccurrenceAsync(Guid caseId, Guid entryId, LogOccurrenceRequest request, CancellationToken token = default);

    /// <summary>Deletes a previously logged occurrence.</summary>
    Task<bool> DeleteOccurrenceAsync(Guid caseId, Guid entryId, CancellationToken token = default);

    // ── Co-client access management ───────────────────────────────────────────

    Task<IReadOnlyList<CoClientItem>> GetCoClientsAsync(Guid caseId, CancellationToken token = default);
    Task<CoClientItem?> AddCoClientAsync(Guid caseId, string email, CancellationToken token = default);
    Task<bool> RemoveCoClientAsync(Guid caseId, Guid accessId, CancellationToken token = default);

    // ── Sub-client invites (item #4) — for people with no account yet ───────────

    /// <summary>Returns this case's pending (not accepted/revoked/expired) invites.</summary>
    Task<IReadOnlyList<CaseClientInviteRecord>> GetCaseInvitesAsync(Guid caseId, CancellationToken token = default);

    /// <summary>
    /// Single entry point for adding a secondary user: an existing account is linked immediately
    /// (see <see cref="InviteCoClientResult.LinkedExistingAccount"/>); no account yet mints an
    /// invite instead.
    /// </summary>
    Task<InviteCoClientResult?> InviteCoClientAsync(Guid caseId, string email, CancellationToken token = default);

    Task<bool> RevokeCaseInviteAsync(Guid caseId, Guid inviteId, CancellationToken token = default);

    // ── Related people (basic-info, no account) ─────────────────────────────────

    /// <summary>Returns people referenced on this case who are not platform users.</summary>
    Task<IReadOnlyList<CaseRelatedPersonRecord>> GetRelatedPeopleAsync(Guid caseId, CancellationToken token = default);

    /// <summary>Adds a basic-info reference to someone connected to the case (no account created).</summary>
    Task<CaseRelatedPersonRecord?> AddRelatedPersonAsync(Guid caseId, AddRelatedPersonRequest request, CancellationToken token = default);

    /// <summary>Removes a related-person reference.</summary>
    Task<bool> RemoveRelatedPersonAsync(Guid caseId, Guid personId, CancellationToken token = default);

    /// <summary>
    /// How much of the help documentation the signed-in caller may see. Computed server-side —
    /// the org role needed for the administration documents isn't available to the browser.
    /// </summary>
    Task<Ben.Data.Common.Enums.HelpAudience?> GetMyHelpAudienceAsync(CancellationToken token = default);

    // ── Case Messages (org side) ───────────────────────────────────────────────

    /// <summary>Returns all case messages visible to the org (marks client messages read).</summary>
    Task<IReadOnlyList<CaseMessageRecord>> GetCaseMessagesAsync(Guid orgId, Guid caseId, CancellationToken token = default);

    /// <summary>Posts a message from the org to the client on this case.</summary>
    Task<CaseMessageRecord?> PostCaseMessageAsync(Guid orgId, Guid caseId, string body, CancellationToken token = default);

    /// <summary>Returns the count of unread client messages the org hasn't seen yet.</summary>
    Task<int> GetCaseMessageUnreadCountAsync(Guid orgId, Guid caseId, CancellationToken token = default);
}
