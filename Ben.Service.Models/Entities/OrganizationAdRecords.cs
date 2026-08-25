using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

/// <summary>A group's ad as its own administrators see it (item 166 W3).</summary>
public sealed record OrganizationAdRecord(
    Guid Id, Guid OrganizationId, string Headline, string Body, Guid? ImageUploadFileId,
    string TargetKind, OrganizationAdStatus Status, string? RejectionReason,
    DateTime? DateSubmitted, DateTime? DateReviewed, DateTime DateCreated,
    /// <summary>Times the card was served to a page (item 186 F8) — serves, not eyeballs.</summary>
    long Impressions = 0,
    /// <summary>Times somebody followed it through /go.</summary>
    long Clicks = 0);

/// <summary>What the group writes: everything reviewable, nothing about status.</summary>
public sealed record SaveOrganizationAdRequest(
    string Headline, string Body, Guid? ImageUploadFileId, string TargetKind);

/// <summary>A queue row for the SuperAdmin review screen.</summary>
public sealed record AdminOrganizationAdRecord(
    Guid Id, Guid OrganizationId, string OrganizationName, string Headline, string Body,
    Guid? ImageUploadFileId, string TargetKind, OrganizationAdStatus Status,
    string? RejectionReason, DateTime? DateSubmitted, DateTime? DateReviewed);

/// <summary>One promoted card, as the anonymous placements render it — approved content only,
/// and never a raw file id: the image travels through the ad's own approved-gated route.</summary>
public sealed record PromotedGroupCard(
    Guid AdId, string Headline, string Body, string OrganizationName, string OrganizationUrlName,
    string TargetKind, bool HasImage,
    /// <summary>Miles from the viewer's consented location to the group's nearest PUBLIC address
    /// (item 186 F8). Null when the viewer shared no location, or the group has no public address
    /// — an area-of-operation circle deliberately yields no distance, since its centre is
    /// somebody's privacy compromise, not a place.</summary>
    double? DistanceMiles = null);

/// <summary>Where a clicked promoted card leads (item 186 F8): the group's public page or the
/// group finder — a CLOSED set, and the website's /go route renders the redirect from nothing
/// but these two fields.</summary>
public sealed record PromotedClickTarget(string TargetKind, string OrganizationUrlName);
