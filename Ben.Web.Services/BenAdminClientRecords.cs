using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Data.Common.Enums;

namespace Ben.Web.Services;

// Data-transfer records for IBenAdminClient.
//
// These used to sit in the same file as the interface, where they were 995 of its 2,165 lines —
// nearly half the file was type declarations rather than the contract it is named for. Splitting
// them out changes nothing about the types themselves; it just means opening IBenAdminClient.cs
// shows the API surface.

/// <summary>
/// Combined result returned by <c>GET /api/admin/upload-file-types/with-extensions</c>.
/// </summary>
/// <param name="FileType">The file type record.</param>
/// <param name="Extensions">All extension patterns currently registered for the file type.</param>
public sealed record AdminFileTypeWithExtensionsResponse(
    UploadFileTypeRecord FileType,
    IReadOnlyList<UploadFileTypeExtensionRecord> Extensions);

/// <summary>Request body for creating a new <c>UploadFileType</c>.</summary>
public sealed record AdminCreateFileTypeRequest(
    string Name,
    string? Description,
    string? IconClass,
    string? ColorClass,
    bool IsActive,
    bool IsPublic,
    int SortOrder,
    bool AllowAllExtensions,
    Guid CreatedByAppUserId);

/// <summary>Request body for updating an existing <c>UploadFileType</c>.</summary>
public sealed record AdminUpdateFileTypeRequest(
    string Name,
    string? Description,
    string? IconClass,
    string? ColorClass,
    bool IsActive,
    bool IsPublic,
    int SortOrder,
    bool AllowAllExtensions,
    Guid? UpdatedByAppUserId);

/// <summary>Request body for adding an extension pattern to an existing <c>UploadFileType</c>.</summary>
public sealed record AdminCreateFileTypeExtensionRequest(
    Guid UploadFileTypeId,
    /// <summary>Pattern string, e.g. <c>.txt</c> (exact) or <c>.doc*</c> (wildcard suffix). See <c>FileExtensionPatternMatcher</c>.</summary>
    string Pattern,
    Guid CreatedByAppUserId);

/// <summary>Organization list row returned by <c>GET /api/organizations</c>.</summary>
public sealed record OrganizationListItemResponse(
    Guid Id,
    string Name,
    string UrlName,
    DateTime DateCreated,
    bool IsAcceptingApplications,
    bool CanEdit,
    bool CanDelete,
    // 0 unless the caller is SuperAdmin — see OrganizationController.GetAllWithPermissions.
    int MemberCount = 0,
    int CaseCount = 0,
    int InvestigationCount = 0);

/// <summary>Request body for creating a new organization (SuperAdmin only).</summary>
public sealed record AdminCreateOrganizationRequest(string Name, string UrlName,
    // Ghost walking tours (2026-08-24): what this group primarily is. It decides the
    // DEFAULTS the new group starts with — see OrgKindDefaults — and nothing else.
    Ben.Data.Common.Enums.OrganizationKind Kind = Ben.Data.Common.Enums.OrganizationKind.InvestigationGroup,
    string? PublicPhone = null, string? PublicEmail = null, string? PublicWebsite = null);

/// <summary>Request body for updating an organization's Name and UrlName.</summary>
public sealed record AdminUpdateOrganizationRequest(string Name, string UrlName,
    bool IsAcceptingApplications = false,
    // Null means "leave as-is" — an older caller that omits these must not silently
    // reclassify a group or switch its tours off.
    Ben.Data.Common.Enums.OrganizationKind? Kind = null,
    bool? RunsPublicTours = null,
    string? PublicPhone = null, string? PublicEmail = null, string? PublicWebsite = null,
    // Optional so an existing caller that omits it can't silently switch the policy off.
    // Null means "leave as-is"; see OrganizationController.Update.
    bool? AllowMemberPrivatePhotosToClients = null);

/// <summary>Role record paired with its current user count.</summary>
public sealed record AdminRoleWithCountResponse(AppRoleAdminRecord Role, int UserCount);

// ── Public org page response records ─────────────────────────────────────────

public sealed record OrgAddressUpsertRequest(
    Guid   OrganizationAddressTypeId,
    string StreetAddress1,
    string? StreetAddress2,
    string City,
    string State,
    string ZipCode,
    string Country,
    int    SortOrder,
    OrganizationAddressVisibility  Visibility          = OrganizationAddressVisibility.Private,
    OrganizationAddressDisplayMode PublicDisplayMode   = OrganizationAddressDisplayMode.Hidden,
    OrganizationAddressDisplayMode MemberDisplayMode   = OrganizationAddressDisplayMode.FullAddressAndMap,
    bool   IsSearchable     = false,
    OrganizationAddressVisibility  SearchVisibility    = OrganizationAddressVisibility.Public,
    double? SearchRadiusMiles = null,
    decimal? Latitude  = null,
    decimal? Longitude = null);

public sealed record GeocodingPreviewResponse(decimal? Latitude, decimal? Longitude, string? ResultType);

public sealed record ReverseGeocodingResponse(
    string? StreetAddress1,
    string? City,
    string? State,
    string? ZipCode,
    string? Country);

public sealed record OrgPublicHomeResponse(
    Guid OrgId, string OrgName, string OrgUrlName,
    IReadOnlyList<OrgPublicLogoItem> Logos,
    OrgPublicPageItem? HomePage,
    IReadOnlyList<OrgPublicNavItem> NavPages,
    string? PublicPhone = null, string? PublicEmail = null, string? PublicWebsite = null,
    /// <summary>What this group is (2026-08-24) — shown as a badge on its public page.</summary>
    Ben.Data.Common.Enums.OrganizationKind Kind = Ben.Data.Common.Enums.OrganizationKind.InvestigationGroup,
    /// <summary>It runs public walking tours — worth saying even on an investigation group.</summary>
    bool RunsPublicTours = false);

public sealed record OrgPublicPageResponse(
    Guid OrgId, string OrgName, string OrgUrlName,
    IReadOnlyList<OrgPublicLogoItem> Logos,
    OrgPublicPageItem Page,
    IReadOnlyList<OrgPublicNavItem> NavPages);

