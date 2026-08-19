using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

// ── Backlog item #55 — Equipment inventory & checkout tracking ────────────────
// DTOs shared between Ben.Data.WebApi and Ben.Web.Library (both reference this project),
// avoiding the hand-mirrored-record drift risk that BenAdminClientAdapter's HTTP-only slices
// carry. Grouped by area; more records are added here in later phases (sharing, org-owned,
// checkout lifecycle, condition photos/history) rather than a new file per phase.

// ── Catalog (Phase 1) ───────────────────────────────────────────────────────

public sealed record EquipmentCategoryRecord(
    Guid Id,
    string Name,
    string? Description,
    string? IconClass,
    int SortOrder,
    bool IsActive);

public sealed record EquipmentBrandRecord(
    Guid Id,
    string Name,
    bool IsApproved,
    Guid? ProposedByOrganizationId,
    Guid? ProposedByAppUserId,
    DateTime DateCreated);

public sealed record EquipmentModelRecord(
    Guid Id,
    Guid EquipmentBrandId,
    string BrandName,
    Guid EquipmentCategoryId,
    string CategoryName,
    string Name,
    string? ModelNumber,
    string? Description,
    bool IsApproved,
    Guid? ProposedByOrganizationId,
    Guid? ProposedByAppUserId,
    DateTime DateCreated,
    /// <summary>The readable address segment — "h1n" in /equipment/zoom/h1n.</summary>
    string? UrlName = null,
    /// <summary>The make's segment, so a caller holding only this record can build the link.</summary>
    string? BrandUrlName = null);

/// <summary>
/// Proposes a brand for the shared catalog.
/// </summary>
/// <param name="ConfirmDistinct">
/// Set once the person has been shown the close matches and said theirs is genuinely different.
/// Without it a probable typo is refused with the suggestions rather than silently created.
/// </param>
public sealed record UpsertEquipmentBrandRequest(string Name, bool ConfirmDistinct = false);

// ProbableDuplicateResponse and TaxonomyMergeOffer moved to TaxonomyRecords.cs — the experience
// taxonomy grows the same way and hit the same two moments, so they are no longer equipment's.

/// <summary>Renames a model, optionally correcting its model number at the same time.</summary>
public sealed record RenameEquipmentModelRequest(string Name, string? ModelNumber = null);

public sealed record UpsertEquipmentModelRequest(
    Guid EquipmentBrandId,
    Guid EquipmentCategoryId,
    string Name,
    string? ModelNumber,
    string? Description);

public sealed record UpsertEquipmentCategoryRequest(
    string Name,
    string? Description,
    string? IconClass,
    int SortOrder,
    bool IsActive);

// ── Items (Phase 1) ─────────────────────────────────────────────────────────

/// <summary>
/// The server's verdict on what the current caller may do with an <see cref="EquipmentItemRecord"/>.
/// Render as given — never re-derive from whether a call succeeded. Missing/unset means false,
/// per the platform's "a permission gap should close, not open" convention.
/// </summary>
public sealed record EquipmentItemFlags(
    bool IsOwner,
    bool CanEdit,
    bool CanDelete,
    bool CanManageSharing,
    bool CanSeeSerial,
    bool CanManageServiceLog)
{
    public static readonly EquipmentItemFlags None = new(false, false, false, false, false, false);
}

public sealed record EquipmentItemPhotoRecord(
    Guid Id,
    Guid EquipmentItemId,
    Guid UploadFileId,
    bool IsPrimary,
    string? Caption,
    int SortOrder,
    bool ExcludeFromCatalog = false);

