using Ben.Data.Common.Enums;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Web.Services;
using Microsoft.Extensions.Options;

namespace Ben.Web.Services.WebApi;

/// <summary>
/// The Platform half of the adapter — implements <see cref="Ben.Web.Services.IBenPlatformClient"/>.
/// </summary>
/// <remarks>
/// One partial class split across files by domain, matching the slices of IBenAdminClient.
/// The constructor and shared fields live in BenAdminClientAdapter.cs.
/// </remarks>
public sealed partial class BenAdminClientAdapter
{
    // ── Notifications ─────────────────────────────────────────────────────────

    public Task<NotificationSummaryResponse?> GetNotificationSummaryAsync(CancellationToken token = default)
        => _api.GetAsync<NotificationSummaryResponse>("/api/me/notification-summary", token);

    public Task<LoadResult<MyMessageRecord>> GetMyMessagesAsync(bool unreadOnly = false, CancellationToken token = default)
        => _api.GetListAsync<MyMessageRecord>($"/api/me/messages?unreadOnly={(unreadOnly ? "true" : "false")}", token);

    public Task<bool> MarkMyMessageReadAsync(Guid id, CancellationToken token = default)
        => _api.PutVoidAsync<object?>($"/api/me/messages/{id}/read", null, token);

    // ── Audit Log ─────────────────────────────────────────────────────────────

    public async Task<AuditLogPagedResponse?> GetAuditLogsAsync(
        int page = 1, int pageSize = 50, string? entityType = null, int? action = null,
        Guid? userId = null, DateTime? dateFrom = null, DateTime? dateTo = null,
        CancellationToken token = default)
    {
        var qs = $"?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(entityType)) qs += $"&entityType={Uri.EscapeDataString(entityType)}";
        if (action.HasValue)    qs += $"&action={action.Value}";
        if (userId.HasValue)    qs += $"&userId={userId.Value}";
        if (dateFrom.HasValue)  qs += $"&dateFrom={Uri.EscapeDataString(dateFrom.Value.ToString("o"))}";
        if (dateTo.HasValue)    qs += $"&dateTo={Uri.EscapeDataString(dateTo.Value.ToString("o"))}";
        return await _api.GetAsync<AuditLogPagedResponse>($"/api/admin/audit-logs{qs}", token);
    }

    public Task<LoadResult<string>> GetAuditLogEntityTypesAsync(CancellationToken token = default)
        => _api.GetListAsync<string>("/api/admin/audit-logs/entity-types", token);

    public Task<bool> SendAuditLogMessageAsync(SendAuditLogMessageRequest request, CancellationToken token = default)
        => _api.PostAsync<SendAuditLogMessageRequest, bool>("/api/admin/audit-logs/send-message", request, token);

    // ── Generic Lookup Types ──────────────────────────────────────────────────

    public Task<LoadResult<LookupTypeAdminRecord>> GetLookupTypesAsync(string route, CancellationToken token = default)
        => _api.GetListAsync<LookupTypeAdminRecord>($"/{route}", token);

    public Task<LookupTypeAdminRecord?> CreateLookupTypeAsync(string route, LookupTypeUpsertRequest request, CancellationToken token = default)
        => _api.PostAsync<LookupTypeUpsertRequest, LookupTypeAdminRecord>($"/{route}", request, token);

    public Task<LookupTypeAdminRecord?> UpdateLookupTypeAsync(string route, Guid id, LookupTypeUpsertRequest request, CancellationToken token = default)
        => _api.PutAsync<LookupTypeUpsertRequest, LookupTypeAdminRecord>($"/{route}/{id}", request, token);