public sealed record OrgPublicLogoItem(Guid LogoId, Guid UploadFileId, string? AltText, int SortOrder);
public sealed record OrgPublicPageItem(Guid Id, string PageTitle, string UrlName, bool IsHome, IReadOnlyList<OrgPublicSectionItem> Sections);
public sealed record OrgPublicSectionItem(Guid Id, CmsSectionType SectionType, string? Title, string ContentJson, int SortOrder);
public sealed record OrgPublicNavItem(Guid Id, string PageTitle, string UrlName, Guid? ParentPageId, int SortOrder);

// AuditLogRecord, AuditLogPagedResponse, SendAuditLogMessageRequest
// are defined in Ben.Service.Models.Admin (via project reference).

/// <summary>
/// Request body for creating a new application user.
/// Mirrors the <c>AdminCreateUserRequest</c> DTO in <c>Ben.Data.WebApi</c>.
/// </summary>
public sealed record AdminCreateUserRequest(
    string Email,
    string Password,
    string? DisplayName,
    string? UserName,
    bool IsEmailConfirmed,
    bool IsSuperAdmin);

/// <summary>
/// Request body for updating a user's editable profile fields.
/// Mirrors the <c>AdminUpdateUserProfileRequest</c> DTO in <c>Ben.Data.WebApi</c>.
/// </summary>
public sealed record AdminUpdateUserProfileRequest(
    string? DisplayName,
    string? UserName,
    string? Email,
    string? PhoneNumber,
    bool IsEmailConfirmed,
    bool IsTwoFactorEnabled,
    bool IsLockoutEnabled,
    DateTimeOffset? LockoutEnd,
    DateTime DateCreated,
    DateTime? DateUpdated);

// ── CMS types ─────────────────────────────────────────────────────────────────

/// <summary>Page row returned by GET /api/organizations/{orgId}/pages.</summary>
public sealed record CmsPageListItem(
    Guid Id,
    Guid OrganizationId,
    Guid? ParentPageId,
    string PageTitle,
    string UrlName,
    bool IsHome,
    bool IsPublished,
    bool IsPublic,
    int SortOrder,
    int SectionCount,
    bool CanEdit,
    bool CanDelete,
    DateTime DateCreated,
    // True when the slug is one the site itself routes, so the page cannot be opened. Only possible
    // for pages saved before the reserved-word check existed — the symptom is invisible in this
    // list, which is why the server says so rather than leaving it to be discovered.
    bool IsUnreachable = false);

/// <summary>Full page with sections returned by GET /api/organizations/{orgId}/pages/{pageId}.</summary>
public sealed record CmsPageDetail(
    Guid Id,
    Guid OrganizationId,
    Guid? ParentPageId,
    string PageTitle,
    string UrlName,
    string PageHtml,
    bool IsHome,
    bool IsPublished,
    bool IsPublic,
    int SortOrder,
    DateTime DateCreated,
    DateTime? DateUpdated,
    IReadOnlyList<CmsSectionRecord> Sections);

/// <summary>Creates a page, optionally starting from one of the group's saved layouts.</summary>
/// <param name="FromTemplateId">
/// A page-scoped template to copy sections from. Copied, not referenced — editing the template
/// later leaves this page alone.
/// </param>
public sealed record CmsCreatePageRequest(string PageTitle, string UrlName, string? PageHtml, bool IsPublic, Guid? ParentPageId, int SortOrder, Guid? FromTemplateId = null);
public sealed record CmsUpdatePageRequest(string PageTitle, string UrlName, string? PageHtml, bool IsPublished, bool IsPublic, Guid? ParentPageId, int SortOrder);
public sealed record CmsCreateSectionRequest(CmsSectionType SectionType, string? Title, string ContentJson, int SortOrder, bool IsActive);
public sealed record CmsUpdateSectionRequest(string? Title, string ContentJson, bool IsActive);
public sealed record CmsCreateLogoRequest(Guid UploadFileId, string? AltText, bool IsActive, int SortOrder);
public sealed record CmsUpdateLogoRequest(string? AltText, bool IsActive, int SortOrder);

// ── User sub-entity request records ──────────────────────────────────────────

public sealed record UserAddressUpsertRequest(
    Guid UserAddressTypeId,
    string StreetAddress1,
    string? StreetAddress2,
    string City,
    string State,
    string ZipCode,
    string Country,
    bool IsPublic,
    int SortOrder = 0,
    decimal? Latitude  = null,
    decimal? Longitude = null);

public sealed record UserEmailUpsertRequest(
    Guid UserEmailTypeId,
    string EmailAddress,
    bool IsPrimary,
    bool IsPublic,
    int SortOrder = 0);

public sealed record UserPhoneUpsertRequest(
    Guid UserPhoneTypeId,
    string PhoneNumber,
    string? PhoneCountry,
    bool IsPrimary,
    bool IsCellular,
    bool IsPublic);

public sealed record UserLinkUpsertRequest(
    Guid UserLinkTypeId,
    string LinkUrl,
    string? DisplayText,
    bool IsPublic,
    bool IsActive);

public sealed record UserNoteUpsertRequest(
    Guid UserNoteTypeId,
    string NoteSubject,
    string NoteBody,
    bool IsPublic);

/// <summary>
/// One investigation the signed-in person attended. Mirror of the WebApi record in
/// MyInvestigationsController.cs.
/// </summary>
public sealed record AttendedInvestigationItem(
    Guid InvestigationId,
    string Title,
    DateTime ScheduledDateTime,
    Guid OrganizationId,
    string OrganizationName,
    Guid? CaseId,
    string? CaseReference,
    Guid? PlaceId,
    string? PlaceName,
    string? PlaceCity,
    string? PlaceState,
    decimal? Latitude,
    decimal? Longitude,
    string? GeocodeNote,
    bool WasLead);

// ── Place records (Area 9) ────────────────────────────────────────────────────
// Mirrors of the WebApi records in PlaceController.cs / Public/PublicPlaceController.cs.

public sealed record PlaceRecord(
    Guid Id,
    string? Name,
    string? StreetAddress1,
    string? City,
    string? State,
    string? ZipCode,
    string? Country,
    decimal? Latitude,
    decimal? Longitude,
    string? GeocodeNote,
    Ben.Data.Common.Enums.PlaceKind Kind);

/// <summary><c>IsMine</c> lets the page separate our own visits from what others have shared.</summary>
public sealed record PlaceInvestigationRow(
    Guid Id,
    string Title,
    DateTime ScheduledDateTime,
    Ben.Data.Common.Enums.InvestigationStatus Status,
    Ben.Data.Common.Enums.InvestigationVisibility Visibility,
    Guid OrganizationId,
    string OrganizationName,
    bool IsMine);

