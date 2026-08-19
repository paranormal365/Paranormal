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

    public async Task<IReadOnlyList<CaseTransferLogRecord>> GetCaseTransfersAsync(Guid orgId, Guid caseId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<CaseTransferLogRecord>>(
            $"/api/organizations/{orgId}/cases/{caseId}/transfers", token);
        return result ?? [];
    }

    public Task<CaseTransferLogRecord?> ProposeCaseTransferAsync(Guid orgId, Guid caseId, Guid toOrganizationId, string? reason, CancellationToken token = default)
        => _api.PostAsync<object, CaseTransferLogRecord>(
               $"/api/organizations/{orgId}/cases/{caseId}/transfers",
               new { ToOrganizationId = toOrganizationId, TransferReason = reason }, token);

    public Task<CaseTransferLogRecord?> RespondCaseTransferAsync(Guid orgId, Guid caseId, Guid logId, bool accept, string? rejectionReason, CancellationToken token = default)
        => _api.PutAsync<object, CaseTransferLogRecord>(
               $"/api/organizations/{orgId}/cases/{caseId}/transfers/{logId}/respond",
               new { Accept = accept, Reason = rejectionReason }, token);

    public Task<CaseTransferLogRecord?> CancelCaseTransferAsync(Guid orgId, Guid caseId, Guid logId, CancellationToken token = default)
        => _api.PutAsync<object, CaseTransferLogRecord>(
               $"/api/organizations/{orgId}/cases/{caseId}/transfers/{logId}/cancel",
               new { }, token);

    // ── Public Case Discovery ─────────────────────────────────────────────────

    public async Task<IReadOnlyList<PublicCaseListItem>> GetPublicCasesAsync(string orgUrlName, CancellationToken token = default)
    {
        var result = await _api.GetAnonymousAsync<IReadOnlyList<PublicCaseListItem>>(
            $"/api/public/organizations/{Uri.EscapeDataString(orgUrlName)}/cases", token);
        return result ?? [];
    }

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

    public async Task<IReadOnlyList<CaseVoteSummary>> GetCaseVoteSummariesAsync(IEnumerable<Guid> caseIds, CancellationToken token = default)
    {
        var qs = string.Join("&", caseIds.Select(id => $"caseIds={id}"));
        if (string.IsNullOrEmpty(qs)) return [];
        // Same reason as the single-case summary above: these carry the viewer's own vote, which
        // is what marks a card as already voted on in a list.
        var result = await _api.GetAsync<IReadOnlyList<CaseVoteSummary>>(
            $"/api/public/cases/vote-summaries?{qs}", token);
        return result ?? [];
    }

    public Task<CaseVoteSummary?> CastCaseVoteAsync(Guid caseId, Ben.Data.Common.Enums.EvidenceVoteType voteType, CancellationToken token = default)
        => _api.PostAsync<object, CaseVoteSummary>($"/api/public/cases/{caseId}/votes", new { VoteType = voteType }, token);

    public Task<bool> RemoveCaseVoteAsync(Guid caseId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/public/cases/{caseId}/votes", token);

    // ── Cases ─────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<CaseRecord>> GetOrgCasesAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<CaseRecord>>($"/api/organizations/{orgId}/cases", token);
        return result ?? [];
    }

    public Task<CaseRecord?> GetOrgCaseAsync(Guid orgId, Guid caseId, CancellationToken token = default)
        => _api.GetAsync<CaseRecord>($"/api/organizations/{orgId}/cases/{caseId}", token);

    public Task<CaseClientRequestRecord?> GetOrgCaseClientRequestAsync(Guid orgId, Guid caseId, CancellationToken token = default)
        => _api.GetAsync<CaseClientRequestRecord>($"/api/organizations/{orgId}/cases/{caseId}/client-request", token);

    public Task<CaseRecord?> CreateOrgCaseAsync(Guid orgId, CreateCaseRequest request, CancellationToken token = default)
        => _api.PostAsync<CreateCaseRequest, CaseRecord>($"/api/organizations/{orgId}/cases", request, token);

    public async Task<IReadOnlyList<OrgPendingRequestRecord>> GetOrgPendingRequestsAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrgPendingRequestRecord>>($"/api/organizations/{orgId}/cases/pending-requests", token);
        return result ?? [];
    }

    public Task<CaseRecord?> AcceptClientRequestAsCaseAsync(Guid orgId, Guid clientRequestId, AcceptClientRequestAsCaseRequest request, CancellationToken token = default)
        => _api.PostAsync<AcceptClientRequestAsCaseRequest, CaseRecord>(
               $"/api/organizations/{orgId}/cases/accept-client-request/{clientRequestId}", request, token);

    public Task<bool> DeclineClientRequestAsync(Guid orgId, Guid clientRequestId, CancellationToken token = default)
        => _api.PostVoidAsync(
               $"/api/organizations/{orgId}/cases/decline-request/{clientRequestId}", new { }, token);

    public Task<bool> UpdatePendingRequestStatusAsync(Guid orgId, Guid clientRequestId, Ben.Data.Common.Enums.ClientOrgRequestStatus status, CancellationToken token = default)
        => _api.PutVoidAsync(
               $"/api/organizations/{orgId}/cases/request-status/{clientRequestId}",
               new { Status = (int)status }, token);

    public Task<CaseRecord?> UpdateOrgCaseAsync(Guid orgId, Guid caseId, UpdateCaseRequest request, CancellationToken token = default)
        => _api.PutAsync<UpdateCaseRequest, CaseRecord>($"/api/organizations/{orgId}/cases/{caseId}", request, token);

    public async Task<IReadOnlyList<CaseTimelineEntryRecord>> GetCaseTimelineAsync(Guid orgId, Guid caseId, Guid? investigationId = null, CancellationToken token = default)
    {
        var url = $"/api/organizations/{orgId}/cases/{caseId}/timeline";
        if (investigationId is { } id) url += $"?investigationId={id}";
        var result = await _api.GetAsync<IReadOnlyList<CaseTimelineEntryRecord>>(url, token);
        return result ?? [];
    }

    public Task<CaseTimelineEntryRecord?> AddCaseTimelineEntryAsync(Guid orgId, Guid caseId, UpsertTimelineEntryRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertTimelineEntryRequest, CaseTimelineEntryRecord>($"/api/organizations/{orgId}/cases/{caseId}/timeline", request, token);

    public Task<CaseTimelineEntryRecord?> UpdateCaseTimelineEntryAsync(Guid orgId, Guid caseId, Guid entryId, UpsertTimelineEntryRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertTimelineEntryRequest, CaseTimelineEntryRecord>($"/api/organizations/{orgId}/cases/{caseId}/timeline/{entryId}", request, token);

    public Task<bool> DeleteCaseTimelineEntryAsync(Guid orgId, Guid caseId, Guid entryId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/cases/{caseId}/timeline/{entryId}", token);

    // ── Case Report Builder ───────────────────────────────────────────────────

    // Client-facing: published reports only
    public async Task<IReadOnlyList<CaseReportSummary>> GetMyCaseReportsAsync(Guid caseId, CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<CaseReportSummary>>($"/api/my-cases/{caseId}/reports", token) ?? [];

    public string GetMyCaseReportPdfUrl(Guid caseId, Guid reportId)
        => $"/api/my-cases/{caseId}/reports/{reportId}/pdf";

    // Org-facing
    public async Task<IReadOnlyList<CaseReportSummary>> GetCaseReportsAsync(Guid orgId, Guid caseId, CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<CaseReportSummary>>($"/api/orgs/{orgId}/cases/{caseId}/reports", token) ?? [];

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

    public async Task<IReadOnlyList<CaseResearchEntryDto>> GetCaseResearchAsync(Guid orgId, Guid caseId, CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<CaseResearchEntryDto>>($"/api/orgs/{orgId}/cases/{caseId}/research", token) ?? [];

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

    public async Task<IReadOnlyList<CaseFileRecord>> GetCaseFilesAsync(Guid orgId, Guid caseId, CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<CaseFileRecord>>($"/api/orgs/{orgId}/cases/{caseId}/files", token) ?? [];

    public async Task<CaseFileRecord?> UploadCaseFileAsync(Guid orgId, Guid caseId, string? description, Stream content, string fileName, string contentType, CancellationToken token = default)
    {
        using var form = new MultipartFormDataContent();
        if (description is not null) form.Add(new StringContent(description), "description");
        using var sc = new StreamContent(content);
        sc.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(sc, "file", fileName);
        return await _api.PostMultipartAsync<CaseFileRecord>($"/api/orgs/{orgId}/cases/{caseId}/files", form, token);
    }

    public Task<bool> DeleteCaseFileAsync(Guid orgId, Guid caseId, Guid caseFileId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/orgs/{orgId}/cases/{caseId}/files/{caseFileId}", token);

    public Task<CaseFileRecord?> LinkCaseFileAsync(Guid orgId, Guid caseId, Guid uploadFileId, string? description = null, CancellationToken token = default)
        => _api.PostAsync<LinkCaseFileRequest, CaseFileRecord>(
            $"/api/orgs/{orgId}/cases/{caseId}/files/link/{uploadFileId}", new LinkCaseFileRequest(description), token);

    public Task<CaseFileRecord?> ExportAudioMixAsync(Guid orgId, Guid caseId, ExportAudioMixRequest request, CancellationToken token = default)
        => _api.PostAsync<ExportAudioMixRequest, CaseFileRecord>($"/api/orgs/{orgId}/cases/{caseId}/audio-mix/export", request, token);

    // ── Case Notes ────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<CaseNoteDto>> GetCaseNotesAsync(Guid orgId, Guid caseId, CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<CaseNoteDto>>($"/api/organizations/{orgId}/cases/{caseId}/notes", token) ?? [];

    public Task<CaseNoteDto?> CreateCaseNoteAsync(Guid orgId, Guid caseId, UpsertCaseNoteDto request, CancellationToken token = default)
        => _api.PostAsync<UpsertCaseNoteDto, CaseNoteDto>($"/api/organizations/{orgId}/cases/{caseId}/notes", request, token);

    public Task<CaseNoteDto?> UpdateCaseNoteAsync(Guid orgId, Guid caseId, Guid noteId, UpsertCaseNoteDto request, CancellationToken token = default)
        => _api.PutAsync<UpsertCaseNoteDto, CaseNoteDto>($"/api/organizations/{orgId}/cases/{caseId}/notes/{noteId}", request, token);

    public Task<bool> DeleteCaseNoteAsync(Guid orgId, Guid caseId, Guid noteId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/cases/{caseId}/notes/{noteId}", token);

    // ── Client Requests ───────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ClientRequestRecord>> GetMyClientRequestsAsync(CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<ClientRequestRecord>>("/api/client-requests/my", token);
        return result ?? [];
    }

    public Task<ClientRequestRecord?> GetClientRequestAsync(Guid id, CancellationToken token = default)
        => _api.GetAsync<ClientRequestRecord>($"/api/client-requests/{id}", token);

    public async Task<IReadOnlyList<ClientRequestOrganizationRecord>> GetClientRequestOrgsAsync(Guid id, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<ClientRequestOrganizationRecord>>($"/api/client-requests/{id}/organizations", token);
        return result ?? [];
    }

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

    // ── My Cases ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ClientCaseListItem>> GetMyCasesAsync(CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<ClientCaseListItem>>("/api/my-cases", token);
        return result ?? [];
    }

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

    public async Task<IReadOnlyList<CaseMessageRecord>> GetMyCaseMessagesAsync(Guid caseId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<CaseMessageRecord>>($"/api/my-cases/{caseId}/messages", token);
        return result ?? [];
    }

    public Task<CaseMessageRecord?> PostMyCaseMessageAsync(Guid caseId, string body, CancellationToken token = default)
        => _api.PostAsync<object, CaseMessageRecord>($"/api/my-cases/{caseId}/messages", new { Body = body }, token);

    // ── Co-client access management ───────────────────────────────────────────

    public async Task<IReadOnlyList<CoClientItem>> GetCoClientsAsync(Guid caseId, CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<CoClientItem>>($"/api/my-cases/{caseId}/co-clients", token) ?? [];

    public Task<CoClientItem?> AddCoClientAsync(Guid caseId, string email, CancellationToken token = default)
        => _api.PostAsync<object, CoClientItem>($"/api/my-cases/{caseId}/co-clients", new { Email = email }, token);

    public Task<bool> RemoveCoClientAsync(Guid caseId, Guid accessId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/my-cases/{caseId}/co-clients/{accessId}", token);

    // ── Sub-client invites (item #4) ──────────────────────────────────────────

    public async Task<IReadOnlyList<CaseClientInviteRecord>> GetCaseInvitesAsync(Guid caseId, CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<CaseClientInviteRecord>>($"/api/my-cases/{caseId}/invites", token) ?? [];

    public Task<InviteCoClientResult?> InviteCoClientAsync(Guid caseId, string email, CancellationToken token = default)
        => _api.PostAsync<object, InviteCoClientResult>($"/api/my-cases/{caseId}/invites", new { Email = email }, token);

    public Task<bool> RevokeCaseInviteAsync(Guid caseId, Guid inviteId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/my-cases/{caseId}/invites/{inviteId}", token);

    // ── Related people (basic-info, no account) ─────────────────────────────────

    public async Task<IReadOnlyList<CaseRelatedPersonRecord>> GetRelatedPeopleAsync(Guid caseId, CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<CaseRelatedPersonRecord>>($"/api/my-cases/{caseId}/related-people", token) ?? [];

    public Task<CaseRelatedPersonRecord?> AddRelatedPersonAsync(Guid caseId, AddRelatedPersonRequest request, CancellationToken token = default)
        => _api.PostAsync<AddRelatedPersonRequest, CaseRelatedPersonRecord>($"/api/my-cases/{caseId}/related-people", request, token);

    public Task<Ben.Data.Common.Enums.HelpAudience?> GetMyHelpAudienceAsync(CancellationToken token = default)
        => _api.GetAsync<Ben.Data.Common.Enums.HelpAudience?>("/api/me/help-audience", token);

    public async Task<List<VideoAssetAdminRecord>> GetVideoAssetsAsync(CancellationToken token = default)
        => await _api.GetAsync<List<VideoAssetAdminRecord>>("/api/admin/video-assets", token) ?? [];

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

    public async Task<List<SiteSettingRecord>> GetSiteSettingsAsync(CancellationToken token = default)
        => await _api.GetAsync<List<SiteSettingRecord>>("/api/admin/site-settings", token) ?? [];

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

    public async Task<IReadOnlyList<CaseMessageRecord>> GetCaseMessagesAsync(Guid orgId, Guid caseId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<CaseMessageRecord>>($"/api/orgs/{orgId}/cases/{caseId}/messages", token);
        return result ?? [];
    }

    public Task<CaseMessageRecord?> PostCaseMessageAsync(Guid orgId, Guid caseId, string body, CancellationToken token = default)
        => _api.PostAsync<object, CaseMessageRecord>($"/api/orgs/{orgId}/cases/{caseId}/messages", new { Body = body }, token);

    public async Task<int> GetCaseMessageUnreadCountAsync(Guid orgId, Guid caseId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<int>($"/api/orgs/{orgId}/cases/{caseId}/messages/unread-count", token);
        return result;
    }
}