public sealed record EquipmentItemRecord(
    Guid Id,
    Guid? OwnerAppUserId,
    string? OwnerDisplayName,
    Guid? OwningOrganizationId,
    string? OwningOrganizationName,
    Guid EquipmentModelId,
    string ModelName,
    string BrandName,
    string CategoryName,
    string DisplayName,
    /// <summary>Null unless <see cref="Flags"/>.CanSeeSerial is true — resolved server-side.</summary>
    string? SerialNumber,
    DateTime? AcquisitionDate,
    string? Notes,
    bool IsRetired,
    bool IncludeInGlobalCatalog,
    EquipmentLoanAudience LoanAudience,
    string? WebsiteUrl,
    /// <summary>Null unless the caller is an org Administrator or SuperAdmin.</summary>
    EquipmentItemCountersRecord? Counters,
    Guid? CurrentHolderAppUserId,
    string? CurrentHolderDisplayName,
    DateTime? LastServicedDate,
    string? DefectNotes,
    IReadOnlyList<EquipmentItemPhotoRecord> Photos,
    EquipmentItemFlags Flags);

public sealed record UpsertEquipmentItemRequest(
    Guid EquipmentModelId,
    string DisplayName,
    string? SerialNumber,
    DateTime? AcquisitionDate,
    string? Notes,
    bool IncludeInGlobalCatalog = false,
    EquipmentLoanAudience LoanAudience = EquipmentLoanAudience.NotLoanable,
    string? WebsiteUrl = null);

// ── Model pages and interest counters (Phase 6b) ────────────────────────────

/// <summary>
/// One photo as it appears on a make/model page, pooled from every owner's copy of that model.
/// </summary>
/// <remarks>
/// Deliberately carries no <c>EquipmentItemId</c>, no <c>UploadFileId</c> and nothing about an
/// owner. The pooled photos are anonymous, and a shape that cannot carry an identifier cannot leak
/// one — a per-viewer filter written wrongly later would still have nothing to expose. A reflection
/// test asserts that.
///
/// <para><see cref="LinkedItemId"/> is the one exception, and it is computed per viewer: set only
/// when this particular caller is allowed to open that item's page, null for everyone else. The
/// client renders a link when it is present and plain image when it is not — the server's verdict,
/// never re-derived.</para>
/// </remarks>
public sealed record CatalogPhotoRecord(
    Guid PhotoId,
    string? Caption,
    int SortOrder,
    Guid? LinkedItemId);

/// <summary>Everything a make/model page shows, aggregated across every copy of that model.</summary>
public sealed record EquipmentModelPageRecord(
    EquipmentModelRecord Model,
    int ItemCount,
    int AvailableToBorrowCount,
    IReadOnlyList<string> WebsiteLinks,
    IReadOnlyList<CatalogPhotoRecord> Photos,
    /// <summary>
    /// FAQ entries from publicly-listed copies of this model only — a fixed public rule, not a
    /// per-viewer one. An aggregate that widened for members would let a reader infer that somebody
    /// in their group owns one.
    /// </summary>
    IReadOnlyList<CatalogFaqRecord> Faqs);

/// <summary>Who owns a piece, for viewers entitled to know.</summary>
public sealed record EquipmentItemOwnershipRecord(
    Guid? OwnerAppUserId,
    string? OwnerDisplayName,
    Guid? OwningOrganizationId,
    string? OwningOrganizationName);

/// <summary>
/// The parts of a piece only its custodians see: serial, condition, who is holding it.
/// </summary>
/// <remarks>
/// A nested optional record rather than fields on the parent that are sometimes null. Absence is
/// then structural — a viewer who should not see any of this receives a payload with no slot for
/// it, instead of one carrying six nulls that a future change might start filling in.
/// </remarks>
public sealed record EquipmentItemManagementRecord(
    string? SerialNumber,
    Guid? CurrentHolderAppUserId,
    string? CurrentHolderDisplayName,
    DateTime? LastServicedDate,
    string? DefectNotes);

/// <summary>What the viewer may do on the item page. Rendered, never re-derived.</summary>
public sealed record EquipmentItemDetailFlags(
    bool IsOwner,
    bool CanEdit,
    bool CanRetire,
    bool CanManagePhotos,
    bool CanSeeCounters);