    public Task<bool> DeleteLookupTypeAsync(string route, Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/{route}/{id}", token);

    // ── Support tickets ───────────────────────────────────────────────────────

    public Task<SiteContactInfo?> GetSiteContactAsync(CancellationToken token = default)
        => _api.GetAnonymousAsync<SiteContactInfo>("/api/public/site-contact", token);

    public Task<SiteFeaturesInfo?> GetSiteFeaturesAsync(CancellationToken token = default)
        => _api.GetAnonymousAsync<SiteFeaturesInfo>("/api/public/site-features", token);

    // ── Dashboard statistics ──────────────────────────────────────────────────

    public Task<AdminStatsSummary?> GetAdminStatsSummaryAsync(CancellationToken token = default)
        => _api.GetAsync<AdminStatsSummary>("/api/admin/stats/summary", token);

    public Task<AdminStatsCharts?> GetAdminStatsChartsAsync(int days = 30, CancellationToken token = default)
        => _api.GetAsync<AdminStatsCharts>($"/api/admin/stats/charts?days={days}", token);

    public Task<AdminSignInInsights?> GetAdminSignInInsightsAsync(int days = 30, CancellationToken token = default)
        => _api.GetAsync<AdminSignInInsights>($"/api/admin/stats/sign-ins?days={days}", token);

    public async Task<(bool Ok, string? Error)> FlagFieldSessionAsync(
        Guid fieldSessionId, string? reason, CancellationToken token = default)
    {
        var (_, error) = await _api.SendExpectingReasonAsync<object, object>(
            HttpMethod.Post, $"/api/field-sessions/{fieldSessionId}/flag", new { Reason = reason }, token);
        return (error is null, error);
    }

    public string GetArchiveMediaUrl(Guid fieldSessionId, Guid uploadFileId)
        => $"{_webApiBaseUrl}/api/public/field-sessions/{fieldSessionId}/media/{uploadFileId}";

    public Task<OrganizationPurgePreview?> GetOrganizationPurgePreviewAsync(
        Guid organizationId, CancellationToken token = default)
        => _api.GetAsync<OrganizationPurgePreview>(
            $"/api/admin/organizations/{organizationId}/purge", token);

    public async Task<(OrganizationPurgePreview? Removed, string? Error)> PurgeOrganizationAsync(
        Guid organizationId, string confirmName, CancellationToken token = default)
    {
        var (removed, error) = await _api.SendExpectingReasonAsync<object, OrganizationPurgePreview>(
            HttpMethod.Delete, $"/api/admin/organizations/{organizationId}/purge",
            new { ConfirmName = confirmName }, token);
        return (removed, error);
    }

    public Task<SessionInsightsRecord?> GetSessionInsightsAsync(
        Guid sessionId, CancellationToken token = default)
        => _api.GetAsync<SessionInsightsRecord>($"/api/field-sessions/{sessionId}/insights", token);

    public Task<OrgStatsSummary?> GetOrgStatsAsync(Guid organizationId, CancellationToken token = default)
        => _api.GetAsync<OrgStatsSummary>($"/api/organizations/{organizationId}/stats", token);

    public Task<LoadResult<AdminOrganizationAdRecord>> GetAdminOrgAdsAsync(CancellationToken token = default)
        => _api.GetListAsync<AdminOrganizationAdRecord>("/api/admin/organization-ads", token);

    public async Task<(bool Ok, string? Error)> ApproveOrgAdAsync(Guid adId, CancellationToken token = default)
    {
        var (_, error) = await _api.SendExpectingReasonAsync<object, object>(
            HttpMethod.Post, $"/api/admin/organization-ads/{adId}/approve", new { }, token);
        return (error is null, error);
    }

    public async Task<(bool Ok, string? Error)> RejectOrgAdAsync(Guid adId, string reason, CancellationToken token = default)
    {
        var (_, error) = await _api.SendExpectingReasonAsync<object, object>(
            HttpMethod.Post, $"/api/admin/organization-ads/{adId}/reject", new { Reason = reason }, token);
        return (error is null, error);
    }

    public async Task<LoadResult<PromotedGroupCard>> GetPromotedGroupsAnonymousAsync(
        int take = 3, CancellationToken token = default,
        double? lat = null, double? lon = null)
    {
        // The viewer's consented coordinates order the answer nearest-first (item 186 F8) and
        // are never sent unless the person shared them this session.
        var geo = lat is { } la && lon is { } lo
            ? $"&lat={la.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
              + $"&lon={lo.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : string.Empty;

        // Null means the FETCH failed (an empty placement list deserializes as an empty
        // list, not null) — reported as Failure so no caller mistakes an outage for
        // "no ads exist". The card component treats failure as render-nothing, on record
        // in LoadResultRenderedGuardTests.Decorations.
        var cards = await _api.GetAnonymousAsync<List<PromotedGroupCard>>(
            $"/api/public/promoted-groups?take={take}{geo}", token);
        return cards is null
            ? LoadResult<PromotedGroupCard>.Failure()
            : LoadResult<PromotedGroupCard>.Ok(cards);
    }

    public Task<LoadResult<string>> GetMyDismissedToursAsync(CancellationToken token = default)
        => _api.GetListAsync<string>("/api/me/tours", token);

    public async Task DismissTourAsync(string tourName, bool completed, CancellationToken token = default)
        => await _api.PutAsync<object, object>($"/api/me/tours/{Uri.EscapeDataString(tourName)}",
               new { Completed = completed }, token);

    public Task<SupportFormTokenResponse?> GetSupportFormTokenAsync(CancellationToken token = default)
        => _api.GetAnonymousAsync<SupportFormTokenResponse>("/api/public/support-tickets/form-token", token);

    public Task<SubmitSupportTicketResponse?> SubmitSupportTicketAsync(SubmitSupportTicketRequest request, CancellationToken token = default)
        => _api.PostAnonymousAsync<SubmitSupportTicketRequest, SubmitSupportTicketResponse>("/api/public/support-tickets", request, token);

    public Task<SupportTicketPublicRecord?> GetSupportTicketByTokenAsync(Guid accessToken, CancellationToken token = default)
        => _api.GetAnonymousAsync<SupportTicketPublicRecord>($"/api/public/support-tickets/{accessToken}", token);

    public Task<bool> ReplyToSupportTicketByTokenAsync(Guid accessToken, AddSupportTicketReplyRequest request, CancellationToken token = default)
        => _api.PostAnonymousVoidAsync($"/api/public/support-tickets/{accessToken}/replies", request, token);

    public async Task<SupportTicketPage?> GetSupportTicketsAsync(SupportTicketStatus? status = null, SupportTicketTopic? topic = null, string? search = null, int page = 1, int pageSize = 25, CancellationToken token = default)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (status is not null) query.Add($"status={(int)status}");
        if (topic is not null) query.Add($"topic={(int)topic}");
        if (!string.IsNullOrWhiteSpace(search)) query.Add($"search={Uri.EscapeDataString(search)}");
        return await _api.GetAsync<SupportTicketPage>($"/api/admin/support-tickets?{string.Join("&", query)}", token);
    }