/// <summary><c>Since</c> is null when nothing is visible, so the phrase can be omitted entirely.</summary>
public sealed record PlaceSummary(int InvestigationCount, int OrganizationCount, int? Since);

/// <summary>
/// A place that might be the one somebody is about to create.
/// </summary>
/// <remarks>
/// <c>DistanceMiles</c> is null when either side has no coordinates — unknown, not zero, so a page
/// must not print it as "0 miles away". <c>InvestigationCount</c> is what tells an established
/// place from a stray row.
/// </remarks>
public sealed record PlaceCandidate(
    Guid Id,
    string? Name,
    string? StreetAddress1,
    string? City,
    string? State,
    Ben.Data.Common.Enums.PlaceKind Kind,
    double? DistanceMiles,
    int InvestigationCount);

public sealed record PublicPlaceInvestigationRow(
    Guid Id,
    // The readable address of this investigation's own page, or null when it predates slugs.
    string? UrlName,
    string Title,
    DateTime ScheduledDateTime,
    Ben.Data.Common.Enums.InvestigationStatus Status,
    string OrganizationName,
    string OrganizationUrlName);

public sealed record PublicPlaceResponse(
    PlaceRecord Place,
    IReadOnlyList<PublicPlaceInvestigationRow> Investigations,
    PlaceSummary Summary);

// ── Org-wide investigation records (Area 9) ───────────────────────────────────
// Mirrors of the WebApi records in OrgInvestigationsController.cs — this library cannot reference
// the WebApi project, so the shapes are restated here.

/// <summary>
/// One investigation for the organization's map-and-grid view. <c>CanEditRecord</c> and
/// <c>CanCompleteMyFindings</c> are the server's verdicts: render them, never re-derive them.
/// </summary>
public sealed record OrgInvestigationRow(
    Guid Id,
    string Title,
    DateTime ScheduledDateTime,
    DateTime? EndDateTime,
    Ben.Data.Common.Enums.InvestigationStatus Status,
    Ben.Data.Common.Enums.InvestigationVisibility Visibility,
    string? Location,
    Guid? CaseId,
    string? CaseReference,
    string? CaseTitle,
    Guid? PlaceId,
    string? PlaceName,
    string? PlaceCity,
    string? PlaceState,
    decimal? Latitude,
    decimal? Longitude,
    string? GeocodeNote,
    int AttendeeCount,
    bool CanEditRecord,
    bool CanCompleteMyFindings);

/// <summary>
/// A place being created inline with the investigation held there, so scheduling a visit to
/// somewhere new is one step rather than two.
/// </summary>
public sealed record NewPlaceRequest(
    string? Name,
    string? StreetAddress1,
    string? StreetAddress2,
    string? City,
    string? State,
    string? ZipCode,
    string? Country,
    decimal? Latitude = null,
    decimal? Longitude = null,
    Ben.Data.Common.Enums.PlaceKind? Kind = null);

/// <summary>
/// One person's account of a visit. Mirror of the WebApi record.
/// </summary>
/// <remarks>
/// <c>DateUpdated</c> is null until it is revised — the difference between what somebody said on
/// the night and what they say now.
/// </remarks>
public sealed record InvestigationFindingRecord(
    Guid Id,
    Guid AppUserId,
    string? DisplayName,
    string Narrative,
    DateTime DateCreated,
    DateTime? DateUpdated);

/// <summary>
/// One person on an investigation's team, and whether they turned up. Mirror of the WebApi record.
/// </summary>
/// <remarks>
/// <c>SelfReported</c> distinguishes "checked in on site" from "somebody recorded it for them".
/// Who did the recording is deliberately not carried — the roster is read by the whole team.
/// </remarks>
public sealed record InvestigationRosterEntry(
    Guid AttendeeId,
    Guid AppUserId,
    string? DisplayName,
    string? AssignedRole,
    bool IsLead,
    Ben.Data.Common.Enums.RsvpStatus Rsvp,
    bool? DidAttend,
    DateTime? DateArrived,
    bool SelfReported);

/// <summary>Schedules an investigation. With no <c>CaseId</c>, a place is required.</summary>
public sealed record CreateOrgInvestigationRequest(
    string Title,
    DateTime ScheduledDateTime,
    string? Description = null,
    string? Location = null,
    DateTime? EndDateTime = null,
    DateTime? EvidenceDueDate = null,
    Guid? CaseId = null,
    Guid? PlaceId = null,
    NewPlaceRequest? NewPlace = null,
    Ben.Data.Common.Enums.InvestigationVisibility? Visibility = null);

// ── My contact info request/response records ──────────────────────────────────
// Mirrors of the WebApi records in MyContactInfoController.cs / PublicEmailValidationController.cs
// — this library cannot reference the WebApi project, so the shapes are restated here.

public sealed record MyEmailRecord(
    Guid Id, Guid UserEmailTypeId, string EmailAddress, bool IsPrimary, bool IsPublic,
    bool IsValidated, DateTime? DateValidated, DateTime? DateValidationSent, int SortOrder);

public sealed record UpsertMyEmailRequest(
    Guid UserEmailTypeId, string? EmailAddress, bool IsPrimary, bool IsPublic, int SortOrder = 0);

public sealed record SendValidationResponse(string ValidationLink, bool EmailSent);

public sealed record MyPhoneRecord(
    Guid Id, Guid UserPhoneTypeId, string PhoneNumber, string? PhoneCountry,
    bool IsPrimary, bool IsCellular, bool IsPublic);

public sealed record UpsertMyPhoneRequest(
    Guid UserPhoneTypeId, string? PhoneNumber, string? PhoneCountry,
    bool IsPrimary, bool IsCellular, bool IsPublic);

public sealed record MyAddressRecord(
    Guid Id, Guid UserAddressTypeId, string StreetAddress1, string? StreetAddress2,
    string City, string State, string ZipCode, string Country, bool IsPublic, int SortOrder,
    decimal? Latitude, decimal? Longitude);

public sealed record UpsertMyAddressRequest(
    Guid UserAddressTypeId, string? StreetAddress1, string? StreetAddress2,
    string? City, string? State, string? ZipCode, string? Country, bool IsPublic, int SortOrder = 0,
    decimal? Latitude = null, decimal? Longitude = null);

