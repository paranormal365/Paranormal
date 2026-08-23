using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

/// <summary>A group's ad as its own administrators see it (item 166 W3).</summary>
public sealed record OrganizationAdRecord(
    Guid Id, Guid OrganizationId, string Headline, string Body, Guid? ImageUploadFileId,
    string TargetKind, OrganizationAdStatus Status, string? RejectionReason,
    DateTime? DateSubmitted, DateTime? DateReviewed, DateTime DateCreated);

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
    string TargetKind, bool HasImage);
