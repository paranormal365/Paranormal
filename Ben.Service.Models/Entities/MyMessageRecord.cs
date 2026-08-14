namespace Ben.Service.Models.Entities;

/// <summary>
/// One platform message as its recipient sees it — the <c>UserMessage</c> body flattened together
/// with that recipient's own <c>UserMessageTo</c> read state, since a reader never has a use for
/// the other recipients' rows.
/// </summary>
/// <param name="Id">The <c>UserMessageTo</c> row id — what mark-as-read addresses, and unique per recipient.</param>
/// <param name="MessageId">The underlying <c>UserMessage</c> id, shared by every recipient.</param>
/// <param name="Subject">Subject line, or null for messages sent without one.</param>
/// <param name="Body">Message body. Stored as HTML by the SuperAdmin "send as message" flow.</param>
/// <param name="TypeName">Display name of the message's type (e.g. "Audit Record").</param>
/// <param name="TypeIconClass">Icon class from the message type, when it defines one.</param>
/// <param name="TypeColorClass">Colour class from the message type, when it defines one.</param>
/// <param name="SentUtc">When the message was created.</param>
/// <param name="ReadUtc">When this recipient last read it, or null while unread.</param>
/// <param name="SentByAppUserId">Author's user id.</param>
/// <param name="SentByDisplayName">Author's display name, falling back to their email.</param>
public sealed record MyMessageRecord(
    Guid Id,
    Guid MessageId,
    string? Subject,
    string Body,
    string TypeName,
    string? TypeIconClass,
    string? TypeColorClass,
    DateTime SentUtc,
    DateTime? ReadUtc,
    Guid SentByAppUserId,
    string? SentByDisplayName);