public sealed record MyLinkRecord(
    Guid Id, Guid UserLinkTypeId, string LinkUrl, string? DisplayText, bool IsPublic, bool IsVerifiedApproved);

public sealed record UpsertMyLinkRequest(
    Guid UserLinkTypeId, string? LinkUrl, string? DisplayText, bool IsPublic);

public sealed record EmailValidationInfoRecord(string MaskedEmail, bool IsExpired);

/// <summary>
/// Request body for creating or fully replacing a lookup-type row
/// (UserAddressType, OrganizationEmailType, etc.).
/// The Id field must be set to the existing Id on update, or <see cref="Guid.Empty"/> on create.
/// </summary>
public sealed record LookupTypeUpsertRequest(
    Guid Id,
    string Name,
    string? Description,
    string? IconClass,
    string? ColorClass,
    bool IsActive,
    bool IsPublic,
    int SortOrder,
    DateTime DateCreated,
    DateTime? DateUpdated,
    Guid CreatedByAppUserId,
    Guid? UpdatedByAppUserId);

public sealed record OrgGroupUpsertRequest(string Name, string? Description, bool IsActive, int SortOrder);

public sealed record CreateOrgRoleRequest(string Name, string? Description, bool IsActive, int SortOrder);
public sealed record UpdateOrgRoleRequest(string Name, string? Description, bool IsActive, int SortOrder);
public sealed record SetRolePermissionRequest(OrganizationSecurityTable TableName, OrganizationSecurityAction Actions);

public sealed record PagePermissionCreateRequest(Guid? AppUserId, Guid? OrgMemberGroupId, CmsPageAction Actions);

/// <summary>Org membership row from GET /api/organizations/{orgId}/security/users.</summary>
public sealed record OrgMembershipItem(Guid MembershipId, Guid AppUserId, OrganizationMemberRole Role, bool IsActive, string? DisplayName = null,
    Guid? MemberLevelId = null, string? MemberLevelName = null);

/// <summary>The plan's included role areas + name, for the editor's graying (item 156 Phase E).</summary>
public sealed record OrgIncludedAreasItem(
    IReadOnlyList<Ben.Data.Common.Enums.OrganizationPermissionArea> Areas, string? TierName);

/// <summary>Per-area read verdicts for the caller in one group (item 156 Phase D).</summary>
/// <summary>
/// What the signed-in person may do in one group, per area.
/// </summary>
/// <remarks>
/// <para>Two read booleans until 2026-08-26, which was the whole of IH-03: a Case Manager holds
/// create, update and delete on Cases, none of which this could carry, so no button could depend
/// on the grant and an owner assigning roles saw nothing change.</para>
///
/// <para>Use <see cref="May"/> rather than reading the dictionary directly — it answers "no" for
/// an area the server did not mention, which is the safe direction and keeps a call site working
/// against an older server.</para>
/// </remarks>
public sealed record MyOrgPermissionsItem(
    bool CanReadCases,
    bool CanReadInvestigations,
    IReadOnlyDictionary<Ben.Data.Common.Enums.OrganizationPermissionArea, OrgAreaActions>? Areas = null,
    IReadOnlyDictionary<Ben.Data.Common.Enums.TierCapability, bool>? Capabilities = null)
{
    /// <summary>Whether the group's PLAN includes a capability — a different question from
    /// whether this person may act.</summary>
    /// <remarks>
    /// <para>Item 193: the private-engagement toggle rendered for every group, so a free-tier
    /// group could tick it and collect a 400 from the server. A control has to know what the plan
    /// allows BEFORE it is used.</para>
    ///
    /// <para>Fails OPEN, matching the server: capabilities are included unless a tier explicitly
    /// excludes one, so an older server that says nothing leaves controls working rather than
    /// silently disabling them. That is the opposite default from <see cref="May"/>, deliberately
    /// — an unknown PERMISSION should refuse, an unknown PLAN FEATURE should not punish.</para>
    /// </remarks>
    public bool PlanIncludes(Ben.Data.Common.Enums.TierCapability capability)
        => Capabilities is null || !Capabilities.TryGetValue(capability, out var included) || included;

    /// <summary>Whether this person may take one action in one area.</summary>
    /// <remarks>
    /// Absent means NO. An affordance that appeared because the server said nothing would lead
    /// somebody to a refusal, which is the failure this endpoint exists to prevent.
    /// </remarks>
    public bool May(Ben.Data.Common.Enums.OrganizationPermissionArea area,
                    Ben.Data.Common.Enums.OrganizationSecurityAction action)
    {
        if (Areas is null || !Areas.TryGetValue(area, out var actions)) return false;
        return action switch
        {
            Ben.Data.Common.Enums.OrganizationSecurityAction.Create => actions.Create,
            Ben.Data.Common.Enums.OrganizationSecurityAction.Read   => actions.Read,
            Ben.Data.Common.Enums.OrganizationSecurityAction.Update => actions.Update,
            Ben.Data.Common.Enums.OrganizationSecurityAction.Delete => actions.Delete,
            _ => false,
        };
    }
}

/// <summary>What one person may do in one area.</summary>
public sealed record OrgAreaActions(bool Create, bool Read, bool Update, bool Delete);

/// <summary>One of the caller's own groups, shaped for the sidebar (item 159).</summary>
public sealed record MyMembershipOrgItem(Guid OrganizationId, string Name);

/// <summary>One candidate for the share-from-user picker (item 175).</summary>
public sealed record ShareableUserFileItem(
    Guid Id, string FileName, string ContentType, long FileSize, string? Description,
    string? OwnerDisplayName, DateTime DateCreated, bool SharedWithOrganization);

/// <summary>One group's waiting work, for the action-needed banners (item 161).</summary>
public sealed record OrgActionNeededItem(
    Guid OrganizationId, string OrganizationName,
    int PendingClientRequests, int PendingMembershipRequests);

/// <summary>One rung of a group's member-title ladder (item 157). Seniority, never permission.</summary>
public sealed record OrgMemberLevelItem(Guid Id, string Name, int SortOrder, bool IsActive);

/// <summary>Minimal member-directory entry — see <c>IBenAdminClient.GetOrgUserDirectoryAsync</c>.</summary>
public sealed record OrgUserDirectoryItem(Guid Id, string DisplayName);