/// <summary>
/// One piece of equipment as a particular viewer is entitled to see it.
/// </summary>
/// <remarks>
/// Serves owners, group members, borrowers and anonymous visitors from one endpoint, because the
/// question "what may this person see" has one answer and splitting it across four surfaces is how
/// the answers drift apart. Everything audience-dependent lives in a nullable sub-record.
/// </remarks>
public sealed record EquipmentItemDetailRecord(
    Guid Id,
    Guid EquipmentModelId,
    string ModelName,
    string BrandName,
    string CategoryName,
    string DisplayName,
    string? Notes,
    DateTime? AcquisitionDate,
    bool IsRetired,
    EquipmentLoanAudience LoanAudience,
    string? WebsiteUrl,
    IReadOnlyList<EquipmentItemPhotoRecord> Photos,
    EquipmentItemOwnershipRecord? Ownership,
    EquipmentItemManagementRecord? Management,
    EquipmentItemCountersRecord? Counters,
    EquipmentItemDetailFlags Flags);

/// <summary>Lifetime interest in one piece. Org Administrators and SuperAdmin only.</summary>
public sealed record EquipmentItemCountersRecord(int ViewCount, int LinkClickCount);

/// <summary>Hides or restores one photo on the make/model page.</summary>
public sealed record SetPhotoCatalogExclusionRequest(bool Exclude);

// ── Condition photos, renewals, history (Phase 5) ───────────────────────────

/// <summary>One condition photo attached to a loan, at one end of it.</summary>
public sealed record EquipmentCheckoutPhotoRecord(
    Guid Id,
    Guid EquipmentCheckoutId,
    Guid UploadFileId,
    EquipmentPhotoStage Stage,
    string? Caption,
    DateTime DateCreated,
    Guid CreatedByAppUserId,
    string? CreatedByDisplayName);

/// <summary>One request for more time on a loan, and what was said about it.</summary>
public sealed record EquipmentCheckoutRenewalRecord(
    Guid Id,
    Guid EquipmentCheckoutId,
    DateTime RequestedDateDue,
    EquipmentRenewalStatus Status,
    string? RequestNotes,
    string? ReviewNotes,
    Guid? ReviewedByAppUserId,
    string? ReviewedByDisplayName,
    DateTime? DateReviewed,
    DateTime DateCreated,
    bool CanReview,
    bool CanCancel);

/// <summary>Asks for more time on a loan that is already out.</summary>
public sealed record RequestEquipmentRenewalRequest(DateTime RequestedDateDue, string? RequestNotes);

/// <summary>Decides a renewal. A reason is required to refuse one.</summary>
public sealed record ReviewEquipmentRenewalRequest(bool Approve, string? ReviewNotes);

/// <summary>What kind of thing happened to a piece of equipment.</summary>
public enum EquipmentHistoryKind
{
    Loan = 1,
    Renewal = 2,
    Service = 3,
    Defect = 4,
}

/// <summary>
/// One entry in a piece of equipment's combined history — loans, renewals, service and defects
/// merged into a single chronological account.
/// </summary>
/// <remarks>
/// Deliberately flat and pre-described: the server writes the sentence, so every surface showing a
/// history says the same thing about the same event. Carries no serial number, because history is
/// visible to people the serial is not.
/// </remarks>
public sealed record EquipmentHistoryEntryRecord(
    DateTime DateUtc,
    EquipmentHistoryKind Kind,
    string Summary,
    string? ActorDisplayName,
    Guid? CheckoutId,
    int PhotoCount);

/// <summary>
/// What was taken out of a picture and kept beside it. Org Administrators and SuperAdmin only.
/// </summary>
/// <remarks>
/// Deliberately a separate shape from anything on a serve path: the bytes and thumbnail routes
/// cannot carry this even by accident, because they do not return this type.
/// </remarks>
public sealed record UploadFileMetadataRecord(
    string MediaKind,
    int? WidthPixels,
    int? HeightPixels,
    DateTime? CapturedAtUtc,
    double? GpsLatitude,
    double? GpsLongitude,
    double? GpsAltitudeMeters,
    string? CameraManufacturer,
    string? CameraModel,
    double? DurationSeconds,
    DateTime ExtractedAtUtc);

