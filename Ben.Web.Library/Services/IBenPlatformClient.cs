using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Data.Common.Enums;

namespace Ben.Web.Library.Services;

/// <summary>
/// The Platform slice of <see cref="IBenAdminClient"/> — site-wide concerns that belong to no single feature.
/// </summary>
/// <remarks>
/// Part of splitting one 383-method interface into domain-sized pieces.
/// <see cref="IBenAdminClient"/> inherits every slice, so existing callers and the single
/// adapter are unchanged; new code (and test doubles) can depend on just the slice it needs.
/// </remarks>
public interface IBenPlatformClient
{
    // ── Notifications ─────────────────────────────────────────────────────────

    /// <summary>
    /// Everything waiting on the current user, in one round trip: unread counts per bucket plus
    /// the age of the oldest item in each. Backs the bell badge and the drawer counts.
    /// </summary>
    Task<NotificationSummaryResponse?> GetNotificationSummaryAsync(CancellationToken token = default);

    /// <summary>Platform messages addressed to the current user, newest first.</summary>
    /// <param name="unreadOnly">Restrict to messages never opened.</param>
    Task<List<MyMessageRecord>> GetMyMessagesAsync(bool unreadOnly = false, CancellationToken token = default);

    /// <summary>Marks one of the current user's messages read. <paramref name="id"/> is the record's Id.</summary>
    Task<bool> MarkMyMessageReadAsync(Guid id, CancellationToken token = default);

    /// <summary>Marks every unread message of the current user's read. Returns how many changed.</summary>
    Task<int> MarkAllMyMessagesReadAsync(CancellationToken token = default);

    /// <summary>Pending file-permission requests awaiting the current user, with names resolved.</summary>
    Task<List<PendingPermissionRequestRecord>> GetPendingPermissionRequestsForMeAsync(CancellationToken token = default);

    // ── Sidecar telemetry ─────────────────────────────────────────────────────
    /// <summary>Recorded sidecar install/pair events, newest first (SuperAdmin).</summary>
    Task<IReadOnlyList<SidecarInstallLogRecord>> GetSidecarTelemetryAsync(int take = 200, CancellationToken token = default);

    /// <summary>Distinct-install counts and a per-version breakdown (SuperAdmin).</summary>
    Task<SidecarTelemetrySummaryRecord?> GetSidecarTelemetrySummaryAsync(CancellationToken token = default);

    // ── Audit Log ─────────────────────────────────────────────────────────────

    Task<AuditLogPagedResponse?> GetAuditLogsAsync(int page = 1, int pageSize = 50, string? entityType = null, int? action = null, Guid? userId = null, DateTime? dateFrom = null, DateTime? dateTo = null, CancellationToken token = default);
    Task<IReadOnlyList<string>> GetAuditLogEntityTypesAsync(CancellationToken token = default);
    Task<bool> SendAuditLogMessageAsync(SendAuditLogMessageRequest request, CancellationToken token = default);

    // ── Generic Lookup Types ──────────────────────────────────────────────────
    // Covers UserAddressType, UserEmailType, UserPhoneType, UserLinkType, UserNoteType,
    // UserMessageType, and the five Org equivalents — all share the same schema.

    /// <summary>Returns all rows for a lookup-type table at the given admin API route.</summary>
    Task<IReadOnlyList<LookupTypeAdminRecord>> GetLookupTypesAsync(string route, CancellationToken token = default);

    /// <summary>Creates a new row in a lookup-type table.</summary>
    Task<LookupTypeAdminRecord?> CreateLookupTypeAsync(string route, LookupTypeUpsertRequest request, CancellationToken token = default);

    /// <summary>Updates an existing row in a lookup-type table.</summary>
    Task<LookupTypeAdminRecord?> UpdateLookupTypeAsync(string route, Guid id, LookupTypeUpsertRequest request, CancellationToken token = default);

    /// <summary>Deletes a row from a lookup-type table.</summary>
    Task<bool> DeleteLookupTypeAsync(string route, Guid id, CancellationToken token = default);

    // ── Support tickets ───────────────────────────────────────────────────────

    /// <summary>The site's published contact details, for the contact page. Anonymous.</summary>
    Task<SiteContactInfo?> GetSiteContactAsync(CancellationToken token = default);

