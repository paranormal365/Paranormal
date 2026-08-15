using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Support;

/// <summary>What the contact form sends.</summary>
/// <remarks>
/// <c>Website</c> is the honeypot — a field styled out of sight that a person never sees and never
/// fills. Named like something a bot would want to complete, and any value in it means the
/// submission was not typed by a human.
/// </remarks>
public sealed record SubmitSupportTicketRequest(
    string? FromName,
    string? FromEmail,
    SupportTicketTopic Topic,
    string? Subject,
    string? Body,
    string? FormToken,
    string? Website);

/// <summary>Handed back after a successful submission — the sender's way back to their ticket.</summary>
public sealed record SubmitSupportTicketResponse(
    string Reference,
    Guid AccessToken);

/// <summary>Issued when the form is rendered; proves how long it was on screen.</summary>
public sealed record SupportFormTokenResponse(string FormToken);

/// <summary>One message in a ticket thread.</summary>
public sealed record SupportTicketReplyRecord(
    Guid Id,
    string Body,
    bool IsFromStaff,
    bool IsInternalNote,
    string? AuthorDisplayName,
    DateTime DateCreated);

/// <summary>A ticket and its thread, as the sender sees it.</summary>
/// <remarks>
/// Carries no <c>SourceIpHash</c>, no assignee and no internal notes. The sender-facing shape is a
/// different record from the staff one rather than the same record with fields blanked, so a field
/// added for staff cannot leak by being forgotten.
/// </remarks>
public sealed record SupportTicketPublicRecord(
    string Reference,
    SupportTicketTopic Topic,
    string Subject,
    string Body,
    SupportTicketStatus Status,
    DateTime DateCreated,
    IReadOnlyList<SupportTicketReplyRecord> Replies);

/// <summary>A ticket as staff see it, including who owns it.</summary>
public sealed record SupportTicketAdminRecord(
    Guid Id,
    string Reference,
    string FromName,
    string FromEmail,
    SupportTicketTopic Topic,
    string Subject,
    string Body,
    SupportTicketStatus Status,
    Guid? AppUserId,
    Guid? AssignedToAppUserId,
    string? AssignedToDisplayName,
    int ReplyCount,
    DateTime DateCreated,
    DateTime? DateUpdated,
    DateTime? DateClosed);

/// <summary>One page of the staff queue.</summary>
public sealed record SupportTicketPage(
    IReadOnlyList<SupportTicketAdminRecord> Items,
    int TotalCount,
    int Page,
    int PageSize);

/// <summary>Adding a message to a thread.</summary>
public sealed record AddSupportTicketReplyRequest(string? Body, bool IsInternalNote);

/// <summary>Changing a ticket's state.</summary>
public sealed record UpdateSupportTicketRequest(
    SupportTicketStatus? Status,
    Guid? AssignedToAppUserId);