// ── Checkouts (Phase 4) ─────────────────────────────────────────────────────

/// <summary>
/// One loan, as whichever party is looking at it sees it.
/// </summary>
/// <remarks>
/// <c>IsOverdue</c> is computed server-side rather than left to the client: it depends on "now",
/// and a borrower whose clock is wrong should not see a different answer from the lender's.
/// </remarks>
public sealed record EquipmentCheckoutRecord(
    Guid Id,
    Guid EquipmentItemId,
    string ItemDisplayName,
    string BrandName,
    string ModelName,
    Guid? ItemOwnerAppUserId,
    string? ItemOwnerDisplayName,
    Guid? ItemOwningOrganizationId,
    Guid BorrowerAppUserId,
    string? BorrowerDisplayName,
    Guid? BorrowedForOrganizationId,
    string? BorrowedForOrganizationName,
    Guid? InvestigationId,
    string? InvestigationTitle,
    EquipmentCheckoutStatus Status,
    bool IsOverdue,
    string? RequestNotes,
    string? ReviewNotes,
    Guid? ReviewedByAppUserId,
    string? ReviewedByDisplayName,
    DateTime? DateReviewed,
    DateTime? DateNeededFrom,
    DateTime? DateDue,
    DateTime? DateCheckedOut,
    DateTime? DateReturned,
    string? ReturnConditionNotes,
    DateTime DateCreated,
    EquipmentCheckoutFlags Flags);

/// <summary>
/// A group's loan queue, plus whether this caller may review loans at all.
/// </summary>
/// <remarks>
/// Same reason <c>OrgEquipmentListRecord</c> is wrapped: "you may not see this" and "there is
/// nothing here yet" are different answers, and an empty list cannot tell them apart on its own.
/// </remarks>
public sealed record OrgCheckoutListRecord(
    bool CanReviewLoans,
    IReadOnlyList<EquipmentCheckoutRecord> Items);

/// <summary>What the viewer may do with this loan right now. Rendered, never re-derived.</summary>
public sealed record EquipmentCheckoutFlags(
    bool IsBorrower,
    bool IsApprover,
    bool CanCancel,
    bool CanApprove,
    bool CanDeny,
    bool CanConfirmHandoff,
    bool CanReceiveReturn);

/// <summary>
/// A group the caller could borrow a given item for — or, with a null id, borrowing it personally.
/// </summary>
/// <remarks>
/// Personal borrowing is offered as an option in the same list rather than a separate control,
/// because from the borrower's side "who am I borrowing this for?" is one question with several
/// answers, one of which is "myself".
/// </remarks>
public sealed record BorrowOptionRecord(Guid? OrganizationId, string Label);

/// <summary>
/// Whether the caller may ask to borrow an item, and on whose behalf they could.
/// </summary>
/// <remarks>
/// Returned by the server so the request form never has to work out the loan-audience rules for
/// itself. An empty <c>Options</c> with <c>CanRequest</c> false is the ordinary answer for gear that
/// is visible but not lent out.
/// </remarks>
public sealed record BorrowEligibilityRecord(
    Guid EquipmentItemId,
    bool CanRequest,
    string? Reason,
    IReadOnlyList<BorrowOptionRecord> Options,
    /// <summary>True when somebody already has this piece, or is about to collect it.</summary>
    bool IsCurrentlyOut = false,
    /// <summary>When it is expected back, if a due date was set.</summary>
    DateTime? ExpectedBackOn = null);

/// <summary>Asks to borrow a piece of equipment.</summary>
public sealed record RequestEquipmentCheckoutRequest(
    Guid EquipmentItemId,
    Guid? BorrowedForOrganizationId,
    Guid? InvestigationId,
    DateTime? DateNeededFrom,
    string? RequestNotes);