    /// <summary>Issued when the contact form renders; proves later how long it was on screen.</summary>
    Task<SupportFormTokenResponse?> GetSupportFormTokenAsync(CancellationToken token = default);

    /// <summary>Sends a contact-form submission. Anonymous.</summary>
    Task<SubmitSupportTicketResponse?> SubmitSupportTicketAsync(SubmitSupportTicketRequest request, CancellationToken token = default);

    /// <summary>A sender's own ticket, by the token from their tracking link.</summary>
    Task<SupportTicketPublicRecord?> GetSupportTicketByTokenAsync(Guid accessToken, CancellationToken token = default);

    /// <summary>Adds the sender's own reply through their tracking link.</summary>
    Task<bool> ReplyToSupportTicketByTokenAsync(Guid accessToken, AddSupportTicketReplyRequest request, CancellationToken token = default);

    /// <summary>The staff queue, filtered and paged on the server.</summary>
    Task<SupportTicketPage?> GetSupportTicketsAsync(SupportTicketStatus? status = null, SupportTicketTopic? topic = null, string? search = null, int page = 1, int pageSize = 25, CancellationToken token = default);

    /// <summary>One ticket's full thread, internal notes included.</summary>
    Task<IReadOnlyList<SupportTicketReplyRecord>> GetSupportTicketRepliesAsync(Guid id, CancellationToken token = default);

    /// <summary>Replies to the sender, or leaves an internal note.</summary>
    Task<bool> AddSupportTicketReplyAsync(Guid id, AddSupportTicketReplyRequest request, CancellationToken token = default);

    /// <summary>Changes a ticket's status and/or assignment.</summary>
    Task<SupportTicketAdminRecord?> UpdateSupportTicketAsync(Guid id, UpdateSupportTicketRequest request, CancellationToken token = default);

    // ── Messaging ─────────────────────────────────────────────────────────────

    Task<IReadOnlyList<OrgMessageRecord>> GetOrgInboxAsync(Guid orgId, CancellationToken token = default);
    Task<IReadOnlyList<OrgMessageRecord>> GetOrgSentAsync(Guid orgId, CancellationToken token = default);
    Task<OrgMessageRecord?> GetOrgMessageAsync(Guid orgId, Guid messageId, CancellationToken token = default);
    Task<OrgMessageRecord?> SendOrgMessageAsync(Guid orgId, SendOrgMessageRequest request, CancellationToken token = default);

    // ── Calendar ──────────────────────────────────────────────────────────────

    Task<IReadOnlyList<OrgCalendarEventTypeRecord>> GetCalendarEventTypesAsync(Guid orgId, CancellationToken token = default);

    // ── Sitewide settings (SuperAdmin) ───────────────────────────────────────

    /// <summary>Every sitewide setting, including ones never yet given a value.</summary>
    Task<List<SiteSettingRecord>> GetSiteSettingsAsync(CancellationToken token = default);

    /// <summary>Sets one sitewide setting. An empty value clears it.</summary>
    Task<SiteSettingRecord?> SetSiteSettingAsync(
        string key, SetSiteSettingRequest request, CancellationToken token = default);

    /// <summary>The caller's public-facing alias for a case, plus what the public sees today.</summary>
    Task<CaseDisplayAliasRecord?> GetCaseDisplayAliasAsync(Guid caseId, CancellationToken token = default);

    /// <summary>Sets the caller's public-facing alias. Empty clears it. Primary client only.</summary>
    Task<CaseDisplayAliasRecord?> SetCaseDisplayAliasAsync(
        Guid caseId, SetCaseDisplayAliasRequest request, CancellationToken token = default);

    /// <summary>Edits a related person. Sends the whole person — a null photo id clears it.</summary>
    Task<CaseRelatedPersonRecord?> UpdateRelatedPersonAsync(
        Guid caseId, Guid personId, UpdateRelatedPersonRequest request, CancellationToken token = default);

    /// <summary>Attaches a file to an occurrence entry using case-scoped storage.</summary>
    Task<OccurrenceFileItem?> AttachOccurrenceFileAsync(Guid caseId, Guid entryId, Stream content, string fileName, string contentType, CancellationToken token = default);