    public Task<LoadResult<SupportTicketReplyRecord>> GetSupportTicketRepliesAsync(Guid id, CancellationToken token = default)
        => _api.GetListAsync<SupportTicketReplyRecord>($"/api/admin/support-tickets/{id}/replies", token);

    public Task<bool> AddSupportTicketReplyAsync(Guid id, AddSupportTicketReplyRequest request, CancellationToken token = default)
        => _api.PostVoidAsync($"/api/admin/support-tickets/{id}/replies", request, token);

    public Task<SupportTicketAdminRecord?> UpdateSupportTicketAsync(Guid id, UpdateSupportTicketRequest request, CancellationToken token = default)
        => _api.PutAsync<UpdateSupportTicketRequest, SupportTicketAdminRecord>($"/api/admin/support-tickets/{id}", request, token);

    // ── Messaging ─────────────────────────────────────────────────────────────

    public Task<LoadResult<OrgMessageRecord>> GetOrgInboxAsync(Guid orgId, CancellationToken token = default)
        => _api.GetListAsync<OrgMessageRecord>($"/api/organizations/{orgId}/messages/inbox", token);

    public Task<LoadResult<OrgMessageRecord>> GetOrgSentAsync(Guid orgId, CancellationToken token = default)
        => _api.GetListAsync<OrgMessageRecord>($"/api/organizations/{orgId}/messages/sent", token);

    public Task<OrgMessageRecord?> GetOrgMessageAsync(Guid orgId, Guid messageId, CancellationToken token = default)
        => _api.GetAsync<OrgMessageRecord>($"/api/organizations/{orgId}/messages/{messageId}", token);

    public Task<OrgMessageRecord?> SendOrgMessageAsync(Guid orgId, SendOrgMessageRequest request, CancellationToken token = default)
        => _api.PostAsync<SendOrgMessageRequest, OrgMessageRecord>($"/api/organizations/{orgId}/messages", request, token);

    // ── Calendar ──────────────────────────────────────────────────────────────

    public Task<LoadResult<OrgCalendarEventTypeRecord>> GetCalendarEventTypesAsync(Guid orgId, CancellationToken token = default)
        => _api.GetListAsync<OrgCalendarEventTypeRecord>($"/api/organizations/{orgId}/calendar-event-types", token);

