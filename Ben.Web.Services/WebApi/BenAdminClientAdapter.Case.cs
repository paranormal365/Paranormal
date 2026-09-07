using Ben.Data.Common.Enums;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Web.Services;
using Microsoft.Extensions.Options;

namespace Ben.Web.Services.WebApi;

/// <summary>
/// The Case half of the adapter — implements <see cref="Ben.Web.Services.IBenCaseClient"/>.
/// </summary>
/// <remarks>
/// One partial class split across files by domain, matching the slices of IBenAdminClient.
/// The constructor and shared fields live in BenAdminClientAdapter.cs.
/// </remarks>
public sealed partial class BenAdminClientAdapter
{
    // ── Case Transfers ────────────────────────────────────────────────────────

    public async Task<string?> ReassignMyCaseAsync(Guid caseId, Guid toOrganizationId,
        bool shareHistory, bool shareInvestigations, string? note, CancellationToken token = default)
    {
        var (_, error) = await _api.SendExpectingReasonAsync<object, object>(
            HttpMethod.Post, $"/api/my-cases/{caseId}/reassign",
            new { toOrganizationId, shareHistory, shareInvestigations, note }, token);
        return error;   // null on success
    }

    public Task<PendingReassignRecord?> GetMyReassignAsync(Guid caseId, CancellationToken token = default)
        => _api.GetAsync<PendingReassignRecord>($"/api/my-cases/{caseId}/reassign", token);