/// <summary>Computed display label: DisplayName → email → id.</summary>
public static class OrgMembershipItemExtensions
{
    public static string Label(this OrgMembershipItem m) =>
        string.IsNullOrWhiteSpace(m.DisplayName)
            ? $"{m.Role} ({m.MembershipId.ToString()[..8]}…)"
            : $"{m.DisplayName} ({m.Role})";
}

public sealed record DirectionsResult(
    string RouteGeoJson,
    IReadOnlyList<RoutePoint> RoutePoints,
    double TotalDistanceMiles,
    double TotalDurationMinutes,
    IReadOnlyList<RouteStep> Steps);

public sealed record RoutePoint(double Lat, double Lon);

public sealed record RouteStep(string Instruction, double DistanceMiles, double DurationSeconds);

public sealed record OrgSettingsResponse(
    bool ShowAddressMap, bool ShowAddressDirections,
    // Item 181: the group's preference, plus whether it is actually in effect and why not.
    bool StripMediaMetadata = true, bool StripMediaMetadataInEffect = false,
    string? StripMediaMetadataReason = null, bool StripMediaMetadataNeedsUpgrade = false,
    bool StripMediaMetadataCanChoose = false);

public sealed record OrgSettingsRequest(
    bool ShowAddressMap, bool ShowAddressDirections, bool StripMediaMetadata = true);
public sealed record AddAddressMemberAccessRequest(Guid OrganizationUserMembershipId);

// ── Experience Taxonomy request records ──────────────────────────────────────
public sealed record UpsertExperienceCategoryRequest(
    string Name,
    string? Description,
    string? IconClass,
    string? ColorClass,
    int SortOrder,
    bool IsActive);

public sealed record UpsertExperienceTypeRequest(
    string Name,
    string? Description,
    string? IconClass,
    int SortOrder,
    bool IsActive);

/// <summary>A group adding a missing type to an existing category.</summary>
/// <param name="ConfirmDistinct">
/// Set once the person has been shown the close matches and said theirs is genuinely different.
/// Without it a probable typo is refused with the suggestions rather than silently created.
/// </param>
public sealed record AddOrgExperienceTypeRequest(
    Guid ExperienceCategoryId,
    string? Name,
    string? Description,
    bool ConfirmDistinct = false);

/// <summary>What a rejection removed — the type, and how many taggings went with it.</summary>
public sealed record RejectExperienceTypeResponse(Guid ExperienceTypeId, int UsagesRemoved);

public sealed record ExperienceCategoryWithTypesResponse(
    ExperienceCategoryRecord Category,
    IReadOnlyList<ExperienceTypeRecord> Types);

// ── Area of operation / org discovery records ─────────────────────────────────
public sealed record UpsertAreaOfOperationRequest(
    decimal RadiusMiles,
    decimal CenterLatitude,
    decimal CenterLongitude,
    string? DisplayLabel,
    bool IsAcceptingClients,
    bool AcceptsClientsOutsideRange);

/// <summary>
/// Public search result — center coordinates intentionally omitted for privacy.
/// </summary>
public sealed record OrgSearchResult(
    Guid OrganizationId,
    string Name,
    string UrlName,
    string? DisplayLabel,
    double RadiusMiles,
    double DistanceFromSearchMiles,
    bool IsWithinRange,
    bool AcceptsClientsOutsideRange,
    Guid? ActiveLogoFileId);

/// <summary>
/// One organization in the location-free browse listing. Mirrors the WebApi record of the same
/// name — the library cannot reference the WebApi project, so the shape is restated here.
/// </summary>
public sealed record OrgBrowseResult(
    Guid OrganizationId,
    string Name,
    string UrlName,
    string? AreaLabel,
    double? RadiusMiles,
    bool IsAcceptingClients,
    Guid? ActiveLogoFileId,
    Ben.Data.Common.Enums.OrganizationKind Kind = Ben.Data.Common.Enums.OrganizationKind.InvestigationGroup,
    bool RunsPublicTours = false);

/// <summary>One page of the browse listing.</summary>
public sealed record OrgBrowsePage(
    IReadOnlyList<OrgBrowseResult> Items,
    int TotalCount,
    int Page,
    int PageSize);

// ── Phase 6: Case Transfer + Public Discovery records ─────────────────────────
public sealed record PublicCaseListItem(
    string CaseReference,
    // The readable address to link to. Falls back to the reference for a case published before
    // slugs existed, so a card always has somewhere to point.
    string UrlName,
    string Title,
    string City,
    string State,
    Ben.Data.Common.Enums.CaseStatus Status,
    DateTime DateCaseOpened,
    DateTime? DateCaseClosed,
    bool IsHaunted);

/// <summary>
/// Full public case detail returned by <c>GET api/public/organizations/{orgUrlName}/cases/{caseRef}</c>.
/// Consumed by <c>OrgPublicCaseDetail.razor</c>.
/// </summary>
/// <param name="CaseId">DB primary key — used by <c>CaseVoteWidget.razor</c> to fetch/cast votes.</param>
/// <param name="ClientName">
/// The client's <c>PublicPseudonym</c> when set, otherwise <c>null</c>.
/// Real names are never exposed on public endpoints.
/// </param>
/// <param name="Timeline">
/// Public timeline entries ordered by <c>EventDateTime</c>.
/// Evidence entries include <see cref="PublicTimelineEntry.EvidenceFileIds"/> for <c>EvidenceVoteWidget</c>.
/// </param>
public sealed record PublicCaseDetail(
    Guid CaseId,
    string CaseReference,
    string Title,
    string City,
    string State,
    string Country,
    Ben.Data.Common.Enums.CaseStatus Status,
    bool IsHaunted,
    string? ClientName,
    string? Description,
    DateTime DateCaseOpened,
    DateTime? DateCaseClosed,
    IReadOnlyList<PublicTimelineEntry> Timeline,
    string OrgName,
    string OrgUrlName);

/// <summary>
/// A single public timeline entry within a <see cref="PublicCaseDetail"/>.
/// </summary>
/// <param name="EvidenceFileIds">
/// Non-empty only for <c>CaseTimelineEntryType.Evidence</c> entries.
/// Each ID is passed to <c>EvidenceVoteWidget.razor</c> so visitors can vote on individual pieces of evidence.
/// </param>
public sealed record PublicTimelineEntry(
    Ben.Data.Common.Enums.CaseTimelineEntryType EntryType,
    DateTime? EventDateTime,
    string? Title,
    string? Body,
    IReadOnlyList<Guid> EvidenceFileIds);

