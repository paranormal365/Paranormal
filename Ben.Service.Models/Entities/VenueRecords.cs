using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

// ── the organizer's side: this event's venue (item 235 phase 9) ──────────────────

/// <summary>Whether this event's place has a venue on the site, and what it has said.</summary>
/// <param name="VenueName">The verified venue's group, or null when the place has none.</param>
/// <param name="Problem">
/// Why what the venue said does not let this event publish, in the readiness list's words. Null when
/// it does, or when there is no venue on the site to ask.
/// </param>
/// <param name="WhyNotAsk">Why the Ask button is not offered, when it is not.</param>
public sealed record EventVenueRecord(
    Guid? VenueOrganizationId,
    string? VenueName,
    Guid? VenueProfileId,
    bool IsOwnVenue,
    VenueRequestRecord? LatestRequest,
    VenueGrantRecord? Grant,
    string? Problem,
    bool CanAsk,
    string? WhyNotAsk);

/// <summary>What the organizer wants the venue to know when asking.</summary>
public sealed record AskTheVenueRequest(string? Message);

// ── the venue's side ────────────────────────────────────────────────────────────

/// <summary>One group's question to a venue, as either side reads it.</summary>
public sealed record VenueRequestRecord(
    Guid Id,
    Guid HostedEventId,
    string EventName,
    Guid RequestingOrganizationId,
    string RequestingOrganizationName,
    string PlaceName,
    DateTime FromDate,
    DateTime ToDate,
    IReadOnlyList<DateTime> Nights,
    int? DayPassCapacity,
    string? Message,
    VenueHostingRequestStatus Status,
    DateTime AskedUtc,
    DateTime? DecidedUtc,
    string? DecisionNote,
    Guid? GrantId);

/// <summary>A standing or withdrawn yes, and the events resting on it.</summary>
public sealed record VenueGrantRecord(
    Guid Id,
    Guid GranteeOrganizationId,
    string GranteeOrganizationName,
    string PlaceName,
    DateTime ValidFrom,
    DateTime ValidTo,
    bool AllowRooms,
    bool AllowHistory,
    bool AllowStaff,
    IReadOnlyList<VenueGrantEventRecord> Events,
    DateTime? RevokedUtc,
    string? RevokedReason);

/// <summary>An event held under a grant.</summary>
public sealed record VenueGrantEventRecord(Guid Id, string Name, HostedEventLifecycleState State);

/// <summary>Everything waiting on a venue, what it has already answered, and what it has lent.</summary>
public sealed record VenueRequestListRecord(
    IReadOnlyList<VenueRequestRecord> Waiting,
    IReadOnlyList<VenueRequestRecord> Answered,
    IReadOnlyList<VenueGrantRecord> Grants,
    string? Note = null);

/// <summary>A yes, and what goes with it.</summary>
public sealed record ApproveVenueRequestRequest(bool AllowRooms, bool AllowHistory, bool AllowStaff);

/// <summary>A no, or a withdrawal. The reason is required: the organizer and the guests read it.</summary>
public sealed record VenueReasonRequest(string? Reason);

// ── the venue's profile ─────────────────────────────────────────────────────────

/// <summary>One place a group describes as its venue.</summary>
/// <param name="VerifiedUtc">When the group was accepted as the venue there. Null until then.</param>
public sealed record VenueProfileRecord(
    Guid Id,
    Guid PlaceId,
    string PlaceName,
    string? History,
    string? HouseRules,
    int? MaxOvernightGuests,
    bool IsPublished,
    DateTime? VerifiedUtc);

/// <summary>Creates or replaces the group's profile at one place.</summary>
public sealed record SaveVenueProfileRequest(
    Guid PlaceId,
    string? History,
    string? HouseRules,
    int? MaxOvernightGuests,
    bool IsPublished);

// ── what anybody sees ───────────────────────────────────────────────────────────

/// <summary>A verified venue's public page.</summary>
public sealed record PublicVenueRecord(
    string OrganizationName,
    string OrganizationUrlName,
    Guid PlaceId,
    string PlaceName,
    string? City,
    string? State,
    string? History,
    string? HouseRules,
    int? MaxOvernightGuests,
    IReadOnlyList<PublicVenueRoomRecord> Rooms,
    IReadOnlyList<PublicVenueEventRecord> Events,
    /// <summary>The pictures in the venue's library that it chose to keep (item 235 phase 12).</summary>
    IReadOnlyList<PublicVenuePhotoRecord>? Photos = null);

/// <summary>A picture of the venue, on its public page.</summary>
public sealed record PublicVenuePhotoRecord(Guid UploadFileId, string? Caption);

/// <summary>A room the venue chose to show.</summary>
public sealed record PublicVenueRoomRecord(string Name, string? Floor, string? Description, int? Capacity);

/// <summary>Something on at the venue, whoever runs it.</summary>
public sealed record PublicVenueEventRecord(
    string Name, string OrganizerName, string Url, DateTime StartsOn, DateTime EndsOn);