/// <summary>Approves a request, optionally setting when the gear is due back.</summary>
public sealed record ApproveEquipmentCheckoutRequest(DateTime? DateDue, string? ReviewNotes);

/// <summary>Turns a request down. A reason is required.</summary>
public sealed record DenyEquipmentCheckoutRequest(string ReviewNotes);

/// <summary>Records the gear coming back, with any note on its condition.</summary>
public sealed record ReturnEquipmentCheckoutRequest(string? ReturnConditionNotes);

// ── Group-owned equipment and the service log (Phase 3) ─────────────────────

/// <summary>
/// The group's own equipment, plus whether this caller may add to it.
/// </summary>
/// <remarks>
/// The list is wrapped rather than returned bare because <c>CanManage</c> cannot be inferred from
/// the rows: a group with no equipment yet has no row to carry a flag, and deriving the verdict
/// from "are there any editable items" would leave the first piece impossible to add. The server
/// knows the answer whether or not anything exists, so it says so.
/// </remarks>
public sealed record OrgEquipmentListRecord(
    bool CanManage,
    IReadOnlyList<EquipmentItemRecord> Items);

/// <summary>Creates or edits a piece of the group's own equipment.</summary>
public sealed record UpsertOrgEquipmentItemRequest(
    Guid EquipmentModelId,
    string DisplayName,
    string? SerialNumber,
    DateTime? AcquisitionDate,
    string? Notes,
    bool IncludeInGlobalCatalog = false,
    string? WebsiteUrl = null);

/// <summary>Sets (or clears, with null) who is currently holding a piece of group gear.</summary>
public sealed record SetEquipmentHolderRequest(Guid? AppUserId);

/// <summary>One entry in a piece of equipment's service and defect history.</summary>
public sealed record EquipmentServiceLogRecord(
    Guid Id,
    Guid EquipmentItemId,
    EquipmentServiceLogType EntryType,
    DateTime EntryDate,
    string Notes,
    Guid? PerformedByAppUserId,
    string? PerformedByDisplayName,
    DateTime DateCreated,
    Guid CreatedByAppUserId,
    string? CreatedByDisplayName);

/// <summary>
/// Adds a service-log entry. The entry type drives a side effect on the item itself, in the same
/// save: a reported defect becomes the item's current defect note, a resolved one clears it, and a
/// service entry moves its last-serviced date.
/// </summary>
public sealed record AddEquipmentServiceLogRequest(
    EquipmentServiceLogType EntryType,
    DateTime EntryDate,
    string Notes,
    Guid? PerformedByAppUserId);

// ── Sharing (Phase 2) ───────────────────────────────────────────────────────

/// <summary>
/// One group the owner could share an item with, and whether it currently is. Returned as a whole
/// list so the sharing editor needs a single call: the owner's groups and the item's shares are the
/// same question asked from two sides.
/// </summary>
public sealed record EquipmentShareOptionRecord(
    Guid OrganizationId,
    string OrganizationName,
    bool IsShared);

/// <summary>Replaces an item's shares wholesale. Any group not listed is unshared.</summary>
public sealed record SetEquipmentSharesRequest(IReadOnlyList<Guid> OrganizationIds);

/// <summary>
/// Shares or unshares every one of the caller's non-retired items with one group at once — the
/// "share all my gear with this group" convenience. Still writes per-item rows, so a single piece
/// can be excluded afterwards without unpicking anything.
/// </summary>
public sealed record BulkEquipmentShareRequest(Guid OrganizationId, bool Share);

/// <summary>What a bulk share/unshare actually changed, so the UI can say so plainly.</summary>
public sealed record BulkEquipmentShareResult(int ItemsAffected, int TotalItems);

/// <summary>
/// A member's gear as seen by another member of a group it is shared with. Owner name is present —
/// that is the point of sharing — but there is no serial number property on the shape at all: the
/// serial stays with the owner even here, and a projection that cannot carry it cannot leak it.
/// </summary>
public sealed record SharedEquipmentItemRecord(
    Guid Id,
    Guid OwnerAppUserId,
    string? OwnerDisplayName,
    string DisplayName,
    string BrandName,
    string ModelName,
    string CategoryName,
    string? Notes,
    EquipmentLoanAudience LoanAudience,
    bool IsRetired,
    IReadOnlyList<EquipmentItemPhotoRecord> Photos);