// ── Global public case discovery ──────────────────────────────────────────────

/// <summary>Paged wrapper returned by <c>GET api/public/cases</c>. Drives the home-page map and ranked list.</summary>
public sealed record PublicCaseDiscoveryPagedResponse(
    IReadOnlyList<PublicCaseDiscoveryItem> Items,
    int TotalCount,
    int Page,
    int PageSize);

/// <summary>
/// A single row in the global case discovery feed. Used by both the map markers
/// and the ranked card list in <c>PublicCaseDiscovery.razor</c>.
/// </summary>
/// <param name="CaseId">
/// DB primary key — stored on each <c>CaseMapMarker</c> so that the map popup
/// can pass it to <c>CaseVoteWidget.razor</c>.
/// </param>
/// <param name="ApproxLatitude">
/// City-level coordinates geocoded from the case's city/state/country and cached
/// 24 h in <c>IMemoryCache</c>. Null when geocoding fails.
/// Exact street addresses are never included.
/// </param>
/// <param name="ConfirmsCount">Aggregate count of <c>EvidenceVote</c> rows whose
/// type is <c>Confirms</c> across all timeline-entry files of this case.</param>
public sealed record PublicCaseDiscoveryItem(
    Guid     CaseId,
    string   CaseReference,
    string   Title,
    string   City,
    string   State,
    string   Country,
    Ben.Data.Common.Enums.CaseStatus Status,
    bool     IsHaunted,
    DateTime DateCaseOpened,
    DateTime? DateCaseClosed,
    string   OrgName,
    string   OrgUrlName,
    int      ConfirmsCount,
    int      DisputesCount,
    int      InconclusiveCount,
    int      TotalVotes,
    // Signed total: +1 confirms, 0 inconclusive, -1 disputes, computed server-side by
    // EvidenceVoteScore. Rendered as given — a client recomputing it from the three counts is
    // exactly how four surfaces end up disagreeing.
    int      Score,
    decimal? ApproxLatitude,
    decimal? ApproxLongitude,
    string?  ClientName);

// ── Phase 5: Investigation + Evidence Voting request records ──────────────────
public sealed record UpsertInvestigationRequest(
    string Title,
    string? Description,
    string? Location,
    DateTime ScheduledDateTime,
    DateTime? EndDateTime,
    Ben.Data.Common.Enums.InvestigationStatus Status,
    string? Notes,
    Guid? OrgCalendarEventId,
    DateTime? EvidenceDueDate = null,
    Guid? PlaceId = null,
    NewPlaceRequest? NewPlace = null,
    // Null means "leave the sharing scope alone" — defaulted from the place on create, untouched
    // on an edit that says nothing about it.
    Ben.Data.Common.Enums.InvestigationVisibility? Visibility = null);

public sealed record AddInvestigationAttendeeRequest(Guid AppUserId, string? AssignedRole);

// ── Phase 4: Messaging + Calendar request records ─────────────────────────────
public sealed record SendOrgMessageRequest(
    Ben.Data.Common.Enums.OrgMessageChannel ChannelType,
    string? Subject,
    string Body,
    bool IsEncrypted,
    Guid? ParentMessageId,
    Guid? CaseId,
    IList<Guid> RecipientUserIds);

public sealed record UpsertCalendarEventTypeRequest(
    string Name,
    string? ColorClass,
    string? IconClass,
    int SortOrder,
    bool IsActive);

public sealed record UpsertCalendarEventRequest(
    string Title,
    string? Description,
    string? Location,
    DateTime StartDateTime,
    DateTime EndDateTime,
    bool IsAllDay,
    bool IsPublic,
    Guid? EventTypeId,
    Guid? CaseId,
    string? RecurrenceRule,
    Guid? OrganizationAddressId = null,
    string? MeetingUrl = null,
    // Public-event settings (item #87), defaulted so existing callers are unaffected.
    Guid? PlaceId = null,
    bool HideExactLocation = false,
    int? AttendeeCapacity = null,
    DateTime? RsvpClosesAt = null);

public sealed record AddAttendeeRequest(Guid AppUserId, string? AssignedTask);

public sealed record AddAttendeeByEmailRequest(string? Email);

// ── Membership Phase 3 request records ────────────────────────────────────────
public sealed record UpsertMembershipQuestionRequest(
    string QuestionText,
    bool IsRequired,
    int SortOrder,
    bool IsActive);

// ── Case request records ──────────────────────────────────────────────────────
public sealed record CreateCaseRequest(
    string Title,
    string? Description,
    string StreetAddress1,
    string? StreetAddress2,
    string City,
    string State,
    string ZipCode,
    string? Country,
    decimal? Latitude,
    decimal? Longitude);

public sealed record AcceptClientRequestAsCaseRequest(
    string? Title,
    Guid? CaseManagerAppUserId);

public sealed record UpdateCaseRequest(
    string? Title,
    string? Description,
    Ben.Data.Common.Enums.CaseStatus Status,
    string? PublicPseudonym,
    bool IsPublic,
    Guid? CaseManagerAppUserId,
    // Item 184: null = leave the private-engagement designation unchanged.
    bool? IsPrivateEngagement = null);

public sealed record UpsertTimelineEntryRequest(
    Ben.Data.Common.Enums.CaseTimelineEntryType EntryType,
    DateTime? EventDateTime,
    string? Title,
    string? Body,
    Ben.Data.Common.Enums.CaseTimelineVisibility Visibility,
    IList<Guid> ExperienceTypeIds,
    Guid? InvestigationId = null);

// ── Client Request request records ────────────────────────────────────────────
public sealed record UpsertClientRequestRequest(
    string StreetAddress1,
    string? StreetAddress2,
    string City,
    string State,
    string ZipCode,
    string? Country,
    decimal? Latitude,
    decimal? Longitude,
    Ben.Data.Common.Enums.ClientGender Gender,
    int? BirthYear,
    string? Description);
// ── My Cases (client dashboard) response records ─────────────────────────────
public sealed record ClientCaseListItem(
    Guid      CaseId,
    string    CaseReference,
    string    Title,
    string    City,
    string    State,
    Ben.Data.Common.Enums.CaseStatus Status,
    string?   CaseManagerDisplayName,
    DateTime  DateCaseOpened,
    DateTime? NextInvestigationDate = null);