    public Task<bool> CancelMyReassignAsync(Guid caseId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/my-cases/{caseId}/reassign", token);

    public Task<LoadResult<IncomingTransferRecord>> GetIncomingTransfersAsync(Guid orgId, CancellationToken token = default)
        => _api.GetListAsync<IncomingTransferRecord>($"/api/organizations/{orgId}/incoming-transfers", token);

    public Task<LoadResult<CaseTransferLogRecord>> GetCaseTransfersAsync(Guid orgId, Guid caseId, CancellationToken token = default)
        => _api.GetListAsync<CaseTransferLogRecord>($"/api/organizations/{orgId}/cases/{caseId}/transfers", token);

    public Task<(CaseTransferLogRecord? Result, string? Error)> ProposeCaseTransferAsync(Guid orgId, Guid caseId, Guid toOrganizationId, string? reason, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, CaseTransferLogRecord>(
               HttpMethod.Post, $"/api/organizations/{orgId}/cases/{caseId}/transfers",
               new { ToOrganizationId = toOrganizationId, TransferReason = reason }, token);

    public Task<(CaseTransferLogRecord? Result, string? Error)> RespondCaseTransferAsync(Guid orgId, Guid caseId, Guid logId, bool accept, string? rejectionReason, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, CaseTransferLogRecord>(
               HttpMethod.Put, $"/api/organizations/{orgId}/cases/{caseId}/transfers/{logId}/respond",
               new { Accept = accept, Reason = rejectionReason }, token);

    public Task<CaseTransferLogRecord?> CancelCaseTransferAsync(Guid orgId, Guid caseId, Guid logId, CancellationToken token = default)
        => _api.PutAsync<object, CaseTransferLogRecord>(
               $"/api/organizations/{orgId}/cases/{caseId}/transfers/{logId}/cancel",
               new { }, token);

    // ── Public Case Discovery ─────────────────────────────────────────────────

    public Task<LoadResult<PublicCaseListItem>> GetPublicCasesAsync(string orgUrlName, CancellationToken token = default)
        => _api.GetAnonymousListAsync<PublicCaseListItem>($"/api/public/organizations/{Uri.EscapeDataString(orgUrlName)}/cases", token);

    public Task<PublicCaseDetail?> GetPublicCaseAsync(string orgUrlName, string caseRef, CancellationToken token = default)
        => _api.GetAnonymousAsync<PublicCaseDetail>(
               $"/api/public/organizations/{Uri.EscapeDataString(orgUrlName)}/cases/{Uri.EscapeDataString(caseRef)}", token);

    public Task<PublicCaseDiscoveryPagedResponse?> GetPublicCaseDiscoveryAsync(int page = 1, int pageSize = 20, string sort = "votes", CancellationToken token = default)
        => _api.GetAnonymousAsync<PublicCaseDiscoveryPagedResponse>($"/api/public/cases?page={page}&pageSize={pageSize}&sort={Uri.EscapeDataString(sort)}", token);

    // ── Case votes ────────────────────────────────────────────────────────────

    public Task<CaseVoteSummary?> GetCaseVoteSummaryAsync(Guid caseId, CancellationToken token = default)
        // GetAsync, not GetAnonymousAsync: this endpoint is [AllowAnonymous] but fills in the
        // viewer's own answer when a token is present, and sending none meant that field came
        // back empty for everyone. The token is attached only when there is one, so a genuine
        // visitor is unaffected.
        => _api.GetAsync<CaseVoteSummary>($"/api/public/cases/{caseId}/votes", token);

    public Task<LoadResult<CaseVoteSummary>> GetCaseVoteSummariesAsync(IEnumerable<Guid> caseIds, CancellationToken token = default)
    {
        var qs = string.Join("&", caseIds.Select(id => $"caseIds={id}"));
        // Asking about no cases is a successful answer of nothing, not a failure — the caller has
        // an empty list of cards to decorate, which is a different thing from being refused.
        if (string.IsNullOrEmpty(qs)) return Task.FromResult(LoadResult<CaseVoteSummary>.Ok([]));
        // Same reason as the single-case summary above: these carry the viewer's own vote, which
        // is what marks a card as already voted on in a list.
        return _api.GetListAsync<CaseVoteSummary>($"/api/public/cases/vote-summaries?{qs}", token);
    }

    public Task<CaseVoteSummary?> CastCaseVoteAsync(Guid caseId, Ben.Data.Common.Enums.EvidenceVoteType voteType, CancellationToken token = default)
        => _api.PostAsync<object, CaseVoteSummary>($"/api/public/cases/{caseId}/votes", new { VoteType = voteType }, token);

    public Task<bool> RemoveCaseVoteAsync(Guid caseId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/public/cases/{caseId}/votes", token);

    // ── Cases ─────────────────────────────────────────────────────────────────

    public Task<LoadResult<CaseRecord>> GetOrgCasesAsync(Guid orgId, CancellationToken token = default)
        => _api.GetListAsync<CaseRecord>($"/api/organizations/{orgId}/cases", token);

    public Task<CaseRecord?> GetOrgCaseAsync(Guid orgId, Guid caseId, CancellationToken token = default)
        => _api.GetAsync<CaseRecord>($"/api/organizations/{orgId}/cases/{caseId}", token);

    public Task<CaseClientRequestRecord?> GetOrgCaseClientRequestAsync(Guid orgId, Guid caseId, CancellationToken token = default)
        => _api.GetAsync<CaseClientRequestRecord>($"/api/organizations/{orgId}/cases/{caseId}/client-request", token);

    public Task<(CasePrivacyRetrofitResult? Result, string? Error)> ApplyCasePrivacyAsync(
        Guid orgId, Guid caseId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, CasePrivacyRetrofitResult>(
               HttpMethod.Post, $"/api/organizations/{orgId}/cases/{caseId}/apply-privacy", new { }, token);

    public Task<LoadResult<string>> GetPublishLeakWarningsAsync(Guid orgId, Guid caseId, string title, string? pseudonym, CancellationToken token = default)
        => _api.GetListAsync<string>(
               $"/api/organizations/{orgId}/cases/{caseId}/publish-leak-check"
               + $"?title={Uri.EscapeDataString(title)}&pseudonym={Uri.EscapeDataString(pseudonym ?? "")}", token);

    public Task<(CaseRecord? Result, string? Error)> CreateOrgCaseAsync(Guid orgId, CreateCaseRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<CreateCaseRequest, CaseRecord>(
               HttpMethod.Post, $"/api/organizations/{orgId}/cases", request, token);

    public Task<LoadResult<OrgPendingRequestRecord>> GetOrgPendingRequestsAsync(Guid orgId, CancellationToken token = default)
        => _api.GetListAsync<OrgPendingRequestRecord>($"/api/organizations/{orgId}/cases/pending-requests", token);

    public Task<(CaseRecord? Result, string? Error)> AcceptClientRequestAsCaseAsync(Guid orgId, Guid clientRequestId, AcceptClientRequestAsCaseRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<AcceptClientRequestAsCaseRequest, CaseRecord>(
               HttpMethod.Post, $"/api/organizations/{orgId}/cases/accept-client-request/{clientRequestId}", request, token);

    public Task<bool> DeclineClientRequestAsync(Guid orgId, Guid clientRequestId, CancellationToken token = default)
        => _api.PostVoidAsync(
               $"/api/organizations/{orgId}/cases/decline-request/{clientRequestId}", new { }, token);

    public Task<bool> UpdatePendingRequestStatusAsync(Guid orgId, Guid clientRequestId, Ben.Data.Common.Enums.ClientOrgRequestStatus status, CancellationToken token = default)
        => _api.PutVoidAsync(
               $"/api/organizations/{orgId}/cases/request-status/{clientRequestId}",
               new { Status = (int)status }, token);

    public Task<RequestReviewDetailItem?> GetRequestReviewAsync(Guid orgId, Guid clientRequestId, CancellationToken token = default)
        => _api.GetAsync<RequestReviewDetailItem>(
               $"/api/organizations/{orgId}/request-review/{clientRequestId}", token);

    public Task<RequestReviewVoteItem?> CastRequestReviewVoteAsync(Guid orgId, Guid clientRequestId, bool inFavor, string? comment, CancellationToken token = default)
        => _api.PostAsync<object, RequestReviewVoteItem>(
               $"/api/organizations/{orgId}/request-review/{clientRequestId}/vote",
               new { InFavor = inFavor, Comment = comment }, token);

    // Reason-carrying (item 184): the make-public and designation gates refuse with a sentence
    // the dialog must show, and PutAsync would discard it — the write-only-guard trap.
    public Task<(CaseRecord? Result, string? Error)> UpdateOrgCaseAsync(Guid orgId, Guid caseId, UpdateCaseRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<UpdateCaseRequest, CaseRecord>(
               HttpMethod.Put, $"/api/organizations/{orgId}/cases/{caseId}", request, token);

    public Task<LoadResult<CaseTimelineEntryRecord>> GetCaseTimelineAsync(Guid orgId, Guid caseId, Guid? investigationId = null, CancellationToken token = default)
    {
        var url = $"/api/organizations/{orgId}/cases/{caseId}/timeline";
        if (investigationId is { } id) url += $"?investigationId={id}";
        return _api.GetListAsync<CaseTimelineEntryRecord>(url, token);
    }

    public Task<(CaseTimelineEntryRecord? Result, string? Error)> AddCaseTimelineEntryAsync(Guid orgId, Guid caseId, UpsertTimelineEntryRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<UpsertTimelineEntryRequest, CaseTimelineEntryRecord>(
            HttpMethod.Post, $"/api/organizations/{orgId}/cases/{caseId}/timeline", request, token);

    public Task<CaseTimelineEntryRecord?> UpdateCaseTimelineEntryAsync(Guid orgId, Guid caseId, Guid entryId, UpsertTimelineEntryRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertTimelineEntryRequest, CaseTimelineEntryRecord>($"/api/organizations/{orgId}/cases/{caseId}/timeline/{entryId}", request, token);

    public Task<bool> DeleteCaseTimelineEntryAsync(Guid orgId, Guid caseId, Guid entryId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/cases/{caseId}/timeline/{entryId}", token);

    // ── Case Report Builder ───────────────────────────────────────────────────

    // Client-facing: published reports only
    public Task<LoadResult<CaseReportSummary>> GetMyCaseReportsAsync(Guid caseId, CancellationToken token = default)
        => _api.GetListAsync<CaseReportSummary>($"/api/my-cases/{caseId}/reports", token);

    public string GetMyCaseReportPdfUrl(Guid caseId, Guid reportId)
        => $"/api/my-cases/{caseId}/reports/{reportId}/pdf";

    // Org-facing
    public Task<LoadResult<CaseReportSummary>> GetCaseReportsAsync(Guid orgId, Guid caseId, CancellationToken token = default)
        => _api.GetListAsync<CaseReportSummary>($"/api/orgs/{orgId}/cases/{caseId}/reports", token);

    public Task<CaseReportDetail?> GetCaseReportAsync(Guid orgId, Guid caseId, Guid reportId, CancellationToken token = default)
        => _api.GetAsync<CaseReportDetail>($"/api/orgs/{orgId}/cases/{caseId}/reports/{reportId}", token);

    public Task<CaseReportDetail?> CreateCaseReportAsync(Guid orgId, Guid caseId, UpsertCaseReportRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertCaseReportRequest, CaseReportDetail>($"/api/orgs/{orgId}/cases/{caseId}/reports", request, token);

    public Task<CaseReportDetail?> UpdateCaseReportAsync(Guid orgId, Guid caseId, Guid reportId, UpsertCaseReportRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertCaseReportRequest, CaseReportDetail>($"/api/orgs/{orgId}/cases/{caseId}/reports/{reportId}", request, token);

    public Task<CaseReportDetail?> PublishCaseReportAsync(Guid orgId, Guid caseId, Guid reportId, CancellationToken token = default)
        => _api.PostAsync<object, CaseReportDetail>($"/api/orgs/{orgId}/cases/{caseId}/reports/{reportId}/publish", new { }, token);

    public Task<bool> DeleteCaseReportAsync(Guid orgId, Guid caseId, Guid reportId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/orgs/{orgId}/cases/{caseId}/reports/{reportId}", token);

    public Task<CaseReportSectionDto?> AddReportSectionAsync(Guid orgId, Guid caseId, Guid reportId, UpsertSectionRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertSectionRequest, CaseReportSectionDto>($"/api/orgs/{orgId}/cases/{caseId}/reports/{reportId}/sections", request, token);

    public Task<CaseReportSectionDto?> UpdateReportSectionAsync(Guid orgId, Guid caseId, Guid reportId, Guid sectionId, UpsertSectionRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertSectionRequest, CaseReportSectionDto>($"/api/orgs/{orgId}/cases/{caseId}/reports/{reportId}/sections/{sectionId}", request, token);

    public Task<bool> DeleteReportSectionAsync(Guid orgId, Guid caseId, Guid reportId, Guid sectionId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/orgs/{orgId}/cases/{caseId}/reports/{reportId}/sections/{sectionId}", token);

    public Task<CaseReportSectionFileDto?> AddReportSectionFileAsync(Guid orgId, Guid caseId, Guid reportId, Guid sectionId, Guid uploadFileId, string? caption, CancellationToken token = default)
        => _api.PostAsync<object, CaseReportSectionFileDto>($"/api/orgs/{orgId}/cases/{caseId}/reports/{reportId}/sections/{sectionId}/files", new { UploadFileId = uploadFileId, Caption = caption }, token);

    public Task<bool> RemoveReportSectionFileAsync(Guid orgId, Guid caseId, Guid reportId, Guid sectionId, Guid fileId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/orgs/{orgId}/cases/{caseId}/reports/{reportId}/sections/{sectionId}/files/{fileId}", token);

    public Task<LoadResult<AvailableFieldSessionDto>> GetCaseFieldSessionsAsync(Guid orgId, Guid caseId, CancellationToken token = default)
        => _api.GetListAsync<AvailableFieldSessionDto>($"/api/orgs/{orgId}/cases/{caseId}/reports/field-sessions", token);

    public Task<CaseReportSectionFieldSessionDto?> AddReportSectionFieldSessionAsync(Guid orgId, Guid caseId, Guid reportId, Guid sectionId, Guid fieldSessionUploadId, string? caption, CancellationToken token = default)
        => _api.PostAsync<object, CaseReportSectionFieldSessionDto>($"/api/orgs/{orgId}/cases/{caseId}/reports/{reportId}/sections/{sectionId}/field-sessions", new { FieldSessionUploadId = fieldSessionUploadId, Caption = caption }, token);

    public Task<bool> RemoveReportSectionFieldSessionAsync(Guid orgId, Guid caseId, Guid reportId, Guid sectionId, Guid linkId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/orgs/{orgId}/cases/{caseId}/reports/{reportId}/sections/{sectionId}/field-sessions/{linkId}", token);

    public string GetReportPdfUrl(Guid orgId, Guid caseId, Guid reportId)
        => $"/api/orgs/{orgId}/cases/{caseId}/reports/{reportId}/pdf";

    public async Task<(byte[] Data, string FileName)?> DownloadCaseReportPdfAsync(Guid orgId, Guid caseId, Guid reportId, CancellationToken token = default)
    {
        var result = await _api.GetBytesAsync($"/api/orgs/{orgId}/cases/{caseId}/reports/{reportId}/pdf", "report.pdf", token);
        return result is null ? null : (result.Value.Data, result.Value.FileName);
    }

    public async Task<(byte[] Data, string FileName)?> DownloadMyCaseReportPdfAsync(Guid caseId, Guid reportId, CancellationToken token = default)
    {
        var result = await _api.GetBytesAsync($"/api/my-cases/{caseId}/reports/{reportId}/pdf", "report.pdf", token);
        return result is null ? null : (result.Value.Data, result.Value.FileName);
    }

    // ── Case Research ─────────────────────────────────────────────────────────

    public Task<LoadResult<CaseResearchEntryDto>> GetCaseResearchAsync(Guid orgId, Guid caseId, CancellationToken token = default)
        => _api.GetListAsync<CaseResearchEntryDto>($"/api/orgs/{orgId}/cases/{caseId}/research", token);

    public Task<CaseResearchEntryDto?> AddCaseResearchAsync(Guid orgId, Guid caseId, UpsertResearchRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertResearchRequest, CaseResearchEntryDto>($"/api/orgs/{orgId}/cases/{caseId}/research", request, token);

    public async Task<CaseResearchEntryDto?> UploadCaseResearchFileAsync(Guid orgId, Guid caseId, string title, string? description, Stream content, string fileName, string contentType, CancellationToken token = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(title), "title");
        if (description is not null) form.Add(new StringContent(description), "description");
        using var sc = new StreamContent(content);
        sc.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(sc, "file", fileName);
        return await _api.PostMultipartAsync<CaseResearchEntryDto>($"/api/orgs/{orgId}/cases/{caseId}/research/files", form, token);
    }

    public Task<CaseResearchEntryDto?> UpdateCaseResearchAsync(Guid orgId, Guid caseId, Guid entryId, UpsertResearchRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertResearchRequest, CaseResearchEntryDto>($"/api/orgs/{orgId}/cases/{caseId}/research/{entryId}", request, token);

    public Task<bool> DeleteCaseResearchAsync(Guid orgId, Guid caseId, Guid entryId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/orgs/{orgId}/cases/{caseId}/research/{entryId}", token);

    // ── Case Files (Files/Evidence tab) ──────────────────────────────────────

    public Task<LoadResult<CaseFileRecord>> GetCaseFilesAsync(Guid orgId, Guid caseId, CancellationToken token = default)
        => _api.GetListAsync<CaseFileRecord>($"/api/orgs/{orgId}/cases/{caseId}/files", token);

    public async Task<CaseFileRecord?> UploadCaseFileAsync(Guid orgId, Guid caseId, string? description, Stream content, string fileName, string contentType, CancellationToken token = default)
    {
        using var form = new MultipartFormDataContent();
        if (description is not null) form.Add(new StringContent(description), "description");
        using var sc = new StreamContent(content);
        sc.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(sc, "file", fileName);
        var (result, error) = await _api.PostMultipartExpectingReasonAsync<CaseFileRecord>(
            $"/api/orgs/{orgId}/cases/{caseId}/files", form, token);
        LastCaseFileUploadError = error;
        return result;
    }

    /// <summary>
    /// The refusal from the most recent <see cref="UploadCaseFileAsync"/>, when it failed.
    /// </summary>
    /// <remarks>
    /// A side-channel rather than a tuple because the upload's callers thread the record through
    /// several layers that a signature change would ripple across. The item-84 read-only sentence
    /// is the payload that matters; a null here with a null result is a generic failure.
    /// </remarks>
    public string? LastCaseFileUploadError { get; private set; }

    public Task<bool> DeleteCaseFileAsync(Guid orgId, Guid caseId, Guid caseFileId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/orgs/{orgId}/cases/{caseId}/files/{caseFileId}", token);

    public Task<CaseFileRecord?> LinkCaseFileAsync(Guid orgId, Guid caseId, Guid uploadFileId, string? description = null, CancellationToken token = default)
        => _api.PostAsync<LinkCaseFileRequest, CaseFileRecord>(
            $"/api/orgs/{orgId}/cases/{caseId}/files/link/{uploadFileId}", new LinkCaseFileRequest(description), token);

    public Task<CaseFileRecord?> ExportAudioMixAsync(Guid orgId, Guid caseId, ExportAudioMixRequest request, CancellationToken token = default)
        => _api.PostAsync<ExportAudioMixRequest, CaseFileRecord>($"/api/orgs/{orgId}/cases/{caseId}/audio-mix/export", request, token);

    public Task<(CaseFileRecord? Result, string? Error)> ExportAudioMixWithReasonAsync(
        Guid orgId, Guid caseId, ExportAudioMixRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<ExportAudioMixRequest, CaseFileRecord>(
            HttpMethod.Post, $"/api/orgs/{orgId}/cases/{caseId}/audio-mix/export", request, token);

    // ── Case Notes ────────────────────────────────────────────────────────────

    public Task<LoadResult<CaseNoteDto>> GetCaseNotesAsync(Guid orgId, Guid caseId, CancellationToken token = default)
        => _api.GetListAsync<CaseNoteDto>($"/api/organizations/{orgId}/cases/{caseId}/notes", token);

    public Task<(CaseNoteDto? Result, string? Error)> CreateCaseNoteAsync(Guid orgId, Guid caseId, UpsertCaseNoteDto request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<UpsertCaseNoteDto, CaseNoteDto>(
            HttpMethod.Post, $"/api/organizations/{orgId}/cases/{caseId}/notes", request, token);

    public Task<CaseNoteDto?> UpdateCaseNoteAsync(Guid orgId, Guid caseId, Guid noteId, UpsertCaseNoteDto request, CancellationToken token = default)
        => _api.PutAsync<UpsertCaseNoteDto, CaseNoteDto>($"/api/organizations/{orgId}/cases/{caseId}/notes/{noteId}", request, token);

    public Task<bool> DeleteCaseNoteAsync(Guid orgId, Guid caseId, Guid noteId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/cases/{caseId}/notes/{noteId}", token);

    // ── Client Requests ───────────────────────────────────────────────────────

    public Task<LoadResult<ClientRequestRecord>> GetMyClientRequestsAsync(CancellationToken token = default)
        => _api.GetListAsync<ClientRequestRecord>("/api/client-requests/my", token);

    public Task<ClientRequestRecord?> GetClientRequestAsync(Guid id, CancellationToken token = default)
        => _api.GetAsync<ClientRequestRecord>($"/api/client-requests/{id}", token);

    public Task<LoadResult<ClientRequestOrganizationRecord>> GetClientRequestOrgsAsync(Guid id, CancellationToken token = default)
        => _api.GetListAsync<ClientRequestOrganizationRecord>($"/api/client-requests/{id}/organizations", token);

    public Task<ClientRequestRecord?> CreateClientRequestAsync(UpsertClientRequestRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertClientRequestRequest, ClientRequestRecord>("/api/client-requests", request, token);

    public Task<ClientRequestRecord?> UpdateClientRequestAsync(Guid id, UpsertClientRequestRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertClientRequestRequest, ClientRequestRecord>($"/api/client-requests/{id}", request, token);

    public Task<ClientRequestRecord?> SubmitClientRequestAsync(Guid id, IList<Guid> organizationIds, CancellationToken token = default)
        => _api.PostAsync<object, ClientRequestRecord>($"/api/client-requests/{id}/submit",
               new { OrganizationIds = organizationIds }, token);

    public Task<ClientRequestRecord?> WithdrawClientRequestAsync(Guid id, CancellationToken token = default)
        => _api.PostAsync<object, ClientRequestRecord>($"/api/client-requests/{id}/withdraw", new { }, token);

    public Task<ClientRequestRecord?> AddOrganizationToRequestAsync(Guid id, Guid organizationId, CancellationToken token = default)
        => _api.PostAsync<object, ClientRequestRecord>($"/api/client-requests/{id}/add-organization",
               new { OrganizationId = organizationId }, token);

    public Task<bool> DeleteClientRequestDraftAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/client-requests/{id}", token);

    /// <remarks>
    /// The anonymous helper, because the person has no account yet and therefore no token. A null
    /// body is the server failing to answer in shape — a 500, a proxy page, or the rate limiter's
    /// 429, which this endpoint sits behind. Saying "couldn't reach" is the only honest reading;
    /// the request may or may not have been made, and the email is what settles it.
    /// </remarks>
    public async Task<AnonymousSubmitOutcome> SubmitRequestWithoutAccountAsync(
        AnonymousClientRequestSubmission request, CancellationToken token = default)
    {
        var result = await _api.PostAnonymousReadingBodyAsync<AnonymousClientRequestSubmission, AnonymousSubmitOutcome>(
            "/api/public/client-requests/submit", request, token);

        return result ?? new AnonymousSubmitOutcome(false,
            "Couldn't reach the server. Check your email in a moment — if nothing arrives, try again.", null);
    }

    public Task<PendingClientRequestRecord?> GetPendingClientRequestAsync(Guid id, string key, CancellationToken token = default)
        => _api.GetAsync<PendingClientRequestRecord>(
               $"/api/client-requests/pending/{id}?key={Uri.EscapeDataString(key)}", token);

    public Task<ClientRequestRecord?> AdoptPendingClientRequestAsync(Guid id, string key, CancellationToken token = default)
        => _api.PostAsync<object, ClientRequestRecord>(
               $"/api/client-requests/pending/{id}/adopt?key={Uri.EscapeDataString(key)}", new { }, token);

    public Task<bool> DiscardPendingClientRequestAsync(Guid id, string key, CancellationToken token = default)
        => _api.PostVoidAsync(
               $"/api/client-requests/pending/{id}/discard?key={Uri.EscapeDataString(key)}", new { }, token);

    // ── My Cases ─────────────────────────────────────────────────────────────

    public Task<LoadResult<ClientCaseListItem>> GetMyCasesAsync(CancellationToken token = default)
        => _api.GetListAsync<ClientCaseListItem>("/api/my-cases", token);

    public Task<ClientCaseDetail?> GetMyCaseAsync(Guid caseId, CancellationToken token = default)
        => _api.GetAsync<ClientCaseDetail>($"/api/my-cases/{caseId}", token);

    public Task<CaseTimelineEntryRecord?> LogOccurrenceAsync(Guid caseId, LogOccurrenceRequest request, CancellationToken token = default)
        => _api.PostAsync<LogOccurrenceRequest, CaseTimelineEntryRecord>($"/api/my-cases/{caseId}/occurrences", request, token);

    public Task<CaseTimelineEntryRecord?> UpdateOccurrenceAsync(Guid caseId, Guid entryId, LogOccurrenceRequest request, CancellationToken token = default)
        => _api.PutAsync<LogOccurrenceRequest, CaseTimelineEntryRecord>($"/api/my-cases/{caseId}/occurrences/{entryId}", request, token);

    public Task<bool> DeleteOccurrenceAsync(Guid caseId, Guid entryId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/my-cases/{caseId}/occurrences/{entryId}", token);

    public async Task<OccurrenceFileItem?> AttachOccurrenceFileAsync(
        Guid caseId, Guid entryId, Stream content, string fileName, string contentType, CancellationToken token = default)
    {
        using var form = new MultipartFormDataContent();
        using var sc   = new StreamContent(content);
        sc.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(sc, "file", fileName);
        return await _api.PostMultipartAsync<OccurrenceFileItem>(
            $"/api/my-cases/{caseId}/occurrences/{entryId}/files", form, token);
    }

    public Task<bool> DetachOccurrenceFileAsync(Guid caseId, Guid entryId, Guid fileId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/my-cases/{caseId}/occurrences/{entryId}/files/{fileId}", token);

    public Task<LoadResult<CaseMessageRecord>> GetMyCaseMessagesAsync(Guid caseId, CancellationToken token = default)
        => _api.GetListAsync<CaseMessageRecord>($"/api/my-cases/{caseId}/messages", token);

    public Task<CaseMessageRecord?> PostMyCaseMessageAsync(Guid caseId, string body, CancellationToken token = default)
        => _api.PostAsync<object, CaseMessageRecord>($"/api/my-cases/{caseId}/messages", new { Body = body }, token);

    // ── Co-client access management ───────────────────────────────────────────

    public Task<LoadResult<CoClientItem>> GetCoClientsAsync(Guid caseId, CancellationToken token = default)
        => _api.GetListAsync<CoClientItem>($"/api/my-cases/{caseId}/co-clients", token);

    public Task<CoClientItem?> AddCoClientAsync(Guid caseId, string email, CancellationToken token = default)
        => _api.PostAsync<object, CoClientItem>($"/api/my-cases/{caseId}/co-clients", new { Email = email }, token);

    public Task<bool> RemoveCoClientAsync(Guid caseId, Guid accessId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/my-cases/{caseId}/co-clients/{accessId}", token);

    // ── Sub-client invites (item #4) ──────────────────────────────────────────

    public Task<LoadResult<CaseClientInviteRecord>> GetCaseInvitesAsync(Guid caseId, CancellationToken token = default)
        => _api.GetListAsync<CaseClientInviteRecord>($"/api/my-cases/{caseId}/invites", token);

    public Task<InviteCoClientResult?> InviteCoClientAsync(Guid caseId, string email, CancellationToken token = default)
        => _api.PostAsync<object, InviteCoClientResult>($"/api/my-cases/{caseId}/invites", new { Email = email }, token);

    public Task<bool> RevokeCaseInviteAsync(Guid caseId, Guid inviteId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/my-cases/{caseId}/invites/{inviteId}", token);

    // ── Related people (basic-info, no account) ─────────────────────────────────

    public Task<LoadResult<CaseRelatedPersonRecord>> GetRelatedPeopleAsync(Guid caseId, CancellationToken token = default)
        => _api.GetListAsync<CaseRelatedPersonRecord>($"/api/my-cases/{caseId}/related-people", token);

    public Task<CaseRelatedPersonRecord?> AddRelatedPersonAsync(Guid caseId, AddRelatedPersonRequest request, CancellationToken token = default)
        => _api.PostAsync<AddRelatedPersonRequest, CaseRelatedPersonRecord>($"/api/my-cases/{caseId}/related-people", request, token);

    public Task<Ben.Data.Common.Enums.HelpAudience?> GetMyHelpAudienceAsync(CancellationToken token = default)
        => _api.GetAsync<Ben.Data.Common.Enums.HelpAudience?>("/api/me/help-audience", token);

    public Task<LoadResult<VideoAssetAdminRecord>> GetVideoAssetsAsync(CancellationToken token = default)
        => _api.GetListAsync<VideoAssetAdminRecord>("/api/admin/video-assets", token);

    public Task<VideoAssetAdminRecord?> CreateVideoAssetAsync(
        CreateVideoAssetRequest request, CancellationToken token = default)
        => _api.PostAsync<CreateVideoAssetRequest, VideoAssetAdminRecord>(
               "/api/admin/video-assets", request, token);

    public Task<VideoAssetAdminRecord?> UpdateVideoAssetAsync(
        Guid id, UpdateVideoAssetRequest request, CancellationToken token = default)
        => _api.PutAsync<UpdateVideoAssetRequest, VideoAssetAdminRecord>(
               $"/api/admin/video-assets/{id}", request, token);

    public Task<bool> RetireVideoAssetAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/admin/video-assets/{id}", token);

    /// <summary>
    /// Site settings, saying so when they could not be fetched.
    /// </summary>
    /// <remarks>
    /// The page this feeds rendered blank on the ishaunted.com deployment with no error anywhere,
    /// because a refused call and an empty list were the same value (items 120 and 126).
    /// </remarks>
    public Task<LoadResult<SiteSettingRecord>> GetSiteSettingsAsync(CancellationToken token = default)
        => _api.GetListAsync<SiteSettingRecord>("/api/admin/site-settings", token);

    public Task<SiteSettingRecord?> SetSiteSettingAsync(
        string key, SetSiteSettingRequest request, CancellationToken token = default)
        => _api.PutAsync<SetSiteSettingRequest, SiteSettingRecord>(
               $"/api/admin/site-settings/{Uri.EscapeDataString(key)}", request, token);

    public Task<CaseDisplayAliasRecord?> GetCaseDisplayAliasAsync(Guid caseId, CancellationToken token = default)
        => _api.GetAsync<CaseDisplayAliasRecord>($"/api/my-cases/{caseId}/display-alias", token);

    public Task<CaseDisplayAliasRecord?> SetCaseDisplayAliasAsync(
        Guid caseId, SetCaseDisplayAliasRequest request, CancellationToken token = default)
        => _api.PutAsync<SetCaseDisplayAliasRequest, CaseDisplayAliasRecord>(
               $"/api/my-cases/{caseId}/display-alias", request, token);

    public Task<CaseRelatedPersonRecord?> UpdateRelatedPersonAsync(
        Guid caseId, Guid personId, UpdateRelatedPersonRequest request, CancellationToken token = default)
        => _api.PutAsync<UpdateRelatedPersonRequest, CaseRelatedPersonRecord>(
               $"/api/my-cases/{caseId}/related-people/{personId}", request, token);

    public Task<bool> RemoveRelatedPersonAsync(Guid caseId, Guid personId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/my-cases/{caseId}/related-people/{personId}", token);

    // ── Case Messages (org side) ──────────────────────────────────────────────

    public Task<LoadResult<CaseMessageRecord>> GetCaseMessagesAsync(Guid orgId, Guid caseId, CancellationToken token = default)
        => _api.GetListAsync<CaseMessageRecord>($"/api/orgs/{orgId}/cases/{caseId}/messages", token);

    public Task<(CaseMessageRecord? Result, string? Error)> PostCaseMessageAsync(Guid orgId, Guid caseId, string body, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, CaseMessageRecord>(
            HttpMethod.Post, $"/api/orgs/{orgId}/cases/{caseId}/messages", new { Body = body }, token);

    public async Task<int> GetCaseMessageUnreadCountAsync(Guid orgId, Guid caseId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<int>($"/api/orgs/{orgId}/cases/{caseId}/messages/unread-count", token);
        return result;
    }
}
