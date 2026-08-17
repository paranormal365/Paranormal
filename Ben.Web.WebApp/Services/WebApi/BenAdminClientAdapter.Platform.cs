using Ben.Data.Common.Enums;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Web.Library.Services;
using Microsoft.Extensions.Options;

namespace Ben.Web.WebApp.Services.WebApi;

/// <summary>
/// The Platform half of the adapter — implements <see cref="Ben.Web.Library.Services.IBenPlatformClient"/>.
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

    public async Task<List<MyMessageRecord>> GetMyMessagesAsync(bool unreadOnly = false, CancellationToken token = default)
        => await _api.GetAsync<List<MyMessageRecord>>(
               $"/api/me/messages?unreadOnly={(unreadOnly ? "true" : "false")}", token) ?? [];

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

    public async Task<IReadOnlyList<string>> GetAuditLogEntityTypesAsync(CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<string>>("/api/admin/audit-logs/entity-types", token);
        return result ?? [];
    }

    public Task<bool> SendAuditLogMessageAsync(SendAuditLogMessageRequest request, CancellationToken token = default)
        => _api.PostAsync<SendAuditLogMessageRequest, bool>("/api/admin/audit-logs/send-message", request, token);

    // ── Generic Lookup Types ──────────────────────────────────────────────────

    public async Task<IReadOnlyList<LookupTypeAdminRecord>> GetLookupTypesAsync(string route, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<LookupTypeAdminRecord>>($"/{route}", token);
        return result ?? [];
    }

    public Task<LookupTypeAdminRecord?> CreateLookupTypeAsync(string route, LookupTypeUpsertRequest request, CancellationToken token = default)
        => _api.PostAsync<LookupTypeUpsertRequest, LookupTypeAdminRecord>($"/{route}", request, token);

    public Task<LookupTypeAdminRecord?> UpdateLookupTypeAsync(string route, Guid id, LookupTypeUpsertRequest request, CancellationToken token = default)
        => _api.PutAsync<LookupTypeUpsertRequest, LookupTypeAdminRecord>($"/{route}/{id}", request, token);

    public Task<bool> DeleteLookupTypeAsync(string route, Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/{route}/{id}", token);

    // ── Support tickets ───────────────────────────────────────────────────────

    public Task<SiteContactInfo?> GetSiteContactAsync(CancellationToken token = default)
        => _api.GetAnonymousAsync<SiteContactInfo>("/api/public/site-contact", token);

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

    public async Task<IReadOnlyList<SupportTicketReplyRecord>> GetSupportTicketRepliesAsync(Guid id, CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<SupportTicketReplyRecord>>($"/api/admin/support-tickets/{id}/replies", token) ?? [];

    public Task<bool> AddSupportTicketReplyAsync(Guid id, AddSupportTicketReplyRequest request, CancellationToken token = default)
        => _api.PostVoidAsync($"/api/admin/support-tickets/{id}/replies", request, token);

    public Task<SupportTicketAdminRecord?> UpdateSupportTicketAsync(Guid id, UpdateSupportTicketRequest request, CancellationToken token = default)
        => _api.PutAsync<UpdateSupportTicketRequest, SupportTicketAdminRecord>($"/api/admin/support-tickets/{id}", request, token);

    // ── Messaging ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<OrgMessageRecord>> GetOrgInboxAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrgMessageRecord>>($"/api/organizations/{orgId}/messages/inbox", token);
        return result ?? [];
    }

    public async Task<IReadOnlyList<OrgMessageRecord>> GetOrgSentAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrgMessageRecord>>($"/api/organizations/{orgId}/messages/sent", token);
        return result ?? [];
    }

    public Task<OrgMessageRecord?> GetOrgMessageAsync(Guid orgId, Guid messageId, CancellationToken token = default)
        => _api.GetAsync<OrgMessageRecord>($"/api/organizations/{orgId}/messages/{messageId}", token);

    public Task<OrgMessageRecord?> SendOrgMessageAsync(Guid orgId, SendOrgMessageRequest request, CancellationToken token = default)
        => _api.PostAsync<SendOrgMessageRequest, OrgMessageRecord>($"/api/organizations/{orgId}/messages", request, token);

    // ── Calendar ──────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<OrgCalendarEventTypeRecord>> GetCalendarEventTypesAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrgCalendarEventTypeRecord>>($"/api/organizations/{orgId}/calendar-event-types", token);
        return result ?? [];
    }

    public Task<OrgCalendarEventTypeRecord?> CreateCalendarEventTypeAsync(Guid orgId, UpsertCalendarEventTypeRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertCalendarEventTypeRequest, OrgCalendarEventTypeRecord>($"/api/organizations/{orgId}/calendar-event-types", request, token);

    public Task<OrgCalendarEventTypeRecord?> UpdateCalendarEventTypeAsync(Guid orgId, Guid id, UpsertCalendarEventTypeRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertCalendarEventTypeRequest, OrgCalendarEventTypeRecord>($"/api/organizations/{orgId}/calendar-event-types/{id}", request, token);

    public Task<bool> DeleteCalendarEventTypeAsync(Guid orgId, Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/calendar-event-types/{id}", token);

    public async Task<IReadOnlyList<OrgCalendarEventRecord>> GetCalendarEventsAsync(Guid orgId, DateTime? from = null, DateTime? to = null, CancellationToken token = default)
    {
        var qs = string.Empty;
        if (from.HasValue) qs += $"?from={Uri.EscapeDataString(from.Value.ToString("o"))}";
        if (to.HasValue)   qs += (qs.Length > 0 ? "&" : "?") + $"to={Uri.EscapeDataString(to.Value.ToString("o"))}";
        var result = await _api.GetAsync<IReadOnlyList<OrgCalendarEventRecord>>($"/api/organizations/{orgId}/calendar{qs}", token);
        return result ?? [];
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

    public async Task<IReadOnlyList<OrgCalendarEventAttendeeRecord>> GetCalendarEventAttendeesAsync(Guid orgId, Guid eventId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrgCalendarEventAttendeeRecord>>($"/api/organizations/{orgId}/calendar/{eventId}/attendees", token);
        return result ?? [];
    }

    public Task<OrgCalendarEventAttendeeRecord?> AddCalendarAttendeeAsync(Guid orgId, Guid eventId, AddAttendeeRequest request, CancellationToken token = default)
        => _api.PostAsync<AddAttendeeRequest, OrgCalendarEventAttendeeRecord>($"/api/organizations/{orgId}/calendar/{eventId}/attendees", request, token);

    public Task<OrgCalendarEventAttendeeRecord?> AddCalendarAttendeeByEmailAsync(Guid orgId, Guid eventId, string email, CancellationToken token = default)
        => _api.PostAsync<AddAttendeeByEmailRequest, OrgCalendarEventAttendeeRecord>(
               $"/api/organizations/{orgId}/calendar/{eventId}/attendees/by-email",
               new AddAttendeeByEmailRequest(email), token);

    public Task<OrgCalendarEventAttendeeRecord?> RsvpCalendarEventAsync(Guid orgId, Guid eventId, Guid attendeeId, Ben.Data.Common.Enums.RsvpStatus status, CancellationToken token = default)
        => _api.PutAsync<object, OrgCalendarEventAttendeeRecord>(
               $"/api/organizations/{orgId}/calendar/{eventId}/attendees/{attendeeId}/rsvp",
               new { RsvpStatus = status }, token);

    public Task<bool> RemoveCalendarAttendeeAsync(Guid orgId, Guid eventId, Guid attendeeId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/calendar/{eventId}/attendees/{attendeeId}", token);

    // ── Experience Taxonomy ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<ExperienceCategoryWithTypesResponse>> GetExperienceTaxonomyAsync(CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<ExperienceCategoryWithTypesResponse>>("/api/experience-categories/with-types", token);
        return result ?? [];
    }

    public async Task<IReadOnlyList<ExperienceCategoryRecord>> GetAllExperienceCategoriesAsync(CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<ExperienceCategoryRecord>>("/api/admin/experience-categories", token);
        return result ?? [];
    }

    public async Task<IReadOnlyList<ExperienceTypeRecord>> GetAllExperienceTypesAsync(Guid categoryId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<ExperienceTypeRecord>>($"/api/admin/experience-categories/{categoryId}/types", token);
        return result ?? [];
    }

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

    public Task<ExperienceTypeRecord?> AddOrgExperienceTypeAsync(Guid orgId, AddOrgExperienceTypeRequest request, CancellationToken token = default)
        => _api.PostAsync<AddOrgExperienceTypeRequest, ExperienceTypeRecord>($"/api/organizations/{orgId}/experience-types", request, token);

    // ── Votes ────────────────────────────────────────────────

    public Task<UploadFileVoteSummary?> GetVoteSummaryAsync(Guid fileId, CancellationToken token = default)
        => _api.GetVoteSummaryAsync(fileId, token);

    public Task<UploadFileVoteRecord?> UpsertMyVoteAsync(Guid fileId, int score, CancellationToken token = default)
        => _api.UpsertMyVoteAsync(fileId, score, token);

    public Task<bool> RemoveMyVoteAsync(Guid fileId, CancellationToken token = default)
        => _api.RemoveMyVoteAsync(fileId, token);

    // ── Sidecar telemetry ─────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SidecarInstallLogRecord>> GetSidecarTelemetryAsync(
        int take = 200, CancellationToken token = default)
        => await _api.GetAsync<List<SidecarInstallLogRecord>>(
               $"/api/sidecar-telemetry?take={take}", token) ?? [];

    public Task<SidecarTelemetrySummaryRecord?> GetSidecarTelemetrySummaryAsync(
        CancellationToken token = default)
        => _api.GetAsync<SidecarTelemetrySummaryRecord>("/api/sidecar-telemetry/summary", token);

    // ── Published investigations (item #89) ─────────────────────────────────

    public async Task<IReadOnlyList<PublicInvestigationListItem>> GetPublishedInvestigationsAsync(
        string orgUrlName, CancellationToken token = default)
    {
        var result = await _api.GetAnonymousAsync<IReadOnlyList<PublicInvestigationListItem>>(
            $"/api/public/organizations/{Uri.EscapeDataString(orgUrlName)}/investigations", token);
        return result ?? [];
    }

    public Task<PublicInvestigationDetail?> GetPublishedInvestigationAsync(
        string orgUrlName, string investigationSlug, CancellationToken token = default)
        => _api.GetAnonymousAsync<PublicInvestigationDetail>(
               $"/api/public/organizations/{Uri.EscapeDataString(orgUrlName)}/investigations/{Uri.EscapeDataString(investigationSlug)}",
               token);
}