public sealed record ClientCaseDetail(
    Guid      CaseId,
    string    CaseReference,
    string    Title,
    string    City,
    string    State,
    Ben.Data.Common.Enums.CaseStatus Status,
    string?   Description,
    string?   CaseManagerDisplayName,
    DateTime  DateCaseOpened,
    DateTime? DateCaseClosed,
    IReadOnlyList<ClientCaseOccurrence>    Occurrences,
    IReadOnlyList<ClientCaseInvestigation> Investigations,
    int       UnreadMessageCount = 0,
    bool      IsPrimaryClient = false,
    IReadOnlyList<CaseContactItem>? Contacts = null);

/// <summary>Someone the client can talk to about their case. <c>IsFallback</c> marks the case
/// manager standing in because the group set no explicit contact.</summary>
public sealed record CaseContactItem(Guid AppUserId, string DisplayName, bool IsFallback);

/// <summary>The duty board for one visit (item 158): the group's duties and who holds each.</summary>
public sealed record InvestigationDutyBoard(
    IReadOnlyList<InvestigationDutyInfo> Duties,
    IReadOnlyList<InvestigationDutyAssignmentInfo> Assignments);

public sealed record InvestigationDutyInfo(
    Guid Id, string Name, bool IsSingleHolder, string? MinimumLevelName, int? MinimumLevelSortOrder);

public sealed record InvestigationDutyAssignmentInfo(
    Guid AttendeeId, Guid DutyId, bool EligibilityOverridden);

/// <summary>One of a group's investigation duties, as managed in Settings.</summary>
public sealed record OrgInvestigationDutyItem(
    Guid Id, string Name, int SortOrder, bool IsActive, bool IsSingleHolder,
    Guid? MinimumMemberLevelId, string? MinimumMemberLevelName);

public sealed record ClientCaseOccurrence(
    Guid      Id,
    Ben.Data.Common.Enums.CaseTimelineEntryType EntryType,
    DateTime? EventDateTime,
    string?   Title,
    string?   Body,
    bool      FromInvestigators,   // true when the org wrote this and shared it
    DateTime  DateCreated,
    IReadOnlyList<OccurrenceFileItem> Files,
    // Returned as well as accepted: a tag the client sets but can never see back would be a
    // write-only control, and they'd have no way to tell whether it took.
    IReadOnlyList<Guid> ExperienceTypeIds);

public sealed record OccurrenceFileItem(
    Guid   FileId,
    string FileName,
    string ContentType,
    long   FileSize);

// ── Case Report Builder records ───────────────────────────────────────────────
public sealed record UpsertCaseReportRequest(
    string    Title,
    string?   Summary,
    string?   Conclusion,
    DateTime? ExpectedDeliveryDate);

public sealed record UpsertSectionRequest(
    string                                           Title,
    string?                                          Body,
    Ben.Data.Common.Enums.CaseReportSectionType      SectionType);

public sealed record CaseReportSummary(
    Guid                                             Id,
    Guid                                             CaseId,
    string                                           Title,
    Ben.Data.Common.Enums.CaseReportStatus           Status,
    DateTime?                                        ExpectedDeliveryDate,
    DateTime?                                        PublishedAt,
    DateTime                                         DateCreated);

public sealed record CaseReportDetail(
    Guid                                             Id,
    Guid                                             CaseId,
    string                                           Title,
    string?                                          Summary,
    string?                                          Conclusion,
    Ben.Data.Common.Enums.CaseReportStatus           Status,
    DateTime?                                        ExpectedDeliveryDate,
    DateTime?                                        PublishedAt,
    DateTime                                         DateCreated,
    IReadOnlyList<CaseReportSectionDto>              Sections);

public sealed record CaseReportSectionDto(
    Guid                                             Id,
    Guid                                             CaseReportId,
    int                                              SortOrder,
    string                                           Title,
    string?                                          Body,
    Ben.Data.Common.Enums.CaseReportSectionType      SectionType,
    IReadOnlyList<CaseReportSectionFileDto>          Files,
    IReadOnlyList<CaseReportSectionFieldSessionDto>  FieldSessions);

/// <summary>A field session cited by a report section.</summary>
/// <remarks>
/// A reference, never a copy: the readings, recordings and digests stay with the upload, and the
/// report points at them. See <c>CaseReportSectionFieldSession</c>.
/// </remarks>
public sealed record CaseReportSectionFieldSessionDto(
    Guid      Id,
    Guid      FieldSessionUploadId,
    string?   LocationLabel,
    string?   RecordedByName,
    DateTime  StartedAt,
    DateTime? EndedAt,
    int       ReadingCount,
    int       MarkerCount,
    int       FileCount,
    string?   Caption,
    int       SortOrder);

/// <summary>A field session a report section could cite, as the picker lists it.</summary>
public sealed record AvailableFieldSessionDto(
    Guid      Id,
    Guid?     InvestigationId,
    string?   InvestigationTitle,
    string?   LocationLabel,
    string?   RecordedByName,
    string    DeviceModel,
    DateTime  StartedAt,
    DateTime? EndedAt,
    int       ReadingCount,
    int       MarkerCount,
    int       FileCount);

public sealed record CaseReportSectionFileDto(
    Guid    Id,
    Guid    UploadFileId,
    string  FileName,
    string  ContentType,
    long    FileSize,
    string? Caption,
    int     SortOrder);

public sealed record ClientCaseInvestigation(
    Guid       Id,
    string     Title,
    DateTime   ScheduledDateTime,
    DateTime?  EndDateTime,
    string?    Location,
    Ben.Data.Common.Enums.InvestigationStatus Status,
    DateTime?  EvidenceDueDate = null,
    DateTime?  CancellationDeadlineUtc = null);

/// <param name="ExperienceTypeIds">
/// Optional tags from the shared experience taxonomy. Investigators already filter and read these
/// on the org timeline; letting the client set them means the person who was actually there gets
/// to say what kind of thing it was.
/// </param>
public sealed record LogOccurrenceRequest(
    DateTime? EventDateTime,
    string?   Title,
    string?   Body,
    IReadOnlyList<Guid>? ExperienceTypeIds = null);