    public Task<OrgCalendarEventTypeRecord?> CreateCalendarEventTypeAsync(Guid orgId, UpsertCalendarEventTypeRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertCalendarEventTypeRequest, OrgCalendarEventTypeRecord>($"/api/organizations/{orgId}/calendar-event-types", request, token);

    public Task<OrgCalendarEventTypeRecord?> UpdateCalendarEventTypeAsync(Guid orgId, Guid id, UpsertCalendarEventTypeRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertCalendarEventTypeRequest, OrgCalendarEventTypeRecord>($"/api/organizations/{orgId}/calendar-event-types/{id}", request, token);

    public Task<bool> DeleteCalendarEventTypeAsync(Guid orgId, Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/calendar-event-types/{id}", token);

    public Task<LoadResult<OrgCalendarEventRecord>> GetCalendarEventsAsync(Guid orgId, DateTime? from = null, DateTime? to = null, CancellationToken token = default)
    {
        var qs = string.Empty;
        if (from.HasValue) qs += $"?from={Uri.EscapeDataString(from.Value.ToString("o"))}";
        if (to.HasValue)   qs += (qs.Length > 0 ? "&" : "?") + $"to={Uri.EscapeDataString(to.Value.ToString("o"))}";
        return _api.GetListAsync<OrgCalendarEventRecord>($"/api/organizations/{orgId}/calendar{qs}", token);
    }

    public Task<OrgCalendarEventRecord?> GetCalendarEventAsync(Guid orgId, Guid eventId, CancellationToken token = default)
        => _api.GetAsync<OrgCalendarEventRecord>($"/api/organizations/{orgId}/calendar/{eventId}", token);

    public Task<(OrgCalendarEventRecord? Result, string? Error)> SaveCalendarEventAsync(
        Guid orgId, Guid? eventId, UpsertCalendarEventRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<UpsertCalendarEventRequest, OrgCalendarEventRecord>(
               eventId is null ? HttpMethod.Post : HttpMethod.Put,
               eventId is null
                   ? $"/api/organizations/{orgId}/calendar"
                   : $"/api/organizations/{orgId}/calendar/{eventId}",
               request, token);

    public Task<OrgCalendarEventRecord?> CreateCalendarEventAsync(Guid orgId, UpsertCalendarEventRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertCalendarEventRequest, OrgCalendarEventRecord>($"/api/organizations/{orgId}/calendar", request, token);

    public Task<OrgCalendarEventRecord?> UpdateCalendarEventAsync(Guid orgId, Guid eventId, UpsertCalendarEventRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertCalendarEventRequest, OrgCalendarEventRecord>($"/api/organizations/{orgId}/calendar/{eventId}", request, token);

    public Task<bool> DeleteCalendarEventAsync(Guid orgId, Guid eventId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/calendar/{eventId}", token);

    public Task<LoadResult<OrgCalendarEventAttendeeRecord>> GetCalendarEventAttendeesAsync(Guid orgId, Guid eventId, CancellationToken token = default)
        => _api.GetListAsync<OrgCalendarEventAttendeeRecord>($"/api/organizations/{orgId}/calendar/{eventId}/attendees", token);

    public Task<OrgCalendarEventAttendeeRecord?> AddCalendarAttendeeAsync(Guid orgId, Guid eventId, AddAttendeeRequest request, CancellationToken token = default)
        => _api.PostAsync<AddAttendeeRequest, OrgCalendarEventAttendeeRecord>($"/api/organizations/{orgId}/calendar/{eventId}/attendees", request, token);

    public Task<OrgCalendarEventAttendeeRecord?> AddCalendarAttendeeByEmailAsync(Guid orgId, Guid eventId, string email, CancellationToken token = default)
        => _api.PostAsync<AddAttendeeByEmailRequest, OrgCalendarEventAttendeeRecord>(
               $"/api/organizations/{orgId}/calendar/{eventId}/attendees/by-email",
               new AddAttendeeByEmailRequest(email), token);

    public Task<bool> InviteEventGuestAsync(
        Guid orgId, Guid eventId, string email, string? displayName = null, CancellationToken token = default)
        => _api.PostAsync<InviteGuestRequest, bool>(
               $"/api/organizations/{orgId}/calendar/{eventId}/guest-invites",
               new InviteGuestRequest(email, displayName), token);

    public Task<OrgCalendarEventAttendeeRecord?> RsvpCalendarEventAsync(Guid orgId, Guid eventId, Guid attendeeId, Ben.Data.Common.Enums.RsvpStatus status, CancellationToken token = default)
        => _api.PutAsync<object, OrgCalendarEventAttendeeRecord>(
               $"/api/organizations/{orgId}/calendar/{eventId}/attendees/{attendeeId}/rsvp",
               new { RsvpStatus = status }, token);

    public Task<bool> RemoveCalendarAttendeeAsync(Guid orgId, Guid eventId, Guid attendeeId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/calendar/{eventId}/attendees/{attendeeId}", token);

    // ── Experience Taxonomy ───────────────────────────────────────────────────

    public Task<LoadResult<ExperienceCategoryWithTypesResponse>> GetExperienceTaxonomyAsync(CancellationToken token = default)
        => _api.GetListAsync<ExperienceCategoryWithTypesResponse>("/api/experience-categories/with-types", token);

    public Task<LoadResult<ExperienceCategoryRecord>> GetAllExperienceCategoriesAsync(CancellationToken token = default)
        => _api.GetListAsync<ExperienceCategoryRecord>("/api/admin/experience-categories", token);

    public Task<LoadResult<ExperienceTypeRecord>> GetAllExperienceTypesAsync(Guid categoryId, CancellationToken token = default)
        => _api.GetListAsync<ExperienceTypeRecord>($"/api/admin/experience-categories/{categoryId}/types", token);

    public Task<ExperienceCategoryRecord?> CreateExperienceCategoryAsync(UpsertExperienceCategoryRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertExperienceCategoryRequest, ExperienceCategoryRecord>("/api/admin/experience-categories", request, token);

    public Task<ExperienceCategoryRecord?> UpdateExperienceCategoryAsync(Guid id, UpsertExperienceCategoryRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertExperienceCategoryRequest, ExperienceCategoryRecord>($"/api/admin/experience-categories/{id}", request, token);

    public Task<bool> DeleteExperienceCategoryAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/admin/experience-categories/{id}", token);

    public Task<ExperienceCategoryRecord?> ApproveExperienceCategoryAsync(Guid id, CancellationToken token = default)
        => _api.PutAsync<object, ExperienceCategoryRecord>($"/api/admin/experience-categories/{id}/approve", new { }, token);

    public Task<ExperienceTypeRecord?> CreateExperienceTypeAsync(Guid categoryId, UpsertExperienceTypeRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertExperienceTypeRequest, ExperienceTypeRecord>($"/api/admin/experience-categories/{categoryId}/types", request, token);

    public Task<ExperienceTypeRecord?> UpdateExperienceTypeAsync(Guid categoryId, Guid id, UpsertExperienceTypeRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertExperienceTypeRequest, ExperienceTypeRecord>($"/api/admin/experience-categories/{categoryId}/types/{id}", request, token);

    public Task<bool> DeleteExperienceTypeAsync(Guid categoryId, Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/admin/experience-categories/{categoryId}/types/{id}", token);

    public Task<ExperienceTypeRecord?> ApproveExperienceTypeAsync(Guid categoryId, Guid id, CancellationToken token = default)
        => _api.PutAsync<object, ExperienceTypeRecord>($"/api/admin/experience-categories/{categoryId}/types/{id}/approve", new { }, token);

    public Task<RejectExperienceTypeResponse?> RejectExperienceTypeAsync(Guid categoryId, Guid id, CancellationToken token = default)
        => _api.PutAsync<object, RejectExperienceTypeResponse>($"/api/admin/experience-categories/{categoryId}/types/{id}/reject", new { }, token);

    public async Task<TaxonomyProposal<ExperienceTypeRecord>> AddOrgExperienceTypeAsync(
        Guid orgId, AddOrgExperienceTypeRequest request, CancellationToken token = default)
    {
        var (created, conflict) = await _api
            .PostExpectingConflictAsync<AddOrgExperienceTypeRequest, ExperienceTypeRecord, ProbableDuplicateResponse>(
                $"/api/organizations/{orgId}/experience-types", request, token);

        return new TaxonomyProposal<ExperienceTypeRecord>(created, conflict);
    }

    // ── Votes ────────────────────────────────────────────────

    public Task<UploadFileVoteSummary?> GetVoteSummaryAsync(Guid fileId, CancellationToken token = default)
        => _api.GetVoteSummaryAsync(fileId, token);

    public Task<UploadFileVoteRecord?> UpsertMyVoteAsync(Guid fileId, int score, CancellationToken token = default)
        => _api.UpsertMyVoteAsync(fileId, score, token);

    public Task<bool> RemoveMyVoteAsync(Guid fileId, CancellationToken token = default)
        => _api.RemoveMyVoteAsync(fileId, token);

    // ── Rate limits (item 199) ────────────────────────────────────────────────

    public Task<LoadResult<RateLimitRefusalRecord>> GetRateLimitRefusalsAsync(
        CancellationToken token = default)
        => _api.GetListAsync<RateLimitRefusalRecord>("/api/admin/rate-limits", token);

    public Task<bool> ReArmRateLimitNoticeAsync(
        string policyName, CancellationToken token = default)
        => _api.PostAsync<object, bool>(
               $"/api/admin/rate-limits/{Uri.EscapeDataString(policyName)}/notify-again",
               new object(), token);

    // ── Mail diagnostics ──────────────────────────────────────────────────────

    public Task<MailSettingsRecord?> GetMailSettingsAsync(CancellationToken token = default)
        => _api.GetAsync<MailSettingsRecord>("/api/admin/mail/settings", token);

    public Task<MailTestResultRecord?> SendTestEmailAsync(
        string to, CancellationToken token = default)
        => _api.PostAsync<object, MailTestResultRecord>(
               "/api/admin/mail/test", new { To = to }, token);

    // ── Sidecar telemetry ─────────────────────────────────────────────────────

    public Task<LoadResult<SidecarInstallLogRecord>> GetSidecarTelemetryAsync(
        int take = 200, CancellationToken token = default)
        => _api.GetListAsync<SidecarInstallLogRecord>($"/api/sidecar-telemetry?take={take}", token);

    public Task<SidecarTelemetrySummaryRecord?> GetSidecarTelemetrySummaryAsync(
        CancellationToken token = default)
        => _api.GetAsync<SidecarTelemetrySummaryRecord>("/api/sidecar-telemetry/summary", token);

    // ── Published investigations (item #89) ─────────────────────────────────

    public Task<LoadResult<PublicInvestigationListItem>> GetPublishedInvestigationsAsync(
        string orgUrlName, CancellationToken token = default)
        => _api.GetAnonymousListAsync<PublicInvestigationListItem>($"/api/public/organizations/{Uri.EscapeDataString(orgUrlName)}/investigations", token);

    public Task<PublicInvestigationDetail?> GetPublishedInvestigationAsync(
        string orgUrlName, string investigationSlug, CancellationToken token = default)
        => _api.GetAnonymousAsync<PublicInvestigationDetail>(
               $"/api/public/organizations/{Uri.EscapeDataString(orgUrlName)}/investigations/{Uri.EscapeDataString(investigationSlug)}",
               token);

    // ── Local discovery (item #88) ────────────────────────────────────────────

    public Task<NearbyResults?> GetNearbyAsync(
        double latitude, double longitude, double radiusMiles, string? query = null,
        CancellationToken token = default)
    {
        var url = $"/api/public/search/nearby?lat={latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                + $"&lon={longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                + $"&radiusMiles={radiusMiles.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        if (!string.IsNullOrWhiteSpace(query))
            url += $"&query={Uri.EscapeDataString(query)}";

        return _api.GetAnonymousAsync<NearbyResults>(url, token);
    }

    // ── Merging groups (item 110) ─────────────────────────────────────────────

    public async Task<(Ben.Service.Models.Admin.MergePreview? Result, string? Error)> PreviewOrgMergeAsync(
        Guid baseId, Guid mergedId, CancellationToken token = default)
    {
        var preview = await _api.GetAsync<Ben.Service.Models.Admin.MergePreview>(
            $"/api/admin/organization-merge/preview?baseId={baseId}&mergedId={mergedId}", token);
        return preview is null ? (null, "The preview could not be computed.") : (preview, null);
    }

    public async Task<string?> MergeOrganizationsAsync(
        Ben.Service.Models.Admin.OrganizationMergeRequest request, CancellationToken token = default)
    {
        var (_, error) = await _api.SendExpectingReasonAsync<Ben.Service.Models.Admin.OrganizationMergeRequest, object>(
            HttpMethod.Post, "/api/admin/organization-merge", request, token);
        return error;
    }
}