    /// <summary>Removes a file attachment from an occurrence and deletes the stored file.</summary>
    Task<bool> DetachOccurrenceFileAsync(Guid caseId, Guid entryId, Guid fileId, CancellationToken token = default);

    /// <summary>Returns all case messages visible to the client (marks org messages read).</summary>
    Task<IReadOnlyList<CaseMessageRecord>> GetMyCaseMessagesAsync(Guid caseId, CancellationToken token = default);

    /// <summary>Posts a message from the client to the org on this case.</summary>
    Task<CaseMessageRecord?> PostMyCaseMessageAsync(Guid caseId, string body, CancellationToken token = default);

    /// <summary>Client cancels a scheduled investigation (422 if outside cancellation window).</summary>
    Task<bool> CancelMyInvestigationAsync(Guid caseId, Guid investigationId, CancellationToken token = default);

    // ── Experience Taxonomy ───────────────────────────────────────────────────

    /// <summary>Returns all approved, active categories with their types (public — no auth).</summary>
    Task<IReadOnlyList<ExperienceCategoryWithTypesResponse>> GetExperienceTaxonomyAsync(CancellationToken token = default);

    /// <summary>SuperAdmin: all categories including pending/inactive.</summary>
    Task<IReadOnlyList<ExperienceCategoryRecord>> GetAllExperienceCategoriesAsync(CancellationToken token = default);

    /// <summary>SuperAdmin: all types for a category including pending/inactive.</summary>
    Task<IReadOnlyList<ExperienceTypeRecord>> GetAllExperienceTypesAsync(Guid categoryId, CancellationToken token = default);

    Task<ExperienceCategoryRecord?> CreateExperienceCategoryAsync(UpsertExperienceCategoryRequest request, CancellationToken token = default);
    Task<ExperienceCategoryRecord?> UpdateExperienceCategoryAsync(Guid id, UpsertExperienceCategoryRequest request, CancellationToken token = default);
    Task<bool> DeleteExperienceCategoryAsync(Guid id, CancellationToken token = default);
    Task<ExperienceCategoryRecord?> ApproveExperienceCategoryAsync(Guid id, CancellationToken token = default);

    Task<ExperienceTypeRecord?> CreateExperienceTypeAsync(Guid categoryId, UpsertExperienceTypeRequest request, CancellationToken token = default);
    Task<ExperienceTypeRecord?> UpdateExperienceTypeAsync(Guid categoryId, Guid id, UpsertExperienceTypeRequest request, CancellationToken token = default);
    Task<bool> DeleteExperienceTypeAsync(Guid categoryId, Guid id, CancellationToken token = default);
    Task<ExperienceTypeRecord?> ApproveExperienceTypeAsync(Guid categoryId, Guid id, CancellationToken token = default);

    /// <summary>
    /// Rejects a group-added type: deletes it and strips it from every entry tagged with it.
    /// The entries themselves are untouched — only the tagging goes.
    /// </summary>
    Task<RejectExperienceTypeResponse?> RejectExperienceTypeAsync(Guid categoryId, Guid id, CancellationToken token = default);

    /// <summary>
    /// Adds a type a group needs to an existing category. Live immediately and flagged for app
    /// administrators to review. Returns the existing type when the name is already taken.
    /// </summary>
    Task<ExperienceTypeRecord?> AddOrgExperienceTypeAsync(Guid orgId, AddOrgExperienceTypeRequest request, CancellationToken token = default);

    // ── Votes ──────────────────────────────────────────────────────

    /// <summary>Returns the aggregated vote summary including the current user's vote (if any).</summary>
    Task<UploadFileVoteSummary?> GetVoteSummaryAsync(Guid fileId, CancellationToken token = default);

    /// <summary>
    /// Creates or updates the current user's vote (upsert).
    /// Pass score 1 for upvote, -1 for downvote.
    /// </summary>
    Task<UploadFileVoteRecord?> UpsertMyVoteAsync(Guid fileId, int score, CancellationToken token = default);

    /// <summary>Removes the current user's vote. No-op if the user has not voted.</summary>
    Task<bool> RemoveMyVoteAsync(Guid fileId, CancellationToken token = default);
}