/// <summary>
/// One publicly-listed item as an anonymous visitor sees it. Deliberately not
/// <see cref="EquipmentItemRecord"/>: there is no owner id, no owner name, no serial and no
/// permission flags on this shape at all, so a public projection cannot leak them by omission of a
/// check somewhere downstream.
/// </summary>
public sealed record PublicEquipmentItemRecord(
    Guid Id,
    /// <summary>Lets a catalog card navigate to the model page. Identifies a product, not a person.</summary>
    Guid EquipmentModelId,
    string DisplayName,
    string BrandName,
    string ModelName,
    string CategoryName,
    DateTime? AcquisitionDate,
    string? Notes,
    EquipmentLoanAudience LoanAudience,
    string? WebsiteUrl,
    IReadOnlyList<EquipmentItemPhotoRecord> Photos);

// ── Owner FAQs and anonymous questions (Phase 6c) ───────────────────────────

/// <summary>
/// One entry in an item's FAQ, as everybody reads it.
/// </summary>
/// <remarks>
/// No author, anywhere, for anyone — including the owner who wrote it. On an item page the reader
/// already knows whose gear it is, but the same shape feeds the make/model aggregate where several
/// owners' entries sit side by side, and an aggregate that named its authors would say who owns
/// what. One unattributed shape rather than two that could drift.
/// </remarks>
public sealed record EquipmentFaqRecord(
    Guid Id,
    string Question,
    string Answer,
    int SortOrder);

/// <summary>An FAQ entry on a make/model page, with the item it came from deliberately dropped.</summary>
public sealed record CatalogFaqRecord(
    string Question,
    string Answer);

public sealed record UpsertEquipmentFaqRequest(
    string Question,
    string Answer,
    int SortOrder = 0);

/// <summary>
/// A question as the person who <b>asked</b> it sees it: their own words, the answer if one came,
/// and nothing about who wrote that answer.
/// </summary>
public sealed record AskedQuestionRecord(
    Guid Id,
    Guid EquipmentItemId,
    string ItemDisplayName,
    string BrandName,
    string ModelName,
    string QuestionText,
    string? AnswerText,
    EquipmentQuestionStatus Status,
    DateTime DateAsked,
    DateTime? AnsweredDate);

/// <summary>
/// A question as the person who must <b>answer</b> it sees it.
/// </summary>
/// <remarks>
/// A separate type from <see cref="AskedQuestionRecord"/> with no asker id and no asker name — not
/// the same type with those fields left null. The anonymity is then a property of the shape rather
/// than of every projection that builds one, and a later change cannot quietly start filling a slot
/// that should never have existed. A reflection test asserts the absence.
/// </remarks>
public sealed record ReceivedQuestionRecord(
    Guid Id,
    Guid EquipmentItemId,
    string ItemDisplayName,
    string BrandName,
    string ModelName,
    string QuestionText,
    string? AnswerText,
    EquipmentQuestionStatus Status,
    DateTime DateAsked,
    DateTime? AnsweredDate,
    /// <summary>True once this answer has been published as an FAQ entry; publishing twice is refused.</summary>
    bool PromotedToFaq);

public sealed record AskEquipmentQuestionRequest(string QuestionText);

/// <summary>
/// Answers a question, or declines it. <paramref name="AnswerText"/> is required to answer and
/// ignored when declining — an owner who would rather not say should not have to invent something.
/// </summary>
public sealed record AnswerEquipmentQuestionRequest(string? AnswerText, bool Decline = false);

/// <summary>
/// Publishes an answered question as an FAQ entry. The text is editable first: what reads well as
/// a reply to one person rarely reads well as a public answer.
/// </summary>
public sealed record PromoteQuestionToFaqRequest(string Question, string Answer);

