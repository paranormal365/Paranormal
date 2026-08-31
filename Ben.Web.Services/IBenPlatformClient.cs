using Ben.Web.Services.WebApi;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Data.Common.Enums;

namespace Ben.Web.Services;

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
    Task<LoadResult<MyMessageRecord>> GetMyMessagesAsync(bool unreadOnly = false, CancellationToken token = default);

    /// <summary>Marks one of the current user's messages read. <paramref name="id"/> is the record's Id.</summary>
    Task<bool> MarkMyMessageReadAsync(Guid id, CancellationToken token = default);

    /// <summary>Marks every unread message of the current user's read. Returns how many changed.</summary>
    Task<int> MarkAllMyMessagesReadAsync(CancellationToken token = default);

    /// <summary>Pending file-permission requests awaiting the current user, with names resolved.</summary>
    Task<LoadResult<PendingPermissionRequestRecord>> GetPendingPermissionRequestsForMeAsync(CancellationToken token = default);

    // ── Rate limits (item 199) ────────────────────────────────────────────────
    /// <summary>What each rate limit has refused, worst first (SuperAdmin).</summary>
    Task<LoadResult<RateLimitRefusalRecord>> GetRateLimitRefusalsAsync(CancellationToken token = default);

    /// <summary>How this machine is configured to send mail. No secrets.</summary>
    Task<MailSettingsRecord?> GetMailSettingsAsync(CancellationToken token = default);

    /// <summary>Sends one real test message and reports what the server said.</summary>
    Task<MailTestResultRecord?> SendTestEmailAsync(string to, CancellationToken token = default);

    /// <summary>Re-arms the one-time notice for a limit, so the next burst sends a fresh message.</summary>
    Task<bool> ReArmRateLimitNoticeAsync(string policyName, CancellationToken token = default);

    // ── Sidecar telemetry ─────────────────────────────────────────────────────
    /// <summary>Recorded sidecar install/pair events, newest first (SuperAdmin).</summary>
    Task<LoadResult<SidecarInstallLogRecord>> GetSidecarTelemetryAsync(int take = 200, CancellationToken token = default);

    /// <summary>Distinct-install counts and a per-version breakdown (SuperAdmin).</summary>
    Task<SidecarTelemetrySummaryRecord?> GetSidecarTelemetrySummaryAsync(CancellationToken token = default);

    // ── Audit Log ─────────────────────────────────────────────────────────────

    Task<AuditLogPagedResponse?> GetAuditLogsAsync(int page = 1, int pageSize = 50, string? entityType = null, int? action = null, Guid? userId = null, DateTime? dateFrom = null, DateTime? dateTo = null, CancellationToken token = default);
    Task<LoadResult<string>> GetAuditLogEntityTypesAsync(CancellationToken token = default);
    Task<bool> SendAuditLogMessageAsync(SendAuditLogMessageRequest request, CancellationToken token = default);

    // ── Generic Lookup Types ──────────────────────────────────────────────────
    // Covers UserAddressType, UserEmailType, UserPhoneType, UserLinkType, UserNoteType,
    // UserMessageType, and the five Org equivalents — all share the same schema.

    /// <summary>Returns all rows for a lookup-type table at the given admin API route.</summary>
    Task<LoadResult<LookupTypeAdminRecord>> GetLookupTypesAsync(string route, CancellationToken token = default);

    /// <summary>Creates a new row in a lookup-type table.</summary>
    Task<LookupTypeAdminRecord?> CreateLookupTypeAsync(string route, LookupTypeUpsertRequest request, CancellationToken token = default);

    /// <summary>Updates an existing row in a lookup-type table.</summary>
    Task<LookupTypeAdminRecord?> UpdateLookupTypeAsync(string route, Guid id, LookupTypeUpsertRequest request, CancellationToken token = default);

    /// <summary>Deletes a row from a lookup-type table.</summary>
    Task<bool> DeleteLookupTypeAsync(string route, Guid id, CancellationToken token = default);

    // ── Support tickets ───────────────────────────────────────────────────────

    /// <summary>The site's published contact details, for the contact page. Anonymous.</summary>
    Task<SiteContactInfo?> GetSiteContactAsync(CancellationToken token = default);

    /// <summary>
    /// Which sections of the site are switched on. Anonymous, because the navigation and the
    /// route guards need the answer before anyone has signed in. Read through
    /// <c>SiteFeaturesProvider</c> rather than called directly — it caches.
    /// </summary>
    Task<SiteFeaturesInfo?> GetSiteFeaturesAsync(CancellationToken token = default);

    // ── Dashboard statistics ──────────────────────────────────────────────────

    /// <summary>Headline counts for the administrator's dashboard. SuperAdmin.</summary>
    Task<AdminStatsSummary?> GetAdminStatsSummaryAsync(CancellationToken token = default);

    /// <summary>The dashboard's charts over a window of days. SuperAdmin.</summary>
    Task<AdminStatsCharts?> GetAdminStatsChartsAsync(int days = 30, CancellationToken token = default);

    /// <summary>
    /// Who has been signing in over a window of days, and anything odd about how. SuperAdmin.
    /// </summary>
    /// <remarks>The one dashboard call that names accounts — see AdminStatsController.</remarks>
    Task<AdminSignInInsights?> GetAdminSignInInsightsAsync(int days = 30, CancellationToken token = default);

    /// <summary>
    /// Reports a published field session's media, hiding it until a moderator looks.
    /// </summary>
    /// <remarks>
    /// Requires an account on purpose: one flag hides, so an anonymous version would let anybody
    /// erase the archive's pictures. The page shows the control to everyone and asks a visitor to
    /// sign in, rather than hiding a refusal the server would give anyway.
    /// </remarks>
    Task<(bool Ok, string? Error)> FlagFieldSessionAsync(
        Guid fieldSessionId, string? reason, CancellationToken token = default);

    /// <summary>
    /// Absolute URL for one approved archive recording's bytes — the API origin, not the site's,
    /// which is the trap every raw /api href falls into on the split deployment.
    /// </summary>
    string GetArchiveMediaUrl(Guid fieldSessionId, Guid uploadFileId);

    /// <summary>
    /// What deleting a group would destroy. SuperAdmin, and changes nothing.
    /// </summary>
    Task<OrganizationPurgePreview?> GetOrganizationPurgePreviewAsync(
        Guid organizationId, CancellationToken token = default);

    /// <summary>
    /// Deletes a group and everything belonging to it. SuperAdmin, and irreversible.
    /// </summary>
    /// <param name="confirmName">The group's exact name, typed back by the administrator.</param>
    Task<(OrganizationPurgePreview? Removed, string? Error)> PurgeOrganizationAsync(
        Guid organizationId, string confirmName, CancellationToken token = default);

    /// <summary>
    /// What the place's archive says about one of your own field sessions.
    /// </summary>
    /// <remarks>Null when the session is not yours, has no place, or the place is not public.</remarks>
    Task<SessionInsightsRecord?> GetSessionInsightsAsync(
        Guid sessionId, CancellationToken token = default);

    /// <summary>One group's own numbers. Visible to that group's active members.</summary>
    Task<OrgStatsSummary?> GetOrgStatsAsync(Guid organizationId, CancellationToken token = default);

    // ── Group-ad review + public placements (item 166 W3) ────────────────────
    Task<WebApi.LoadResult<AdminOrganizationAdRecord>> GetAdminOrgAdsAsync(CancellationToken token = default);
    Task<(bool Ok, string? Error)> ApproveOrgAdAsync(Guid adId, CancellationToken token = default);
    Task<(bool Ok, string? Error)> RejectOrgAdAsync(Guid adId, string reason, CancellationToken token = default);
    Task<WebApi.LoadResult<PromotedGroupCard>> GetPromotedGroupsAnonymousAsync(
        int take = 3, CancellationToken token = default,
        double? lat = null, double? lon = null);

    /// <summary>Tour names the caller has dismissed (item 166) — nothing listed auto-launches.</summary>
    Task<WebApi.LoadResult<string>> GetMyDismissedToursAsync(CancellationToken token = default);
    /// <summary>Dismisses one tour; completed says seen-through vs skipped. Idempotent.</summary>
    Task DismissTourAsync(string tourName, bool completed, CancellationToken token = default);

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
    Task<LoadResult<SupportTicketReplyRecord>> GetSupportTicketRepliesAsync(Guid id, CancellationToken token = default);

    /// <summary>Replies to the sender, or leaves an internal note.</summary>
    Task<bool> AddSupportTicketReplyAsync(Guid id, AddSupportTicketReplyRequest request, CancellationToken token = default);

    /// <summary>Changes a ticket's status and/or assignment.</summary>
    Task<SupportTicketAdminRecord?> UpdateSupportTicketAsync(Guid id, UpdateSupportTicketRequest request, CancellationToken token = default);

    // ── Messaging ─────────────────────────────────────────────────────────────

    Task<LoadResult<OrgMessageRecord>> GetOrgInboxAsync(Guid orgId, CancellationToken token = default);
    Task<LoadResult<OrgMessageRecord>> GetOrgSentAsync(Guid orgId, CancellationToken token = default);
    Task<OrgMessageRecord?> GetOrgMessageAsync(Guid orgId, Guid messageId, CancellationToken token = default);
    Task<OrgMessageRecord?> SendOrgMessageAsync(Guid orgId, SendOrgMessageRequest request, CancellationToken token = default);

    // ── Calendar ──────────────────────────────────────────────────────────────

    Task<LoadResult<OrgCalendarEventTypeRecord>> GetCalendarEventTypesAsync(Guid orgId, CancellationToken token = default);

    // ── Sitewide settings (SuperAdmin) ───────────────────────────────────────

    /// <summary>Every sitewide setting, including ones never yet given a value.</summary>

    /// <summary>Site settings, distinguishing "could not load" from "there are none".</summary>
    Task<WebApi.LoadResult<SiteSettingRecord>> GetSiteSettingsAsync(CancellationToken token = default);

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
    Task<LoadResult<CaseMessageRecord>> GetMyCaseMessagesAsync(Guid caseId, CancellationToken token = default);

    /// <summary>Posts a message from the client to the org on this case.</summary>
    Task<CaseMessageRecord?> PostMyCaseMessageAsync(Guid caseId, string body, CancellationToken token = default);

    /// <summary>Client cancels a scheduled investigation (422 if outside cancellation window).</summary>
    Task<bool> CancelMyInvestigationAsync(Guid caseId, Guid investigationId, CancellationToken token = default);

    // ── Experience Taxonomy ───────────────────────────────────────────────────

    /// <summary>Returns all approved, active categories with their types (public — no auth).</summary>
    Task<LoadResult<ExperienceCategoryWithTypesResponse>> GetExperienceTaxonomyAsync(CancellationToken token = default);

    /// <summary>SuperAdmin: all categories including pending/inactive.</summary>
    Task<LoadResult<ExperienceCategoryRecord>> GetAllExperienceCategoriesAsync(CancellationToken token = default);

    /// <summary>SuperAdmin: all types for a category including pending/inactive.</summary>
    Task<LoadResult<ExperienceTypeRecord>> GetAllExperienceTypesAsync(Guid categoryId, CancellationToken token = default);

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
    Task<TaxonomyProposal<ExperienceTypeRecord>> AddOrgExperienceTypeAsync(
        Guid orgId, AddOrgExperienceTypeRequest request, CancellationToken token = default);

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

    // ── Local discovery (item #88) ────────────────────────────────────────────

    /// <summary>
    /// What is near a point: organizations that opted into search, and upcoming public events.
    /// </summary>
    /// <remarks>
    /// The two lists in the response are not redacted the same way — an organization is shown as
    /// precisely as it chose to be found, an event only approximately — and that distinction is the
    /// server's, not this client's, to make. See <c>SearchController.Nearby</c>.
    /// </remarks>
    /// <param name="query">Optional text filter, matched against organization and event names.</param>
    Task<NearbyResults?> GetNearbyAsync(
        double latitude, double longitude, double radiusMiles, string? query = null,
        CancellationToken token = default);

    // ── Merging groups (item 110) ─────────────────────────────────────────────

    /// <summary>What merging one group into another WOULD do — mutation-free. SuperAdmin.</summary>
    Task<(Ben.Service.Models.Admin.MergePreview? Result, string? Error)> PreviewOrgMergeAsync(
        Guid baseId, Guid mergedId, CancellationToken token = default);

    /// <summary>Performs the merge. Null on success, otherwise the refusal sentence.</summary>
    Task<string?> MergeOrganizationsAsync(
        Ben.Service.Models.Admin.OrganizationMergeRequest request, CancellationToken token = default);
}