// ── Case message board response records ──────────────────────────────────────
public sealed record CaseMessageRecord(
    Guid                             Id,
    Guid                             CaseId,
    Guid                             AuthorAppUserId,
    string                           AuthorDisplayName,
    string                           Body,
    Ben.Data.Common.Enums.CaseMessageSide SenderSide,
    bool                             IsReadByClient,
    bool                             IsReadByOrg,
    DateTime                         DateCreated);

// ── My Investigations response records ───────────────────────────────────────
public sealed record MyInvestigationItem(
    Guid                               AttendeeId,
    Guid                               InvestigationId,
    // Null together for a visit with no client case. OrgId is never null — it comes off the
    // investigation itself, not through the case.
    Guid?                              CaseId,
    string?                            CaseReference,
    string?                            CaseTitle,
    Guid                               OrgId,
    string                             OrgName,
    string                             OrgUrlName,
    string                             Title,
    DateTime                           ScheduledDateTime,
    DateTime?                          EndDateTime,
    string?                            Location,
    Ben.Data.Common.Enums.InvestigationStatus Status,
    string?                            AssignedRole,
    Ben.Data.Common.Enums.RsvpStatus   Rsvp,
    bool?                              DidAttend,
    DateTime?                          EvidenceDueDate);

// ── Case Research records ─────────────────────────────────────────────────────
public sealed record UpsertResearchRequest(
    Ben.Data.Common.Enums.CaseResearchType ResearchType,
    string  Title,
    string? Body,
    string? Url);

public sealed record CaseResearchEntryDto(
    Guid                                   Id,
    Guid                                   CaseId,
    Ben.Data.Common.Enums.CaseResearchType ResearchType,
    string                                 Title,
    string?                                Body,
    string?                                Url,
    ResearchFileInfo?                      File,
    int                                    SortOrder,
    DateTime                               DateCreated);

public sealed record ResearchFileInfo(Guid FileId, string FileName, string ContentType, long FileSize);

// ── Investigation Scheduling records ─────────────────────────────────────────
public sealed record CreateProposalRequest(string? Notes, IReadOnlyList<SlotInput> Slots);
public sealed record SlotInput(DateTime StartDateTime, DateTime? EndDateTime);
public sealed record ConvertProposalRequest(Guid? SlotId, string? Title);

public sealed record ScheduleProposalDto(
    Guid                                              Id,
    Guid                                              CaseId,
    Ben.Data.Common.Enums.ScheduleProposalStatus      Status,
    string?                                           Notes,
    Guid?                                             AcceptedSlotId,
    DateTime?                                         ClientCounterDateTime,
    string?                                           ClientResponseNotes,
    DateTime?                                         ClientRespondedAt,
    Guid?                                             InvestigationId,
    DateTime                                          DateCreated,
    IReadOnlyList<SlotDto>                            Slots);

public sealed record SlotDto(Guid Id, DateTime StartDateTime, DateTime? EndDateTime, int SortOrder);

// ── Co-client access records ──────────────────────────────────────────────────
public sealed record CoClientItem(Guid AccessId, Guid AppUserId, string DisplayName);

// ── Sub-client invite records (item #4) ─────────────────────────────────────────
public sealed record CaseClientInviteRecord(Guid Id, Guid CaseId, string Email, string Token, DateTime DateExpires, DateTime DateCreated);
public sealed record InviteCoClientResult(bool LinkedExistingAccount, CoClientItem? CoClient, CaseClientInviteRecord? Invite, bool EmailSent);

// ── Case note records ─────────────────────────────────────────────────────────
public sealed record CaseNoteDto(
    Guid      Id,
    Guid      CaseId,
    Guid      AuthorAppUserId,
    string?   AuthorDisplayName,
    string?   Title,
    string    Body,
    bool      IsPinned,
    DateTime  DateCreated,
    DateTime? DateUpdated);

public sealed record UpsertCaseNoteDto(string? Title, string Body, bool IsPinned = false);

// ── Sidecar telemetry records ────────────────────────────────────────────────

/// <summary>One recorded sidecar install or pairing.</summary>
public sealed record SidecarInstallLogRecord(
    Guid Id,
    Guid InstallId,
    string EventType,
    string? Version,
    string? Platform,
    Guid? AppUserId,
    string? AppUserDisplay,
    string? IpAddress,
    DateTime DateCreated);

/// <summary>Distinct installations reporting a given sidecar version.</summary>
public sealed record SidecarVersionCountRecord(string Version, int Installs);

/// <summary>Counts worth having without reading the whole event list.</summary>
public sealed record SidecarTelemetrySummaryRecord(
    int DistinctInstalls,
    int InstallsPairedToAnAccount,
    int DistinctPeople,
    IReadOnlyList<SidecarVersionCountRecord> ByVersion);

/// <summary>A client's pending case move, as their own case page shows it.</summary>
public sealed record PendingReassignRecord(
    Guid LogId, string ToOrganizationName, bool ShareHistory, bool ShareInvestigations, DateTime DateProposed);

/// <summary>One transfer waiting on an organization's answer — including client-proposed moves.</summary>
public sealed record IncomingTransferRecord(
    Guid LogId, Guid CaseId, string CaseTitle, string City, string State,
    string FromOrganizationName, bool ProposedByClient,
    bool ShareHistory, bool ShareInvestigations,
    string? Reason, DateTime DateProposed);

// ── Field sessions recorded on a phone ────────────────────────────────────────

/// <summary>A field session as the website lists it.</summary>
public sealed record FieldSessionSummaryRecord(
    Guid Id, Guid? InvestigationId, Guid DeviceSessionId, string DeviceModel,
    string? LocationLabel, DateTime StartedAt, DateTime? EndedAt,
    int ReadingCount, int MarkerCount, Guid DocumentUploadFileId,
    Guid? RecordedByAppUserId, string? RecordedByName, DateTime DateCreated,
    IReadOnlyList<FieldSessionFileSummary> Files);

public sealed record FieldSessionFileSummary(
    Guid Id, string RelativePath, long FileSize, string? Sha256, bool DigestMatched,
    DateTime DateCreated);

/// <summary>
/// A session and its document.
/// </summary>
/// <remarks>
/// <see cref="Document"/> is the Device Data Format v1 JSON exactly as the phone wrote it. The
/// playback page parses it here rather than having the server reshape it, because it is the only
/// copy that is definitely what the instruments recorded.
/// </remarks>
public sealed record FieldSessionDetailRecord(
    FieldSessionSummaryRecord Session, string Document);