// ── Mutual loan feedback (Phase 6d) ─────────────────────────────────────────

/// <summary>
/// What one side of a finished loan is submitting about the other.
/// </summary>
/// <remarks>
/// <paramref name="ProductComment"/> is borrower-only and rejected with 400 from a lender — a lender
/// reviewing their own gear on its public model page would be an advertisement, not a review.
/// </remarks>
public sealed record SubmitLoanFeedbackRequest(
    string? CounterpartyComment,
    int? Rating,
    string? ProductComment);

/// <summary>
/// One lender's comment about a borrower, shown to a future lender weighing their request.
/// </summary>
/// <remarks>
/// <b>Attributed.</b> This is lender-to-lender context, and an unattributed warning is hard to
/// weigh — you want to know whether it came from someone who lends constantly or someone who has
/// lent once. Deliberately the opposite of <see cref="LenderFeedbackRecord"/>: the asymmetry is the
/// point, and flipping it is one projection field if Ben ever changes his mind.
/// </remarks>
public sealed record BorrowerFeedbackRecord(
    Guid Id,
    string? Comment,
    int? Rating,
    string AuthorDisplayName,
    string ItemDisplayName,
    DateTime DateReturned,
    DateTime DateCreated);

/// <summary>
/// What past borrowers said about a lender, shown to someone considering asking them for something.
/// </summary>
/// <remarks>
/// <b>Unattributed</b>, unlike the lender-facing direction. A borrower saying a lender was
/// unreasonable has more to lose by being named than a lender does, so the protection goes where it
/// is needed. The shape has no author field to fill in.
/// </remarks>
public sealed record LenderFeedbackRecord(
    Guid Id,
    string? Comment,
    int? Rating,
    DateTime DateCreated);

/// <summary>
/// An aggregate over feedback about one person, always carried with its count.
/// </summary>
/// <remarks>
/// <see cref="AverageRating"/> is null below <see cref="MinimumRatingsForAverage"/> ratings — a
/// single sour opinion rendered as "2.0" reads as a verdict when it is one voice. The comments are
/// shown instead, which is the more honest thing to read at that sample size anyway.
/// </remarks>
public sealed record LoanFeedbackSummaryRecord(
    double? AverageRating,
    int RatingCount,
    int CommentCount)
{
    public const int MinimumRatingsForAverage = 3;
}

/// <summary>Feedback about a borrower, with its aggregate — what an approver sees on a request.</summary>
public sealed record BorrowerFeedbackPanelRecord(
    LoanFeedbackSummaryRecord Summary,
    IReadOnlyList<BorrowerFeedbackRecord> Comments);

/// <summary>Feedback about a lender, with its aggregate — what a would-be borrower sees.</summary>
public sealed record LenderFeedbackPanelRecord(
    LoanFeedbackSummaryRecord Summary,
    IReadOnlyList<LenderFeedbackRecord> Comments);

/// <summary>
/// A borrower's remark about the gear itself, on the make/model page. Public, so the shape carries
/// no author, no item and no loan.
/// </summary>
public sealed record ProductReviewRecord(
    string Comment,
    DateTime DateCreated);

/// <summary>
/// Feedback as a moderator sees it — the only shape that names both sides, because acting on a
/// complaint means knowing who wrote what about whom.
/// </summary>
public sealed record ModeratedFeedbackRecord(
    Guid Id,
    Guid EquipmentCheckoutId,
    string ItemDisplayName,
    EquipmentFeedbackRole Role,
    string AuthorDisplayName,
    string? SubjectDisplayName,
    string? CounterpartyComment,
    int? Rating,
    string? ProductComment,
    DateTime DateCreated);

/// <summary>What the loan page needs to know about leaving feedback on this loan.</summary>
public sealed record LoanFeedbackStateRecord(
    bool CanLeaveFeedback,
    EquipmentFeedbackRole? AsRole,
    bool AlreadyLeft);