/// <summary>Who runs a place as its venue, for the place page.</summary>
/// <param name="VenuePageUrl">Null when the venue has not published its page.</param>
public sealed record PlaceVenueRecord(string OrganizationName, string OrganizationUrlName, string? VenuePageUrl);

// ── a place's contact details ───────────────────────────────────────────────────

/// <summary>One way to reach a place, as the viewer may see it.</summary>
/// <param name="AddedBy">The group that recorded it, or null for the site's own staff.</param>
/// <param name="IsProvisional">
/// A public detail nobody confirmed as the venue has vouched for. Always true before a place has a
/// confirmed venue.
/// </param>
/// <param name="CanRemove">Whether this viewer may take it off.</param>
/// <param name="CanConfirm">Whether this viewer answers for the venue and may vouch for it.</param>
public sealed record PlaceContactRecord(
    Guid Id,
    PlaceContactKind Kind,
    string Value,
    string? Label,
    bool IsPublic,
    string? AddedBy,
    Guid? AddedByOrganizationId,
    bool IsProvisional,
    bool CanRemove,
    bool CanConfirm);

/// <summary>A place's details, and who — if anybody — runs it.</summary>
/// <param name="VenueOrganizationName">The confirmed venue, when there is one.</param>
/// <param name="WhyNoPublic">Why this viewer's groups may add only private details, when that is so.</param>
public sealed record PlaceContactListRecord(
    IReadOnlyList<PlaceContactRecord> Contacts,
    string? VenueOrganizationName,
    string? WhyNoPublic);

/// <summary>Records a way to reach a place, for one of the viewer's groups.</summary>
public sealed record AddPlaceContactRequest(
    Guid OrganizationId, PlaceContactKind Kind, string Value, string? Label, bool IsPublic);

// ── claiming a place ────────────────────────────────────────────────────────────

/// <summary>An address that may prove a claim, masked.</summary>
public sealed record ProvingContactRecord(Guid Id, string Masked, string? Label);

/// <summary>Everything the claim page needs about one place for one group.</summary>
/// <param name="AlreadyVenue">The group confirmed as the venue there, when there is one.</param>
public sealed record VenueClaimStartRecord(
    Guid PlaceId,
    string PlaceName,
    string? AlreadyVenue,
    IReadOnlyList<ProvingContactRecord> ProvingContacts,
    VenueClaimRecord? OpenClaim);

/// <summary>Claims to be the venue at a place.</summary>
/// <param name="PlaceContactId">A proving address to send a code to; null to ask for a review instead.</param>
public sealed record StartVenueClaimRequest(
    Guid PlaceId, VenueClaimantRole Role, string? Evidence, Guid? PlaceContactId);

/// <summary>The code from the venue's email.</summary>
public sealed record VenueClaimCodeRequest(string? Code);

/// <summary>A claim, as the claimant, an objector or the reviewer reads it.</summary>
public sealed record VenueClaimRecord(
    Guid Id,
    Guid PlaceId,
    string PlaceName,
    Guid OrganizationId,
    string OrganizationName,
    string ClaimantName,
    VenueClaimantRole Role,
    string? Evidence,
    VenueClaimState State,
    string? CodeSentTo,
    DateTime AskedUtc,
    DateTime? ProvedUtc,
    DateTime? ObjectionsCloseUtc,
    string? ObjectingOrganizationName,
    string? ObjectionText,
    DateTime? DecidedUtc,
    string? DecisionNote,
    IReadOnlyList<Guid> MayObjectFor);

/// <summary>An objection, for one of the viewer's groups.</summary>
public sealed record ObjectToVenueClaimRequest(Guid OrganizationId, string? Reason);

/// <summary>A reviewer's decision.</summary>
public sealed record DecideVenueClaimRequest(string? Note);

/// <summary>A confirmed venue, for the reviewer who may need to undo one.</summary>
public sealed record ConfirmedVenueRecord(
    Guid ProfileId, Guid PlaceId, string PlaceName, Guid OrganizationId, string OrganizationName, DateTime VerifiedUtc);

/// <summary>Everything a reviewer has to decide, and the venues already confirmed.</summary>
public sealed record AdminVenueClaimListRecord(
    IReadOnlyList<VenueClaimRecord> ToDecide,
    IReadOnlyList<VenueClaimRecord> Standing,
    IReadOnlyList<VenueClaimRecord> Settled,
    IReadOnlyList<ConfirmedVenueRecord> Confirmed,
    string? Note = null);

/// <summary>A picture in a venue's library, for the venue's own page (item 235 phase 12).</summary>
/// <param name="AcceptedUtc">Null while an organizer's offer waits for the venue.</param>
public sealed record VenuePhotoRecord(
    Guid Id, Guid UploadFileId, string? Caption, int SortOrder, DateTime? AcceptedUtc,
    string? OfferedByOrganizationName, string? OfferedFromEventName);

public sealed record UpdateVenuePhotoRequest(string? Caption, int? SortOrder = null);

/// <summary>The venue an event's gallery can offer pictures to, and which pictures it already has.</summary>
public sealed record GalleryVenueRecord(string? VenueName, IReadOnlyList<Guid> OfferedUploadFileIds);

